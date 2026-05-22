using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIUsageMonitor.Models;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.Collectors;

public sealed partial class ClaudeStatusFileUsageCollector : IUsageCollector
{
    private const string OAuthUsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    private const string OAuthBetaHeader = "oauth-2025-04-20";
    private const string OAuthUserAgent = "claude-code/2.1.71";
    private const string ExportName = "ai-usage-monitor-usage.json";
    private const string LegacyExportName = "apimonitor-usage.json";
    private const string ExporterScriptName = "ai-usage-monitor-statusline.ps1";
    private static readonly TimeSpan LocalExportMaxAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CommandSuccessCooldown = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CommandFailureBackoff = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CommandUnavailableBackoff = TimeSpan.FromHours(1);
    private static readonly TimeSpan UnknownExhaustionBackoff = TimeSpan.FromMinutes(30);
    private DateTimeOffset _nextCommandRefreshAt = DateTimeOffset.MinValue;
    private string _commandRefreshPauseMessage = string.Empty;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public string ProviderName => KnownProviders.Anthropic;

    public async Task<ProviderUsage> CollectAsync(CancellationToken cancellationToken)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claudeDirectory = Path.Combine(home, ".claude");
        var credentialsPath = Path.Combine(claudeDirectory, ".credentials.json");
        var account = TryReadClaudeAccount(credentialsPath);
        var planName = account?.PlanName ?? string.Empty;
        var candidatePaths = new[]
        {
            Path.Combine(claudeDirectory, ExportName),
            Path.Combine(claudeDirectory, LegacyExportName),
            Path.Combine(claudeDirectory, "usage-status.json"),
            Path.Combine(claudeDirectory, "usage-status.md")
        };

        var localUsage = TryReadLocalUsage(candidatePaths, planName, cancellationToken);

        var oauthUsage = await TryCollectOAuthUsageAsync(account, cancellationToken);
        if (oauthUsage is not null && !oauthUsage.IsUnavailable)
        {
            return oauthUsage;
        }

        if (localUsage.FreshUsage is not null)
        {
            return localUsage.FreshUsage;
        }

        var refreshNote = string.Empty;
        var now = DateTimeOffset.Now;

        if (_nextCommandRefreshAt <= now)
        {
            var refreshResult = await CliQuotaRefreshRunner.RefreshClaudeAsync(cancellationToken);
            var refreshedLocalUsage = TryReadLocalUsage(candidatePaths, planName, cancellationToken);
            now = DateTimeOffset.Now;

            if (refreshResult.Succeeded)
            {
                _nextCommandRefreshAt = now.Add(CommandSuccessCooldown);
                _commandRefreshPauseMessage = string.Empty;

                if (refreshedLocalUsage.FreshUsage is not null)
                {
                    return WithStatusNote(refreshedLocalUsage.FreshUsage, "Refreshed by Claude command.");
                }

                refreshNote = "Claude refresh command ran, but no fresh status export was written.";
                localUsage = refreshedLocalUsage;
            }
            else
            {
                var retryAt = refreshResult.IsQuotaExhausted
                    ? GetExhaustionRetryAt(localUsage.StaleUsage, now) ?? now.Add(UnknownExhaustionBackoff)
                    : now.Add(refreshResult.CommandFound ? CommandFailureBackoff : CommandUnavailableBackoff);

                PauseCommandRefresh(retryAt, refreshResult.Message);
                refreshNote = $"{refreshResult.Message} Next command retry after {retryAt.ToLocalTime():h:mm tt}.";
            }
        }
        else if (!string.IsNullOrWhiteSpace(_commandRefreshPauseMessage))
        {
            refreshNote = $"{_commandRefreshPauseMessage} Next command retry after {_nextCommandRefreshAt.ToLocalTime():h:mm tt}.";
        }

        if (oauthUsage is not null)
        {
            return WithStatusNote(oauthUsage, refreshNote);
        }

        if (localUsage.StaleExport is not null)
        {
            var staleMessage = $"Claude status export is stale; last update was {FormatRelativeAge(localUsage.StaleExport.UpdatedAt)}. Start Claude Code and send one prompt, or wait for OAuth usage collection to recover.";
            if (!string.IsNullOrWhiteSpace(refreshNote))
            {
                staleMessage += " " + refreshNote;
            }

            return ProviderUsageFactory.Unavailable(
                ProviderName,
                staleMessage,
                localUsage.StaleExport.Path,
                planName);
        }

        var exporterPath = Path.Combine(claudeDirectory, ExporterScriptName);
        var message = File.Exists(exporterPath)
            ? "Claude status exporter is installed, but no usage export exists yet. Start Claude Code interactively and send one prompt so the status line receives rate_limits."
            : $"No Claude quota status file found. Configure Claude Code status-line/proxy output to write ~/.claude/{ExportName}.";

