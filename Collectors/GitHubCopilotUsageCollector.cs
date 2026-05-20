using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

using System.Text.Json;
using AIUsageMonitor.Models;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.Collectors;

public sealed class GitHubCopilotUsageCollector : IUsageCollector
{
    private readonly AppSettingsService _settingsService;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public GitHubCopilotUsageCollector(AppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string ProviderName => KnownProviders.GitHubCopilot;

    public async Task<ProviderUsage> CollectAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsService.Load();
        var pat = string.IsNullOrWhiteSpace(settings.GitHubCopilotApiKey)
            ? Environment.GetEnvironmentVariable("GITHUB_COPILOT_PAT")
            : settings.GitHubCopilotApiKey;

        if (string.IsNullOrWhiteSpace(pat))
        {
            return ProviderUsageFactory.Unavailable(
                ProviderName,
                "GitHub Personal Access Token is not configured. Open Settings, then use GitHub Copilot Setup.",
                "GitHub Copilot not configured");
        }

        // Use the PAT directly — the internal user endpoint accepts GitHub PATs
        using var quotaRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/copilot_internal/user");
        quotaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat);
        quotaRequest.Headers.TryAddWithoutValidation("User-Agent", "AIUsageMonitor");
        quotaRequest.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2025-04-01");
        quotaRequest.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage quotaResponse;
        try
        {
            quotaResponse = await HttpClient.SendAsync(quotaRequest, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderUsageFactory.Unavailable(ProviderName, "GitHub Copilot API timed out.", "GitHub Copilot API");
        }
        catch (HttpRequestException ex)
        {
            return ProviderUsageFactory.Unavailable(ProviderName, $"GitHub Copilot API failed: {ex.Message}", "GitHub Copilot API");
        }

        using (quotaResponse)
        {
            if (quotaResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return ProviderUsageFactory.Unavailable(
                    ProviderName,
                    "GitHub token was rejected. Check the token in Settings — ensure the account has an active Copilot subscription.",
                    "GitHub Copilot API");
            }

            quotaResponse.EnsureSuccessStatusCode();

            await using var quotaStream = await quotaResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var quotaDoc = await JsonDocument.ParseAsync(quotaStream, cancellationToken: cancellationToken);
            return ParseUserResponse(quotaDoc.RootElement);
        }
    }

    private static ProviderUsage ParseUserResponse(JsonElement root)
    {
        var plan = TryGetString(root, "copilot_plan") ?? string.Empty;

        DateTimeOffset? resetAt = null;
        if (root.TryGetProperty("quota_reset_date_utc", out var resetEl) &&
            resetEl.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(resetEl.GetString(), out var parsedReset))
        {
            resetAt = parsedReset;
        }

        var windows = new List<UsageWindow>();

        if (root.TryGetProperty("quota_snapshots", out var snapshotsEl) &&
            snapshotsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in snapshotsEl.EnumerateObject())
            {
                var snapshot = entry.Value;

                var isUnlimited = snapshot.TryGetProperty("unlimited", out var unlimitedEl) &&
                                  unlimitedEl.ValueKind == JsonValueKind.True;
                if (isUnlimited) continue;

                var quotaId = TryGetString(snapshot, "quota_id") ?? entry.Name;
                var title = GetQuotaTitle(quotaId);
                var remaining = TryGetInt(snapshot, "remaining");
                var entitlement = TryGetInt(snapshot, "entitlement");
                var percentRemaining = TryGetDouble(snapshot, "percent_remaining");

                double usedPercent;
                if (percentRemaining.HasValue)
                {
                    usedPercent = Math.Clamp(100 - percentRemaining.Value, 0, 100);
                }
                else if (entitlement is > 0 && remaining.HasValue)
                {
                    usedPercent = Math.Clamp((double)(entitlement.Value - remaining.Value) / entitlement.Value * 100, 0, 100);
                }
                else
                {
                    continue;
                }

                string detail;
                if (remaining.HasValue && entitlement is > 0)
                {
                    detail = $"{remaining.Value} of {entitlement.Value} remaining";
                }
                else if (remaining.HasValue)
                {
                    detail = $"{remaining.Value} remaining";
                }
                else
                {
                    detail = $"{100 - usedPercent:0}% remaining";
                }

                windows.Add(ProviderUsageFactory.PercentWindow(title, usedPercent, resetAt, detail));
            }
        }

        var planName = FormatPlanName(plan);

        if (windows.Count == 0)
        {
            return new ProviderUsage
            {
                Name = KnownProviders.GitHubCopilot,
                PlanName = planName,
                Source = "GitHub Copilot internal API",
                StatusMessage = "GitHub Copilot active.",
                Windows = []
            };
        }

        var allExhausted = windows.All(w => w.RemainingPercent <= 0);
        var anyExhausted = windows.Any(w => w.RemainingPercent <= 0);

        return new ProviderUsage
        {
            Name = KnownProviders.GitHubCopilot,
            PlanName = planName,
            Source = "GitHub Copilot internal API",
            StatusMessage = allExhausted
                ? "GitHub Copilot premium requests exhausted."
                : anyExhausted
                    ? "Some GitHub Copilot quotas exhausted."
                    : "GitHub Copilot quota available.",
            Windows = windows
        };
    }

    private static string GetQuotaTitle(string quotaId) => quotaId switch
    {
        "premium_interactions" => "Premium requests",
        "chat" => "Chat",
        "code_completions" => "Code completions",
        _ => quotaId
    };

    private static string FormatPlanName(string plan) => plan switch
    {
        "pro" => "Pro",
        "individual" => "Individual",
        "business" => "Business",
        "enterprise" => "Enterprise",
        _ when !string.IsNullOrWhiteSpace(plan) => plan,
        _ => string.Empty
    };

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            return number;
        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsed))
            return parsed;
        return null;
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
            return number;
        if (property.ValueKind == JsonValueKind.String && double.TryParse(property.GetString(), out var parsed))
            return parsed;
        return null;
    }
}
