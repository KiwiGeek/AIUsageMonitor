using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Collectors;

public sealed partial class ClaudeStatusFileUsageCollector : IUsageCollector
{
    private const string ExportName = "ai-usage-monitor-usage.json";
    private const string ExporterScriptName = "ai-usage-monitor-statusline.ps1";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public string ProviderName => "Anthropic";

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
            Path.Combine(claudeDirectory, "usage-status.json"),
            Path.Combine(claudeDirectory, "usage-status.md")
        };

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

            if (usage is not null && !usage.IsUnavailable)
            {
                return usage;
            }
        }

        var oauthUsage = await TryCollectOAuthUsageAsync(claudeDirectory, account, cancellationToken);
        if (oauthUsage is not null)
        {
            return oauthUsage;
        }

        var exporterPath = Path.Combine(claudeDirectory, ExporterScriptName);
        var message = File.Exists(exporterPath)
            ? "Claude status exporter is installed, but no usage export exists yet. Start Claude Code interactively and send one prompt so the status line receives rate_limits."
            : $"No Claude quota status file found. Configure Claude Code status-line/proxy output to write ~/.claude/{ExportName}.";

        return ProviderUsageFactory.Unavailable(
            ProviderName,
            message,
            claudeDirectory,
            planName);
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

    private static async Task<ProviderUsage?> TryCollectOAuthUsageAsync(
        string claudeDirectory,
        ClaudeAccount? account,
        CancellationToken cancellationToken)
    {
        var credentialsPath = Path.Combine(claudeDirectory, ".credentials.json");
        if (!File.Exists(credentialsPath))
        {
            return null;
        }

        account ??= TryReadClaudeAccount(credentialsPath);
        var accessToken = account?.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var windows = new List<UsageWindow>();

        AddOAuthWindow(windows, root, "five_hour", "5h");
        AddOAuthWindow(windows, root, "seven_day", "7d");

        if (windows.Count == 0)
        {
            return null;
        }

        return new ProviderUsage
        {
            Name = "Anthropic",
            PlanName = account?.PlanName ?? string.Empty,
            Source = "Claude OAuth usage endpoint",
            StatusMessage = string.IsNullOrWhiteSpace(account?.PlanName)
                ? "Claude quota from local Claude Code OAuth credentials."
                : $"Claude {account.PlanName} quota from local Claude Code OAuth credentials.",
            Windows = windows
        };
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

            if (root.TryGetProperty("claudeAiOauth", out var oauth) &&
                oauth.TryGetProperty("accessToken", out var tokenElement))
            {
                return new ClaudeAccount(tokenElement.GetString(), planName);
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

    private sealed record ClaudeAccount(string? AccessToken, string PlanName);

    [GeneratedRegex(@"5h\s*=\s*(?<used>\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase)]
    private static partial Regex FiveHourRegex();

    [GeneratedRegex(@"7d\s*=\s*(?<used>\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase)]
    private static partial Regex SevenDayRegex();
}
