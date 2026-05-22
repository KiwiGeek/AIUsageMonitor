using System.Diagnostics;
using System.Windows.Navigation;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.Views;

public partial class GeminiSettingsWindow : FluentDialogWindow
{
    public GeminiSettingsWindow(AppSettingsStore store)
    {
        InitializeComponent();
        DataContext = store;
    }

    private void HyperlinkOnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
