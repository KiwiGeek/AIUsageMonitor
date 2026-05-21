using System.Windows;
using AIUsageMonitor.Models;
using WinForms = System.Windows.Forms;

namespace AIUsageMonitor.Services;

internal static class OverlayEdgeSnapService
{
    private const double SnapThresholdPixels = 40;

    public static bool TryGetSnapEdge(Window window, out OverlayEdgeSnap snapEdge, out WinForms.Screen? screen)
    {
        snapEdge = OverlayEdgeSnap.None;
        screen = WindowBoundsHelper.GetScreenForWindow(window);
        if (screen is null || !WindowBoundsHelper.TryGetScreenBoundsPixels(window, out var windowBounds))
        {
            return false;
        }

        var workArea = WindowBoundsHelper.GetWorkingAreaPixels(screen);
        var distances = new Dictionary<OverlayEdgeSnap, double>
        {
            [OverlayEdgeSnap.Left] = Math.Abs(windowBounds.Left - workArea.Left),
            [OverlayEdgeSnap.Top] = Math.Abs(windowBounds.Top - workArea.Top),
            [OverlayEdgeSnap.Right] = Math.Abs(workArea.Right - windowBounds.Right),
            [OverlayEdgeSnap.Bottom] = Math.Abs(workArea.Bottom - windowBounds.Bottom)
        };

        var best = distances
            .OrderBy(pair => pair.Value)
            .First();

        if (best.Value > SnapThresholdPixels)
        {
            return false;
        }

        snapEdge = best.Key;
        return true;
    }

    public static Rect GetSnappedBoundsPixels(WinForms.Screen screen)
    {
        return WindowBoundsHelper.GetWorkingAreaPixels(screen);
    }

    public static WinForms.Screen? FindScreenByDeviceName(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }

        return WinForms.Screen.AllScreens.FirstOrDefault(screen =>
            string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
    }

    public static void ApplySnap(Window window, OverlayEdgeSnap snapEdge, WinForms.Screen screen, AppBarRegistration appBar)
    {
        var bounds = GetSnappedBoundsPixels(screen);
        appBar.TryRegister(window, snapEdge, bounds);
    }

    public static void ClearSnap(Window window, AppBarRegistration appBar)
    {
        appBar.Unregister(window);
    }
}
