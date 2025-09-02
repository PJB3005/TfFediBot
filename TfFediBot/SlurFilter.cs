using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace TfFediBot;

public static class SlurFilter
{
    public static void ValidateRegexes(Config config)
    {
        foreach (var regexes in config.SlurFilter.Values)
        {
            foreach (var regex in regexes)
            {
                try
                {
                    _ = new Regex(regex);
                }
                catch (ArgumentException e)
                {
                    Console.WriteLine($"!!! ERROR: Regex '{regex}' is invalid: {e.Message}");
                }
            }
        }
    }

    public static bool Sanitize(string message, [NotNullWhen(true)] out string? warning)
    {
        var warningBuilder = new StringBuilder();

        var config = Config.Load();

        foreach (var (slurType, regexList) in config.SlurFilter)
        {
            foreach (var regex in regexList)
            {
                if (Regex.IsMatch(message, regex, RegexOptions.IgnoreCase))
                {
                    if (warningBuilder.Length > 0)
                        warningBuilder.Append(", ");

                    warningBuilder.Append(slurType);
                    goto nextType;
                }
            }

            nextType: ;
        }

        if (warningBuilder.Length > 0)
        {
            warning = warningBuilder.ToString();
            return true;
        }

        warning = null;
        return false;
    }
}
