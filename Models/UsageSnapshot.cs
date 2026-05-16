namespace AIUsageMonitor.Models;

public sealed class UsageSnapshot
{
    public DateTimeOffset GeneratedAt { get; init; }

    public string Source { get; init; } = string.Empty;

    public List<ProviderUsage> Providers { get; init; } = [];
}
