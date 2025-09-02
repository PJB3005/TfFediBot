namespace TfFediBot;

public static class RingFormatting
{
    private static readonly Dictionary<string, string> Locale = new()
    {
        {
            "TF_WeddingRing_ClientMessageBody",
            "%receiver_name% has accepted %gifter_name%'s \"%ring_name%\"! Congratulations!"
        },
        {
            "TF_WeddingRing",
            "Something Special For Someone Special"
        }
    };

    public static string Format(string content, Dictionary<string, string> replacements)
    {
        var baseContent = FormatSingle(content);

        foreach (var (k, v) in replacements)
        {
            baseContent = baseContent.Replace($"%{k}%", FormatSingle(v));
        }

        return baseContent;
    }

    private static string FormatSingle(string msg)
    {
        if (msg.StartsWith('#') && Locale.TryGetValue(msg[1..], out var localized))
            return localized;

        return msg;
    }
}
