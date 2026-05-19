namespace AIUsageMonitor.Models;

public static class KnownProviders
{
    public const string Anthropic = "Anthropic";
    public const string OpenAI = "OpenAI";
    public const string Gemini = "Gemini";
    public const string Cursor = "Cursor";

    public static readonly IReadOnlyList<string> All =
    [
        Anthropic,
        OpenAI,
        Gemini,
        Cursor
    ];
}
