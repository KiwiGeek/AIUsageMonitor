using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIUsageMonitor.ViewModels;

namespace AIUsageMonitor.Views;

public partial class UsageOverlayWindow : Window
{
    private const string FullDisplayMode = "Full";
    private const string CompactDisplayMode = "Compact";
    private const string MiniDisplayMode = "Mini";
    private const double CompactWidthBreakpoint = 760;
    private const double CompactHeightBreakpoint = 520;
    private const double MiniWidthBreakpoint = 500;
    private const double MiniHeightBreakpoint = 290;
    private const double FullMinimumCardWidth = 330;
    private const double CompactMinimumCardWidth = 220;
    private const double MiniMinimumCardWidth = 136;
    private const double FullMinimumVerticalInset = 210;
    private const double FullMinimumRowHeight = 265;
    private const double CompactMinimumVerticalInset = 36;
    private const double CompactMinimumRowHeight = 100;
    private const double MiniMinimumVerticalInset = 38;
    private const double MiniMinimumRowHeight = 39;
    private const double StrongLandscapeRatio = 3.75;
    private const double CompactHorizontalChromeInset = 28;
    private const double CompactButtonsHorizontalInset = 180;
    private const double HeightTrimTolerance = 1;

    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(
        nameof(DisplayMode),
        typeof(string),
        typeof(UsageOverlayWindow),
        new PropertyMetadata(FullDisplayMode));

    public static readonly DependencyProperty ShowCompactButtonsProperty = DependencyProperty.Register(
        nameof(ShowCompactButtons),
        typeof(bool),
        typeof(UsageOverlayWindow),
        new PropertyMetadata(false));

    public static readonly DependencyProperty CardSlotWidthProperty = DependencyProperty.Register(
        nameof(CardSlotWidth),
        typeof(double),
        typeof(UsageOverlayWindow),
        new PropertyMetadata(400d));

    public static readonly DependencyProperty ProvidersListWidthProperty = DependencyProperty.Register(
        nameof(ProvidersListWidth),
        typeof(double),
        typeof(UsageOverlayWindow),
        new PropertyMetadata(816d));

    private double _resizeStartHeight;
    private double _resizeStartWidth;
    private System.Windows.Point _resizeStartScreenPoint;
    private INotifyCollectionChanged? _providersCollection;
    private bool _responsiveLayoutQueued;
    private bool _isAutoTrimmingHeight;

    public event EventHandler? ReloadRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? LogsRequested;

    public event EventHandler? ExitRequested;

    public UsageOverlayWindow()
    {
        InitializeComponent();
        Loaded += WindowOnLoaded;
        SizeChanged += WindowOnSizeChanged;
        DataContextChanged += WindowOnDataContextChanged;
    }

    public string DisplayMode
    {
        get => (string)GetValue(DisplayModeProperty);
        private set => SetValue(DisplayModeProperty, value);
    }

    public bool ShowCompactButtons
    {
        get => (bool)GetValue(ShowCompactButtonsProperty);
        private set => SetValue(ShowCompactButtonsProperty, value);
    }

    public double CardSlotWidth
    {
        get => (double)GetValue(CardSlotWidthProperty);
        private set => SetValue(CardSlotWidthProperty, value);
    }

    public double ProvidersListWidth
    {
        get => (double)GetValue(ProvidersListWidthProperty);
        private set => SetValue(ProvidersListWidthProperty, value);
    }

    private void HideButtonOnClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void ShowMenuItemOnClick(object sender, RoutedEventArgs e)
    {
        Show();
        Activate();
    }

    private void RefreshMenuItemOnClick(object sender, RoutedEventArgs e)
    {
        ReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SettingsMenuItemOnClick(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void LogsMenuItemOnClick(object sender, RoutedEventArgs e)
    {
        LogsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExitMenuItemOnClick(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
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

    private void WindowOnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveElement(source))
        {
            return;
        }

        BeginDragMove(e);
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
        var requestedWidth = Math.Max(MinWidth, _resizeStartWidth + delta.X);
        var requestedHeight = _resizeStartHeight + delta.Y;
        var layout = CalculateResponsiveLayout(requestedWidth, requestedHeight, ProvidersList.Items.Count);
        var minimumHeight = GetMinimumWindowHeight(layout.DisplayMode, layout.Rows);

        MinHeight = minimumHeight;
        Width = requestedWidth;
        Height = minimumHeight;
    }

    private void ProvidersListOnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        QueueResponsiveLayoutUpdate();
    }

    private void WindowOnLoaded(object sender, RoutedEventArgs e)
    {
        QueueResponsiveLayoutUpdate();
    }

    private void WindowOnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        QueueResponsiveLayoutUpdate();
    }

