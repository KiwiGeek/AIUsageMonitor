using System.Windows;
using System.Windows.Interop;
using AIUsageMonitor.Interop;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

/// <summary>
/// Restores Win32 resize borders and hit-testing after frame changes (Fluent chrome, auto-hide collapse, snap).
/// </summary>
internal static class WindowResizeInteropService
{
    private const int GwlStyle = -16;

    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;

    private const int WmNcHitTest = NativeMethods.WmNcHitTest;

    private const int ResizeGripPixels = 10;

    public static void Attach(Window window, Func<OverlayEdgeSnap> getSnapEdge, Func<bool> isResizeSuppressed)
    {
        void AttachHook()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var source = HwndSource.FromHwnd(handle);
            source?.AddHook((hwnd, msg, wParam, lParam, ref handled) =>
                HitTestHook(hwnd, msg, lParam, getSnapEdge(), isResizeSuppressed(), ref handled));
        }

        if (window.IsLoaded)
        {
            AttachHook();
        }
        else
        {
            window.SourceInitialized += (_, _) => AttachHook();
        }
    }

    public static void EnsureResizableHostFrame(Window window)
    {
        if (window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLong(handle, GwlStyle);
        style &= ~WsPopup;
        style |= WsCaption | WsThickFrame;
        NativeMethods.SetWindowLong(handle, GwlStyle, style);
        RefreshFrame(handle);
    }

    private static IntPtr HitTestHook(
        IntPtr hwnd,
        int msg,
        IntPtr lParam,
        OverlayEdgeSnap snapEdge,
        bool isResizeSuppressed,
        ref bool handled)
    {
        if (msg != WmNcHitTest || isResizeSuppressed)
        {
            return IntPtr.Zero;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return IntPtr.Zero;
        }

        var x = (short)(lParam.ToInt32() & 0xFFFF);
        var y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
        var hit = MapPointToResizeHit(rect, x, y, snapEdge);
        if (hit is null)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return (IntPtr)hit.Value;
    }

    private static int? MapPointToResizeHit(NativeMethods.RectNative rect, int x, int y, OverlayEdgeSnap snapEdge)
    {
        var left = x >= rect.Left && x < rect.Left + ResizeGripPixels;
        var right = x < rect.Right && x >= rect.Right - ResizeGripPixels;
        var top = y >= rect.Top && y < rect.Top + ResizeGripPixels;
        var bottom = y < rect.Bottom && y >= rect.Bottom - ResizeGripPixels;

        switch (snapEdge)
        {
            case OverlayEdgeSnap.Left:
                left = false;
                break;
            case OverlayEdgeSnap.Right:
                right = false;
                break;
            case OverlayEdgeSnap.Top:
                top = false;
                break;
            case OverlayEdgeSnap.Bottom:
                bottom = false;
                break;
        }

        if (left && top)
        {
            return NativeMethods.HtTopLeft;
        }

        if (right && top)
        {
            return NativeMethods.HtTopRight;
        }

        if (left && bottom)
        {
            return NativeMethods.HtBottomLeft;
        }

        if (right && bottom)
        {
            return NativeMethods.HtBottomRight;
        }

        if (left)
        {
            return NativeMethods.HtLeft;
        }

        if (right)
        {
            return NativeMethods.HtRight;
        }

        if (top)
        {
            return NativeMethods.HtTop;
        }

        if (bottom)
        {
            return NativeMethods.HtBottom;
        }

        return null;
    }

    private static void RefreshFrame(IntPtr handle)
    {
        _ = NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTop,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoZOrder |
            NativeMethods.SwpFrameChanged |
            NativeMethods.SwpNoActivate);
    }
}
