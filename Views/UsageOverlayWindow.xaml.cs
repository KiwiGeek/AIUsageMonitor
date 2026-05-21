using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using WpfSlider = System.Windows.Controls.Slider;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIUsageMonitor.Models;
using AIUsageMonitor.ViewModels;

namespace AIUsageMonitor.Views;

public partial class UsageOverlayWindow : FluentAppWindow
{
    private const string FullDisplayMode = "Full";
    private const string CompactDisplayMode = "Compact";
    private const string MiniDisplayMode = "Mini";
    private const double CompactWidthBreakpoint = 760;
    private const double MiniWidthBreakpoint = 500;
    private const double MiniHeightBreakpoint = 290;
    private const double FullMinimumCardWidth = 330;
    private const double CompactMinimumCardWidth = 220;
    private const double MiniMinimumCardWidth = 136;
    private const double FullEmptyMinimumHeight = 240;
    private const double CompactEmptyMinimumHeight = 96;
    private const double MiniEmptyMinimumHeight = 58;
    private const double FullMinimumVerticalChrome = 175;
    private const double CompactMinimumVerticalChrome = 36;
    private const double MiniMinimumVerticalChrome = 22;
    private const double FullMinimumCardSlotHeight = 265;
    private const double CompactMinimumCardSlotHeight = 100;
    private const double MiniMinimumCardSlotHeight = 36;
    private const double WindowHorizontalGutter = 36;
    private const double BodyHorizontalGutter = 12;
    private const double CardHostHorizontalFudge = 10;
    private const double MiniHorizontalChromeInset = WindowHorizontalGutter + BodyHorizontalGutter;
    private const double CompactHorizontalChromeInset = WindowHorizontalGutter + BodyHorizontalGutter;
    private const double CompactButtonsHorizontalInset = 180;
    private const double LayoutComparisonTolerance = 0.01;

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

    public static readonly DependencyProperty CardSlotHeightProperty = DependencyProperty.Register(
        nameof(CardSlotHeight),
        typeof(double),
        typeof(UsageOverlayWindow),
        new PropertyMetadata(265d));

    private INotifyCollectionChanged? _providersCollection;
    private bool _responsiveLayoutQueued;

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

    public void ApplyStartupPlacement(OverlayWindowPlacement? placement)
    {
        var virtualScreenBounds = GetVirtualScreenBounds();
        if (virtualScreenBounds.IsEmpty)
        {
            return;
        }

        var width = CoerceDimension(placement?.Width, Width, MinWidth, virtualScreenBounds.Width);
        var height = CoerceDimension(placement?.Height, Height, MinHeight, virtualScreenBounds.Height);

        Width = width;
        Height = height;

        var savedLeft = placement?.Left;
        var savedTop = placement?.Top;
        if (!HasFiniteValue(savedLeft) || !HasFiniteValue(savedTop))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Clamp(savedLeft.GetValueOrDefault(), virtualScreenBounds.Left, virtualScreenBounds.Right - width);
        Top = Clamp(savedTop.GetValueOrDefault(), virtualScreenBounds.Top, virtualScreenBounds.Bottom - height);
    }

    public OverlayWindowPlacement GetCurrentPlacement()
    {
        return new OverlayWindowPlacement
        {
            Left = HasFiniteValue(Left) ? Left : null,
            Top = HasFiniteValue(Top) ? Top : null,
            Width = GetCurrentDimension(ActualWidth, Width),
            Height = GetCurrentDimension(ActualHeight, Height)
        };
    }

    public void EnsureValidPlacement()
    {
        ApplyStartupPlacement(GetCurrentPlacement());
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

    public double CardSlotHeight
    {
        get => (double)GetValue(CardSlotHeightProperty);
        private set => SetValue(CardSlotHeightProperty, value);
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

    private void ProvidersListOnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            QueueResponsiveLayoutUpdate();
        }
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

        var providerCount = ProvidersList.Items.Count;
        var layout = CalculateResponsiveLayout(ActualWidth, ActualHeight, providerCount);
        if (!string.Equals(DisplayMode, layout.DisplayMode, StringComparison.Ordinal))
        {
            DisplayMode = layout.DisplayMode;
        }

        if (ShowCompactButtons != layout.ShowCompactButtons)
        {
            ShowCompactButtons = layout.ShowCompactButtons;
        }

        MinHeight = GetMinimumWindowHeight(layout.DisplayMode, layout.ShowCompactButtons);
        CardSlotWidth = layout.CardSlotWidth;
        CardSlotHeight = layout.CardSlotHeight;
        ApplyProvidersListLayout(layout);
    }

