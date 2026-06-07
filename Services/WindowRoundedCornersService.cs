using System.Windows;
using System.Windows.Interop;
using AIUsageMonitor.Interop;
using Wpf.Ui.Controls;

namespace AIUsageMonitor.Services;

internal static class WindowRoundedCornersService
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int DwmwcpRoundSmall = 3;

    private const int DwmwcpDoNotRound = 1;

    public static void Apply(Window window, bool squareCorners = false)
    {
        if (window is not FluentWindow fluentWindow)
        {
            return;
        }

        if (squareCorners)
        {
            fluentWindow.WindowCornerPreference = WindowCornerPreference.DoNotRound;
        }
        else
        {
            var preferSmall = window.ActualHeight < 180 || window.ActualWidth < 420;
            fluentWindow.WindowCornerPreference = preferSmall
                ? WindowCornerPreference.RoundSmall
                : WindowCornerPreference.Round;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var preference = squareCorners
            ? DwmwcpDoNotRound
            : window.ActualHeight < 180 || window.ActualWidth < 420
                ? DwmwcpRoundSmall
                : DwmwcpRound;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle,
            DwmwaWindowCornerPreference,
            ref preference,
            sizeof(int));
    }
}
