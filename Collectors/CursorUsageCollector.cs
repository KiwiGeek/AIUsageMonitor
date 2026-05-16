using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIUsageMonitor.Models;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.Collectors;

public sealed class CursorUsageCollector : IUsageCollector
{
    private readonly AppSettingsService _settingsService;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public CursorUsageCollector(AppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string ProviderName => "Cursor";

    public async Task<ProviderUsage> CollectAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsService.Load();
        var dashboardUsage = await TryCollectDashboardUsageAsync(settings, cancellationToken);
        if (dashboardUsage is not null)
        {
            return dashboardUsage;
        }

        var apiKey = string.IsNullOrWhiteSpace(settings.CursorApiKey)
            ? Environment.GetEnvironmentVariable("CURSOR_API_KEY")
            : settings.CursorApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ProviderUsageFactory.Unavailable(
                ProviderName,
                "Cursor personal dashboard login is not saved. Open Settings, then use Set up Cursor login.",
                "Cursor dashboard not configured");
        }

        if (apiKey.StartsWith("crsr_", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderUsageFactory.Unavailable(
                ProviderName,
                "The saved crsr_ key is not accepted by Cursor's Teams Admin API. For personal plans, open Settings and use Set up Cursor login.",
                "Cursor personal plan");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.cursor.com/teams/spend");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:")));
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var spendCents = 0;

        if (root.TryGetProperty("teamMemberSpend", out var members) && members.ValueKind == JsonValueKind.Array)
        {
            foreach (var member in members.EnumerateArray())
            {
                if (member.TryGetProperty("spendCents", out var spendElement))
                {
                    spendCents += spendElement.GetInt32();
                }
            }
        }

        var monthlyBudgetDollars = settings.CursorIncludedBudgetDollars;
        if (string.IsNullOrWhiteSpace(settings.CursorApiKey) &&
            double.TryParse(Environment.GetEnvironmentVariable("CURSOR_INCLUDED_BUDGET_DOLLARS"), out var parsedBudget))
        {
            monthlyBudgetDollars = parsedBudget;
        }

        var spendDollars = spendCents / 100d;
        var usedPercent = monthlyBudgetDollars <= 0 ? 0 : Math.Clamp(spendDollars * 100d / monthlyBudgetDollars, 0, 100);

        return new ProviderUsage
        {
            Name = ProviderName,
            PlanName = "Teams",
            Source = "Cursor Admin API",
            StatusMessage = $"Current-cycle spend ${spendDollars:0.00} of configured ${monthlyBudgetDollars:0.00} budget.",
            Windows =
            [
                ProviderUsageFactory.PercentWindow("Monthly", usedPercent, TryParseSubscriptionCycle(root))
            ]
        };
    }

    private static async Task<ProviderUsage?> TryCollectDashboardUsageAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        string cookieHeader;

        try
        {
            cookieHeader = ProtectedStringService.Unprotect(settings.CursorDashboardCookieHeaderProtected);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return ProviderUsageFactory.Unavailable(
                "Cursor",
                $"Saved Cursor dashboard login could not be decrypted: {ex.Message}. Re-save it from Cursor Dashboard Login.",
                "Cursor dashboard cookies");
        }

        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return null;
        }

        using var request = BuildDashboardRequest(HttpMethod.Get, "https://cursor.com/api/usage-summary", cookieHeader);
        using var response = await HttpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            return ProviderUsageFactory.Unavailable(
                "Cursor",
                "Saved Cursor dashboard login was rejected. Open Settings, then use Set up Cursor login again.",
                "Cursor dashboard");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseDashboardUsage(document.RootElement);
    }

    private static HttpRequestMessage BuildDashboardRequest(HttpMethod method, string url, string cookieHeader)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.Referrer = new Uri("https://cursor.com/dashboard/usage");
        return request;
    }

    private static ProviderUsage ParseDashboardUsage(JsonElement root)
    {
        var billingCycleEnd = TryGetDateTimeOffset(root, "billingCycleEnd");
        var membershipType = root.TryGetProperty("membershipType", out var membershipElement)
            ? membershipElement.GetString()
            : null;
        var windows = new List<UsageWindow>();

        if (root.TryGetProperty("individualUsage", out var individualUsage) &&
            individualUsage.TryGetProperty("plan", out var plan) &&
            TryGetDouble(plan, "limit", out var limitCents) &&
            limitCents > 0 &&
            TryGetDouble(plan, "used", out var usedCents))
        {
            var remainingCents = TryGetDouble(plan, "remaining", out var parsedRemaining)
                ? parsedRemaining
                : Math.Max(limitCents - usedCents, 0);
            var usedPercent = Math.Clamp(usedCents * 100d / limitCents, 0, 100);
            windows.Add(ProviderUsageFactory.PercentWindow(
                "Monthly included",
                usedPercent,
                billingCycleEnd,
                $"${usedCents / 100d:0.00} used of ${limitCents / 100d:0.00}; ${remainingCents / 100d:0.00} left"));
        }

        if (root.TryGetProperty("individualUsage", out individualUsage) &&
            individualUsage.TryGetProperty("onDemand", out var onDemand) &&
            onDemand.TryGetProperty("enabled", out var enabledElement) &&
            enabledElement.ValueKind == JsonValueKind.True &&
            TryGetDouble(onDemand, "limit", out var onDemandLimitCents) &&
            onDemandLimitCents > 0 &&
            TryGetDouble(onDemand, "used", out var onDemandUsedCents))
        {
            var remainingCents = TryGetDouble(onDemand, "remaining", out var parsedRemaining)
                ? parsedRemaining
                : Math.Max(onDemandLimitCents - onDemandUsedCents, 0);
            var usedPercent = Math.Clamp(onDemandUsedCents * 100d / onDemandLimitCents, 0, 100);
            windows.Add(ProviderUsageFactory.PercentWindow(
                "On-demand",
                usedPercent,
                billingCycleEnd,
                $"${onDemandUsedCents / 100d:0.00} used of ${onDemandLimitCents / 100d:0.00}; ${remainingCents / 100d:0.00} left"));
        }

        if (windows.Count == 0)
        {
            return ProviderUsageFactory.Unavailable(
                "Cursor",
                "Cursor dashboard response did not include individual plan usage fields.",
                "Cursor dashboard");
        }

        var planName = PlanNameFormatter.Format(membershipType);

        return new ProviderUsage
        {
            Name = "Cursor",
            PlanName = planName,
            Source = "Cursor dashboard",
            StatusMessage = string.IsNullOrWhiteSpace(planName)
                ? "Personal Cursor usage from dashboard."
                : $"Cursor {planName} usage from dashboard.",
            Windows = windows
        };
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;

        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
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

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(property.GetString(), out var parsed))
        {
            return null;
        }

        return parsed;
    }

    private static DateTimeOffset? TryParseSubscriptionCycle(JsonElement root)
    {
        if (!root.TryGetProperty("subscriptionCycleStart", out var cycleStartElement))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(cycleStartElement.GetInt64()).AddMonths(1);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