    private void ApplyProvidersListLayout(ResponsiveLayout layout)
    {
        if (layout.Columns <= 0 || layout.Rows <= 0)
        {
            ProvidersList.ClearValue(FrameworkElement.WidthProperty);
            ProvidersList.ClearValue(FrameworkElement.HeightProperty);
            return;
        }

        ProvidersList.Width = layout.Columns * layout.CardSlotWidth;
        ProvidersList.Height = layout.Rows * layout.CardSlotHeight;

        if (string.Equals(layout.DisplayMode, FullDisplayMode, StringComparison.Ordinal))
        {
            ProvidersScrollViewer.VerticalScrollBarVisibility = layout.MeetsMinimumSlotSize
                ? ScrollBarVisibility.Disabled
                : ScrollBarVisibility.Auto;
        }
    }

    private ResponsiveLayout CalculateResponsiveLayout(double width, double height, int providerCount)
    {
        providerCount = Math.Max(0, providerCount);
        var displayMode = GetDisplayMode(width, height, providerCount);
        var showCompactButtons = displayMode == CompactDisplayMode && providerCount == 0;

        if (displayMode == CompactDisplayMode && providerCount > 0)
        {
            var withButtons = CalculateCardGrid(
                displayMode,
                GetAvailableCardWidth(displayMode, true),
                GetAvailableCardHeight(displayMode, height),
                providerCount);
            showCompactButtons = withButtons.Columns > 1;
        }

        var grid = CalculateCardGrid(
            displayMode,
            GetAvailableCardWidth(displayMode, showCompactButtons),
            GetAvailableCardHeight(displayMode, height),
            providerCount);

        return new ResponsiveLayout(
            displayMode,
            grid.Columns,
            grid.Rows,
            grid.CardSlotWidth,
            grid.CardSlotHeight,
            grid.MeetsMinimumSlotSize,
            showCompactButtons);
    }

    private double GetAvailableCardWidth(string displayMode, bool showCompactButtons)
    {
        if (ProvidersScrollViewer.ActualWidth > 1)
        {
            return ProvidersScrollViewer.ActualWidth;
        }

        return Math.Max(1, ActualWidth - EstimatedHorizontalChromeInset(displayMode, showCompactButtons));
    }

    private double GetAvailableCardHeight(string displayMode, double windowHeight)
    {
        if (ProvidersScrollViewer.ActualHeight > 1)
        {
            return ProvidersScrollViewer.ActualHeight;
        }

        return Math.Max(1, windowHeight - EstimatedVerticalChromeInset(displayMode));
    }

    private static double GetMinimumWindowHeight(string displayMode, bool showCompactButtons)
    {
        return displayMode switch
        {
            MiniDisplayMode => MiniEmptyMinimumHeight,
            CompactDisplayMode => CompactEmptyMinimumHeight,
            _ => FullEmptyMinimumHeight
        };
    }

    private string GetDisplayMode(double width, double height, int providerCount)
    {
        if (width < MiniWidthBreakpoint ||
            (height < MiniHeightBreakpoint && !CanFitMode(CompactDisplayMode, width, height, providerCount, false)))
        {
            return MiniDisplayMode;
        }

        if (width >= CompactWidthBreakpoint &&
            CanFitMode(FullDisplayMode, width, height, providerCount, false))
        {
            return FullDisplayMode;
        }

        return CanFitMode(CompactDisplayMode, width, height, providerCount, false)
            ? CompactDisplayMode
            : MiniDisplayMode;
    }

