using System.Windows;
using AIUsageMonitor.Models;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.Views;

public partial class SettingsWindow : FluentDialogWindow
{
    private readonly AppSettingsService _settingsService;
    private readonly AppLogService _logService;

    public SettingsWindow(AppSettings settings, AppSettingsService settingsService, AppLogService logService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _logService = logService;
        Settings = settings.Clone();
        UpdateIntervalTextBox.Text = Settings.UpdateIntervalMinutes.ToString();
        AnthropicProviderEnabledCheckBox.IsChecked = Settings.IsProviderEnabled(KnownProviders.Anthropic);
        OpenAiProviderEnabledCheckBox.IsChecked = Settings.IsProviderEnabled(KnownProviders.OpenAI);
        GeminiProviderEnabledCheckBox.IsChecked = Settings.IsProviderEnabled(KnownProviders.Gemini);
        CursorProviderEnabledCheckBox.IsChecked = Settings.IsProviderEnabled(KnownProviders.Cursor);
        DeepSeekProviderEnabledCheckBox.IsChecked = Settings.IsProviderEnabled(KnownProviders.DeepSeek);
        GitHubCopilotProviderEnabledCheckBox.IsChecked = Settings.IsProviderEnabled(KnownProviders.GitHubCopilot);
        UpdateCursorModeSummary();
        UpdateDeepSeekApiKeySummary();
        UpdateGitHubCopilotApiKeySummary();
        ClaudeStatusExporterCheckBox.IsChecked = Settings.ClaudeStatusExporterEnabled;
        AutoRunAtLoginCheckBox.IsChecked = Settings.AutoRunAtLoginEnabled || Services.AutoRunService.IsEnabled();
        OverlaySnapToScreenCheckBox.IsChecked = Settings.OverlaySnapToScreenEnabled;
        OverlaySnapToScreenCheckBox.Checked += (_, _) => UpdateOverlaySnapDockOptionsEnabled();
        OverlaySnapToScreenCheckBox.Unchecked += (_, _) => UpdateOverlaySnapDockOptionsEnabled();
        OverlaySnapReserveScreenSpaceCheckBox.IsChecked = Settings.OverlaySnapReserveScreenSpaceEnabled;
        OverlaySnapAutoHideWhenSnappedCheckBox.IsChecked = Settings.OverlaySnapAutoHideWhenSnappedEnabled;
        UpdateOverlaySnapDockOptionsEnabled();
        WaifuSquadCheckBox.IsChecked = Settings.WaifuSquadEnabled;
        UpdateIntervalTextBox.SelectAll();
        UpdateIntervalTextBox.Focus();
    }

    public AppSettings Settings { get; private set; }

