using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AIUsageMonitor.Views;

public partial class UsageOverlayWindow : Window
{
    private const double OverlayAspectRatio = 900d / 760d;

    private double _resizeStartHeight;
    private double _resizeStartWidth;
    private System.Windows.Point _resizeStartScreenPoint;

    public event EventHandler? ReloadRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? LogsRequested;

    public UsageOverlayWindow()
    {
        InitializeComponent();
    }

    private void HideButtonOnClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void ReloadButtonOnClick(object sender, RoutedEventArgs e)
    {
        ReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SettingsButtonOnClick(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void LogsButtonOnClick(object sender, RoutedEventArgs e)
    {
        LogsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HeaderOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        BeginDragMove(e);
    }

    private void WindowOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var point = e.GetPosition(WindowRoot);
        var draggableHeight = Math.Max(72, ActualHeight * 0.18);

        if (point.Y <= draggableHeight)
        {
            BeginDragMove(e);
        }
    }

    private void ResizeThumbOnDragStarted(object sender, DragStartedEventArgs e)
    {
        _resizeStartWidth = ActualWidth;
        _resizeStartHeight = ActualHeight;
        _resizeStartScreenPoint = GetMouseScreenPosition();
    }

    private void ResizeThumbOnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var delta = ScreenPixelsToDips(GetMouseScreenPosition() - _resizeStartScreenPoint);
        var requestedWidth = _resizeStartWidth + delta.X;
        var requestedHeight = _resizeStartHeight + delta.Y;
        var widthScale = requestedWidth / Math.Max(1, _resizeStartWidth);
        var heightScale = requestedHeight / Math.Max(1, _resizeStartHeight);
        var scale = Math.Abs(widthScale - 1) >= Math.Abs(heightScale - 1)
            ? widthScale
            : heightScale;
        var minScale = Math.Max(
            MinWidth / Math.Max(1, _resizeStartWidth),
            MinHeight / Math.Max(1, _resizeStartHeight));

        scale = Math.Max(minScale, scale);
        var width = Math.Max(MinWidth, _resizeStartWidth * scale);
        var height = width / OverlayAspectRatio;

        if (height < MinHeight)
        {
            height = MinHeight;
            width = height * OverlayAspectRatio;
        }

        Width = width;
        Height = height;
    }

    private System.Windows.Point GetMouseScreenPosition()
    {
        return PointToScreen(Mouse.GetPosition(this));
    }

    private Vector ScreenPixelsToDips(Vector screenPixels)
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformFromDevice.Transform(screenPixels) ?? screenPixels;
    }

    private void BeginDragMove(MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if the mouse capture changes during the drag.
        }
    }

    private void WindowOnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or Thumb)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
