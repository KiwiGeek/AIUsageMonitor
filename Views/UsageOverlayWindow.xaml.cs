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
using AIUsageMonitor.Services;
using AIUsageMonitor.ViewModels;
using WinForms = System.Windows.Forms;

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
    /// <summary>Compact cards with two quota rows need more than the floating compact minimum.</summary>
    private const double CompactHorizontalStripCardSlotHeight = 128;
    /// <summary>Minimal window padding on top/bottom dock (toolbar is on the right).</summary>
    private const double CompactHorizontalStripVerticalChrome = 12;
    private const double CompactHorizontalStripToolbarWidth = 48;
    private const double CompactHorizontalStripHorizontalChromeInset = 70;
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

    public static readonly DependencyProperty IsHorizontalDockProperty = DependencyProperty.Register(
        nameof(IsHorizontalDock),
        typeof(bool),
        typeof(UsageOverlayWindow),
        new PropertyMetadata(false));

    private INotifyCollectionChanged? _providersCollection;
    private bool _responsiveLayoutQueued;
    private bool _snappedLayoutRefreshQueued;
    private bool _isWindowDragging;
    private bool _isManualDragging;
    private bool _isDragFloatingPreview;
    private bool _isApplyingPlacement;
    private System.Windows.Point _dragPointerToWindowOriginOffsetPixels;
    private double _dragFloatingWidthPixels;
    private double _dragFloatingHeightPixels;
    private double _floatingWidthDip;
    private double _floatingHeightDip;
    private double _floatingLeftDip;
    private double _floatingTopDip;
    private bool _snapToScreenEnabled = true;
    private ResizeMode _resizeModeBeforeDrag;
    private OverlayEdgeSnap _currentSnapEdge = OverlayEdgeSnap.None;
    private OverlayEdgeSnap _dragPreviewSnapEdge = OverlayEdgeSnap.None;
    private string? _snapMonitorDeviceName;

    // AppBar registration disabled while tuning edge snap behavior.
    // private readonly AppBarRegistration _appBarRegistration = new();

    public event EventHandler? ReloadRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? LogsRequested;

    public event EventHandler? ExitRequested;

    public UsageOverlayWindow()
    {
        InitializeComponent();
        Loaded += WindowOnLoaded;
        SourceInitialized += WindowOnSourceInitialized;
        SizeChanged += WindowOnSizeChanged;
        DataContextChanged += WindowOnDataContextChanged;
        PreviewMouseLeftButtonUp += WindowOnPreviewMouseLeftButtonUp;
        PreviewMouseMove += WindowOnPreviewMouseMove;
        IsVisibleChanged += WindowOnIsVisibleChanged;

        WindowsSnapSuppression.Attach(this, AllowSystemWindowPositionChanges);
    }

    private bool AllowSystemWindowPositionChanges()
    {
        return _isManualDragging || _isApplyingPlacement;
    }

    public void ApplySettings(AppSettings settings)
    {
        var snapWasEnabled = _snapToScreenEnabled;
        _snapToScreenEnabled = settings.OverlaySnapToScreenEnabled;

        if (!settings.OverlaySnapToScreenEnabled && _currentSnapEdge != OverlayEdgeSnap.None)
        {
            ReleaseEdgeSnapToFloating();
        }
        else if (!snapWasEnabled && settings.OverlaySnapToScreenEnabled)
        {
            // Snap re-enabled; keep current window placement until the user docks manually.
        }
    }

    public void ApplyStartupPlacement(OverlayWindowPlacement? placement)
    {
        var virtualScreenBounds = GetVirtualScreenBounds();
        if (virtualScreenBounds.IsEmpty)
        {
            return;
        }

        _isApplyingPlacement = true;
        try
        {
            ApplyStartupPlacementCore(placement, virtualScreenBounds);
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private void ApplyStartupPlacementCore(OverlayWindowPlacement? placement, Rect virtualScreenBounds)
    {
        _floatingWidthDip = CoerceDimension(
            placement?.FloatingWidth ?? placement?.Width,
            Width,
            MinWidth,
            virtualScreenBounds.Width);
        _floatingHeightDip = CoerceDimension(
            placement?.FloatingHeight ?? placement?.Height,
            Height,
            MinHeight,
            virtualScreenBounds.Height);
        _floatingLeftDip = CoerceFloatingPosition(
            placement?.FloatingLeft ?? placement?.Left,
            Left,
            virtualScreenBounds.Left,
            virtualScreenBounds.Right - _floatingWidthDip);
        _floatingTopDip = CoerceFloatingPosition(
            placement?.FloatingTop ?? placement?.Top,
            Top,
            virtualScreenBounds.Top,
            virtualScreenBounds.Bottom - _floatingHeightDip);

        var savedSnapEdge = placement?.SnapEdge ?? OverlayEdgeSnap.None;
        _currentSnapEdge = OverlayEdgeSnap.None;
        if (_snapToScreenEnabled && savedSnapEdge != OverlayEdgeSnap.None)
        {
            var screen = OverlayEdgeSnapService.FindScreenByDeviceName(placement?.SnapMonitorDeviceName)
                ?? WindowBoundsHelper.GetScreenForWindow(this);
            if (screen is not null)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                ApplyEdgeSnap(savedSnapEdge, screen);
                return;
            }
        }

        Width = _floatingWidthDip;
        Height = _floatingHeightDip;

        var savedLeft = placement?.Left;
        var savedTop = placement?.Top;
        if (!HasFiniteValue(savedLeft) || !HasFiniteValue(savedTop))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Clamp(savedLeft.GetValueOrDefault(), virtualScreenBounds.Left, virtualScreenBounds.Right - _floatingWidthDip);
        Top = Clamp(savedTop.GetValueOrDefault(), virtualScreenBounds.Top, virtualScreenBounds.Bottom - _floatingHeightDip);
    }

    public bool ShouldPersistPlacement =>
        !_isManualDragging && _dragPreviewSnapEdge == OverlayEdgeSnap.None;

    public OverlayWindowPlacement GetCurrentPlacement()
    {
        var screen = WindowBoundsHelper.GetScreenForWindow(this);
        var currentWidth = GetCurrentDimension(ActualWidth, Width);
        var currentHeight = GetCurrentDimension(ActualHeight, Height);

        if (ShouldPersistFloatingSize())
        {
            if (currentWidth is > 0)
            {
                _floatingWidthDip = currentWidth.Value;
            }

            if (currentHeight is > 0)
            {
                _floatingHeightDip = currentHeight.Value;
            }

            if (HasFiniteValue(Left))
            {
                _floatingLeftDip = Left;
            }

            if (HasFiniteValue(Top))
            {
                _floatingTopDip = Top;
            }
        }

        return new OverlayWindowPlacement
        {
            Left = HasFiniteValue(Left) ? Left : null,
            Top = HasFiniteValue(Top) ? Top : null,
            Width = ShouldPersistFloatingSize() ? currentWidth : _floatingWidthDip > 0 ? _floatingWidthDip : null,
            Height = ShouldPersistFloatingSize() ? currentHeight : _floatingHeightDip > 0 ? _floatingHeightDip : null,
            FloatingWidth = _floatingWidthDip > 0 ? _floatingWidthDip : null,
            FloatingHeight = _floatingHeightDip > 0 ? _floatingHeightDip : null,
            FloatingLeft = HasFiniteValue(_floatingLeftDip) ? _floatingLeftDip : null,
            FloatingTop = HasFiniteValue(_floatingTopDip) ? _floatingTopDip : null,
            SnapEdge = _snapToScreenEnabled ? _currentSnapEdge : OverlayEdgeSnap.None,
            SnapMonitorDeviceName = _snapToScreenEnabled && _currentSnapEdge != OverlayEdgeSnap.None
                ? _snapMonitorDeviceName ?? screen?.DeviceName
                : null
        };
    }

    private bool ShouldPersistFloatingSize() =>
        _currentSnapEdge == OverlayEdgeSnap.None &&
        !_isManualDragging &&
        _dragPreviewSnapEdge == OverlayEdgeSnap.None;

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

    public bool IsHorizontalDock
    {
        get => (bool)GetValue(IsHorizontalDockProperty);
        private set => SetValue(IsHorizontalDockProperty, value);
    }

    /// <summary>
    /// Which snap edge drives toolbar chrome. While dragging, only the live preview counts —
    /// committed <see cref="_currentSnapEdge"/> must not keep the horizontal dock toolbar visible
    /// when previewing left/right (or floating).
    /// </summary>
    private OverlayEdgeSnap GetChromeSnapEdge()
    {
        if (_isDragFloatingPreview)
        {
            return OverlayEdgeSnap.None;
        }

        if (_isManualDragging)
        {
            return _dragPreviewSnapEdge;
        }

        if (_currentSnapEdge != OverlayEdgeSnap.None)
        {
            return _currentSnapEdge;
        }

        return _dragPreviewSnapEdge;
    }

    private void SyncHorizontalDockChromeState()
    {
        var chromeEdge = GetChromeSnapEdge();
        IsHorizontalDock = chromeEdge is OverlayEdgeSnap.Top or OverlayEdgeSnap.Bottom;
    }

    private void CancelQueuedLayoutUpdates()
    {
        _responsiveLayoutQueued = false;
        _snappedLayoutRefreshQueued = false;
    }

    private bool ShouldRunSnapLayoutCoercion() =>
        !_isManualDragging &&
        (_currentSnapEdge != OverlayEdgeSnap.None || _dragPreviewSnapEdge != OverlayEdgeSnap.None);

    private bool ShouldRunResponsiveLayout()
    {
        if (_isManualDragging)
        {
            if (_dragPreviewSnapEdge != OverlayEdgeSnap.None)
            {
                return false;
            }

            return _isDragFloatingPreview || _currentSnapEdge == OverlayEdgeSnap.None;
        }

        return !IsEdgeSnapLayoutLocked();
    }

    private void QueueFloatingDragLayoutUpdate()
    {
        if (!_isManualDragging || !_isDragFloatingPreview)
        {
            return;
        }

        QueueResponsiveLayoutUpdate();
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
        if (!e.WidthChanged)
        {
            return;
        }

        if (_isManualDragging)
        {
            if (ShouldRunResponsiveLayout())
            {
                QueueResponsiveLayoutUpdate();
            }

            return;
        }

        if (ShouldRunSnapLayoutCoercion())
        {
            if (IsVerticalStripSnap())
            {
                QueueSnappedStripLayoutCoercion();
            }
            else if (IsHorizontalStripSnap())
            {
                QueueSnappedHorizontalStripLayoutCoercion();
            }

            return;
        }

        if (ShouldRunResponsiveLayout())
        {
            QueueResponsiveLayoutUpdate();
        }
    }

    private void WindowOnLoaded(object sender, RoutedEventArgs e)
    {
        QueueResponsiveLayoutUpdate();
    }

    private void WindowOnSourceInitialized(object? sender, EventArgs e)
    {
        if (_snapToScreenEnabled && _currentSnapEdge != OverlayEdgeSnap.None)
        {
            var screen = WindowBoundsHelper.GetScreenForWindow(this);
            if (screen is not null)
            {
                ApplyEdgeSnap(_currentSnapEdge, screen);
            }
        }
    }

    private void WindowOnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isManualDragging)
        {
            if (ShouldRunResponsiveLayout())
            {
                QueueResponsiveLayoutUpdate();
            }
        }
        else if (ShouldRunSnapLayoutCoercion())
        {
            if (IsVerticalStripSnap())
            {
                QueueSnappedStripLayoutCoercion();
            }
            else if (IsHorizontalStripSnap())
            {
                QueueSnappedHorizontalStripLayoutCoercion();
            }
        }
        else if (ShouldRunResponsiveLayout())
        {
            QueueResponsiveLayoutUpdate();
        }

        if (_currentSnapEdge == OverlayEdgeSnap.None ||
            _isManualDragging ||
            _isApplyingPlacement)
        {
            return;
        }

        var resizedAlongFreeAxis = _currentSnapEdge switch
        {
            OverlayEdgeSnap.Left or OverlayEdgeSnap.Right => e.WidthChanged,
            OverlayEdgeSnap.Top or OverlayEdgeSnap.Bottom => e.HeightChanged,
            _ => e.WidthChanged || e.HeightChanged
        };

        // AppBar refresh disabled — snap position/size only.
        // if (resizedAlongFreeAxis)
        // {
        //     TryRefreshSnappedAppBar();
        // }
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
        if (_responsiveLayoutQueued || !ShouldRunResponsiveLayout())
        {
            return;
        }

        _responsiveLayoutQueued = true;
        var priority = _isManualDragging
            ? DispatcherPriority.Render
            : DispatcherPriority.Loaded;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _responsiveLayoutQueued = false;
                UpdateResponsiveLayout();
            }),
            priority);
    }

    private void QueueSnappedStripLayoutCoercion()
    {
        if (_snappedLayoutRefreshQueued || !ShouldRunSnapLayoutCoercion() || !IsVerticalStripSnap())
        {
            return;
        }

        _snappedLayoutRefreshQueued = true;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _snappedLayoutRefreshQueued = false;
                ExpandSnappedStripCardsToHost();
            }),
            DispatcherPriority.Render);
    }

    private bool IsVerticalStripSnap() =>
        _currentSnapEdge is OverlayEdgeSnap.Left or OverlayEdgeSnap.Right ||
        _dragPreviewSnapEdge is OverlayEdgeSnap.Left or OverlayEdgeSnap.Right;

    private bool IsHorizontalStripSnap() =>
        _currentSnapEdge is OverlayEdgeSnap.Top or OverlayEdgeSnap.Bottom ||
        _dragPreviewSnapEdge is OverlayEdgeSnap.Top or OverlayEdgeSnap.Bottom;

    private OverlayEdgeSnap GetHorizontalStripSnapEdge() =>
        _currentSnapEdge is OverlayEdgeSnap.Top or OverlayEdgeSnap.Bottom
            ? _currentSnapEdge
            : _dragPreviewSnapEdge;

    private OverlayEdgeSnap GetVerticalStripSnapEdge() =>
        _currentSnapEdge is OverlayEdgeSnap.Left or OverlayEdgeSnap.Right
            ? _currentSnapEdge
            : _dragPreviewSnapEdge;

    private bool IsEdgeSnapLayoutLocked() =>
        _currentSnapEdge != OverlayEdgeSnap.None || _dragPreviewSnapEdge != OverlayEdgeSnap.None;

    private void UpdateResponsiveLayout()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var providerCount = ProvidersList.Items.Count;
        if (!ShouldRunResponsiveLayout())
        {
            return;
        }

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

    private static CardGrid CalculateCardGridFixedColumns(
        string displayMode,
        double availableHeight,
        int providerCount)
    {
        availableHeight = Math.Max(1, availableHeight);
        const int columns = 1;
        var minimumCardWidth = GetMinimumCardWidth(displayMode);
        var minimumCardSlotHeight = GetMinimumCardSlotHeight(displayMode);
        var rows = Math.Max(1, providerCount);
        var (_, marginHeight) = GetCardMargin(displayMode);
        var cardSlotWidth = minimumCardWidth;
        var cardSlotHeight = Math.Floor((availableHeight - (rows * marginHeight)) / rows);
        var meets = cardSlotHeight >= minimumCardSlotHeight;

        return new CardGrid(
            columns,
            rows,
            cardSlotWidth,
            Math.Max(minimumCardSlotHeight, cardSlotHeight),
            meets);
    }

    private static CardGrid CalculateCardGridFixedRows(
        string displayMode,
        double availableWidth,
        int providerCount,
        double? cardSlotHeightOverride = null)
    {
        availableWidth = Math.Max(1, availableWidth);
        const int rows = 1;
        var minimumCardWidth = GetMinimumCardWidth(displayMode);
        var minimumCardSlotHeight = GetMinimumCardSlotHeight(displayMode);
        var cardSlotHeight = Math.Max(
            minimumCardSlotHeight,
            cardSlotHeightOverride ?? minimumCardSlotHeight);

        if (providerCount <= 0)
        {
            return new CardGrid(1, rows, availableWidth, cardSlotHeight, false);
        }

        var (marginWidth, _) = GetCardMargin(displayMode);
        var maxColumns = Math.Clamp(
            (int)Math.Floor((availableWidth + marginWidth) / (minimumCardWidth + marginWidth)),
            1,
            providerCount);
        var columns = Math.Min(providerCount, maxColumns);
        var cardSlotWidth = Math.Floor((availableWidth - (columns * marginWidth)) / columns);
        var meets = cardSlotWidth >= minimumCardWidth;

        return new CardGrid(
            columns,
            rows,
            Math.Max(minimumCardWidth, cardSlotWidth),
            cardSlotHeight,
            meets);
    }

    private static Rect BuildSnappedWindowRectDip(
        OverlayEdgeSnap snapEdge,
        Rect workAreaDip,
        CardGrid grid,
        string displayMode,
        bool showCompactButtons)
    {
        var horizontalChrome = GetSnappedHorizontalChromeInset(snapEdge, displayMode, showCompactButtons);
        var verticalChrome = GetSnappedVerticalChromeInset(snapEdge, displayMode);
        var (marginWidth, marginHeight) = GetCardMargin(displayMode);
        var cardHostWidth = (grid.Columns * grid.CardSlotWidth) + (grid.Columns * marginWidth);
        var cardHostHeight = (grid.Rows * grid.CardSlotHeight) + (grid.Rows * marginHeight);
        var outerWidth = horizontalChrome + cardHostWidth;
        var outerHeight = verticalChrome + cardHostHeight;

        return snapEdge switch
        {
            OverlayEdgeSnap.Left => new Rect(workAreaDip.Left, workAreaDip.Top, outerWidth, workAreaDip.Height),
            OverlayEdgeSnap.Right => new Rect(workAreaDip.Right - outerWidth, workAreaDip.Top, outerWidth, workAreaDip.Height),
            OverlayEdgeSnap.Top => new Rect(workAreaDip.Left, workAreaDip.Top, workAreaDip.Width, outerHeight),
            OverlayEdgeSnap.Bottom => new Rect(workAreaDip.Left, workAreaDip.Bottom - outerHeight, workAreaDip.Width, outerHeight),
            _ => workAreaDip
        };
    }

    private bool TryBuildSnappedBounds(
        OverlayEdgeSnap snapEdge,
        WinForms.Screen screen,
        out Rect boundsPixels,
        out ResponsiveLayout layout)
    {
        boundsPixels = Rect.Empty;
        layout = default;

        var providerCount = ProvidersList.Items.Count;
        var workAreaDip = WindowBoundsHelper.GetWorkingAreaDip(this, screen);
        var displayMode = GetSnappedDisplayMode(snapEdge, workAreaDip, providerCount);
        var showCompactButtons = displayMode == CompactDisplayMode && providerCount == 0;
        var horizontalChrome = GetSnappedHorizontalChromeInset(snapEdge, displayMode, showCompactButtons);
        var verticalChrome = GetSnappedVerticalChromeInset(snapEdge, displayMode);
        var cardAreaWidth = Math.Max(1, workAreaDip.Width - horizontalChrome);
        var cardAreaHeight = Math.Max(1, workAreaDip.Height - verticalChrome);

        var grid = snapEdge is OverlayEdgeSnap.Left or OverlayEdgeSnap.Right
            ? CalculateCardGridFixedColumns(displayMode, cardAreaHeight, providerCount)
            : CalculateCardGridFixedRows(
                displayMode,
                cardAreaWidth,
                providerCount,
                CompactHorizontalStripCardSlotHeight);

        var windowDip = BuildSnappedWindowRectDip(snapEdge, workAreaDip, grid, displayMode, showCompactButtons);
        boundsPixels = WindowBoundsHelper.ConvertDipToScreenPixels(this, windowDip);
        layout = new ResponsiveLayout(
            displayMode,
            grid.Columns,
            grid.Rows,
            grid.CardSlotWidth,
            grid.CardSlotHeight,
            grid.MeetsMinimumSlotSize,
            showCompactButtons);
        return true;
    }

    private string GetSnappedDisplayMode(OverlayEdgeSnap snapEdge, Rect workAreaDip, int providerCount)
    {
        if (snapEdge is OverlayEdgeSnap.Top or OverlayEdgeSnap.Bottom)
        {
            return CompactDisplayMode;
        }

        if (providerCount <= 0)
        {
            return CompactDisplayMode;
        }

        var minimumCardWidth = FullMinimumCardWidth;
        var horizontalChrome = WindowHorizontalGutter + BodyHorizontalGutter + CardHostHorizontalFudge;
        var stripWidth = horizontalChrome + minimumCardWidth + 10;
        var stripHeight = workAreaDip.Height;

        return GetDisplayMode(stripWidth, stripHeight, providerCount);
    }

    private void ApplySnappedLayout(ResponsiveLayout layout)
    {
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

        if (!_isManualDragging)
        {
            if (IsVerticalStripSnap())
            {
                QueueSnappedStripLayoutCoercion();
            }
            else if (IsHorizontalStripSnap())
            {
                QueueSnappedHorizontalStripLayoutCoercion();
            }
        }

        SyncHorizontalDockChromeState();
    }

    private void QueueSnappedHorizontalStripLayoutCoercion()
    {
        if (_snappedLayoutRefreshQueued || !ShouldRunSnapLayoutCoercion() || !IsHorizontalStripSnap())
        {
            return;
        }

        _snappedLayoutRefreshQueued = true;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _snappedLayoutRefreshQueued = false;
                ExpandSnappedHorizontalStripCardsToHost();
            }),
            DispatcherPriority.Render);
    }

    /// <summary>
    /// Left/right snap sets a minimum card width for window sizing; after layout the host can be wider.
    /// Stretch cards to the measured host width so they are not stuck at half the window.
    /// </summary>
    private void ExpandSnappedStripCardsToHost()
    {
        if (!IsVerticalStripSnap())
        {
            return;
        }

        var providerCount = ProvidersList.Items.Count;
        if (providerCount <= 0)
        {
            return;
        }

        var displayMode = DisplayMode;
        var showCompactButtons = ShowCompactButtons;
        var cardWidth = Math.Max(
            GetMinimumCardWidth(displayMode),
            GetAvailableCardWidth(displayMode, showCompactButtons));

        var (_, marginHeight) = GetCardMargin(displayMode);
        var availableHeight = GetAvailableCardHeight(displayMode, ActualHeight);
        var rows = Math.Max(1, providerCount);
        var cardHeight = Math.Max(
            GetMinimumCardSlotHeight(displayMode),
            Math.Floor((availableHeight - (rows * marginHeight)) / rows));

        CardSlotWidth = cardWidth;
        CardSlotHeight = cardHeight;
        ProvidersList.Width = cardWidth;
        ProvidersList.Height = (rows * cardHeight) + (rows * marginHeight);

        if (GetVerticalStripSnapEdge() == OverlayEdgeSnap.Right)
        {
            ReanchorRightStripToWorkArea();
        }
    }

    /// <summary>
    /// Right strip snap sizes position from the pre-expand width; widening cards grows the window to the right.
    /// Re-pin the HWND so its right edge stays on the monitor work area.
    /// </summary>
    private void ReanchorRightStripToWorkArea()
    {
        if (GetVerticalStripSnapEdge() != OverlayEdgeSnap.Right)
        {
            return;
        }

        var screen = OverlayEdgeSnapService.FindScreenByDeviceName(_snapMonitorDeviceName)
            ?? WindowBoundsHelper.GetScreenForWindow(this);
        if (screen is null ||
            !WindowBoundsHelper.TryGetScreenBoundsPixels(this, out var currentBounds))
        {
            return;
        }

        var workArea = WindowBoundsHelper.GetWorkingAreaPixels(screen);
        var targetLeft = workArea.Right - currentBounds.Width;
        if (Math.Abs(targetLeft - currentBounds.Left) < 0.5 &&
            Math.Abs(currentBounds.Top - workArea.Top) < 0.5 &&
            Math.Abs(currentBounds.Height - workArea.Height) < 0.5)
        {
            return;
        }

        ApplyBoundsPixels(new Rect(targetLeft, workArea.Top, currentBounds.Width, workArea.Height));
    }

    /// <summary>
    /// Top/bottom dock uses one compact row; stretch cards to the measured host after layout.
    /// </summary>
    private void ExpandSnappedHorizontalStripCardsToHost()
    {
        if (!IsHorizontalStripSnap())
        {
            return;
        }

        var providerCount = ProvidersList.Items.Count;
        if (providerCount <= 0)
        {
            return;
        }

        var displayMode = DisplayMode;
        var showCompactButtons = ShowCompactButtons;
        var availableWidth = Math.Max(
            GetMinimumCardWidth(displayMode),
            GetAvailableCardWidth(displayMode, showCompactButtons));
        var (marginWidth, marginHeight) = GetCardMargin(displayMode);
        var maxColumns = Math.Clamp(
            (int)Math.Floor((availableWidth + marginWidth) / (GetMinimumCardWidth(displayMode) + marginWidth)),
            1,
            providerCount);
        var columns = Math.Min(providerCount, maxColumns);
        var cardWidth = Math.Floor((availableWidth - (columns * marginWidth)) / columns);
        cardWidth = Math.Max(GetMinimumCardWidth(displayMode), cardWidth);
        var cardSlotHeight = CompactHorizontalStripCardSlotHeight;

        CardSlotWidth = cardWidth;
        CardSlotHeight = cardSlotHeight;
        ProvidersList.Width = (columns * cardWidth) + (columns * marginWidth);
        ProvidersList.Height = cardSlotHeight + marginHeight;

        ApplyHorizontalStripWindowHeight(cardSlotHeight, marginHeight, displayMode, showCompactButtons);
        ReanchorHorizontalStripToWorkArea();
    }

    private void ApplyHorizontalStripWindowHeight(
        double cardSlotHeight,
        double marginHeight,
        string displayMode,
        bool showCompactButtons)
    {
        var snapEdge = GetHorizontalStripSnapEdge();
        if (snapEdge is not (OverlayEdgeSnap.Top or OverlayEdgeSnap.Bottom))
        {
            return;
        }

        var screen = OverlayEdgeSnapService.FindScreenByDeviceName(_snapMonitorDeviceName)
            ?? WindowBoundsHelper.GetScreenForWindow(this);
        if (screen is null)
        {
            return;
        }

        var verticalChrome = GetSnappedVerticalChromeInset(snapEdge, displayMode);
        var outerHeightDip = verticalChrome + cardSlotHeight + marginHeight;
        var workAreaDip = WindowBoundsHelper.GetWorkingAreaDip(this, screen);
        var windowDip = snapEdge switch
        {
            OverlayEdgeSnap.Top => new Rect(workAreaDip.Left, workAreaDip.Top, workAreaDip.Width, outerHeightDip),
            OverlayEdgeSnap.Bottom => new Rect(
                workAreaDip.Left,
                workAreaDip.Bottom - outerHeightDip,
                workAreaDip.Width,
                outerHeightDip),
            _ => new Rect(Left, Top, Width, outerHeightDip)
        };

        ApplyBoundsPixels(WindowBoundsHelper.ConvertDipToScreenPixels(this, windowDip));
    }

    private static double GetSnappedVerticalChromeInset(OverlayEdgeSnap snapEdge, string displayMode)
    {
        if (snapEdge is OverlayEdgeSnap.Top or OverlayEdgeSnap.Bottom)
        {
            return CompactHorizontalStripVerticalChrome;
        }

        return EstimatedVerticalChromeInset(displayMode);
    }

    private static double GetSnappedHorizontalChromeInset(
        OverlayEdgeSnap snapEdge,
        string displayMode,
        bool showCompactButtons)
    {
        if (snapEdge is OverlayEdgeSnap.Top or OverlayEdgeSnap.Bottom)
        {
            return CompactHorizontalStripHorizontalChromeInset;
        }

        return EstimatedHorizontalChromeInset(displayMode, showCompactButtons);
    }

    private void ReanchorHorizontalStripToWorkArea()
    {
        var snapEdge = GetHorizontalStripSnapEdge();
        if (snapEdge is not (OverlayEdgeSnap.Top or OverlayEdgeSnap.Bottom))
        {
            return;
        }

        var screen = OverlayEdgeSnapService.FindScreenByDeviceName(_snapMonitorDeviceName)
            ?? WindowBoundsHelper.GetScreenForWindow(this);
        if (screen is null ||
            !WindowBoundsHelper.TryGetScreenBoundsPixels(this, out var currentBounds))
        {
            return;
        }

        var workArea = WindowBoundsHelper.GetWorkingAreaPixels(screen);
        var targetTop = snapEdge == OverlayEdgeSnap.Top
            ? workArea.Top
            : workArea.Bottom - currentBounds.Height;
        var targetLeft = workArea.Left;

        if (Math.Abs(targetTop - currentBounds.Top) < 0.5 &&
            Math.Abs(targetLeft - currentBounds.Left) < 0.5 &&
            Math.Abs(currentBounds.Width - workArea.Width) < 0.5)
        {
            return;
        }

        ApplyBoundsPixels(new Rect(targetLeft, targetTop, workArea.Width, currentBounds.Height));
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

    private static bool HasFiniteValue(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
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

        if (!WindowBoundsHelper.TryGetScreenBoundsPixels(this, out var windowBounds))
        {
            return;
        }

        _isWindowDragging = true;
        _isManualDragging = true;
        _isDragFloatingPreview = false;
        _dragPreviewSnapEdge = OverlayEdgeSnap.None;
        CancelQueuedLayoutUpdates();
        _resizeModeBeforeDrag = ResizeMode;
        if (ResizeMode != ResizeMode.NoResize)
        {
            ResizeMode = ResizeMode.NoResize;
            UpdateLayout();
        }

        var mouseScreen = PointToScreen(e.GetPosition(this));
        _dragPointerToWindowOriginOffsetPixels = new System.Windows.Point(
            mouseScreen.X - windowBounds.Left,
            mouseScreen.Y - windowBounds.Top);

        if (_currentSnapEdge == OverlayEdgeSnap.None)
        {
            var floatingDip = WindowBoundsHelper.ConvertScreenPixelsToDip(this, windowBounds);
            _floatingWidthDip = floatingDip.Width;
            _floatingHeightDip = floatingDip.Height;
            _floatingLeftDip = floatingDip.Left;
            _floatingTopDip = floatingDip.Top;
            _dragFloatingWidthPixels = windowBounds.Width;
            _dragFloatingHeightPixels = windowBounds.Height;
        }
        else if (_floatingWidthDip > 0 && _floatingHeightDip > 0)
        {
            var floatingBoundsDip = new Rect(_floatingLeftDip, _floatingTopDip, _floatingWidthDip, _floatingHeightDip);
            var floatingBoundsPixels = WindowBoundsHelper.ConvertDipToScreenPixels(this, floatingBoundsDip);
            _dragFloatingWidthPixels = floatingBoundsPixels.Width;
            _dragFloatingHeightPixels = floatingBoundsPixels.Height;
        }
        else
        {
            _dragFloatingWidthPixels = windowBounds.Width;
            _dragFloatingHeightPixels = windowBounds.Height;
        }

        // if (_appBarRegistration.IsRegistered)
        // {
        //     _appBarRegistration.Unregister(this);
        // }

        CaptureMouse();
        e.Handled = true;
    }

    private void WindowOnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isManualDragging || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var mouseScreen = PointToScreen(e.GetPosition(this));
        var freeBounds = new Rect(
            mouseScreen.X - _dragPointerToWindowOriginOffsetPixels.X,
            mouseScreen.Y - _dragPointerToWindowOriginOffsetPixels.Y,
            _dragFloatingWidthPixels,
            _dragFloatingHeightPixels);

        if (_snapToScreenEnabled &&
            OverlayEdgeSnapService.TryGetSnapEdge(freeBounds, out var previewSnapEdge, out var screen) &&
            screen is not null &&
            TryBuildSnappedBounds(previewSnapEdge, screen, out var previewBounds, out var previewLayout))
        {
            var previewChanged = _dragPreviewSnapEdge != previewSnapEdge;
            _isDragFloatingPreview = false;
            _dragPreviewSnapEdge = previewSnapEdge;
            SyncHorizontalDockChromeState();
            if (previewChanged)
            {
                ApplySnappedLayout(previewLayout);
            }

            ApplyBoundsPixels(previewBounds);
            return;
        }

        var enteringFloatingPreview = _currentSnapEdge != OverlayEdgeSnap.None && !_isDragFloatingPreview;
        _dragPreviewSnapEdge = OverlayEdgeSnap.None;
        _isDragFloatingPreview = _currentSnapEdge != OverlayEdgeSnap.None;
        SyncHorizontalDockChromeState();

        ApplyBoundsPixels(freeBounds);
        QueueFloatingDragLayoutUpdate();
    }

    private void WindowOnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isWindowDragging)
        {
            return;
        }

        var wasManualDragging = _isManualDragging;
        var hadSnapPreview = _dragPreviewSnapEdge != OverlayEdgeSnap.None;
        _isWindowDragging = false;
        _isManualDragging = false;
        _isDragFloatingPreview = false;
        if (wasManualDragging && IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        TryApplyEdgeSnapOnRelease(e, hadSnapPreview);

        if (wasManualDragging && ResizeMode != _resizeModeBeforeDrag)
        {
            ResizeMode = _resizeModeBeforeDrag;
        }

        if (_currentSnapEdge == OverlayEdgeSnap.None)
        {
            UpdateLayout();
            QueueResponsiveLayoutUpdate();
        }
        else if (IsVerticalStripSnap())
        {
            QueueSnappedStripLayoutCoercion();
        }
        else if (IsHorizontalStripSnap())
        {
            QueueSnappedHorizontalStripLayoutCoercion();
        }
    }

    private Rect GetFloatingBoundsPixelsAtRelease(MouseButtonEventArgs e)
    {
        var mouseScreen = PointToScreen(e.GetPosition(this));
        return new Rect(
            mouseScreen.X - _dragPointerToWindowOriginOffsetPixels.X,
            mouseScreen.Y - _dragPointerToWindowOriginOffsetPixels.Y,
            _dragFloatingWidthPixels,
            _dragFloatingHeightPixels);
    }

    private void TryApplyEdgeSnapOnRelease(MouseButtonEventArgs e, bool hadSnapPreview)
    {
        var freeBounds = GetFloatingBoundsPixelsAtRelease(e);

        OverlayEdgeSnap snapEdge = OverlayEdgeSnap.None;
        WinForms.Screen? screen = null;
        if (_snapToScreenEnabled &&
            OverlayEdgeSnapService.TryGetSnapEdge(freeBounds, out var detectedEdge, out var detectedScreen) &&
            detectedScreen is not null)
        {
            snapEdge = detectedEdge;
            screen = detectedScreen;
        }
        else if (_snapToScreenEnabled && _dragPreviewSnapEdge != OverlayEdgeSnap.None)
        {
            snapEdge = _dragPreviewSnapEdge;
            screen = WindowBoundsHelper.GetScreenForWindow(this);
        }

        if (snapEdge != OverlayEdgeSnap.None && screen is not null)
        {
            ApplyEdgeSnap(snapEdge, screen);
            _dragPreviewSnapEdge = OverlayEdgeSnap.None;
            return;
        }

        _dragPreviewSnapEdge = OverlayEdgeSnap.None;

        if (_currentSnapEdge != OverlayEdgeSnap.None)
        {
            ClearEdgeSnap();
            RestoreFloatingWindowAtDrop(e);
            return;
        }

        if (hadSnapPreview)
        {
            ApplyBoundsPixels(freeBounds);
            QueueResponsiveLayoutUpdate();
        }
    }

    private void ApplyEdgeSnap(OverlayEdgeSnap snapEdge, WinForms.Screen screen)
    {
        _currentSnapEdge = snapEdge;
        _snapMonitorDeviceName = screen.DeviceName;

        if (!TryBuildSnappedBounds(snapEdge, screen, out var boundsPixels, out var layout))
        {
            _currentSnapEdge = OverlayEdgeSnap.None;
            _snapMonitorDeviceName = null;
            return;
        }

        _isApplyingPlacement = true;
        try
        {
            ApplyBoundsPixels(boundsPixels);
            ApplySnappedLayout(layout);

            if (snapEdge is OverlayEdgeSnap.Left or OverlayEdgeSnap.Right)
            {
                UpdateLayout();
                ExpandSnappedStripCardsToHost();
            }
            else if (snapEdge is OverlayEdgeSnap.Top or OverlayEdgeSnap.Bottom)
            {
                UpdateLayout();
                ExpandSnappedHorizontalStripCardsToHost();
            }

            // AppBar registration disabled while tuning edge snap behavior.
            // var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            // if (handle != IntPtr.Zero)
            // {
            //     OverlayEdgeSnapService.ApplySnap(this, snapEdge, screen, boundsPixels, _appBarRegistration);
            // }
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    // AppBar registration disabled while tuning edge snap behavior.
    // private void TryRefreshSnappedAppBar()
    // {
    //     if (_currentSnapEdge == OverlayEdgeSnap.None ||
    //         _isManualDragging ||
    //         _isApplyingPlacement)
    //     {
    //         return;
    //     }
    //
    //     var screen = OverlayEdgeSnapService.FindScreenByDeviceName(_snapMonitorDeviceName)
    //         ?? WindowBoundsHelper.GetScreenForWindow(this);
    //     if (screen is null || !WindowBoundsHelper.TryGetScreenBoundsPixels(this, out var currentBounds))
    //     {
    //         return;
    //     }
    //
    //     var appBarBounds = OverlayEdgeSnapService.GetSnappedAppBarBoundsPixels(_currentSnapEdge, screen, currentBounds);
    //     _isApplyingPlacement = true;
    //     try
    //     {
    //         OverlayEdgeSnapService.ApplySnap(this, _currentSnapEdge, screen, appBarBounds, _appBarRegistration);
    //     }
    //     finally
    //     {
    //         _isApplyingPlacement = false;
    //     }
    // }

    private void ApplyBoundsPixels(Rect boundsPixels)
    {
        _isApplyingPlacement = true;
        try
        {
            WindowBoundsHelper.SetBoundsFromScreenPixels(this, boundsPixels);
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private void ClearEdgeSnap()
    {
        _currentSnapEdge = OverlayEdgeSnap.None;
        _snapMonitorDeviceName = null;
        SyncHorizontalDockChromeState();

        // OverlayEdgeSnapService.ClearSnap(this, _appBarRegistration);
    }

    public void ReleaseEdgeSnapToFloating()
    {
        if (_currentSnapEdge == OverlayEdgeSnap.None)
        {
            return;
        }

        ClearEdgeSnap();
        RestoreFloatingWindowBounds();
        QueueResponsiveLayoutUpdate();
    }

    private void RestoreFloatingWindowBounds()
    {
        if (_floatingWidthDip <= 0 || _floatingHeightDip <= 0)
        {
            return;
        }

        var floatingPixels = WindowBoundsHelper.ConvertDipToScreenPixels(
            this,
            new Rect(_floatingLeftDip, _floatingTopDip, _floatingWidthDip, _floatingHeightDip));

        _isApplyingPlacement = true;
        try
        {
            ApplyBoundsPixels(floatingPixels);
            Width = _floatingWidthDip;
            Height = _floatingHeightDip;
            Left = _floatingLeftDip;
            Top = _floatingTopDip;
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private void RestoreFloatingWindowAtDrop(MouseButtonEventArgs e)
    {
        if (_floatingWidthDip <= 0 || _floatingHeightDip <= 0)
        {
            return;
        }

        var mouseScreen = PointToScreen(e.GetPosition(this));
        var floatingBoundsDip = new Rect(
            mouseScreen.X - _dragPointerToWindowOriginOffsetPixels.X,
            mouseScreen.Y - _dragPointerToWindowOriginOffsetPixels.Y,
            _floatingWidthDip,
            _floatingHeightDip);
        var floatingBoundsPixels = WindowBoundsHelper.ConvertDipToScreenPixels(this, floatingBoundsDip);

        _isApplyingPlacement = true;
        try
        {
            ApplyBoundsPixels(floatingBoundsPixels);
            _floatingLeftDip = floatingBoundsDip.Left;
            _floatingTopDip = floatingBoundsDip.Top;
            Width = _floatingWidthDip;
            Height = _floatingHeightDip;
            Left = _floatingLeftDip;
            Top = _floatingTopDip;
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private static double CoerceFloatingPosition(double? savedValue, double fallbackValue, double minimumBound, double maximumBound)
    {
        var value = HasFiniteValue(savedValue) ? savedValue.GetValueOrDefault() : fallbackValue;
        if (!HasFiniteValue(value))
        {
            value = HasFiniteValue(fallbackValue) ? fallbackValue : minimumBound;
        }

        return Clamp(value, minimumBound, maximumBound);
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

    private void WindowOnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            // _appBarRegistration.Unregister(this);
            return;
        }

        if (!_snapToScreenEnabled || _currentSnapEdge == OverlayEdgeSnap.None)
        {
            return;
        }

        var screen = OverlayEdgeSnapService.FindScreenByDeviceName(_snapMonitorDeviceName)
            ?? WindowBoundsHelper.GetScreenForWindow(this);
        if (screen is not null)
        {
            ApplyEdgeSnap(_currentSnapEdge, screen);
        }
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
        // _appBarRegistration.Dispose();

        if (_providersCollection is not null)
        {
            _providersCollection.CollectionChanged -= ProvidersOnCollectionChanged;
            _providersCollection = null;
        }

        base.OnClosed(e);
    }
}
