using System.Text.Json.Serialization;

namespace AIUsageMonitor.Models;

public sealed class UsageWindow
{
    public string Title { get; init; } = string.Empty;

    public double Limit { get; init; } = 100;

    public double Used { get; init; }

    public double? Remaining { get; init; }

    public DateTimeOffset? ResetAt { get; init; }

    public string Detail { get; init; } = string.Empty;

    [JsonIgnore]
    public double EffectiveRemaining => Remaining ?? Math.Max(Limit - Used, 0);

    [JsonIgnore]
    public double UsedPercent => Limit <= 0 ? 0 : Math.Clamp(Used * 100d / Limit, 0, 100);

    [JsonIgnore]
    public double RemainingPercent => Limit <= 0 ? 0 : Math.Clamp(EffectiveRemaining * 100d / Limit, 0, 100);
}
