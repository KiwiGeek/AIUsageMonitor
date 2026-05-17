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
            ? FormatResetText(resetAt)
            : "Reset time unavailable";
        ResetRelativeText = usageWindow.ResetAt is { } relativeResetAt
            ? FormatResetRelativeText(relativeResetAt)
            : string.Empty;
        ResetRelativeBrush = usageWindow.ResetAt is { } relativeBrushResetAt
            ? ResetRelativeBrushFor(relativeBrushResetAt)
            : UsageBrushes.FrozenBrush("#A8AFBA");
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

    public string ResetRelativeText { get; }

    public MediaBrush ResetRelativeBrush { get; }

    public string Detail { get; }

    public string StatusLabel { get; }

    public MediaBrush StatusBrush { get; }

    public MediaBrush StatusBackground { get; }

    public MediaBrush ProgressBrush { get; }

    private static string FormatResetRelativeText(DateTimeOffset resetAt)
    {
        var remaining = resetAt.ToLocalTime() - DateTimeOffset.Now;

        if (remaining.TotalMinutes <= -1)
        {
            return "reset passed";
        }

        if (remaining.TotalMinutes < 1)
        {
            return "now";
        }

        if (remaining.TotalMinutes < 90)
        {
            var minutes = Math.Max(1, (int)Math.Round(remaining.TotalMinutes));
            return minutes == 1 ? "in 1 minute" : $"in {minutes} minutes";
        }

        if (remaining.TotalHours < 36)
        {
            var hours = Math.Max(1, (int)Math.Round(remaining.TotalHours));
            return hours == 1 ? "in 1 hour" : $"in {hours} hours";
        }

        var days = Math.Max(1, (int)Math.Round(remaining.TotalDays));
        return days == 1 ? "in 1 day" : $"in {days} days";
    }

    private static MediaBrush ResetRelativeBrushFor(DateTimeOffset resetAt)
    {
        var remaining = resetAt.ToLocalTime() - DateTimeOffset.Now;

        if (remaining.TotalMinutes <= -1)
        {
            return UsageBrushes.FrozenBrush("#A8AFBA");
        }

        if (remaining.TotalMinutes <= 0)
        {
            return UsageBrushes.FrozenBrush("#FB7185");
        }

        if (remaining.TotalHours <= 2)
        {
            return UsageBrushes.FrozenBrush("#FBBF24");
        }

        return UsageBrushes.FrozenBrush("#93C5FD");
    }

    private static string FormatResetText(DateTimeOffset resetAt)
    {
        var localResetAt = resetAt.ToLocalTime();
        var prefix = localResetAt <= DateTimeOffset.Now ? "Reset" : "Resets";
        return $"{prefix} {localResetAt:MMM d, h:mm tt}";
    }
}
