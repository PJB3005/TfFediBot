using System.Text.Json;
using System.Text.Json.Serialization;

namespace TfFediBot;

public sealed class Config
{
    public const string ConfigPath = "config.json";

    [JsonInclude] public required string SteamUsername;
    [JsonInclude] public required string SteamPassword;
    [JsonInclude] public required string FediUrl;
    [JsonInclude] public required string FediAccessToken;
    [JsonInclude] public required Dictionary<string, string[]> SlurFilter;

    public static Config Load()
    {
        using var file = File.OpenRead(ConfigPath);

        var config = JsonSerializer.Deserialize<Config>(file, ConfigContext.Default.Config);
        if (config == null)
            throw new InvalidOperationException("Config is null!");

        return config;
    }
}

[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(Config))]
internal partial class ConfigContext : JsonSerializerContext
{
}
