using Mastonet;

namespace TfFediBot;

public sealed class SocialBot
{
    private readonly MastodonClient _mastodonClient;

    public SocialBot(Config config)
    {
        _mastodonClient = new MastodonClient(config.FediUrl, config.FediAccessToken);
    }

    public void PublishMessage(string content)
    {
        Task.Run(() =>
        {
            PublishMessageCore(content);
        });
    }

    private async void PublishMessageCore(string content)
    {
        try
        {
            SlurFilter.Sanitize(content, out var warning);

            await _mastodonClient.PublishStatus(content, Visibility.Unlisted, spoilerText: warning);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error while publishing post! {e}");
        }
    }
}
