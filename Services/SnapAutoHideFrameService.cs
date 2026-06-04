using System.Windows;
using System.Windows.Interop;
using AIUsageMonitor.Interop;
using Wpf.Ui.Controls;

namespace AIUsageMonitor.Services;

/// <summary>
/// Shrinks the HWND below normal WPF minimums by switching to a borderless popup frame while auto-hide is collapsed.
/// </summary>
internal static class SnapAutoHideFrameService
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsVisible = 0x10000000;
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int WsExToolwindow = 0x00000080;

    internal sealed class SavedFrameState
    {
        public bool IsActive { get; set; }
        public bool HasHostStyles { get; set; }
        public int HostStyle { get; set; }
        public int HostExtendedStyle { get; set; }
        public WindowStyle WindowStyle { get; set; }
        public bool ExtendsContentIntoTitleBar { get; set; }
        public WindowBackdropType WindowBackdropType { get; set; }
        public WindowCornerPreference WindowCornerPreference { get; set; }
    }

    public static void ApplyCollapsedFrame(Window window, SavedFrameState state)
    {
        if (state.IsActive)
        {
            RestoreExpandedFrame(window, state);
        }

        if (window is FluentWindow fluentWindow)
        {
            state.ExtendsContentIntoTitleBar = fluentWindow.ExtendsContentIntoTitleBar;
            state.WindowBackdropType = fluentWindow.WindowBackdropType;
            state.WindowCornerPreference = fluentWindow.WindowCornerPreference;
            fluentWindow.ExtendsContentIntoTitleBar = false;
            fluentWindow.WindowBackdropType = WindowBackdropType.None;
            fluentWindow.WindowCornerPreference = WindowCornerPreference.DoNotRound;
        }

        state.WindowStyle = window.WindowStyle;
        window.WindowStyle = WindowStyle.None;
        state.IsActive = true;

        ApplyBorderlessHostStyle(window, state);
        RefreshHostFrame(window);
    }

    public static void RestoreExpandedFrame(Window window, SavedFrameState state)
    {
        if (!state.IsActive)
        {
            return;
        }

        RestoreHostStyles(window, state);

        window.WindowStyle = state.WindowStyle;
        if (window is FluentWindow fluentWindow)
        {
            fluentWindow.ExtendsContentIntoTitleBar = state.ExtendsContentIntoTitleBar;
            fluentWindow.WindowBackdropType = state.WindowBackdropType;
            fluentWindow.WindowCornerPreference = state.WindowCornerPreference;
        }

        state.IsActive = false;
        RefreshHostFrame(window);
    }

    private static void ApplyBorderlessHostStyle(Window window, SavedFrameState state)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        state.HostStyle = NativeMethods.GetWindowLong(handle, GwlStyle);
        state.HostExtendedStyle = NativeMethods.GetWindowLong(handle, GwlExStyle);
        state.HasHostStyles = true;

        var style = state.HostStyle;
        style &= ~(WsCaption | WsThickFrame);
        style |= WsPopup | WsVisible;
        NativeMethods.SetWindowLong(handle, GwlStyle, style);

        var extendedStyle = state.HostExtendedStyle | WsExToolwindow;
        NativeMethods.SetWindowLong(handle, GwlExStyle, extendedStyle);
    }

    private static void RestoreHostStyles(Window window, SavedFrameState state)
    {
        if (!state.HasHostStyles)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowLong(handle, GwlStyle, state.HostStyle);
        NativeMethods.SetWindowLong(handle, GwlExStyle, state.HostExtendedStyle);
        state.HasHostStyles = false;
    }

    private static void RefreshHostFrame(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

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