    private bool CanFitMode(string displayMode, double width, double height, int providerCount, bool showCompactButtons)
    {
        if (providerCount <= 0)
        {
            return GetMinimumWindowHeight(displayMode, showCompactButtons) <= height;
        }

        var grid = CalculateCardGrid(
            displayMode,
            Math.Max(1, width - EstimatedHorizontalChromeInset(displayMode, showCompactButtons)),
            Math.Max(1, height - EstimatedVerticalChromeInset(displayMode)),
            providerCount);

        return grid.MeetsMinimumSlotSize;
    }

    private static CardGrid CalculateCardGrid(
        string displayMode,
        double availableWidth,
        double availableHeight,
        int providerCount)
    {
        availableWidth = Math.Max(1, availableWidth);
        availableHeight = Math.Max(1, availableHeight);

        if (providerCount <= 0)
        {
            return new CardGrid(0, 0, availableWidth, availableHeight, false);
        }

        var minimumCardWidth = GetMinimumCardWidth(displayMode);
        var minimumCardSlotHeight = GetMinimumCardSlotHeight(displayMode);
        var (marginWidth, marginHeight) = GetCardMargin(displayMode);
        var maxColumns = Math.Clamp(
            (int)Math.Floor((availableWidth + marginWidth) / (minimumCardWidth + marginWidth)),
            1,
            providerCount);

        var idealAspectRatio = GetIdealCardAspectRatio(displayMode);
        CardGrid? bestGrid = null;
        var bestShapeScore = 0d;
        var bestArea = 0d;

        for (var columns = 1; columns <= maxColumns; columns++)
        {
            var rows = (int)Math.Ceiling(providerCount / (double)columns);
            var cardSlotWidth = Math.Floor((availableWidth - (columns * marginWidth)) / columns);
            var cardSlotHeight = Math.Floor((availableHeight - (rows * marginHeight)) / rows);

            if (cardSlotWidth < minimumCardWidth || cardSlotHeight < minimumCardSlotHeight)
            {
                continue;
            }

            var area = cardSlotWidth * cardSlotHeight;
            var aspectRatio = cardSlotWidth / cardSlotHeight;
            var shapeScore = GetCardShapeScore(aspectRatio, idealAspectRatio);
            if (IsBetterCardLayout(shapeScore, area, bestShapeScore, bestArea))
            {
                bestShapeScore = shapeScore;
                bestArea = area;
                bestGrid = new CardGrid(columns, rows, cardSlotWidth, cardSlotHeight, true);
            }
        }

        if (bestGrid is not null)
        {
            return bestGrid.Value;
        }

        var fallbackColumns = maxColumns;
        var fallbackRows = (int)Math.Ceiling(providerCount / (double)fallbackColumns);
        return new CardGrid(
            fallbackColumns,
            fallbackRows,
            Math.Max(minimumCardWidth, Math.Floor((availableWidth - (fallbackColumns * marginWidth)) / fallbackColumns)),
            Math.Max(minimumCardSlotHeight, Math.Floor((availableHeight - (fallbackRows * marginHeight)) / fallbackRows)),
            false);
    }

    private static bool IsBetterCardLayout(
        double shapeScore,
        double area,
        double bestShapeScore,
        double bestArea)
    {
        if (bestShapeScore <= 0)
        {
            return true;
        }

        if (shapeScore > bestShapeScore + LayoutComparisonTolerance)
        {
            return true;
        }

        if (shapeScore < bestShapeScore - LayoutComparisonTolerance)
        {
            return false;
        }

        return area > bestArea + LayoutComparisonTolerance;
    }

    private static double GetIdealCardAspectRatio(string displayMode)
    {
        return GetMinimumCardWidth(displayMode) / GetMinimumCardSlotHeight(displayMode);
    }

    private static double GetCardShapeScore(double aspectRatio, double idealAspectRatio)
    {
        var logDelta = Math.Abs(Math.Log(aspectRatio / idealAspectRatio));
        return 1d / (1d + logDelta);
    }

    private static (double Width, double Height) GetCardMargin(string displayMode)
    {
        return displayMode switch
        {
            MiniDisplayMode => (6, 4),
            CompactDisplayMode => (8, 8),
            _ => (10, 10)
        };
    }

