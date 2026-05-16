using System.IO;
using System.Text.Json;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Collectors;

public sealed class CodexLogUsageCollector : IUsageCollector
{
    public string ProviderName => "OpenAI";

    public Task<ProviderUsage> CollectAsync(CancellationToken cancellationToken)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sessionsDirectory = Path.Combine(home, ".codex", "sessions");

        if (!Directory.Exists(sessionsDirectory))
        {
            return Task.FromResult(ProviderUsageFactory.Unavailable(
                ProviderName,
                "No Codex session directory found.",
                sessionsDirectory));
        }

        CodexRateLimitSnapshot? latest = null;

        foreach (var file in EnumerateRecentJsonlFiles(sessionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var line in ReadLinesLenient(file))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var snapshot = TryParseRateLimitLine(line);

                if (snapshot is null)
                {
                    continue;
                }

                if (latest is null || snapshot.Timestamp > latest.Timestamp)
                {
                    latest = snapshot;
                }
            }
        }

        if (latest is null)
        {
            return Task.FromResult(ProviderUsageFactory.Unavailable(
                ProviderName,
                "No Codex quota snapshots were found in local session logs.",
                sessionsDirectory));
        }

        var windows = new List<UsageWindow>();

        if (latest.Primary is not null)
        {
            windows.Add(ProviderUsageFactory.PercentWindow(
                WindowTitle(latest.Primary.WindowMinutes),
                latest.Primary.UsedPercent,
                latest.Primary.ResetsAt));
        }

        if (latest.Secondary is not null)
        {
            windows.Add(ProviderUsageFactory.PercentWindow(
                WindowTitle(latest.Secondary.WindowMinutes),
                latest.Secondary.UsedPercent,
                latest.Secondary.ResetsAt));
        }

        var planName = PlanNameFormatter.Format(latest.PlanType);

        return Task.FromResult(new ProviderUsage
        {
            Name = ProviderName,
            PlanName = planName,
            Source = "Codex local session logs",
            StatusMessage = string.IsNullOrWhiteSpace(planName)
                ? "Codex quota from latest local token-count event."
                : $"Codex {planName} quota from latest local token-count event.",
            Windows = windows
        });
    }

    private static IEnumerable<string> EnumerateRecentJsonlFiles(string sessionsDirectory)
    {
        return Directory.EnumerateFiles(sessionsDirectory, "*.jsonl", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(40)
            .Select(file => file.FullName);
    }

    private static IEnumerable<string> ReadLinesLenient(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();

            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            return lines;
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static CodexRateLimitSnapshot? TryParseRateLimitLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!root.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty("rate_limits", out var rateLimits))
            {
                return null;
            }

            var timestamp = root.TryGetProperty("timestamp", out var timestampElement) &&
                            DateTimeOffset.TryParse(timestampElement.GetString(), out var parsedTimestamp)
                ? parsedTimestamp
                : DateTimeOffset.MinValue;

            var limitId = rateLimits.TryGetProperty("limit_id", out var limitIdElement)
                ? limitIdElement.GetString()
                : null;

            if (!string.Equals(limitId, "codex", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new CodexRateLimitSnapshot(
                timestamp,
                ParseWindow(rateLimits, "primary"),
                ParseWindow(rateLimits, "secondary"),
                rateLimits.TryGetProperty("plan_type", out var planElement) ? planElement.GetString() : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CodexWindow? ParseWindow(JsonElement rateLimits, string propertyName)
    {
        if (!rateLimits.TryGetProperty(propertyName, out var window))
        {
            return null;
        }

        var usedPercent = window.TryGetProperty("used_percent", out var usedElement)
            ? usedElement.GetDouble()
            : 0;
        var windowMinutes = window.TryGetProperty("window_minutes", out var minutesElement)
            ? minutesElement.GetInt32()
            : 0;
        var resetsAt = window.TryGetProperty("resets_at", out var resetElement)
            ? DateTimeOffset.FromUnixTimeSeconds(resetElement.GetInt64())
            : (DateTimeOffset?)null;

        return new CodexWindow(usedPercent, windowMinutes, resetsAt);
    }

    private static string WindowTitle(int windowMinutes)
    {
        return windowMinutes switch
        {
            300 => "5h",
            10080 => "7d",
            >= 1440 when windowMinutes % 1440 == 0 => $"{windowMinutes / 1440}d",
            >= 60 when windowMinutes % 60 == 0 => $"{windowMinutes / 60}h",
            > 0 => $"{windowMinutes}m",
            _ => "Usage"
        };
    }

    private sealed record CodexRateLimitSnapshot(
        DateTimeOffset Timestamp,
        CodexWindow? Primary,
        CodexWindow? Secondary,
        string? PlanType);

    private sealed record CodexWindow(double UsedPercent, int WindowMinutes, DateTimeOffset? ResetsAt);
}
