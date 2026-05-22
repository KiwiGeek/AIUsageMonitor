using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;

namespace AIUsageMonitor.ViewModels;

internal static class UsageBrushes
{
    private static readonly MediaBrush AnthropicAccent = FrozenBrush("#F59E0B");
    private static readonly MediaBrush OpenAiAccent = FrozenBrush("#10B981");
    private static readonly MediaBrush GeminiAccent = FrozenBrush("#60A5FA");
    private static readonly MediaBrush CursorAccent = FrozenBrush("#A78BFA");
    private static readonly MediaBrush DeepSeekAccent = FrozenBrush("#4D9EFF");
    private static readonly MediaBrush GitHubCopilotAccent = FrozenBrush("#8957E5");
    private static readonly MediaBrush DefaultAccent = FrozenBrush("#E5E7EB");

    // Sampled from waifu card art (dominant saturated accents).
    private static readonly MediaBrush AnthropicWaifuAccent = FrozenBrush("#F07818");
    private static readonly MediaBrush OpenAiWaifuAccent = FrozenBrush("#4986E1");
    private static readonly MediaBrush GeminiWaifuAccent = FrozenBrush("#0CB0F8");
    private static readonly MediaBrush CursorWaifuAccent = FrozenBrush("#A8E04A");
    private static readonly MediaBrush DeepSeekWaifuAccent = FrozenBrush("#F01811");
    private static readonly MediaBrush GitHubCopilotWaifuAccent = FrozenBrush("#00D7FF");

    public static MediaBrush ProviderAccent(string providerName) =>
        ResolveProviderBrush(providerName, useWaifuPalette: false);

    public static MediaBrush ProviderWaifuAccent(string providerName) =>
        ResolveProviderBrush(providerName, useWaifuPalette: true);

    private static MediaBrush ResolveProviderBrush(string providerName, bool useWaifuPalette)
    {
        var normalized = providerName.Trim().ToLowerInvariant();

        if (normalized.StartsWith("anthropic"))
        {
            return useWaifuPalette ? AnthropicWaifuAccent : AnthropicAccent;
        }

        if (normalized.StartsWith("openai"))
        {
            return useWaifuPalette ? OpenAiWaifuAccent : OpenAiAccent;
        }

        if (normalized.StartsWith("gemini"))
        {
            return useWaifuPalette ? GeminiWaifuAccent : GeminiAccent;
        }

        if (normalized.StartsWith("cursor"))
        {
            return useWaifuPalette ? CursorWaifuAccent : CursorAccent;
        }

        if (normalized.StartsWith("deepseek"))
        {
            return useWaifuPalette ? DeepSeekWaifuAccent : DeepSeekAccent;
        }

        if (normalized.StartsWith("github"))
        {
            return useWaifuPalette ? GitHubCopilotWaifuAccent : GitHubCopilotAccent;
        }

        return DefaultAccent;
    }

    public static MediaBrush FrozenBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
