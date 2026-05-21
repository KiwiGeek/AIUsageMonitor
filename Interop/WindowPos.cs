using System.Runtime.InteropServices;

namespace AIUsageMonitor.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowPos
{
    public IntPtr hwnd;
    public IntPtr hwndInsertAfter;
    public int x;
    public int y;
    public int cx;
    public int cy;
    public uint flags;
}

internal static class WindowPosFlags
{
    public const uint Nosize = 0x0001;
    public const uint Nomove = 0x0002;
}
