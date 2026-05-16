using System.Globalization;
using System.Text.RegularExpressions;

namespace AIUsageMonitor.Collectors;

internal static partial class PlanNameFormatter
{
    public static string Format(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim();
        cleaned = StripKnownPrefix(cleaned, "Gemini Code Assist in");
        cleaned = StripKnownPrefix(cleaned, "Gemini Code Assist");
        cleaned = StripKnownPrefix(cleaned, "ChatGPT");
        cleaned = StripKnownPrefix(cleaned, "Codex");
        cleaned = StripKnownPrefix(cleaned, "Claude");
        cleaned = StripKnownPrefix(cleaned, "Cursor");
        cleaned = StripKnownPrefix(cleaned, "default_claude");

        cleaned = cleaned.Replace('_', ' ').Replace('-', ' ').Replace('.', ' ');
        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim();
        if (cleaned.Length == 0)
        {
            return string.Empty;
        }

        cleaned = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
        cleaned = WordRegex("Ai").Replace(cleaned, "AI");
        cleaned = WordRegex("Api").Replace(cleaned, "API");
        cleaned = WordRegex("Gpt").Replace(cleaned, "GPT");

        return cleaned;
    }

    public static string FormatClaude(string? subscriptionType, string? rateLimitTier)
    {
        var baseName = Format(subscriptionType);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return Format(rateLimitTier);
        }

        var multiplier = ExtractClaudeMultiplier(rateLimitTier);
        return !string.IsNullOrWhiteSpace(multiplier) &&
               string.Equals(baseName, "Max", StringComparison.OrdinalIgnoreCase)
            ? $"{baseName} {multiplier}"
            : baseName;
    }

    private static string StripKnownPrefix(string value, string prefix)
    {
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..].Trim()
            : value;
    }

    private static string ExtractClaudeMultiplier(string? rateLimitTier)
    {
        if (string.IsNullOrWhiteSpace(rateLimitTier))
        {
            return string.Empty;
        }

        var match = ClaudeMultiplierRegex().Match(rateLimitTier);
        return match.Success ? match.Value.ToLowerInvariant() : string.Empty;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private static Regex WordRegex(string word)
    {
        return new Regex($@"\b{Regex.Escape(word)}\b", RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"\d+x", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeMultiplierRegex();
}
