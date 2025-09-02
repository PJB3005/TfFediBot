using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.GC;
using SteamKit2.GC.TF2.Internal;
using SteamKit2.Internal;
using TfFediBot;
using TfFediBot.DataManagement;

const int tf2AppId = 440;

var cts = new CancellationTokenSource();

Console.WriteLine("Starting TfFediBot");
Console.WriteLine($"Loading config from {Config.ConfigPath}");

var config = Config.Load();

SlurFilter.ValidateRegexes(config);

Console.WriteLine("Loaded config");
Console.WriteLine($"Steam username: {config.SteamUsername}");
Console.WriteLine($"Fedi URL: {config.FediUrl}");

var dataManager = new DataManager();
dataManager.Start();

int runId;
await using (var con = dataManager.OpenConnection())
{
    runId = CreateNewRun(con);
}

var socialBot = new SocialBot(config);

Console.WriteLine($"Program run is '{runId}'");
Console.WriteLine("Setting up SteamKit...");

var steamClient = new SteamClient();
var callbackManager = new CallbackManager(steamClient);

var gameCoordinator = steamClient.GetHandler<SteamGameCoordinator>() ?? throw new InvalidOperationException();
var steamUser = steamClient.GetHandler<SteamUser>() ?? throw new InvalidOperationException();

callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnectedToSteam);
callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnectedFromSteam);

callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
callbackManager.Subscribe<SteamGameCoordinator.MessageCallback>(OnGCMessage);

Console.WriteLine("Connecting to Steam...");

steamClient.Connect();

while (!cts.IsCancellationRequested)
{
    await callbackManager.RunWaitCallbackAsync(cts.Token);
}

return 1;

async void OnConnectedToSteam(SteamClient.ConnectedCallback callback)
{
    Console.WriteLine("Connected to Steam! Logging in!");

    await using var con = dataManager.OpenConnection();

    var existingGuardData = GetGuardData(con, config);

    var authSession = await steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
    {
        Username = config.SteamUsername,
        Password = config.SteamPassword,
        IsPersistentSession = true,

        // See NewGuardData comment below
        GuardData = existingGuardData,

        Authenticator = new UserConsoleAuthenticator(),
    });

    var pollResponse = await authSession.PollingWaitForResultAsync();

    if (pollResponse.NewGuardData != null)
    {
        await using var tx = con.BeginTransaction();

        con.Execute("INSERT OR REPLACE INTO StoredGuardData (UserName, Data) VALUES (@UserName, @Data)",
            new
            {
                UserName = config.SteamUsername,
                Data = pollResponse.NewGuardData
            });
        tx.Commit();
    }

    steamUser.LogOn(new SteamUser.LogOnDetails
    {
        Username = pollResponse.AccountName,
        AccessToken = pollResponse.RefreshToken,
        ShouldRememberPassword = true,
    });
}

async void OnLoggedOn(SteamUser.LoggedOnCallback callback)
{
    if (callback.Result != EResult.OK)
    {
        Console.WriteLine($"Failed to log on! {callback.Result}");
        Environment.Exit(1);
    }

    Console.WriteLine("Logged onto steam! Let's go!");

    var playGame = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);

    playGame.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
    {
        game_id = new GameID(tf2AppId),
    });

    Console.WriteLine("Logged onto steam! Let's start playing TF2!");

    steamClient.Send(playGame);

    await Task.Delay(5_000);

    var clientHello =
        new ClientGCMsgProtobuf<SteamKit2.GC.TF2.Internal.CMsgClientHello>((uint)EGCBaseClientMsg.k_EMsgGCClientHello);
    gameCoordinator.Send(clientHello, tf2AppId);
}

void OnDisconnectedFromSteam(SteamClient.DisconnectedCallback callback)
{
    Console.WriteLine("We got disconnected from Steam :(");

    cts.Cancel();
}

void OnGCMessage(SteamGameCoordinator.MessageCallback callback)
{
    Console.WriteLine($"Received game coordinator message {callback.EMsg} {callback.Message}!");

    using (var con = dataManager.OpenConnection())
    {
        con.Execute(
            "INSERT INTO ReceivedGCMessage (ReceivedAt, ReceivedOnRun, Protobuf, MsgType, Data) VALUES (datetime('now'), @RunId, @ProtoBuf, @MsgType, @Data)",
            new
            {
                RunId = runId,
                ProtoBuf = callback.IsProto,
                MsgType = callback.Message.MsgType,
                Data = callback.Message.GetData()
            });
    }

    if (!callback.Message.IsProto)
    {
        Console.WriteLine("Message is not protobuf, ignoring!");
        return;
    }

    switch (callback.Message.MsgType)
    {
        case (uint)EGCBaseClientMsg.k_EMsgGCClientWelcome:
        {
            var msg = new ClientGCMsgProtobuf<CMsgClientWelcome>(callback.Message);

            Console.WriteLine($"GC welcomed us! Version: {msg.Body.version}");
            break;
        }
        case (uint)EGCBaseClientMsg.k_EMsgGCClientGoodbye:
        {
            Console.WriteLine("Server wished us goodbye??? Well that's strange. Time to die.");
            cts.Cancel();
            break;
        }
        case (uint)EGCItemMsg.k_EMsgGCClientDisplayNotification:
        {
            var msg = new ClientGCMsgProtobuf<CMsgGCClientDisplayNotification>(callback.Message);

            Console.WriteLine("We got a message! Let's go");
            ReceivedNotification(msg.Body);

            break;
        }
    }
}

void ReceivedNotification(CMsgGCClientDisplayNotification notification)
{
    using var con = dataManager.OpenConnection();

    var dict = notification.body_substring_keys.Zip(notification.body_substring_values).ToDictionary();
    var json = JsonSerializer.Serialize(dict);

    var formatted = RingFormatting.Format(notification.notification_body_localization_key, dict);

    con.Execute("""
        INSERT INTO ReceivedNotification (ReceivedAt, ReceivedOnRun, Title, Body, Substring, Formatted)
        VALUES (datetime('now'), @RunId, @Title, @Body, @Substring, @Formatted)
        """,
        new
        {
            RunId = runId,
            Title = notification.notification_title_localization_key,
            Body = notification.notification_body_localization_key,
            Substring = json,
            Formatted = formatted
        });

    socialBot.PublishMessage(formatted);
}

static int CreateNewRun(SqliteConnection connection)
{
    return connection.QuerySingle<int>("INSERT INTO ProgramRun (StartedAt) VALUES (datetime('now')) RETURNING Id");
}

static string? GetGuardData(SqliteConnection connection, Config config)
{
    using var tx = connection.BeginTransaction();

    var (user, data) =
        connection.QuerySingleOrDefault<(string?, string)>("SELECT UserName, Data FROM StoredGuardData",
            transaction: tx);

    if (user == null)
        return null;

    if (user != config.SteamUsername)
    {
        connection.Execute("DELETE FROM StoredGuardData", transaction: tx);
        tx.Commit();
    }

    Console.WriteLine("Retrieved stored guard data");

    return data.Trim();
}
