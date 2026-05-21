using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AIUsageMonitor.Interop;

namespace AIUsageMonitor.Services;

/// <summary>
/// Blocks Windows Aero Snap / Snap Assist from resizing or moving the window.
/// There is no documented per-window "unregister from snap" API; this uses
/// WM_WINDOWPOSCHANGING plus an optional Win11 DWM attribute when available.
/// </summary>
internal sealed class WindowsSnapSuppression
{
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int WmWindowPosChanging = 0x0046;
    private const int DwmwaExcludedFromSnap = 34;

    private readonly Func<bool> _allowSystemPositionChanges;
    private HwndSourceHook? _hook;
    private bool _inSystemSizeMoveLoop;

    private WindowsSnapSuppression(Func<bool> allowSystemPositionChanges)
    {
        _allowSystemPositionChanges = allowSystemPositionChanges;
    }

    public static void Attach(Window window, Func<bool> allowSystemPositionChanges)
    {
        var suppression = new WindowsSnapSuppression(allowSystemPositionChanges);
        if (window.IsLoaded)
        {
            suppression.TryAttachHook(window);
        }
        else
        {
            window.SourceInitialized += (_, _) => suppression.TryAttachHook(window);
        }
    }

    private void TryAttachHook(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var source = HwndSource.FromHwnd(handle);
        if (source is null || _hook is not null)
        {
            return;
        }

        TryExcludeFromSnap(handle);
        _hook = WndProc;
        source.AddHook(_hook);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmEnterSizeMove:
                _inSystemSizeMoveLoop = true;
                break;
            case WmExitSizeMove:
                _inSystemSizeMoveLoop = false;
                break;
            case WmWindowPosChanging when ShouldBlockWindowPosChange():
                BlockWindowPosChange(lParam, ref handled);
                break;
        }

        return IntPtr.Zero;
    }

    private bool ShouldBlockWindowPosChange()
    {
        if (_allowSystemPositionChanges() || _inSystemSizeMoveLoop)
        {
            return false;
        }

        return true;
    }

    private static void BlockWindowPosChange(IntPtr lParam, ref bool handled)
    {
        var windowPos = Marshal.PtrToStructure<WindowPos>(lParam);
        windowPos.flags |= WindowPosFlags.Nosize;
        windowPos.flags |= WindowPosFlags.Nomove;
        Marshal.StructureToPtr(windowPos, lParam, true);
        handled = true;
    }

    private static void TryExcludeFromSnap(IntPtr hwnd)
    {
        try
        {
            var exclude = 1;
            _ = NativeMethods.DwmSetWindowAttribute(
                hwnd,
                DwmwaExcludedFromSnap,
                ref exclude,
                Marshal.SizeOf<int>());
        }
        catch (DllNotFoundException)
        {
            // dwmapi unavailable
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows builds may not export the attribute.
        }
    }
}
