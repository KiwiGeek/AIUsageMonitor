using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using AIUsageMonitor.Models;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.Collectors;

public sealed class DeepSeekUsageCollector : IUsageCollector
{
    private readonly AppSettingsService _settingsService;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public DeepSeekUsageCollector(AppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string ProviderName => KnownProviders.DeepSeek;

    public async Task<ProviderUsage> CollectAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsService.Load();
        var apiKey = string.IsNullOrWhiteSpace(settings.DeepSeekApiKey)
            ? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            : settings.DeepSeekApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ProviderUsageFactory.Unavailable(
                ProviderName,
                "DeepSeek API key is not configured. Open Settings, then use DeepSeek Setup.",
                "DeepSeek API not configured");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage response;
        try
        {
            response = await HttpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderUsageFactory.Unavailable(ProviderName, "DeepSeek balance API timed out.", "DeepSeek API");
        }
        catch (HttpRequestException ex)
        {
            return ProviderUsageFactory.Unavailable(ProviderName, $"DeepSeek balance API failed: {ex.Message}", "DeepSeek API");
        }

        using (response)
        {
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                return ProviderUsageFactory.Unavailable(
                    ProviderName,
                    "DeepSeek API key was rejected. Check the key in Settings.",
                    "DeepSeek API");
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ParseBalanceResponse(document.RootElement, settings);
        }
    }

    private ProviderUsage ParseBalanceResponse(JsonElement root, AppSettings settings)
    {
        var isAvailable = root.TryGetProperty("is_available", out var availableElement) &&
                          availableElement.ValueKind == JsonValueKind.True;

        if (!root.TryGetProperty("balance_infos", out var infosElement) ||
            infosElement.ValueKind != JsonValueKind.Array)
        {
            return ProviderUsageFactory.Unavailable(
                "DeepSeek",
                "DeepSeek balance API response did not include balance info.",
                "DeepSeek API");
        }

        var windows = new List<UsageWindow>();
        var entries = infosElement.EnumerateArray().ToList();
        var peaksDirty = false;

        foreach (var info in entries)
        {
            var currency = TryGetString(info, "currency") ?? "USD";
            var totalBalance = TryGetDecimal(info, "total_balance");

            // Last observed balance acts as the peak: resets whenever balance increases
            // (first run or top-up), so $20 topped up after spending $50 becomes the new 100%.
            settings.DeepSeekLastBalances.TryGetValue(currency, out var peak);

            if (totalBalance > peak)
            {
                peak = totalBalance;
                settings.DeepSeekLastBalances[currency] = peak;
                peaksDirty = true;
            }

            var title = entries.Count > 1 ? $"{currency} balance" : "Balance";
            double usedPercent;
            string detail;

            if (peak <= 0)
            {
                usedPercent = isAvailable ? 0 : 100;
                detail = isAvailable
                    ? $"{currency} {totalBalance:0.00} available"
                    : $"No {currency} credits remaining";
            }
            else
            {
                usedPercent = Math.Clamp((double)((peak - totalBalance) / peak * 100), 0, 100);
                detail = $"{currency} {totalBalance:0.00} of {currency} {peak:0.00} remaining";
            }

            windows.Add(ProviderUsageFactory.PercentWindow(title, usedPercent, null, detail));
        }

        if (peaksDirty)
        {
            TrySavePeaks(settings);
        }

        if (windows.Count == 0)
        {
            return ProviderUsageFactory.Unavailable(
                "DeepSeek",
                "DeepSeek balance API returned no balance entries.",
                "DeepSeek API");
        }

        return new ProviderUsage
        {
            Name = "DeepSeek",
            Source = "DeepSeek balance API",
            StatusMessage = isAvailable
                ? "DeepSeek API credits available."
                : "DeepSeek API credits exhausted.",
            Windows = windows
        };
    }

    private void TrySavePeaks(AppSettings settings)
    {
        try
        {
            _settingsService.Save(settings);
        }
        catch (Exception)
        {
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static decimal TryGetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String &&
            decimal.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return 0;
    }
}
