using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Views;

public partial class DeepSeekSetupWindow : FluentAppWindow
{
    public DeepSeekSetupWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings.Clone();
        ApiKeyTextBox.Text = Settings.DeepSeekApiKey;
        ApiKeyTextBox.SelectAll();
        ApiKeyTextBox.Focus();
    }

    public AppSettings Settings { get; private set; }

    private void SaveButtonOnClick(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ValidationTextBlock.Text = "Enter a DeepSeek API key.";
            return;
        }

        Settings.DeepSeekApiKey = apiKey;
        DialogResult = true;
    }

    private void CancelButtonOnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void HyperlinkOnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
