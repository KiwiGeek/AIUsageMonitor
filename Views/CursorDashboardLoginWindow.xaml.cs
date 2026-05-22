using System.IO;
using System.Security.Cryptography;
using System.Windows;
using AIUsageMonitor.Services;
using Microsoft.Web.WebView2.Core;

namespace AIUsageMonitor.Views;

public partial class CursorDashboardLoginWindow : FluentDialogWindow
{
    private readonly AppSettingsService _settingsService;
    private readonly AppLogService _logService;

    public CursorDashboardLoginWindow(AppSettingsService settingsService, AppLogService logService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _logService = logService;
        Loaded += WindowOnLoaded;
    }

    private async void WindowOnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WindowOnLoaded;
        StatusTextBlock.Text = "Loading Cursor dashboard...";

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SethsAIUsageMonitor",
                "CursorWebView2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await DashboardWebView.EnsureCoreWebView2Async(environment);
            DashboardWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            DashboardWebView.CoreWebView2.Navigate("https://cursor.com/dashboard/usage");
            StatusTextBlock.Text = "Sign in if prompted. After the usage dashboard loads, click Save Login.";
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusTextBlock.Text = $"Could not load WebView2: {ex.Message}";
            _logService.Error("Cursor", $"Could not load Cursor dashboard login window: {ex.Message}");
        }
    }

    private async void SaveLoginButtonOnClick(object sender, RoutedEventArgs e)
    {
        if (DashboardWebView.CoreWebView2 is null)
        {
            StatusTextBlock.Text = "Cursor dashboard is not ready yet.";
            return;
        }

        try
        {
            var cookies = new List<CoreWebView2Cookie>();
            cookies.AddRange(await DashboardWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://cursor.com"));
            cookies.AddRange(await DashboardWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.cursor.com"));

            var cookieHeader = string.Join("; ",
                cookies
                    .Where(cookie => !string.IsNullOrWhiteSpace(cookie.Name) &&
                                     !string.IsNullOrWhiteSpace(cookie.Value) &&
                                     cookie.Domain.Contains("cursor.com", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(cookie => cookie.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .Select(cookie => $"{cookie.Name}={cookie.Value}"));

            if (string.IsNullOrWhiteSpace(cookieHeader))
            {
                StatusTextBlock.Text = "No Cursor dashboard cookies were found. Make sure you are signed in first.";
                return;
            }

            var settings = _settingsService.Load();
            settings.CursorDashboardCookieHeaderProtected = ProtectedStringService.Protect(cookieHeader);
            settings.CursorDashboardCookiesCapturedAt = DateTimeOffset.Now;
            _settingsService.Save(settings);

            StatusTextBlock.Text = "Cursor dashboard login saved. You can close this window and refresh Seth's AI Usage Monitor.";
            _logService.Info("Cursor", "Cursor dashboard login cookies saved for personal usage scraping.");
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusTextBlock.Text = $"Could not save Cursor login: {ex.Message}";
            _logService.Error("Cursor", $"Could not save Cursor dashboard login cookies: {ex.Message}");
        }
    }

    private void CloseButtonOnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
