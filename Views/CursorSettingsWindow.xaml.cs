using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AIUsageMonitor.Models;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.Views;

public partial class CursorSettingsWindow : FluentDialogWindow
{
    private readonly AppSettingsStore _store;
    private readonly AppSettingsService _settingsService;
    private readonly AppLogService _logService;
    private readonly DispatcherTimer _saveTimer;
    private CursorDashboardLoginWindow? _cursorDashboardLoginWindow;

    public CursorSettingsWindow(AppSettingsStore store, AppSettingsService settingsService, AppLogService logService)
    {
        InitializeComponent();
        _store = store;
        _settingsService = settingsService;
        _logService = logService;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveImmediate(); };
        DataContext = store;
        LoadFromSettings();
        Closed += (_, _) => { _saveTimer.Stop(); SaveImmediate(); };
    }

    private void LoadFromSettings()
    {
        var settings = _settingsService.Load();
        settings.Normalize();

        PersonalSubscriptionRadioButton.IsChecked = string.Equals(
            settings.CursorUsageMode, AppSettings.CursorUsageModePersonal, StringComparison.Ordinal);
        TeamsApiKeyRadioButton.IsChecked = string.Equals(
            settings.CursorUsageMode, AppSettings.CursorUsageModeTeamsApiKey, StringComparison.Ordinal);
        CursorApiKeyTextBox.Text = settings.CursorApiKey;
        CursorBudgetTextBox.Text = settings.CursorIncludedBudgetDollars.ToString("0.##");
        UpdateModeFields();
        UpdateDashboardLoginStatus(settings);
    }

    private void CursorModeRadioButtonOnChecked(object sender, RoutedEventArgs e)
    {
        UpdateModeFields();
        SaveImmediate();
    }

    private void DashboardLoginButtonOnClick(object sender, RoutedEventArgs e)
    {
        PersonalSubscriptionRadioButton.IsChecked = true;
        UpdateModeFields();

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
            UpdateDashboardLoginStatus(_settingsService.Load());
            _store.NotifyExternalSave();
        };
        _cursorDashboardLoginWindow.Show();
        _cursorDashboardLoginWindow.Activate();
    }

    private void TextBoxOnTextChanged(object sender, TextChangedEventArgs e)
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void UpdateModeFields()
    {
        var isTeamsApiMode = TeamsApiKeyRadioButton?.IsChecked == true;
        if (TeamsApiSettingsPanel is not null)
            TeamsApiSettingsPanel.IsEnabled = isTeamsApiMode;
        if (DashboardLoginButton is not null)
            DashboardLoginButton.IsEnabled = !isTeamsApiMode;
    }

    private void UpdateDashboardLoginStatus(AppSettings settings)
    {
        DashboardLoginStatusTextBlock.Text = settings.CursorDashboardCookiesCapturedAt is { } capturedAt
            ? $"Dashboard login saved {capturedAt.ToLocalTime():MMM d, yyyy h:mm tt}."
            : "Dashboard login is not saved yet.";
    }

    private void SaveImmediate()
    {
        try
        {
            var settings = _settingsService.Load();
            settings.CursorUsageMode = TeamsApiKeyRadioButton.IsChecked == true
                ? AppSettings.CursorUsageModeTeamsApiKey
                : AppSettings.CursorUsageModePersonal;
            settings.CursorApiKey = CursorApiKeyTextBox.Text.Trim();
            if (double.TryParse(CursorBudgetTextBox.Text.Trim(), out var budget) && budget > 0)
                settings.CursorIncludedBudgetDollars = budget;
            settings.Normalize();
            _settingsService.Save(settings);
            _store.NotifyExternalSave();
        }
        catch (Exception ex)
        {
            _logService.Warning("Cursor", $"Could not save settings: {ex.Message}");
        }
    }
}
