using System.Windows;
using System.Windows.Media;

namespace AIUsageMonitor.Views;

/// <summary>
/// Clips a border's contents to a rounded rectangle (WPF does not do this from CornerRadius alone).
/// </summary>
public static class RoundedCornerClip
{
    public static readonly DependencyProperty ClipRadiusProperty = DependencyProperty.RegisterAttached(
        "ClipRadius",
        typeof(double),
        typeof(RoundedCornerClip),
        new PropertyMetadata(0d, OnClipRadiusChanged));

    public static void SetClipRadius(DependencyObject element, double value) =>
        element.SetValue(ClipRadiusProperty, value);

    public static double GetClipRadius(DependencyObject element) =>
        (double)element.GetValue(ClipRadiusProperty);

    private static void OnClipRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.SizeChanged -= ElementOnSizeChanged;

        if (e.NewValue is double radius && radius > 0)
        {
            element.SizeChanged += ElementOnSizeChanged;
            UpdateClip(element, radius);
        }
        else
        {
            element.Clip = null;
        }
    }

    private static void ElementOnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            UpdateClip(element, GetClipRadius(element));
        }
    }

    private static void UpdateClip(FrameworkElement element, double radius)
    {
        if (radius <= 0 || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            element.Clip = null;
            return;
        }

        var effectiveRadius = Math.Min(radius, Math.Min(element.ActualWidth, element.ActualHeight) / 2);
        element.Clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight),
            effectiveRadius,
            effectiveRadius);
    }
}
