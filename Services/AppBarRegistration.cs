using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AIUsageMonitor.Interop;
using AIUsageMonitor.Models;
using WinForms = System.Windows.Forms;

namespace AIUsageMonitor.Services;

internal sealed class AppBarRegistration : IDisposable
{
    private IntPtr _registeredWindowHandle = IntPtr.Zero;

    public bool IsRegistered => _registeredWindowHandle != IntPtr.Zero;

    public bool TryRegister(
        Window window,
        OverlayEdgeSnap snapEdge,
        WinForms.Screen screen,
        Rect intendedBoundsPixels)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || snapEdge == OverlayEdgeSnap.None)
        {
            return false;
        }

        // Same HWND on a different edge still requires ABM_REMOVE before ABM_NEW.
        if (_registeredWindowHandle != IntPtr.Zero)
        {
            Unregister(_registeredWindowHandle);
        }

        var appBarEdge = ToAppBarEdge(snapEdge);
        if (appBarEdge is null)
        {
            return false;
        }

        var barBounds = WindowBoundsHelper.TryGetScreenBoundsPixels(window, out var currentBounds)
            ? currentBounds
            : intendedBoundsPixels;
        var proposal = OverlayEdgeSnapService.BuildAppBarProposalBoundsPixels(snapEdge, screen, barBounds);

        var data = new NativeMethods.AppBarData
        {
            cbSize = Marshal.SizeOf<NativeMethods.AppBarData>(),
            hWnd = handle,
            uEdge = appBarEdge.Value,
            rc = ToNativeRect(proposal)
        };

        _ = NativeMethods.SHAppBarMessage(NativeMethods.AbmNew, ref data);
        _ = NativeMethods.SHAppBarMessage(NativeMethods.AbmQueryPos, ref data);
        _ = NativeMethods.SHAppBarMessage(NativeMethods.AbmSetPos, ref data);

        var negotiated = FromNativeRect(data.rc);
        if (negotiated.Width > 0 &&
            negotiated.Height > 0 &&
            WindowBoundsHelper.TryGetScreenBoundsPixels(window, out var actualBounds))
        {
            var targetBounds = MergeNegotiatedAppBarBounds(snapEdge, actualBounds, negotiated);
            if (WindowBoundsHelper.TryGetWindowBoundsMismatchPixels(window, targetBounds, tolerancePixels: 2))
            {
                WindowBoundsHelper.SetBoundsFromScreenPixels(
                    window,
                    targetBounds,
                    enforceMinimumSize: true,
                    syncWpfBounds: true);
            }
        }

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

    private static Rect FromNativeRect(NativeMethods.RectNative rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    /// <summary>
    /// Apply shell thickness on the dock axis while preserving user position on the free axis.
    /// </summary>
    private static Rect MergeNegotiatedAppBarBounds(OverlayEdgeSnap snapEdge, Rect actual, Rect negotiated) =>
        snapEdge switch
        {
            OverlayEdgeSnap.Left => new Rect(negotiated.Left, actual.Top, negotiated.Width, actual.Height),
            OverlayEdgeSnap.Right => new Rect(negotiated.Left, actual.Top, negotiated.Width, actual.Height),
            OverlayEdgeSnap.Top => new Rect(actual.Left, negotiated.Top, actual.Width, negotiated.Height),
            OverlayEdgeSnap.Bottom => new Rect(actual.Left, negotiated.Top, actual.Width, negotiated.Height),
            _ => negotiated
        };
}
