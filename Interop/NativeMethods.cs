using System.Runtime.InteropServices;

namespace AIUsageMonitor.Interop;

internal static class NativeMethods
{
    public const int AbmNew = 0;
    public const int AbmRemove = 1;
    public const int AbmQueryPos = 2;
    public const int AbmSetPos = 3;

    public const int AbeLeft = 0;
    public const int AbeTop = 1;
    public const int AbeRight = 2;
    public const int AbeBottom = 3;

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern uint SHAppBarMessage(int dwMessage, ref AppBarData data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RectNative rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    public static readonly IntPtr HwndTop = IntPtr.Zero;

    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpFrameChanged = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public const int WmNcHitTest = 0x0084;

    public const int HtLeft = 10;
    public const int HtRight = 11;
    public const int HtTop = 12;
    public const int HtTopLeft = 13;
    public const int HtTopRight = 14;
    public const int HtBottom = 15;
    public const int HtBottomLeft = 16;
    public const int HtBottomRight = 17;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    public const int DwmwaBorderColor = 34;

    /// <summary>Suppresses the Win11 DWM-drawn window border.</summary>
    public const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);

    /// <summary>Restores the system default window border color.</summary>
    public const int DwmwaColorDefault = unchecked((int)0xFFFFFFFF);

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    public struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AppBarData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public int uEdge;
        public RectNative rc;
        public int lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
