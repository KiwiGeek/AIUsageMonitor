using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AIUsageMonitor.Interop;
using WinForms = System.Windows.Forms;

namespace AIUsageMonitor.Services;

internal static class WindowBoundsHelper
{
    public static bool TryGetScreenBoundsPixels(Window window, out Rect bounds)
    {
        bounds = Rect.Empty;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var rect))
        {
            return false;
        }

        bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public static void SetBoundsFromScreenPixels(Window window, Rect screenPixels) =>
        SetBoundsFromScreenPixels(window, screenPixels, enforceMinimumSize: true, syncWpfBounds: true);

    public static void SetBoundsFromScreenPixels(
        Window window,
        Rect screenPixels,
        bool enforceMinimumSize,
        bool syncWpfBounds = true)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            var left = (int)Math.Round(screenPixels.Left);
            var top = (int)Math.Round(screenPixels.Top);
            var width = (int)Math.Max(1, Math.Round(screenPixels.Width));
            var height = (int)Math.Max(1, Math.Round(screenPixels.Height));
            _ = NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HwndTop,
                left,
                top,
                width,
                height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        }

        if (syncWpfBounds)
        {
            SyncWindowPropertiesFromScreenPixels(window, screenPixels, enforceMinimumSize);
        }
    }

    public static bool TryGetWindowBoundsMismatchPixels(
        Window window,
        Rect expectedBoundsPixels,
        double tolerancePixels = 1.5)
    {
        if (!TryGetScreenBoundsPixels(window, out var actual))
        {
            return true;
        }

        return Math.Abs(actual.Left - expectedBoundsPixels.Left) > tolerancePixels ||
               Math.Abs(actual.Top - expectedBoundsPixels.Top) > tolerancePixels ||
               Math.Abs(actual.Width - expectedBoundsPixels.Width) > tolerancePixels ||
               Math.Abs(actual.Height - expectedBoundsPixels.Height) > tolerancePixels;
    }

    private static void SyncWindowPropertiesFromScreenPixels(
        Window window,
        Rect screenPixels,
        bool enforceMinimumSize)
    {
        var transform = GetTransformFromDevice(window);
        var topLeft = transform.Transform(new System.Windows.Point(screenPixels.Left, screenPixels.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(screenPixels.Right, screenPixels.Bottom));
        var width = bottomRight.X - topLeft.X;
        var height = bottomRight.Y - topLeft.Y;

        window.Left = topLeft.X;
        window.Top = topLeft.Y;
        window.Width = enforceMinimumSize ? Math.Max(window.MinWidth, width) : Math.Max(1, width);
        window.Height = enforceMinimumSize ? Math.Max(window.MinHeight, height) : Math.Max(1, height);
    }

    public static WinForms.Screen? GetScreenForWindow(Window window)
    {
        if (!TryGetScreenBoundsPixels(window, out var bounds))
        {
            return null;
        }

        var centerX = (int)Math.Round(bounds.Left + (bounds.Width / 2));
        var centerY = (int)Math.Round(bounds.Top + (bounds.Height / 2));
        return WinForms.Screen.FromPoint(new System.Drawing.Point(centerX, centerY));
    }

    public static Rect GetWorkingAreaPixels(WinForms.Screen screen)
    {
        var area = screen.WorkingArea;
        return new Rect(area.Left, area.Top, area.Width, area.Height);
    }

    public static Rect GetBoundsPixels(WinForms.Screen screen)
    {
        var area = screen.Bounds;
        return new Rect(area.Left, area.Top, area.Width, area.Height);
    }

    public static Rect ConvertScreenPixelsToDip(Window window, Rect screenPixels)
    {
        var transform = GetTransformFromDevice(window);
        var topLeft = transform.Transform(new System.Windows.Point(screenPixels.Left, screenPixels.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(screenPixels.Right, screenPixels.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    public static Rect ConvertDipToScreenPixels(Window window, Rect dipBounds)
    {
        var source = PresentationSource.FromVisual(window) as HwndSource;
        var transform = source?.CompositionTarget.TransformToDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new System.Windows.Point(dipBounds.Left, dipBounds.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(dipBounds.Right, dipBounds.Bottom));
        return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    public static Rect GetWorkingAreaDip(Window window, WinForms.Screen screen)
    {
        return ConvertScreenPixelsToDip(window, GetWorkingAreaPixels(screen));
    }

    private static Matrix GetTransformFromDevice(Window window)
    {
        var source = PresentationSource.FromVisual(window) as HwndSource;
        return source?.CompositionTarget.TransformFromDevice ?? Matrix.Identity;
    }
}
