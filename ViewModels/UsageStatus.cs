using MediaBrush = System.Windows.Media.Brush;

namespace AIUsageMonitor.ViewModels;

public sealed class UsageStatus
{
    private static readonly UsageStatus Healthy = new("Healthy", "#34D399", "#153528");
    private static readonly UsageStatus Low = new("Low", "#FBBF24", "#3A2E12");
    private static readonly UsageStatus Critical = new("Critical", "#FB7185", "#3B1720");
    private static readonly UsageStatus Exhausted = new("Exhausted", "#FCA5A5", "#451A1A");
    public static readonly UsageStatus Unavailable = new("No data", "#C7CED8", "#2A2F3A");

    private UsageStatus(string label, string foreground, string background)
    {
        Label = label;
        Foreground = UsageBrushes.FrozenBrush(foreground);
        Background = UsageBrushes.FrozenBrush(background);
    }

    public string Label { get; }

    public MediaBrush Foreground { get; }

    public MediaBrush Background { get; }

    public static UsageStatus FromRemainingPercent(double remainingPercent)
    {
        return remainingPercent switch
        {
            <= 0 => Exhausted,
            <= 10 => Critical,
            <= 25 => Low,
            _ => Healthy
        };
    }
}