    private void SaveButtonOnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(UpdateIntervalTextBox.Text.Trim(), out var minutes))
        {
            ValidationTextBlock.Text = "Enter a whole number of minutes.";
            return;
        }

        if (minutes is < AppSettings.MinimumUpdateIntervalMinutes or > AppSettings.MaximumUpdateIntervalMinutes)
        {
            ValidationTextBlock.Text = $"Enter a value from {AppSettings.MinimumUpdateIntervalMinutes} to {AppSettings.MaximumUpdateIntervalMinutes} minutes.";
            return;
        }

        Settings.UpdateIntervalMinutes = minutes;
        ApplySettingsFromControls();
        DialogResult = true;
    }

    private void CursorSetupButtonOnClick(object sender, RoutedEventArgs e)
    {
        ApplySettingsFromControls();

        var setupWindow = new CursorSetupWindow(Settings, _settingsService, _logService)
        {
            Owner = this
        };

        if (setupWindow.ShowDialog() != true)
        {
            return;
        }

        Settings = setupWindow.Settings;
        UpdateCursorModeSummary();
    }

    private void ProviderSetupButtonOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string providerName })
        {
            return;
        }

        var setupInfo = providerName switch
        {
            KnownProviders.Anthropic => new ProviderSetupInfo(
                "Anthropic Setup",
                "Install Claude Code, run claude, and sign in. Seth's AI Usage Monitor reads ~/.claude/.credentials.json for OAuth usage, with local status-line export as fallback.",
                "Open Claude Code setup",
                "https://docs.claude.com/en/docs/claude-code/setup"),
            KnownProviders.OpenAI => new ProviderSetupInfo(
                "OpenAI Setup",
                "Install the Codex CLI, run codex, and sign in. Seth's AI Usage Monitor reads quota snapshots from local Codex session logs.",
                "Open Codex CLI setup",
                "https://help.openai.com/en/articles/11096431"),
            KnownProviders.Gemini => new ProviderSetupInfo(
                "Gemini Setup",
                "Install Gemini CLI, run gemini, and sign in. Seth's AI Usage Monitor reads Gemini CLI credentials, quota status exports, and local session usage.",
                "Open Gemini CLI setup",
                "https://google-gemini.github.io/gemini-cli/docs/get-started/"),
            _ => null
        };

        if (setupInfo is null)
        {
            return;
        }

        var setupWindow = new ProviderSetupInfoWindow(
            setupInfo.Title,
            setupInfo.Message,
            setupInfo.LinkText,
            setupInfo.Url)
        {
            Owner = this
        };
        setupWindow.ShowDialog();
    }

    private void AboutButtonOnClick(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow
        {
            Owner = this
        };
        aboutWindow.ShowDialog();
    }

    private void CancelButtonOnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ApplySettingsFromControls()
    {
        Settings.SetProviderEnabled(KnownProviders.Anthropic, AnthropicProviderEnabledCheckBox.IsChecked == true);
        Settings.SetProviderEnabled(KnownProviders.OpenAI, OpenAiProviderEnabledCheckBox.IsChecked == true);
        Settings.SetProviderEnabled(KnownProviders.Gemini, GeminiProviderEnabledCheckBox.IsChecked == true);
        Settings.SetProviderEnabled(KnownProviders.Cursor, CursorProviderEnabledCheckBox.IsChecked == true);
        Settings.SetProviderEnabled(KnownProviders.DeepSeek, DeepSeekProviderEnabledCheckBox.IsChecked == true);
        Settings.SetProviderEnabled(KnownProviders.GitHubCopilot, GitHubCopilotProviderEnabledCheckBox.IsChecked == true);
        Settings.ClaudeStatusExporterEnabled = ClaudeStatusExporterCheckBox.IsChecked == true;
        Settings.AutoRunAtLoginEnabled = AutoRunAtLoginCheckBox.IsChecked == true;
        Settings.OverlaySnapToScreenEnabled = OverlaySnapToScreenCheckBox.IsChecked == true;
        Settings.OverlaySnapReserveScreenSpaceEnabled = OverlaySnapReserveScreenSpaceCheckBox.IsChecked == true;
        Settings.OverlaySnapAutoHideWhenSnappedEnabled = OverlaySnapAutoHideWhenSnappedCheckBox.IsChecked == true;
        Settings.WaifuSquadEnabled = WaifuSquadCheckBox.IsChecked == true;
        MergeSavedCursorDashboardLogin();
        Settings.Normalize();
    }

    private void DeepSeekSetupButtonOnClick(object sender, RoutedEventArgs e)
    {
        ApplySettingsFromControls();

        var setupWindow = new DeepSeekSetupWindow(Settings)
        {
            Owner = this
        };

        if (setupWindow.ShowDialog() != true)
        {
            return;
        }

        Settings = setupWindow.Settings;
        UpdateDeepSeekApiKeySummary();
    }

    private void UpdateDeepSeekApiKeySummary()
    {
        DeepSeekApiKeySummaryTextBlock.Text = string.IsNullOrWhiteSpace(Settings.DeepSeekApiKey)
            ? "API key: not configured"
            : "API key: configured";
    }

    private void GitHubCopilotSetupButtonOnClick(object sender, RoutedEventArgs e)
    {
        ApplySettingsFromControls();

        var setupWindow = new GitHubCopilotSetupWindow(Settings)
        {
            Owner = this
        };

        if (setupWindow.ShowDialog() != true)
        {
            return;
        }

        Settings = setupWindow.Settings;
        UpdateGitHubCopilotApiKeySummary();
    }

    private void UpdateGitHubCopilotApiKeySummary()
    {
        GitHubCopilotApiKeySummaryTextBlock.Text = string.IsNullOrWhiteSpace(Settings.GitHubCopilotApiKey)
            ? "Token: not configured"
            : "Token: configured";
    }

    private void UpdateCursorModeSummary()
    {
        Settings.Normalize();
        CursorModeSummaryTextBlock.Text = string.Equals(Settings.CursorUsageMode, AppSettings.CursorUsageModeTeamsApiKey, StringComparison.Ordinal)
            ? "Mode: Teams Admin API key"
            : "Mode: Personal subscription dashboard login";
    }

    private void OverlaySnapDockOptionOnChecked(object sender, RoutedEventArgs e)
    {
        if (sender == OverlaySnapReserveScreenSpaceCheckBox &&
            OverlaySnapReserveScreenSpaceCheckBox.IsChecked == true)
        {
            OverlaySnapAutoHideWhenSnappedCheckBox.IsChecked = false;
        }
        else if (sender == OverlaySnapAutoHideWhenSnappedCheckBox &&
                 OverlaySnapAutoHideWhenSnappedCheckBox.IsChecked == true)
        {
            OverlaySnapReserveScreenSpaceCheckBox.IsChecked = false;
        }
    }

    private void UpdateOverlaySnapDockOptionsEnabled()
    {
        var snapEnabled = OverlaySnapToScreenCheckBox.IsChecked == true;
        OverlaySnapReserveScreenSpaceCheckBox.IsEnabled = snapEnabled;
        OverlaySnapAutoHideWhenSnappedCheckBox.IsEnabled = snapEnabled;
    }

    private void MergeSavedCursorDashboardLogin()
    {
        var savedSettings = _settingsService.Load();
        Settings.CursorDashboardCookieHeaderProtected = savedSettings.CursorDashboardCookieHeaderProtected;
        Settings.CursorDashboardCookiesCapturedAt = savedSettings.CursorDashboardCookiesCapturedAt;
    }

    private sealed record ProviderSetupInfo(string Title, string Message, string LinkText, string Url);
}
