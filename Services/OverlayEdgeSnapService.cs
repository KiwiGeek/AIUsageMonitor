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

    /// <summary>
    /// Stub until Windows exposes per-monitor taskbar placement. Assumes bottom taskbar for now.
    /// </summary>
    public static bool IsTaskbarAtBottom(WinForms.Screen screen)
    {
        _ = screen;
        return true;
    }

    /// <summary>
    /// Auto-hide is disabled on the bottom edge while the taskbar occupies the screen bottom,
    /// because reveal would otherwise trigger on the work-area seam above the taskbar.
    /// </summary>
    public static bool IsSnapAutoHidePermitted(OverlayEdgeSnap snapEdge, WinForms.Screen screen)
    {
        if (snapEdge == OverlayEdgeSnap.None)
        {
            return false;
        }

        if (snapEdge == OverlayEdgeSnap.Bottom && IsTaskbarAtBottom(screen))
        {
            return false;
        }

        return true;
    }

    public static Rect GetVirtualDesktopWorkingAreaPixels()
    {
        var bounds = GetVirtualDesktopWorkingAreaBounds();
        return new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    public static bool IsPointerInSnapAutoHideRevealZone(
        DrawingPoint pointerPixels,
        OverlayEdgeSnap snapEdge,
        WinForms.Screen snapScreen,
        Rect dockedBoundsPixels,
        double revealZonePixels)
    {
        if (snapEdge == OverlayEdgeSnap.None)
        {
            return false;
        }

        var monitorArea = WindowBoundsHelper.GetWorkingAreaPixels(snapScreen);
        var virtualArea = GetVirtualDesktopWorkingAreaPixels();
        var zone = Math.Max(1, revealZonePixels);
        var dockRight = Math.Max(dockedBoundsPixels.Right, monitorArea.Right);

        return snapEdge switch
        {
            OverlayEdgeSnap.Left when IsPerimeterEdge(monitorArea, snapEdge, ToVirtualBounds(virtualArea)) =>
                pointerPixels.X >= monitorArea.Left &&
                pointerPixels.X <= monitorArea.Left + zone &&
                pointerPixels.Y >= monitorArea.Top &&
                pointerPixels.Y <= monitorArea.Bottom,
            OverlayEdgeSnap.Right when IsPerimeterEdge(monitorArea, snapEdge, ToVirtualBounds(virtualArea)) =>
                pointerPixels.X <= dockRight &&
                pointerPixels.X >= dockRight - zone &&
                pointerPixels.Y >= monitorArea.Top &&
                pointerPixels.Y <= monitorArea.Bottom,
            OverlayEdgeSnap.Top when IsPerimeterEdge(monitorArea, snapEdge, ToVirtualBounds(virtualArea)) =>
                pointerPixels.Y >= monitorArea.Top &&
                pointerPixels.Y <= monitorArea.Top + zone &&
                pointerPixels.X >= monitorArea.Left &&
                pointerPixels.X <= monitorArea.Right,
            OverlayEdgeSnap.Bottom when IsPerimeterEdge(monitorArea, snapEdge, ToVirtualBounds(virtualArea)) &&
                IsSnapAutoHidePermitted(snapEdge, snapScreen) =>
                IsPointerInBottomAutoHideRevealZone(pointerPixels, snapScreen, zone),
            _ => false
        };
    }

    /// <summary>
    /// Bottom reveal uses the physical screen bottom (over the taskbar), not the work-area seam.
    /// </summary>
    private static bool IsPointerInBottomAutoHideRevealZone(
        DrawingPoint pointerPixels,
        WinForms.Screen snapScreen,
        double revealZonePixels)
    {
        var screenBounds = WindowBoundsHelper.GetBoundsPixels(snapScreen);
        return pointerPixels.Y <= screenBounds.Bottom &&
               pointerPixels.Y >= screenBounds.Bottom - revealZonePixels &&
               pointerPixels.X >= screenBounds.Left &&
               pointerPixels.X <= screenBounds.Right;
    }

    private static VirtualDesktopBounds ToVirtualBounds(Rect virtualArea) =>
        new(virtualArea.Left, virtualArea.Top, virtualArea.Right, virtualArea.Bottom);

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
        var workArea = WindowBoundsHelper.GetWorkingAreaPixels(screen);
        var visible = Math.Max(1, visibleStripPixels);

        var dockRight = Math.Max(fullBoundsPixels.Right, workArea.Right);

        return snapEdge switch
        {
            OverlayEdgeSnap.Left => new Rect(workArea.Left, workArea.Top, visible, workArea.Height),
            OverlayEdgeSnap.Right => new Rect(
                dockRight - visible,
                workArea.Top,
                visible,
                workArea.Height),
            OverlayEdgeSnap.Top => new Rect(workArea.Left, workArea.Top, workArea.Width, visible),
            OverlayEdgeSnap.Bottom => new Rect(
                workArea.Left,
                workArea.Bottom - visible,
                workArea.Width,
                visible),
            _ => GetSnappedDockBoundsPixels(snapEdge, screen, fullBoundsPixels)
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
