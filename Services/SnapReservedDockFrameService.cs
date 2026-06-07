using System.Windows;
using System.Windows.Interop;
using AIUsageMonitor.Interop;
using Wpf.Ui.Controls;

namespace AIUsageMonitor.Services;

/// <summary>
/// Removes the visible Win32/DWM border on docked edges while screen space is reserved.
/// Resize on the free axis is handled by <see cref="WindowResizeInteropService"/>.
/// </summary>
internal static class SnapReservedDockFrameService
{
    private const int GwlStyle = -16;

    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int WsBorder = 0x00800000;

    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmnCrpDisabled = 1;
    private const int DwmnCrpUseWindowStyle = 0;

    internal sealed class SavedFrameState
    {
        public bool IsActive { get; set; }
        public bool HasHostStyle { get; set; }
        public int HostStyle { get; set; }
        public WindowStyle WindowStyle { get; set; }
        public bool ExtendsContentIntoTitleBar { get; set; }
        public WindowBackdropType WindowBackdropType { get; set; }
        public WindowCornerPreference WindowCornerPreference { get; set; }
    }

    public static void SyncHostFrame(Window window, SavedFrameState state, bool borderlessDock)
    {
        if (borderlessDock)
        {
            ApplyBorderlessDockFrame(window, state);
            return;
        }

        RestoreStandardFrame(window, state);
    }

    public static void RestoreStandardFrame(Window window, SavedFrameState state)
    {
        if (!state.IsActive)
        {
            WindowResizeInteropService.EnsureResizableHostFrame(window);
            return;
        }

        ResetDockDwmAttributes(window);
        RestoreHostStyle(window, state);
        RestoreFluentChrome(window, state);
        window.WindowStyle = state.WindowStyle;
        state.IsActive = false;
        WindowResizeInteropService.EnsureResizableHostFrame(window);
        RefreshHostFrame(window);
    }

    private static void ApplyBorderlessDockFrame(Window window, SavedFrameState state)
    {
        if (!state.IsActive)
        {
            CaptureFluentChrome(window, state);
            state.WindowStyle = window.WindowStyle;
            window.WindowStyle = WindowStyle.None;
            state.IsActive = true;
        }

        SuppressFluentChrome(window);
        ApplyBorderlessHostStyle(window, state);
        ApplyDockDwmAttributes(window);
        RefreshHostFrame(window);
    }

    private static void CaptureFluentChrome(Window window, SavedFrameState state)
    {
        if (window is not FluentWindow fluentWindow)
        {
            return;
        }

        state.ExtendsContentIntoTitleBar = fluentWindow.ExtendsContentIntoTitleBar;
        state.WindowBackdropType = fluentWindow.WindowBackdropType;
        state.WindowCornerPreference = fluentWindow.WindowCornerPreference;
    }

    private static void SuppressFluentChrome(Window window)
    {
        if (window is not FluentWindow fluentWindow)
        {
            return;
        }

        fluentWindow.ExtendsContentIntoTitleBar = false;
        fluentWindow.WindowCornerPreference = WindowCornerPreference.DoNotRound;
        WindowBackdrop.RemoveBackdrop(window);
        fluentWindow.WindowBackdropType = WindowBackdropType.None;
    }

    private static void RestoreFluentChrome(Window window, SavedFrameState state)
    {
        if (window is not FluentWindow fluentWindow)
        {
            return;
        }

        fluentWindow.ExtendsContentIntoTitleBar = state.ExtendsContentIntoTitleBar;
        fluentWindow.WindowCornerPreference = state.WindowCornerPreference;

        WindowBackdrop.RemoveBackdrop(window);
        fluentWindow.WindowBackdropType = state.WindowBackdropType;
        _ = WindowBackdrop.ApplyBackdrop(window, state.WindowBackdropType);
    }

    private static void ApplyBorderlessHostStyle(Window window, SavedFrameState state)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (!state.HasHostStyle)
        {
            state.HostStyle = NativeMethods.GetWindowLong(handle, GwlStyle);
            state.HasHostStyle = true;
        }

        var style = NativeMethods.GetWindowLong(handle, GwlStyle);
        style &= ~(WsCaption | WsThickFrame | WsBorder);
        NativeMethods.SetWindowLong(handle, GwlStyle, style);
    }

    private static void RestoreHostStyle(Window window, SavedFrameState state)
    {
        if (!state.HasHostStyle)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowLong(handle, GwlStyle, state.HostStyle);
        state.HasHostStyle = false;
    }

    private static void ApplyDockDwmAttributes(IntPtr handle)
    {
        var margins = new NativeMethods.Margins
        {
            Left = -1,
            Right = -1,
            Top = -1,
            Bottom = -1
        };
        _ = NativeMethods.DwmExtendFrameIntoClientArea(handle, ref margins);

        var borderColor = NativeMethods.DwmwaColorNone;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle,
            NativeMethods.DwmwaBorderColor,
            ref borderColor,
            sizeof(int));

        var ncPolicy = DwmnCrpDisabled;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle,
            DwmwaNcRenderingPolicy,
            ref ncPolicy,
            sizeof(int));
    }

    private static void ApplyDockDwmAttributes(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        ApplyDockDwmAttributes(handle);
    }

    private static void ResetDockDwmAttributes(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var reset = default(NativeMethods.Margins);
        _ = NativeMethods.DwmExtendFrameIntoClientArea(handle, ref reset);

        var defaultBorder = NativeMethods.DwmwaColorDefault;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle,
            NativeMethods.DwmwaBorderColor,
            ref defaultBorder,
            sizeof(int));

        var ncPolicy = DwmnCrpUseWindowStyle;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle,
            DwmwaNcRenderingPolicy,
            ref ncPolicy,
            sizeof(int));
    }

    private static void RefreshHostFrame(IntPtr handle)
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

    private static void RefreshHostFrame(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        RefreshHostFrame(handle);
    }
}
