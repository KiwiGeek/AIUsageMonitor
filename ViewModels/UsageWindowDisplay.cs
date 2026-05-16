using AIUsageMonitor.Models;
using MediaBrush = System.Windows.Media.Brush;

namespace AIUsageMonitor.ViewModels;

public sealed class UsageWindowDisplay
{
    public UsageWindowDisplay(UsageWindow usageWindow)
    {
        var status = UsageStatus.FromRemainingPercent(usageWindow.RemainingPercent);

        Title = string.IsNullOrWhiteSpace(usageWindow.Title) ? "Usage" : usageWindow.Title;
        Limit = usageWindow.Limit;
        Used = usageWindow.Used;
        Remaining = usageWindow.EffectiveRemaining;
        UsedPercent = usageWindow.UsedPercent;
        RemainingPercent = usageWindow.RemainingPercent;
        Detail = usageWindow.Detail;
        UsedPercentText = $"{usageWindow.UsedPercent:0}% used";
        RemainingText = $"{usageWindow.RemainingPercent:0}% left";
        LimitText = string.IsNullOrWhiteSpace(Detail) ? UsedPercentText : Detail;
        ResetText = usageWindow.ResetAt is { } resetAt
            ? $"Resets {resetAt.ToLocalTime():MMM d, h:mm tt}"
            : "Reset time unavailable";
        StatusLabel = status.Label;
        StatusBrush = status.Foreground;
        StatusBackground = status.Background;
        ProgressBrush = status.Foreground;
    }

    public string Title { get; }

    public double Limit { get; }

    public double Used { get; }

    public double Remaining { get; }

    public double UsedPercent { get; }

    public double RemainingPercent { get; }

    public string UsedPercentText { get; }

    public string RemainingText { get; }

    public string LimitText { get; }

    public string ResetText { get; }

    public string Detail { get; }

    public string StatusLabel { get; }

    public MediaBrush StatusBrush { get; }

    public MediaBrush StatusBackground { get; }

    public MediaBrush ProgressBrush { get; }
}
