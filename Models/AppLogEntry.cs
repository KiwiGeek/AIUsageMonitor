namespace AIUsageMonitor.Models;

public sealed class AppLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string Level { get; init; } = "Info";

    public string Source { get; init; } = "App";

    public string Message { get; init; } = string.Empty;
}
