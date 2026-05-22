using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.Views;

public partial class AboutWindow : FluentDialogWindow
{
    public AboutWindow()
    {
        InitializeComponent();
        TitleTextBlock.Text = AppMetadata.DisplayName;
        VersionTextBlock.Text = AppMetadata.VersionText;
    }

    private void LinkOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink { NavigateUri: not null } hyperlink)
        {
            OpenUrl(hyperlink.NavigateUri.ToString());
        }
    }

    private void CloseButtonOnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Could not open link: {ex.Message}",
                "About",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