    private static double GetMinimumCardWidth(string displayMode)
    {
        return displayMode switch
        {
            MiniDisplayMode => MiniMinimumCardWidth,
            CompactDisplayMode => CompactMinimumCardWidth,
            _ => FullMinimumCardWidth
        };
    }

    private static double GetMinimumCardSlotHeight(string displayMode)
    {
        return displayMode switch
        {
            MiniDisplayMode => MiniMinimumCardSlotHeight,
            CompactDisplayMode => CompactMinimumCardSlotHeight,
            _ => FullMinimumCardSlotHeight
        };
    }

    private static double EstimatedHorizontalChromeInset(string displayMode, bool showCompactButtons)
    {
        return displayMode switch
        {
            MiniDisplayMode => MiniHorizontalChromeInset,
            CompactDisplayMode => showCompactButtons ? CompactButtonsHorizontalInset : CompactHorizontalChromeInset,
            _ => WindowHorizontalGutter + BodyHorizontalGutter + CardHostHorizontalFudge
        };
    }

    private static double EstimatedVerticalChromeInset(string displayMode)
    {
        return displayMode switch
        {
            MiniDisplayMode => MiniMinimumVerticalChrome,
            CompactDisplayMode => CompactMinimumVerticalChrome,
            _ => FullMinimumVerticalChrome
        };
    }

    private static Rect GetVirtualScreenBounds()
    {
        if (!HasFiniteValue(SystemParameters.VirtualScreenLeft) ||
            !HasFiniteValue(SystemParameters.VirtualScreenTop) ||
            !HasFiniteValue(SystemParameters.VirtualScreenWidth) ||
            !HasFiniteValue(SystemParameters.VirtualScreenHeight) ||
            SystemParameters.VirtualScreenWidth <= 0 ||
            SystemParameters.VirtualScreenHeight <= 0)
        {
            return Rect.Empty;
        }

        return new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
    }

    private static double CoerceDimension(double? savedValue, double fallbackValue, double minimumValue, double maximumValue)
    {
        var effectiveMinimum = HasFiniteValue(minimumValue) && minimumValue > 0
            ? minimumValue
            : 1;
        var effectiveMaximum = HasFiniteValue(maximumValue) && maximumValue > 0
            ? Math.Max(effectiveMinimum, maximumValue)
            : effectiveMinimum;
        var value = fallbackValue;
        if (HasFiniteValue(savedValue) && savedValue.GetValueOrDefault() > 0)
        {
            value = savedValue.GetValueOrDefault();
        }

        if (!HasFiniteValue(value) || value <= 0)
        {
            value = effectiveMinimum;
        }

        return Clamp(value, effectiveMinimum, effectiveMaximum);
    }

    private static double? GetCurrentDimension(double actualValue, double configuredValue)
    {
        var value = HasFiniteValue(actualValue) && actualValue > 0
            ? actualValue
            : configuredValue;

        return HasFiniteValue(value) && value > 0
            ? value
            : null;
    }

    private static double Clamp(double value, double minimumValue, double maximumValue)
    {
        if (!HasFiniteValue(value))
        {
            return minimumValue;
        }

        if (maximumValue < minimumValue)
        {
            return minimumValue;
        }

        return Math.Min(Math.Max(value, minimumValue), maximumValue);
    }

    private static bool HasFiniteValue(double? value)
    {
        return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
    }

    private readonly record struct ResponsiveLayout(
        string DisplayMode,
        int Columns,
        int Rows,
        double CardSlotWidth,
        double CardSlotHeight,
        bool MeetsMinimumSlotSize,
        bool ShowCompactButtons);

    private readonly record struct CardGrid(
        int Columns,
        int Rows,
        double CardSlotWidth,
        double CardSlotHeight,
        bool MeetsMinimumSlotSize);

    private void WindowOnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        BeginDragMove(e);
    }

    private void WindowOnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
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
            // DragMove can throw if mouse capture changes during the drag.
        }
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is WpfButtonBase or Thumb or WpfScrollBar or WpfTextBoxBase or WpfSlider or WpfComboBox or Hyperlink)
            {
                return true;
            }

            if (source.GetType().Name is "TitleBarButton")
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }

        base.OnStateChanged(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
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
}
