using System.Globalization;
using System.Windows;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Views;

public partial class DeepSeekPeakWindow : FluentDialogWindow
{
    private readonly string _currency;

    public DeepSeekPeakWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings.Clone();

        _currency = settings.DeepSeekLastBalances.Keys.FirstOrDefault() ?? "USD";
        CurrencyLabel.Text = $"Peak balance ({_currency})";

        if (settings.DeepSeekLastBalances.TryGetValue(_currency, out var peak) && peak > 0)
        {
            PeakTextBox.Text = peak.ToString("0.00", CultureInfo.InvariantCulture);
        }

        PeakTextBox.SelectAll();
        PeakTextBox.Focus();
    }

    public AppSettings Settings { get; private set; }

    private void SaveButtonOnClick(object sender, RoutedEventArgs e)
    {
        var text = PeakTextBox.Text.Trim().TrimStart('$');

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var peak) || peak <= 0)
        {
            ValidationTextBlock.Text = "Enter a valid positive amount (e.g. 20.00).";
            return;
        }

        Settings.DeepSeekLastBalances[_currency] = peak;
        DialogResult = true;
    }

    private void CancelButtonOnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
