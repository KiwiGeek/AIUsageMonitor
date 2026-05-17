using System.IO;
using System.Text.Json;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Collectors;

public sealed class CodexLogUsageCollector : IUsageCollector
{
    private static readonly TimeSpan FreshSnapshotMaxAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CommandFailureBackoff = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CommandUnavailableBackoff = TimeSpan.FromHours(1);
    private static readonly TimeSpan UnknownExhaustionBackoff = TimeSpan.FromMinutes(30);
    private DateTimeOffset _nextCommandRefreshAt = DateTimeOffset.MinValue;
    private string _commandRefreshPauseMessage = string.Empty;

    public string ProviderName => "OpenAI";

    public async Task<ProviderUsage> CollectAsync(CancellationToken cancellationToken)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sessionsDirectory = Path.Combine(home, ".codex", "sessions");
        var latest = TryReadLatestSnapshot(sessionsDirectory, cancellationToken);
        var now = DateTimeOffset.Now;
        var refreshNote = string.Empty;

        if (GetExhaustionRetryAt(latest, now) is { } exhaustionRetryAt)
        {
            refreshNote = $"Codex quota appears exhausted; refresh command paused until {exhaustionRetryAt.ToLocalTime():MMM d, h:mm tt}.";
            PauseCommandRefresh(exhaustionRetryAt, refreshNote);
        }
        else if (ShouldRunCommandRefresh(latest, now))
        {
            var previousTimestamp = latest?.Timestamp;
            var refreshResult = await CliQuotaRefreshRunner.RefreshCodexAsync(cancellationToken);
            latest = TryReadLatestSnapshot(sessionsDirectory, cancellationToken);
            now = DateTimeOffset.Now;

            if (refreshResult.Succeeded)
            {
                _nextCommandRefreshAt = now.Add(FreshSnapshotMaxAge);
                _commandRefreshPauseMessage = string.Empty;

                if (latest is null || previousTimestamp is not null && latest.Timestamp <= previousTimestamp.Value)
                {
                    refreshNote = "Codex refresh command ran, but no newer quota snapshot was written.";
                }
            }
            else
            {
                var retryAt = refreshResult.IsQuotaExhausted
                    ? GetExhaustionRetryAt(latest, now) ?? now.Add(UnknownExhaustionBackoff)
                    : now.Add(refreshResult.CommandFound ? CommandFailureBackoff : CommandUnavailableBackoff);

                PauseCommandRefresh(retryAt, refreshResult.Message);
                refreshNote = $"{refreshResult.Message} Next command retry after {retryAt.ToLocalTime():h:mm tt}.";
            }
        }
        else if (_nextCommandRefreshAt > now && !string.IsNullOrWhiteSpace(_commandRefreshPauseMessage))
        {
            refreshNote = $"{_commandRefreshPauseMessage} Next command retry after {_nextCommandRefreshAt.ToLocalTime():h:mm tt}.";
        }

        if (latest is null)
        {
            var message = Directory.Exists(sessionsDirectory)
                ? "No Codex quota snapshots were found in local session logs."
                : "No Codex session directory found.";

            if (!string.IsNullOrWhiteSpace(refreshNote))
            {
                message += " " + refreshNote;
            }

            return ProviderUsageFactory.Unavailable(ProviderName, message, sessionsDirectory);
        }

        return BuildProviderUsage(latest, refreshNote);
    }

    private static CodexRateLimitSnapshot? TryReadLatestSnapshot(string sessionsDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sessionsDirectory))
        {
            return null;
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

        return latest;
    }

    private ProviderUsage BuildProviderUsage(CodexRateLimitSnapshot latest, string refreshNote)
    {
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
        var statusMessage = string.IsNullOrWhiteSpace(planName)
            ? "Codex quota from latest local token-count event."
            : $"Codex {planName} quota from latest local token-count event.";

        if (!string.IsNullOrWhiteSpace(refreshNote))
        {
            statusMessage += " " + refreshNote;
        }

        return new ProviderUsage
        {
            Name = ProviderName,
            PlanName = planName,
            Source = "Codex local session logs",
            StatusMessage = statusMessage,
            Windows = windows
        };
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

    private bool ShouldRunCommandRefresh(CodexRateLimitSnapshot? latest, DateTimeOffset now)
    {
        if (_nextCommandRefreshAt > now)
        {
            return false;
        }

        return latest is null ||
            now - latest.Timestamp.ToLocalTime() > FreshSnapshotMaxAge ||
            EnumerateWindows(latest).Any(window => window.ResetsAt is { } resetAt && resetAt.ToLocalTime() <= now.AddMinutes(-1));
    }

    private static DateTimeOffset? GetExhaustionRetryAt(CodexRateLimitSnapshot? latest, DateTimeOffset now)
    {
        if (latest is null)
        {
            return null;
        }

        var retryAt = EnumerateWindows(latest)
            .Where(window => window.UsedPercent >= 99.9 && window.ResetsAt is not null && window.ResetsAt.Value.ToLocalTime() > now)
            .Select(window => window.ResetsAt!.Value.ToLocalTime())
            .DefaultIfEmpty()
            .Max();

        return retryAt == default ? null : retryAt;
    }

    private static IEnumerable<CodexWindow> EnumerateWindows(CodexRateLimitSnapshot snapshot)
    {
        if (snapshot.Primary is not null)
        {
            yield return snapshot.Primary;
        }

        if (snapshot.Secondary is not null)
        {
            yield return snapshot.Secondary;
        }
    }

    private void PauseCommandRefresh(DateTimeOffset retryAt, string message)
    {
        _nextCommandRefreshAt = retryAt <= DateTimeOffset.Now
            ? DateTimeOffset.Now.Add(CommandFailureBackoff)
            : retryAt;
        _commandRefreshPauseMessage = message;
    }

    private sealed record CodexRateLimitSnapshot(
        DateTimeOffset Timestamp,
        CodexWindow? Primary,
        CodexWindow? Secondary,
        string? PlanType);

    private sealed record CodexWindow(double UsedPercent, int WindowMinutes, DateTimeOffset? ResetsAt);
}