        if (!string.IsNullOrWhiteSpace(refreshNote))
        {
            message += " " + refreshNote;
        }

        return ProviderUsageFactory.Unavailable(
            ProviderName,
            message,
            claudeDirectory,
            planName);
    }

    private static LocalUsageReadResult TryReadLocalUsage(
        IReadOnlyList<string> candidatePaths,
        string planName,
        CancellationToken cancellationToken)
    {
        ProviderUsage? freshLocalUsage = null;
        ProviderUsage? staleLocalUsage = null;
        StaleLocalExport? staleLocalExport = null;

        foreach (var path in candidatePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            var usage = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? TryParseJson(text, path, planName)
                : TryParseText(text, path, planName);

            if (!IsFreshLocalExport(path, text, out var exportUpdatedAt))
            {
                if (staleLocalExport is null || exportUpdatedAt > staleLocalExport.UpdatedAt)
                {
                    staleLocalExport = new StaleLocalExport(path, exportUpdatedAt);
                    staleLocalUsage = usage is not null && !usage.IsUnavailable ? usage : staleLocalUsage;
                }

                continue;
            }

            if (usage is not null && !usage.IsUnavailable)
            {
                freshLocalUsage = usage;
                break;
            }
        }

        return new LocalUsageReadResult(freshLocalUsage, staleLocalUsage, staleLocalExport);
    }

    private static ProviderUsage? TryParseJson(string text, string path, string fallbackPlanName)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var planName = TryGetClaudePlanName(root, fallbackPlanName);
            var windows = new List<UsageWindow>();

            if (root.TryGetProperty("rate_limits", out var rateLimits))
            {
                AddClaudeWindow(windows, rateLimits, "five_hour", "5h");
                AddClaudeWindow(windows, rateLimits, "seven_day", "7d");
            }
            else
            {
                AddClaudeWindow(windows, root, "five_hour", "5h");
                AddClaudeWindow(windows, root, "seven_day", "7d");
            }

            if (windows.Count == 0)
            {
                var statusMessage = root.TryGetProperty("statusMessage", out var statusMessageElement)
                    ? statusMessageElement.GetString()
                    : null;

                return ProviderUsageFactory.Unavailable(
                    "Anthropic",
                    string.IsNullOrWhiteSpace(statusMessage) ? "Claude status export did not contain rate limit windows yet." : statusMessage,
                    path,
                    planName);
            }

            return new ProviderUsage
            {
                Name = "Anthropic",
                PlanName = planName,
                Source = path,
                StatusMessage = "Claude quota from local status output.",
                Windows = windows
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsFreshLocalExport(string path, string text, out DateTimeOffset exportUpdatedAt)
    {
        exportUpdatedAt = File.GetLastWriteTime(path);

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.TryGetProperty("generatedAt", out var generatedAtElement) &&
                    generatedAtElement.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(generatedAtElement.GetString(), out var generatedAt))
                {
                    exportUpdatedAt = generatedAt;
                }
            }
            catch (JsonException)
            {
            }
        }

        return DateTimeOffset.Now - exportUpdatedAt.ToLocalTime() <= LocalExportMaxAge;
    }

    private static string FormatRelativeAge(DateTimeOffset updatedAt)
    {
        var elapsed = DateTimeOffset.Now - updatedAt.ToLocalTime();
        if (elapsed.TotalMinutes < 90)
        {
            return $"{Math.Max(1, (int)Math.Round(elapsed.TotalMinutes))} minutes ago";
        }

        if (elapsed.TotalHours < 36)
        {
            return $"{Math.Max(1, (int)Math.Round(elapsed.TotalHours))} hours ago";
        }

        return $"{Math.Max(1, (int)Math.Round(elapsed.TotalDays))} days ago";
    }

    private static async Task<ProviderUsage?> TryCollectOAuthUsageAsync(
        ClaudeAccount? account,
        CancellationToken cancellationToken)
    {
        var credentialsPath = ClaudeOAuthCredentialsService.GetDefaultCredentialsPath();
        var planName = account?.PlanName ?? string.Empty;

        var tokenResult = await ClaudeOAuthCredentialsService.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(tokenResult.AccessToken))
        {
            if (!string.IsNullOrWhiteSpace(tokenResult.ErrorMessage))
            {
                return ProviderUsageFactory.Unavailable(
                    "Anthropic",
                    tokenResult.ErrorMessage,
                    tokenResult.CredentialsPath,
                    planName);
            }

            return null;
        }

        account ??= TryReadClaudeAccount(credentialsPath);
        planName = account?.PlanName ?? planName;

        return await FetchOAuthUsageAsync(
            tokenResult.AccessToken,
            planName,
            credentialsPath,
            string.IsNullOrWhiteSpace(planName)
                ? "Claude quota from Claude Code OAuth credentials."
                : $"Claude {planName} quota from Claude Code OAuth credentials.",
            "Claude OAuth usage endpoint rejected credentials (token refresh also failed). Re-run Claude Code login to restore access.",
            cancellationToken);
    }

    private static async Task<ProviderUsage?> FetchOAuthUsageAsync(
        string accessToken,
        string planName,
        string source,
        string successStatusMessage,
        string rejectedCredentialsMessage,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, OAuthUsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBetaHeader);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", OAuthUserAgent);

        HttpResponseMessage response;
        try
        {
            response = await HttpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderUsageFactory.Unavailable(
                "Anthropic",
                "Claude OAuth usage endpoint timed out. Local status-line export will be used if it is fresh.",
                source,
                planName);
        }
        catch (HttpRequestException ex)
        {
            return ProviderUsageFactory.Unavailable(
                "Anthropic",
                $"Claude OAuth usage endpoint failed: {ex.Message}",
                source,
                planName);
        }

        using (response)
        {
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                return ProviderUsageFactory.Unavailable(
                    "Anthropic",
                    rejectedCredentialsMessage,
                    source,
                    planName);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                return ProviderUsageFactory.Unavailable(
                    "Anthropic",
                    "Claude OAuth usage endpoint is rate limited. Local status-line export will be used if it is fresh.",
                    source,
                    planName);
            }

            if (!response.IsSuccessStatusCode)
            {
                return ProviderUsageFactory.Unavailable(
                    "Anthropic",
                    $"Claude OAuth usage endpoint returned {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    source,
                    planName);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var windows = new List<UsageWindow>();
            PopulateOAuthWindows(windows, document.RootElement);

            if (windows.Count == 0)
            {
                return null;
            }

            return new ProviderUsage
            {
                Name = "Anthropic",
                PlanName = planName,
                Source = "Claude OAuth usage endpoint",
                StatusMessage = successStatusMessage,
                Windows = windows
            };
        }
    }

    private static void PopulateOAuthWindows(List<UsageWindow> windows, JsonElement root)
    {
        AddOAuthWindow(windows, root, "five_hour", "5h");
        AddOAuthWindow(windows, root, "seven_day", "7d");
        AddOAuthWindow(windows, root, "seven_day_sonnet", "Sonnet");
        AddOAuthWindow(windows, root, "seven_day_opus", "Opus");
    }

    private static ClaudeAccount? TryReadClaudeAccount(string credentialsPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(credentialsPath));
            var root = document.RootElement;
            var subscriptionType = TryFindStringProperty(root, "subscriptionType");
            var rateLimitTier = TryFindStringProperty(root, "rateLimitTier");
            var planName = PlanNameFormatter.FormatClaude(subscriptionType, rateLimitTier);

            foreach (var oauthPropertyName in new[] { "claudeAiOauth", "oauthAccount" })
            {
                if (!root.TryGetProperty(oauthPropertyName, out var oauth) ||
                    !oauth.TryGetProperty("accessToken", out var tokenElement))
                {
                    continue;
                }

                var accessToken = tokenElement.GetString();
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    continue;
                }

                var refreshToken = oauth.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;
                long? expiresAtMs = oauth.TryGetProperty("expiresAt", out var exp) && exp.TryGetInt64(out var ms) ? ms : null;
                return new ClaudeAccount(accessToken, refreshToken, expiresAtMs, planName);
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static void AddOAuthWindow(List<UsageWindow> windows, JsonElement root, string propertyName, string title)
    {
        if (!root.TryGetProperty(propertyName, out var window) ||
            window.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (!TryGetDouble(window, "utilization", out var usedPercent) &&
            !TryGetDouble(window, "used_percentage", out usedPercent))
        {
            return;
        }

        DateTimeOffset? resetAt = null;
        if (window.TryGetProperty("resets_at", out var resetElement))
        {
            resetAt = resetElement.ValueKind switch
            {
                JsonValueKind.Number => DateTimeOffset.FromUnixTimeSeconds(resetElement.GetInt64()),
                JsonValueKind.String when DateTimeOffset.TryParse(resetElement.GetString(), out var parsed) => parsed,
                _ => null
            };
        }

        windows.Add(ProviderUsageFactory.PercentWindow(title, usedPercent, resetAt));
    }

    private static void AddClaudeWindow(List<UsageWindow> windows, JsonElement container, string propertyName, string title)
    {
        if (!container.TryGetProperty(propertyName, out var window))
        {
            return;
        }

        if (!TryGetDouble(window, "used_percentage", out var usedPercent) &&
            !TryGetDouble(window, "used_percent", out usedPercent))
        {
            return;
        }

        DateTimeOffset? resetAt = null;
        if (window.TryGetProperty("resets_at", out var resetElement) ||
            window.TryGetProperty("reset_at", out resetElement))
        {
            resetAt = resetElement.ValueKind switch
            {
                JsonValueKind.Number => DateTimeOffset.FromUnixTimeSeconds(resetElement.GetInt64()),
                JsonValueKind.String when DateTimeOffset.TryParse(resetElement.GetString(), out var parsed) => parsed,
                _ => null
            };
        }

        windows.Add(ProviderUsageFactory.PercentWindow(title, usedPercent, resetAt));
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(property.GetString(), out value),
            _ => false
        };
    }

    private static ProviderUsage? TryParseText(string text, string path, string planName)
    {
        var windows = new List<UsageWindow>();
        var fiveHourMatch = FiveHourRegex().Match(text);
        var sevenDayMatch = SevenDayRegex().Match(text);

        if (fiveHourMatch.Success && double.TryParse(fiveHourMatch.Groups["used"].Value, out var fiveHourUsed))
        {
            windows.Add(ProviderUsageFactory.PercentWindow("5h", fiveHourUsed, null));
        }

        if (sevenDayMatch.Success && double.TryParse(sevenDayMatch.Groups["used"].Value, out var sevenDayUsed))
        {
            windows.Add(ProviderUsageFactory.PercentWindow("7d", sevenDayUsed, null));
        }

        if (windows.Count == 0)
        {
            return null;
        }

        return new ProviderUsage
        {
            Name = "Anthropic",
            PlanName = planName,
            Source = path,
            StatusMessage = "Claude quota from local text status output.",
            Windows = windows
        };
    }

    private static string TryGetClaudePlanName(JsonElement root, string fallbackPlanName)
    {
        var subscriptionType = TryFindStringProperty(root, "subscriptionType");
        var rateLimitTier = TryFindStringProperty(root, "rateLimitTier");
        var planName = PlanNameFormatter.FormatClaude(subscriptionType, rateLimitTier);
        if (!string.IsNullOrWhiteSpace(planName))
        {
            return planName;
        }

        return PlanNameFormatter.Format(
            TryFindStringProperty(root, "planName") ??
            TryFindStringProperty(root, "plan")) is { Length: > 0 } formatted
                ? formatted
                : fallbackPlanName;
    }

    private static string? TryFindStringProperty(JsonElement element, string propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }

                    var nested = TryFindStringProperty(property.Value, propertyName);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = TryFindStringProperty(item, propertyName);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }

                break;
        }

        return null;
    }

    private static DateTimeOffset? GetExhaustionRetryAt(ProviderUsage? usage, DateTimeOffset now)
    {
        if (usage is null)
        {
            return null;
        }

        var retryAt = usage.Windows
            .Where(window => window.RemainingPercent <= 0.1 && window.ResetAt is not null && window.ResetAt.Value.ToLocalTime() > now)
            .Select(window => window.ResetAt!.Value.ToLocalTime())
            .DefaultIfEmpty()
            .Max();

        return retryAt == default ? null : retryAt;
    }

    private static ProviderUsage WithStatusNote(ProviderUsage usage, string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return usage;
        }

        return new ProviderUsage
        {
            Name = usage.Name,
            PlanName = usage.PlanName,
            Source = usage.Source,
            StatusMessage = usage.StatusMessage + " " + note,
            IsUnavailable = usage.IsUnavailable,
            LastCheckedAt = usage.LastCheckedAt,
            Windows = usage.Windows
        };
    }

    private void PauseCommandRefresh(DateTimeOffset retryAt, string message)
    {
        _nextCommandRefreshAt = retryAt <= DateTimeOffset.Now
            ? DateTimeOffset.Now.Add(CommandFailureBackoff)
            : retryAt;
        _commandRefreshPauseMessage = message;
    }

    private sealed record LocalUsageReadResult(
        ProviderUsage? FreshUsage,
        ProviderUsage? StaleUsage,
        StaleLocalExport? StaleExport);

    private sealed record ClaudeAccount(string AccessToken, string? RefreshToken, long? ExpiresAtMs, string PlanName);

    private sealed record StaleLocalExport(string Path, DateTimeOffset UpdatedAt);

    [GeneratedRegex(@"5h\s*=\s*(?<used>\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase)]
    private static partial Regex FiveHourRegex();

    [GeneratedRegex(@"7d\s*=\s*(?<used>\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase)]
    private static partial Regex SevenDayRegex();
}