    private void WindowOnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_providersCollection is not null)
        {
            _providersCollection.CollectionChanged -= ProvidersOnCollectionChanged;
            _providersCollection = null;
        }

        if (e.NewValue is UsageOverlayViewModel viewModel)
        {
            _providersCollection = viewModel.Providers;
            _providersCollection.CollectionChanged += ProvidersOnCollectionChanged;
        }

        QueueResponsiveLayoutUpdate();
    }

    private void ProvidersOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        QueueResponsiveLayoutUpdate();
    }

    private void QueueResponsiveLayoutUpdate()
    {
        if (_responsiveLayoutQueued)
        {
            return;
        }

        _responsiveLayoutQueued = true;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _responsiveLayoutQueued = false;
                UpdateResponsiveLayout();
            }),
            DispatcherPriority.Loaded);
    }

    private void UpdateResponsiveLayout()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var layout = CalculateResponsiveLayout(ActualWidth, ActualHeight, ProvidersList.Items.Count);
        if (!string.Equals(DisplayMode, layout.DisplayMode, StringComparison.Ordinal))
        {
            DisplayMode = layout.DisplayMode;
        }

        if (ShowCompactButtons != layout.ShowCompactButtons)
        {
            ShowCompactButtons = layout.ShowCompactButtons;
        }

        MinHeight = GetMinimumWindowHeight(layout.DisplayMode, layout.Rows);
        CardSlotWidth = layout.CardSlotWidth;
        ProvidersListWidth = layout.ProvidersListWidth;
        TrimHeightToContent(MinHeight);
    }

    private void TrimHeightToContent(double targetHeight)
    {
        if (_isAutoTrimmingHeight || ActualHeight <= targetHeight + HeightTrimTolerance)
        {
            return;
        }

        _isAutoTrimmingHeight = true;
        try
        {
            Height = targetHeight;
        }
        finally
        {
            _isAutoTrimmingHeight = false;
        }
    }

    private static ResponsiveLayout CalculateResponsiveLayout(double width, double height, int providerCount)
    {
        providerCount = Math.Max(1, providerCount);
        var displayMode = GetDisplayMode(width, height, providerCount);
        var showCompactButtons = false;

        if (displayMode == CompactDisplayMode)
        {
            var withButtons = CalculateCardGrid(displayMode, width, height, providerCount, true);
            showCompactButtons = withButtons.Columns > 1;
        }

        var grid = CalculateCardGrid(displayMode, width, height, providerCount, showCompactButtons);
        return new ResponsiveLayout(displayMode, grid.Rows, grid.CardSlotWidth, grid.ProvidersListWidth, showCompactButtons);
    }

    private static double GetMinimumWindowHeight(string displayMode, int rows)
    {
        var rowCount = Math.Max(1, rows);

        return displayMode switch
        {
            MiniDisplayMode => MiniMinimumVerticalInset + rowCount * MiniMinimumRowHeight,
            CompactDisplayMode => CompactMinimumVerticalInset + rowCount * CompactMinimumRowHeight,
            _ => FullMinimumVerticalInset + rowCount * FullMinimumRowHeight
        };
    }

    private static int ChooseColumnsForHeight(string displayMode, double width, double height, int providerCount, int maxColumns)
    {
        if (width / Math.Max(1, height) >= StrongLandscapeRatio)
        {
            return maxColumns;
        }

        for (var columns = 1; columns <= maxColumns; columns++)
        {
            var rows = (int)Math.Ceiling(providerCount / (double)columns);
            if (GetMinimumWindowHeight(displayMode, rows) <= height)
            {
                return columns;
            }
        }

        return maxColumns;
    }

    private static string GetDisplayMode(double width, double height, int providerCount)
    {
        if (width < MiniWidthBreakpoint ||
            (height < MiniHeightBreakpoint && !CanFitMode(CompactDisplayMode, width, height, providerCount, false)))
        {
            return MiniDisplayMode;
        }

        if (width >= CompactWidthBreakpoint &&
            height >= CompactHeightBreakpoint &&
            CanFitMode(FullDisplayMode, width, height, providerCount, false))
        {
            return FullDisplayMode;
        }

        return CanFitMode(CompactDisplayMode, width, height, providerCount, false)
            ? CompactDisplayMode
            : MiniDisplayMode;
    }

    private static bool CanFitMode(string displayMode, double width, double height, int providerCount, bool showCompactButtons)
    {
        var grid = CalculateCardGrid(displayMode, width, height, providerCount, showCompactButtons);
        return GetMinimumWindowHeight(displayMode, grid.Rows) <= height;
    }

    private static CardGrid CalculateCardGrid(
        string displayMode,
        double width,
        double height,
        int providerCount,
        bool showCompactButtons)
    {
        var availableWidth = Math.Max(1, width - EstimatedHorizontalChromeInset(displayMode, showCompactButtons));
        var minimumCardWidth = displayMode switch
        {
            MiniDisplayMode => MiniMinimumCardWidth,
            CompactDisplayMode => CompactMinimumCardWidth,
            _ => FullMinimumCardWidth
        };
        var maxColumns = Math.Clamp((int)Math.Floor(availableWidth / minimumCardWidth), 1, providerCount);
        var columns = ChooseColumnsForHeight(displayMode, width, height, providerCount, maxColumns);
        var rows = (int)Math.Ceiling(providerCount / (double)columns);
        var cardSlotWidth = Math.Max(1, Math.Floor(availableWidth / columns));

        return new CardGrid(columns, rows, cardSlotWidth, cardSlotWidth * columns);
    }

    private static double EstimatedHorizontalChromeInset(string displayMode, bool showCompactButtons)
    {
        return displayMode switch
        {
            MiniDisplayMode => 34,
            CompactDisplayMode => showCompactButtons ? CompactButtonsHorizontalInset : CompactHorizontalChromeInset,
            _ => 84
        };
    }

    private readonly record struct ResponsiveLayout(
        string DisplayMode,
        int Rows,
        double CardSlotWidth,
        double ProvidersListWidth,
        bool ShowCompactButtons);

    private readonly record struct CardGrid(
        int Columns,
        int Rows,
        double CardSlotWidth,
        double ProvidersListWidth);

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

    protected override void OnClosed(EventArgs e)
    {
        if (_providersCollection is not null)
        {
            _providersCollection.CollectionChanged -= ProvidersOnCollectionChanged;
            _providersCollection = null;
        }

        base.OnClosed(e);
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
