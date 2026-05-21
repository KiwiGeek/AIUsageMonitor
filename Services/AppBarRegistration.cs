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

        var data = new NativeMethods.AppBarData
        {
            cbSize = Marshal.SizeOf<NativeMethods.AppBarData>(),
            hWnd = handle,
            uEdge = appBarEdge.Value,
            rc = ToNativeRect(intendedBoundsPixels)
        };

        _ = NativeMethods.SHAppBarMessage(NativeMethods.AbmNew, ref data);
        _ = NativeMethods.SHAppBarMessage(NativeMethods.AbmQueryPos, ref data);
        _ = NativeMethods.SHAppBarMessage(NativeMethods.AbmSetPos, ref data);

        var shellBounds = FromNativeRect(data.rc);
        var mergedBounds = MergeAppBarBounds(snapEdge, intendedBoundsPixels, shellBounds);
        data.rc = ToNativeRect(mergedBounds);
        _ = NativeMethods.SHAppBarMessage(NativeMethods.AbmSetPos, ref data);

        WindowBoundsHelper.SetBoundsFromScreenPixels(window, mergedBounds);
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

    private static Rect MergeAppBarBounds(OverlayEdgeSnap snapEdge, Rect intended, Rect shellAdjusted)
    {
        return snapEdge switch
        {
            OverlayEdgeSnap.Left => new Rect(
                shellAdjusted.Left,
                shellAdjusted.Top,
                intended.Width,
                shellAdjusted.Height),
            OverlayEdgeSnap.Right => new Rect(
                shellAdjusted.Right - intended.Width,
                shellAdjusted.Top,
                intended.Width,
                shellAdjusted.Height),
            OverlayEdgeSnap.Top => new Rect(
                shellAdjusted.Left,
                shellAdjusted.Top,
                shellAdjusted.Width,
                intended.Height),
            OverlayEdgeSnap.Bottom => new Rect(
                shellAdjusted.Left,
                shellAdjusted.Bottom - intended.Height,
                shellAdjusted.Width,
                intended.Height),
            _ => intended
        };
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

    private static Rect FromNativeRect(NativeMethods.RectNative rect)
    {
        return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
}
