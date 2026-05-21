using System.Windows;
using AIUsageMonitor.Models;
using WinForms = System.Windows.Forms;

namespace AIUsageMonitor.Services;

internal static class OverlayEdgeSnapService
{
    private const double SnapThresholdPixels = 40;

    public static bool TryGetSnapEdge(
        Rect proposedBoundsPixels,
        out OverlayEdgeSnap snapEdge,
        out WinForms.Screen? screen)
    {
        snapEdge = OverlayEdgeSnap.None;
        var centerX = (int)Math.Round(proposedBoundsPixels.Left + (proposedBoundsPixels.Width / 2));
        var centerY = (int)Math.Round(proposedBoundsPixels.Top + (proposedBoundsPixels.Height / 2));
        screen = WinForms.Screen.FromPoint(new System.Drawing.Point(centerX, centerY));

        var workArea = WindowBoundsHelper.GetWorkingAreaPixels(screen);
        var distances = new Dictionary<OverlayEdgeSnap, double>
        {
            [OverlayEdgeSnap.Left] = Math.Abs(proposedBoundsPixels.Left - workArea.Left),
            [OverlayEdgeSnap.Top] = Math.Abs(proposedBoundsPixels.Top - workArea.Top),
            [OverlayEdgeSnap.Right] = Math.Abs(workArea.Right - proposedBoundsPixels.Right),
            [OverlayEdgeSnap.Bottom] = Math.Abs(workArea.Bottom - proposedBoundsPixels.Bottom)
        };

        var minDistance = distances.Values.Min();
        if (minDistance > SnapThresholdPixels)
        {
            screen = null;
            return false;
        }

        snapEdge = distances
            .Where(pair => pair.Value <= minDistance + 0.5)
            .Select(pair => pair.Key)
            .OrderBy(GetSnapEdgeTieBreakOrder)
            .First();

        return true;
    }

    private static int GetSnapEdgeTieBreakOrder(OverlayEdgeSnap snapEdge)
    {
        return snapEdge switch
        {
            OverlayEdgeSnap.Top => 0,
            OverlayEdgeSnap.Bottom => 1,
            OverlayEdgeSnap.Left => 2,
            OverlayEdgeSnap.Right => 3,
            _ => 4
        };
    }

    public static bool TryGetSnapEdge(Window window, out OverlayEdgeSnap snapEdge, out WinForms.Screen? screen)
    {
        if (!WindowBoundsHelper.TryGetScreenBoundsPixels(window, out var windowBounds))
        {
            snapEdge = OverlayEdgeSnap.None;
            screen = null;
            return false;
        }

        return TryGetSnapEdge(windowBounds, out snapEdge, out screen);
    }

    public static Rect GetSnappedAppBarBoundsPixels(
        OverlayEdgeSnap snapEdge,
        WinForms.Screen screen,
        Rect currentBoundsPixels)
    {
        var workArea = WindowBoundsHelper.GetWorkingAreaPixels(screen);

        return snapEdge switch
        {
            OverlayEdgeSnap.Left => new Rect(workArea.Left, workArea.Top, currentBoundsPixels.Width, workArea.Height),
            OverlayEdgeSnap.Right => new Rect(
                workArea.Right - currentBoundsPixels.Width,
                workArea.Top,
                currentBoundsPixels.Width,
                workArea.Height),
            OverlayEdgeSnap.Top => new Rect(workArea.Left, workArea.Top, workArea.Width, currentBoundsPixels.Height),
            OverlayEdgeSnap.Bottom => new Rect(
                workArea.Left,
                workArea.Bottom - currentBoundsPixels.Height,
                workArea.Width,
                currentBoundsPixels.Height),
            _ => currentBoundsPixels
        };
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

    public static void ApplySnap(
        Window window,
        OverlayEdgeSnap snapEdge,
        WinForms.Screen screen,
        Rect appBarBoundsPixels,
        AppBarRegistration appBar)
    {
        appBar.TryRegister(window, snapEdge, appBarBoundsPixels);
    }

    public static void ClearSnap(Window window, AppBarRegistration appBar)
    {
        appBar.Unregister(window);
    }
}
