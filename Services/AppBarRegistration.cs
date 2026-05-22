using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AIUsageMonitor.Interop;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

internal sealed class AppBarRegistration : IDisposable
{
    private IntPtr _registeredWindowHandle = IntPtr.Zero;

    public bool IsRegistered => _registeredWindowHandle != IntPtr.Zero;

    public bool TryRegister(Window window, OverlayEdgeSnap snapEdge, Rect intendedBoundsPixels)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || snapEdge == OverlayEdgeSnap.None)
        {
            return false;
        }

        if (_registeredWindowHandle != IntPtr.Zero && _registeredWindowHandle != handle)
        {
            Unregister(_registeredWindowHandle);
        }

        var appBarEdge = ToAppBarEdge(snapEdge);
        if (appBarEdge is null)
        {
            return false;
        }

        if (!WindowBoundsHelper.TryGetScreenBoundsPixels(window, out var barBounds))
        {
            barBounds = intendedBoundsPixels;
        }

        var data = new NativeMethods.AppBarData
        {
            cbSize = Marshal.SizeOf<NativeMethods.AppBarData>(),
            hWnd = handle,
            uEdge = appBarEdge.Value,
            rc = ToNativeRect(barBounds)
        };

        _ = NativeMethods.SHAppBarMessage(NativeMethods.AbmNew, ref data);
        _ = NativeMethods.SHAppBarMessage(NativeMethods.AbmQueryPos, ref data);
        _ = NativeMethods.SHAppBarMessage(NativeMethods.AbmSetPos, ref data);

        // Do not reposition the HWND here. The overlay is already docked; AppBar only
        // reserves desktop space. Moving the window to shell-adjusted coordinates offsets
        // it by the reserved width/height (past the strip into the new work area).
        _registeredWindowHandle = handle;
        return true;
    }

    public void Unregister(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        Unregister(handle);
    }

    public void Dispose()
    {
        if (_registeredWindowHandle != IntPtr.Zero)
        {
            Unregister(_registeredWindowHandle);
        }
    }

    private void Unregister(IntPtr handle)
    {
        var data = new NativeMethods.AppBarData
        {
            cbSize = Marshal.SizeOf<NativeMethods.AppBarData>(),
            hWnd = handle
        };

        _ = NativeMethods.SHAppBarMessage(NativeMethods.AbmRemove, ref data);
        if (_registeredWindowHandle == handle)
        {
            _registeredWindowHandle = IntPtr.Zero;
        }
    }

    private static int? ToAppBarEdge(OverlayEdgeSnap snapEdge)
    {
        return snapEdge switch
        {
            OverlayEdgeSnap.Left => NativeMethods.AbeLeft,
            OverlayEdgeSnap.Top => NativeMethods.AbeTop,
            OverlayEdgeSnap.Right => NativeMethods.AbeRight,
            OverlayEdgeSnap.Bottom => NativeMethods.AbeBottom,
            _ => null
        };
    }

    private static NativeMethods.RectNative ToNativeRect(Rect rect)
    {
        return new NativeMethods.RectNative
        {
            Left = (int)Math.Round(rect.Left),
            Top = (int)Math.Round(rect.Top),
            Right = (int)Math.Round(rect.Right),
            Bottom = (int)Math.Round(rect.Bottom)
        };
    }

}
