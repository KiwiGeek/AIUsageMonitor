using System.Windows;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings.Clone();
        UpdateIntervalTextBox.Text = Settings.UpdateIntervalMinutes.ToString();
        CursorApiKeyTextBox.Text = Settings.CursorApiKey;
        CursorBudgetTextBox.Text = Settings.CursorIncludedBudgetDollars.ToString("0.##");
        ClaudeStatusExporterCheckBox.IsChecked = Settings.ClaudeStatusExporterEnabled;
        AutoRunAtLoginCheckBox.IsChecked = Settings.AutoRunAtLoginEnabled || Services.AutoRunService.IsEnabled();
        UpdateIntervalTextBox.SelectAll();
        UpdateIntervalTextBox.Focus();
    }

    public AppSettings Settings { get; private set; }

    public bool OpenCursorLoginRequested { get; private set; }

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

        Settings.UpdateIntervalMinutes = minutes;
        Settings.CursorApiKey = CursorApiKeyTextBox.Text.Trim();
        Settings.CursorIncludedBudgetDollars = cursorBudgetDollars;
        Settings.ClaudeStatusExporterEnabled = ClaudeStatusExporterCheckBox.IsChecked == true;
        Settings.AutoRunAtLoginEnabled = AutoRunAtLoginCheckBox.IsChecked == true;
        Settings.Normalize();
        DialogResult = true;
    }

    private void CursorLoginButtonOnClick(object sender, RoutedEventArgs e)
    {
        Settings.UpdateIntervalMinutes = int.TryParse(UpdateIntervalTextBox.Text.Trim(), out var minutes)
            ? Math.Clamp(minutes, AppSettings.MinimumUpdateIntervalMinutes, AppSettings.MaximumUpdateIntervalMinutes)
            : Settings.UpdateIntervalMinutes;
        Settings.CursorApiKey = CursorApiKeyTextBox.Text.Trim();
        if (double.TryParse(CursorBudgetTextBox.Text.Trim(), out var cursorBudgetDollars) && cursorBudgetDollars > 0)
        {
            Settings.CursorIncludedBudgetDollars = cursorBudgetDollars;
        }

        Settings.ClaudeStatusExporterEnabled = ClaudeStatusExporterCheckBox.IsChecked == true;
        Settings.AutoRunAtLoginEnabled = AutoRunAtLoginCheckBox.IsChecked == true;
        Settings.Normalize();
        OpenCursorLoginRequested = true;
        DialogResult = true;
    }

    private void CancelButtonOnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
