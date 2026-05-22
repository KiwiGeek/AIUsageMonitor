using System.Diagnostics;
using System.Windows.Navigation;
using AIUsageMonitor.ViewModels;

namespace AIUsageMonitor.Views;

public partial class DeepSeekSettingsWindow : FluentDialogWindow
{
    public DeepSeekSettingsWindow(DeepSeekSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void HyperlinkOnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
