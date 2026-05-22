using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AIUsageMonitor.Interop;
using Wpf.Ui.Controls;

namespace AIUsageMonitor.Views;

/// <summary>
/// Settings and setup dialogs: Mica chrome with only a close button (no minimize/maximize).
/// </summary>
public class FluentDialogWindow : FluentAppWindow
{
    private const int GwlStyle = -16;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsMaximizeBox = 0x00010000;

    protected FluentDialogWindow()
    {
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource { Handle: var handle })
        {
            return;
        }

        var style = NativeMethods.GetWindowLong(handle, GwlStyle);
        NativeMethods.SetWindowLong(handle, GwlStyle, style & ~WsMinimizeBox & ~WsMaximizeBox);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        HideTitleBarMinimizeAndMaximize(this);
    }

    private static void HideTitleBarMinimizeAndMaximize(DependencyObject node)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is TitleBar titleBar)
            {
                titleBar.ShowMinimize = false;
                titleBar.ShowMaximize = false;
            }

            HideTitleBarMinimizeAndMaximize(child);
        }
    }
}
