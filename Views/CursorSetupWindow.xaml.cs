using System.Windows;
using AIUsageMonitor.Models;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.Views;

public partial class CursorSetupWindow : FluentAppWindow
{
    private readonly AppSettingsService _settingsService;
    private readonly AppLogService _logService;
    private CursorDashboardLoginWindow? _cursorDashboardLoginWindow;

    public CursorSetupWindow(AppSettings settings, AppSettingsService settingsService, AppLogService logService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _logService = logService;
        Settings = settings.Clone();
        Settings.Normalize();

        PersonalSubscriptionRadioButton.IsChecked = string.Equals(
            Settings.CursorUsageMode,
            AppSettings.CursorUsageModePersonal,
            StringComparison.Ordinal);
        TeamsApiKeyRadioButton.IsChecked = string.Equals(
            Settings.CursorUsageMode,
            AppSettings.CursorUsageModeTeamsApiKey,
            StringComparison.Ordinal);
        CursorApiKeyTextBox.Text = Settings.CursorApiKey;
        CursorBudgetTextBox.Text = Settings.CursorIncludedBudgetDollars.ToString("0.##");
        UpdateModeFields();
        UpdateDashboardLoginStatus();
    }

    public AppSettings Settings { get; private set; }

    private void CursorModeRadioButtonOnChecked(object sender, RoutedEventArgs e)
    {
        UpdateModeFields();
    }

    private void DashboardLoginButtonOnClick(object sender, RoutedEventArgs e)
    {
        PersonalSubscriptionRadioButton.IsChecked = true;
        UpdateSettingsFromControls();

        if (_cursorDashboardLoginWindow is not null)
        {
            _cursorDashboardLoginWindow.Activate();
            return;
        }

        _cursorDashboardLoginWindow = new CursorDashboardLoginWindow(_settingsService, _logService)
        {
            Owner = this
        };
        _cursorDashboardLoginWindow.Closed += (_, _) =>
        {
            _cursorDashboardLoginWindow = null;
            MergeSavedCursorDashboardLogin();
            UpdateDashboardLoginStatus();
        };
        _cursorDashboardLoginWindow.Show();
        _cursorDashboardLoginWindow.Activate();
    }

    private void SaveButtonOnClick(object sender, RoutedEventArgs e)
    {
        ValidationTextBlock.Text = string.Empty;

        if (TeamsApiKeyRadioButton.IsChecked == true)
        {
            var apiKey = CursorApiKeyTextBox.Text.Trim();
            if (apiKey.StartsWith("crsr_", StringComparison.OrdinalIgnoreCase))
            {
                ValidationTextBlock.Text = "Personal crsr_ keys are not accepted by Cursor's Teams Admin API. Choose Personal Subscription instead.";
                return;
            }

            if (!double.TryParse(CursorBudgetTextBox.Text.Trim(), out var cursorBudgetDollars))
            {
                ValidationTextBlock.Text = "Enter a Cursor budget amount, such as 20.";
                return;
            }

            if (cursorBudgetDollars <= 0)
            {
                ValidationTextBlock.Text = "Enter a Cursor budget greater than 0.";
                return;
            }
        }

        UpdateSettingsFromControls();
        MergeSavedCursorDashboardLogin();
        DialogResult = true;
    }

    private void CloseButtonOnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateModeFields()
    {
        var isTeamsApiMode = TeamsApiKeyRadioButton.IsChecked == true;
        TeamsApiSettingsPanel.IsEnabled = isTeamsApiMode;
        DashboardLoginButton.IsEnabled = !isTeamsApiMode;
    }

    private void UpdateSettingsFromControls()
    {
        Settings.CursorUsageMode = TeamsApiKeyRadioButton.IsChecked == true
            ? AppSettings.CursorUsageModeTeamsApiKey
            : AppSettings.CursorUsageModePersonal;
        Settings.CursorApiKey = CursorApiKeyTextBox.Text.Trim();

        if (double.TryParse(CursorBudgetTextBox.Text.Trim(), out var cursorBudgetDollars) && cursorBudgetDollars > 0)
        {
            Settings.CursorIncludedBudgetDollars = cursorBudgetDollars;
        }

        Settings.Normalize();
    }

    private void MergeSavedCursorDashboardLogin()
    {
        var savedSettings = _settingsService.Load();
        Settings.CursorDashboardCookieHeaderProtected = savedSettings.CursorDashboardCookieHeaderProtected;
        Settings.CursorDashboardCookiesCapturedAt = savedSettings.CursorDashboardCookiesCapturedAt;
    }

    private void UpdateDashboardLoginStatus()
    {
        DashboardLoginStatusTextBlock.Text = Settings.CursorDashboardCookiesCapturedAt is { } capturedAt
            ? $"Dashboard login saved {capturedAt.ToLocalTime():MMM d, yyyy h:mm tt}."
            : "Dashboard login is not saved yet.";
    }
}
