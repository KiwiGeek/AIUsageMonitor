using System.Windows;
using DrawingPoint = System.Drawing.Point;
using AIUsageMonitor.Models;
using WinForms = System.Windows.Forms;

namespace AIUsageMonitor.Services;

internal static class OverlayEdgeSnapService
{
    private const double SnapThresholdPixels = 40;
    private const double PerimeterTolerancePixels = 2;

    public static bool TryGetSnapEdge(
        DrawingPoint pointerPixels,
        out OverlayEdgeSnap snapEdge,
        out WinForms.Screen? screen)
    {
        snapEdge = OverlayEdgeSnap.None;
        screen = WinForms.Screen.FromPoint(pointerPixels);

        var workArea = WindowBoundsHelper.GetWorkingAreaPixels(screen);
        var virtualBounds = GetVirtualDesktopWorkingAreaBounds();
        var distances = new Dictionary<OverlayEdgeSnap, double>();

        if (IsPerimeterEdge(workArea, OverlayEdgeSnap.Left, virtualBounds))
        {
            distances[OverlayEdgeSnap.Left] = Math.Abs(pointerPixels.X - workArea.Left);
        }

        if (IsPerimeterEdge(workArea, OverlayEdgeSnap.Top, virtualBounds))
        {
            distances[OverlayEdgeSnap.Top] = Math.Abs(pointerPixels.Y - workArea.Top);
        }

        if (IsPerimeterEdge(workArea, OverlayEdgeSnap.Right, virtualBounds))
        {
            distances[OverlayEdgeSnap.Right] = Math.Abs(workArea.Right - pointerPixels.X);
        }

        if (IsPerimeterEdge(workArea, OverlayEdgeSnap.Bottom, virtualBounds))
        {
            distances[OverlayEdgeSnap.Bottom] = Math.Abs(workArea.Bottom - pointerPixels.Y);
        }

        if (distances.Count == 0)
        {
            screen = null;
            return false;
        }

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

    public static bool IsValidSnapEdge(WinForms.Screen screen, OverlayEdgeSnap snapEdge)
    {
        if (snapEdge == OverlayEdgeSnap.None)
        {
            return false;
        }

        var workArea = WindowBoundsHelper.GetWorkingAreaPixels(screen);
        var virtualBounds = GetVirtualDesktopWorkingAreaBounds();
        return IsPerimeterEdge(workArea, snapEdge, virtualBounds);
    }

    private static VirtualDesktopBounds GetVirtualDesktopWorkingAreaBounds()
    {
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;

        foreach (var monitor in WinForms.Screen.AllScreens)
        {
            var area = WindowBoundsHelper.GetWorkingAreaPixels(monitor);
            left = Math.Min(left, area.Left);
            top = Math.Min(top, area.Top);
            right = Math.Max(right, area.Right);
            bottom = Math.Max(bottom, area.Bottom);
        }

        return new VirtualDesktopBounds(left, top, right, bottom);
    }

    private static bool IsPerimeterEdge(
        Rect workArea,
        OverlayEdgeSnap snapEdge,
        VirtualDesktopBounds virtualBounds)
    {
        return snapEdge switch
        {
            OverlayEdgeSnap.Left => Math.Abs(workArea.Left - virtualBounds.Left) <= PerimeterTolerancePixels,
            OverlayEdgeSnap.Top => Math.Abs(workArea.Top - virtualBounds.Top) <= PerimeterTolerancePixels,
            OverlayEdgeSnap.Right => Math.Abs(workArea.Right - virtualBounds.Right) <= PerimeterTolerancePixels,
            OverlayEdgeSnap.Bottom => Math.Abs(workArea.Bottom - virtualBounds.Bottom) <= PerimeterTolerancePixels,
            _ => false
        };
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

    /// <summary>
    /// AppBar reservation uses the overlay's current screen rectangle. Do not re-anchor to
    /// <see cref="WindowBoundsHelper.GetWorkingAreaPixels"/> here; after registration the work
    /// area already shrinks and re-anchoring would shift the window into the new inset.
    /// </summary>
    public static Rect GetSnappedAppBarBoundsPixels(
        OverlayEdgeSnap snapEdge,
        WinForms.Screen screen,
        Rect currentBoundsPixels) =>
        currentBoundsPixels;

    /// <summary>
    /// Aligns the snapped strip on its dock edge while preserving size. Uses the window's
    /// current dock-edge coordinate (or <paramref name="dockAnchorBoundsPixels"/> when set).
    /// Do not use <see cref="WindowBoundsHelper.GetWorkingAreaPixels"/> for the dock axis:
    /// after AppBar registration the work area inset moves and would double-offset the HWND.
    /// </summary>
    public static Rect GetSnappedDockBoundsPixels(
        OverlayEdgeSnap snapEdge,
        WinForms.Screen screen,
        Rect currentBoundsPixels,
        Rect? dockAnchorBoundsPixels = null)
    {
        var workArea = WindowBoundsHelper.GetWorkingAreaPixels(screen);
        var width = Math.Clamp(currentBoundsPixels.Width, 1, workArea.Width);
        var height = Math.Clamp(currentBoundsPixels.Height, 1, workArea.Height);
        var anchor = dockAnchorBoundsPixels ?? currentBoundsPixels;

        return snapEdge switch
        {
            OverlayEdgeSnap.Left => new Rect(
                anchor.Left,
                ClampVerticalPosition(currentBoundsPixels.Top, height, workArea),
                width,
                height),
            OverlayEdgeSnap.Right => new Rect(
                anchor.Right - width,
                ClampVerticalPosition(currentBoundsPixels.Top, height, workArea),
                width,
                height),
            OverlayEdgeSnap.Top => new Rect(
                ClampHorizontalPosition(currentBoundsPixels.Left, width, workArea),
                anchor.Top,
                width,
                height),
            OverlayEdgeSnap.Bottom => new Rect(
                ClampHorizontalPosition(currentBoundsPixels.Left, width, workArea),
                anchor.Bottom - height,
                width,
                height),
            _ => currentBoundsPixels
        };
    }

    public static Rect GetSnapAutoHideCollapsedBoundsPixels(
        OverlayEdgeSnap snapEdge,
        WinForms.Screen screen,
        Rect fullBoundsPixels,
        double visibleStripPixels)
    {
        var docked = GetSnappedDockBoundsPixels(snapEdge, screen, fullBoundsPixels);
        var visible = Math.Max(1, visibleStripPixels);

        return snapEdge switch
        {
            OverlayEdgeSnap.Left => new Rect(docked.Left, docked.Top, visible, docked.Height),
            OverlayEdgeSnap.Right => new Rect(docked.Right - visible, docked.Top, visible, docked.Height),
            OverlayEdgeSnap.Top => new Rect(docked.Left, docked.Top, docked.Width, visible),
            OverlayEdgeSnap.Bottom => new Rect(docked.Left, docked.Bottom - visible, docked.Width, visible),
            _ => docked
        };
    }

    private static double ClampVerticalPosition(double top, double height, Rect workArea)
    {
        var maxTop = workArea.Bottom - height;
        return Math.Clamp(top, workArea.Top, maxTop);
    }

    private static double ClampHorizontalPosition(double left, double width, Rect workArea)
    {
        var maxLeft = workArea.Right - width;
        return Math.Clamp(left, workArea.Left, maxLeft);
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

    private readonly record struct VirtualDesktopBounds(double Left, double Top, double Right, double Bottom);
}
