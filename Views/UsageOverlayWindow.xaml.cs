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
using System.Windows.Media.Animation;
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
    private const double SnapAutoHideRevealZonePixels = 8;
    private const double SnapAutoHideVisibleStripPixels = 4;
    private const int SnapAutoHidePollIntervalMs = 100;
    private const double PortraitToolbarAspectRatioThreshold = 0.85;

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

    public static readonly DependencyProperty ShowSideToolbarProperty = DependencyProperty.Register(
        nameof(ShowSideToolbar),
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
    private bool _snapReserveScreenSpace;
    private bool _snapAutoHideWhenSnapped;
    private bool _isSnapAutoHideExpanded = true;
    private Rect _snappedFullBoundsPixels = Rect.Empty;
    private Rect? _appBarDockAnchorBoundsPixels;
    private readonly AppBarRegistration _appBarRegistration = new();
    private DispatcherTimer? _snapAutoHideTimer;
    private ResizeMode _resizeModeBeforeDrag;
    private readonly Dictionary<Border, RadialGradientBrush> _cardShimmerBrushCache = new();
    private OverlayEdgeSnap _currentSnapEdge = OverlayEdgeSnap.None;
    private OverlayEdgeSnap _dragPreviewSnapEdge = OverlayEdgeSnap.None;
    private string? _snapMonitorDeviceName;

    public event EventHandler? ReloadRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? DeepSeekPeakOverrideRequested;

    public event EventHandler? LogsRequested;

    public event EventHandler? ExitRequested;

    public UsageOverlayWindow()
    {
        InitializeComponent();
        Loaded += WindowOnLoaded;
        SourceInitialized += WindowOnSourceInitialized;
        SizeChanged += WindowOnSizeChanged;
        MouseEnter += WindowOnMouseEnter;
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
        _snapReserveScreenSpace = settings.OverlaySnapReserveScreenSpaceEnabled;
        _snapAutoHideWhenSnapped = settings.OverlaySnapAutoHideWhenSnappedEnabled;

        if (!settings.OverlaySnapToScreenEnabled && _currentSnapEdge != OverlayEdgeSnap.None)
        {
            ReleaseEdgeSnapToFloating();
        }
        else if (!snapWasEnabled && settings.OverlaySnapToScreenEnabled)
        {
            // Snap re-enabled; keep current window placement until the user docks manually.
        }
        else if (_currentSnapEdge != OverlayEdgeSnap.None && !_isManualDragging)
        {
            RefreshSnappedScreenIntegration();
        }

        if (DataContext is UsageOverlayViewModel viewModel)
        {
            viewModel.ApplyWaifuAppearance(settings.WaifuSquadEnabled, settings.WaifuSquadOpacity);
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

    public bool ShowSideToolbar
    {
        get => (bool)GetValue(ShowSideToolbarProperty);
        private set => SetValue(ShowSideToolbarProperty, value);
    }

    private bool ComputeShowSideToolbar() =>
        IsHorizontalDock ||
        (ActualWidth >= MiniWidthBreakpoint &&
         (ActualHeight > ActualWidth * PortraitToolbarAspectRatioThreshold ||
          ActualHeight < FullMinimumVerticalChrome + FullMinimumCardSlotHeight));

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
        ShowSideToolbar = ComputeShowSideToolbar();
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
        WindowRoundedCornersService.Apply(this);
        QueueResponsiveLayoutUpdate();
    }

    private void WindowOnSourceInitialized(object? sender, EventArgs e)
    {
        WindowRoundedCornersService.Apply(this);

        if (_snapToScreenEnabled && _currentSnapEdge != OverlayEdgeSnap.None)
        {
            var screen = WindowBoundsHelper.GetScreenForWindow(this);
            if (screen is not null)
            {
                ApplyEdgeSnap(_currentSnapEdge, screen);
            }
        }
    }

    private void WindowOnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_snapAutoHideWhenSnapped &&
            _currentSnapEdge != OverlayEdgeSnap.None &&
            !_isSnapAutoHideExpanded)
        {
            SetSnapAutoHideExpanded(true);
        }
    }

    private void WindowOnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        WindowRoundedCornersService.Apply(this);

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

        if (!_snapAutoHideWhenSnapped || _isSnapAutoHideExpanded)
        {
            ReanchorSnappedStripToWorkArea();
        }

        if (_snapAutoHideWhenSnapped && _isSnapAutoHideExpanded &&
            WindowBoundsHelper.TryGetScreenBoundsPixels(this, out var expandedBounds))
        {
            var screen = OverlayEdgeSnapService.FindScreenByDeviceName(_snapMonitorDeviceName)
                ?? WindowBoundsHelper.GetScreenForWindow(this);
            if (screen is not null)
            {
                _snappedFullBoundsPixels = GetSnappedDockBoundsPixels(screen, expandedBounds);
            }
        }

        if (_snapReserveScreenSpace && !_snapAutoHideWhenSnapped)
        {
            TryRefreshSnappedAppBar();
        }

        if (_snapAutoHideWhenSnapped)
        {
            EvaluateSnapAutoHide();
        }

        var resizedAlongFreeAxis = _currentSnapEdge switch
        {
            OverlayEdgeSnap.Left or OverlayEdgeSnap.Right => e.WidthChanged,
            OverlayEdgeSnap.Top or OverlayEdgeSnap.Bottom => e.HeightChanged,
            _ => e.WidthChanged || e.HeightChanged
        };

        _ = resizedAlongFreeAxis;
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

        ShowSideToolbar = ComputeShowSideToolbar();
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
        Dispatcher.BeginInvoke(
            () => WindowRoundedCornersService.Apply(this),
            DispatcherPriority.Loaded);
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
            var cardSlotWidth = (availableWidth - (columns * marginWidth)) / columns;
            var cardSlotHeight = (availableHeight - (rows * marginHeight)) / rows;

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

        ReanchorVerticalStripToWorkArea();
    }

    /// <summary>
    /// Left/right strip snap can drift from the work-area edge after card expansion or user resize.
    /// Re-pin the HWND to the dock edge on the snap monitor.
    /// </summary>
    private void ReanchorVerticalStripToWorkArea()
    {
        var snapEdge = GetVerticalStripSnapEdge();
        if (snapEdge is not (OverlayEdgeSnap.Left or OverlayEdgeSnap.Right))
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

        var docked = GetSnappedDockBoundsPixels(screen, currentBounds);
        if (RectsNearlyEqual(currentBounds, docked))
        {
            return;
        }

        ApplyBoundsPixels(docked);
    }

    private void ReanchorSnappedStripToWorkArea()
    {
        if (IsVerticalStripSnap())
        {
            ReanchorVerticalStripToWorkArea();
        }
        else if (IsHorizontalStripSnap())
        {
            ReanchorHorizontalStripToWorkArea();
        }
    }

    private static bool RectsNearlyEqual(Rect a, Rect b) =>
        Math.Abs(a.Left - b.Left) < 0.5 &&
        Math.Abs(a.Top - b.Top) < 0.5 &&
        Math.Abs(a.Width - b.Width) < 0.5 &&
        Math.Abs(a.Height - b.Height) < 0.5;

    private Rect GetSnappedDockBoundsPixels(WinForms.Screen screen, Rect currentBoundsPixels) =>
        OverlayEdgeSnapService.GetSnappedDockBoundsPixels(
            _currentSnapEdge,
            screen,
            currentBoundsPixels,
            _appBarDockAnchorBoundsPixels);

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

        var docked = GetSnappedDockBoundsPixels(screen, currentBounds);
        if (RectsNearlyEqual(currentBounds, docked))
        {
            return;
        }

        ApplyBoundsPixels(docked);
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

        // Reject extreme aspect ratios; among acceptable layouts, prefer largest card area.
        const double minAcceptableShape = 0.4;
        var newOk = shapeScore >= minAcceptableShape;
        var bestOk = bestShapeScore >= minAcceptableShape;
        if (newOk != bestOk)
        {
            return newOk;
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
        if (_snapAutoHideWhenSnapped && _currentSnapEdge != OverlayEdgeSnap.None)
        {
            SetSnapAutoHideExpanded(true);
        }

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

            if (WindowBoundsHelper.TryGetScreenBoundsPixels(this, out var visibleBounds))
            {
                _snappedFullBoundsPixels = GetSnappedDockBoundsPixels(screen, visibleBounds);
            }
            else
            {
                _snappedFullBoundsPixels = GetSnappedDockBoundsPixels(screen, boundsPixels);
            }

            ApplySnapScreenIntegration(snapEdge, screen);
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private void ApplySnapScreenIntegration(OverlayEdgeSnap snapEdge, WinForms.Screen screen)
    {
        if (snapEdge == OverlayEdgeSnap.None)
        {
            ClearSnapScreenIntegration();
            return;
        }

        if (_snapAutoHideWhenSnapped)
        {
            _appBarRegistration.Unregister(this);
            _isSnapAutoHideExpanded = true;
            StartSnapAutoHide();
            return;
        }

        StopSnapAutoHide();

        if (_snapReserveScreenSpace)
        {
            if (!WindowBoundsHelper.TryGetScreenBoundsPixels(this, out var appBarBounds))
            {
                appBarBounds = _snappedFullBoundsPixels;
            }

            _appBarDockAnchorBoundsPixels = appBarBounds;
            OverlayEdgeSnapService.ApplySnap(this, snapEdge, screen, appBarBounds, _appBarRegistration);

            if (WindowBoundsHelper.TryGetScreenBoundsPixels(this, out var adjustedBounds) &&
                !RectsNearlyEqual(adjustedBounds, appBarBounds))
            {
                ApplyBoundsPixels(appBarBounds);
            }

            _snappedFullBoundsPixels = appBarBounds;
        }
        else
        {
            _appBarDockAnchorBoundsPixels = null;
            _appBarRegistration.Unregister(this);
        }
    }

    private void ClearSnapScreenIntegration()
    {
        StopSnapAutoHide();
        _appBarRegistration.Unregister(this);
        _appBarDockAnchorBoundsPixels = null;
        _snappedFullBoundsPixels = Rect.Empty;
        _isSnapAutoHideExpanded = true;
    }

    private void RefreshSnappedScreenIntegration()
    {
        if (_currentSnapEdge == OverlayEdgeSnap.None)
        {
            return;
        }

        var screen = OverlayEdgeSnapService.FindScreenByDeviceName(_snapMonitorDeviceName)
            ?? WindowBoundsHelper.GetScreenForWindow(this);
        if (screen is null)
        {
            return;
        }

        ApplySnapScreenIntegration(_currentSnapEdge, screen);
    }

    private void TryRefreshSnappedAppBar()
    {
        if (_currentSnapEdge == OverlayEdgeSnap.None ||
            _isManualDragging ||
            _isApplyingPlacement ||
            !_snapReserveScreenSpace ||
            _snapAutoHideWhenSnapped)
        {
            return;
        }

        var screen = OverlayEdgeSnapService.FindScreenByDeviceName(_snapMonitorDeviceName)
            ?? WindowBoundsHelper.GetScreenForWindow(this);
        if (screen is null || !WindowBoundsHelper.TryGetScreenBoundsPixels(this, out var currentBounds))
        {
            return;
        }

        var appBarBounds = OverlayEdgeSnapService.GetSnappedAppBarBoundsPixels(_currentSnapEdge, screen, currentBounds);
        _isApplyingPlacement = true;
        try
        {
            OverlayEdgeSnapService.ApplySnap(this, _currentSnapEdge, screen, appBarBounds, _appBarRegistration);
            if (WindowBoundsHelper.TryGetScreenBoundsPixels(this, out var adjustedBounds))
            {
                _snappedFullBoundsPixels = GetSnappedDockBoundsPixels(screen, adjustedBounds);
            }
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private void StartSnapAutoHide()
    {
        if (!_snapAutoHideWhenSnapped || _currentSnapEdge == OverlayEdgeSnap.None)
        {
            return;
        }

        _snapAutoHideTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(SnapAutoHidePollIntervalMs),
            DispatcherPriority.Background,
            SnapAutoHideTimerOnTick,
            Dispatcher);
        _snapAutoHideTimer.Start();
        EvaluateSnapAutoHide();
    }

    private void StopSnapAutoHide()
    {
        if (_snapAutoHideTimer is null)
        {
            return;
        }

        _snapAutoHideTimer.Stop();
        _snapAutoHideTimer = null;
    }

    private void SnapAutoHideTimerOnTick(object? sender, EventArgs e)
    {
        EvaluateSnapAutoHide();
    }

    private void EvaluateSnapAutoHide()
    {
        if (!_snapAutoHideWhenSnapped ||
            _currentSnapEdge == OverlayEdgeSnap.None ||
            _snappedFullBoundsPixels.IsEmpty)
        {
            return;
        }

        if (ShouldKeepSnapAutoHideExpanded())
        {
            SetSnapAutoHideExpanded(true);
            return;
        }

        var screen = OverlayEdgeSnapService.FindScreenByDeviceName(_snapMonitorDeviceName)
            ?? WindowBoundsHelper.GetScreenForWindow(this);
        if (screen is null)
        {
            return;
        }

        var workArea = WindowBoundsHelper.GetWorkingAreaPixels(screen);
        var dockedBounds = GetSnappedDockBoundsPixels(screen, _snappedFullBoundsPixels);
        var mouse = WinForms.Control.MousePosition;
        var mousePoint = new System.Drawing.Point(mouse.X, mouse.Y);
        var shouldExpand = IsMouseInSnapRevealZone(mousePoint, workArea, dockedBounds, _currentSnapEdge) ||
                           IsMouseOverWindowBounds();

        SetSnapAutoHideExpanded(shouldExpand);
    }

    private bool ShouldKeepSnapAutoHideExpanded() =>
        _isManualDragging || _isApplyingPlacement || !IsVisible || IsMouseCaptured;

    private static bool IsMouseInSnapRevealZone(
        System.Drawing.Point mouse,
        Rect workAreaPixels,
        Rect dockedBoundsPixels,
        OverlayEdgeSnap snapEdge)
    {
        var zone = SnapAutoHideRevealZonePixels;
        return snapEdge switch
        {
            OverlayEdgeSnap.Left => mouse.X >= workAreaPixels.Left &&
                                    mouse.X <= dockedBoundsPixels.Left + zone,
            OverlayEdgeSnap.Right => mouse.X <= workAreaPixels.Right &&
                                     mouse.X >= dockedBoundsPixels.Right - zone,
            OverlayEdgeSnap.Top => mouse.Y >= workAreaPixels.Top &&
                                   mouse.Y <= dockedBoundsPixels.Top + zone,
            OverlayEdgeSnap.Bottom => mouse.Y <= workAreaPixels.Bottom &&
                                      mouse.Y >= dockedBoundsPixels.Bottom - zone,
            _ => false
        };
    }

    private bool IsMouseOverWindowBounds()
    {
        if (!WindowBoundsHelper.TryGetScreenBoundsPixels(this, out var bounds))
        {
            return false;
        }

        var mouse = WinForms.Control.MousePosition;
        return mouse.X >= bounds.Left &&
               mouse.X < bounds.Right &&
               mouse.Y >= bounds.Top &&
               mouse.Y < bounds.Bottom;
    }

    private void SetSnapAutoHideExpanded(bool expanded)
    {
        if (_isSnapAutoHideExpanded == expanded || _snappedFullBoundsPixels.IsEmpty)
        {
            return;
        }

        _isSnapAutoHideExpanded = expanded;
        var screen = OverlayEdgeSnapService.FindScreenByDeviceName(_snapMonitorDeviceName)
            ?? WindowBoundsHelper.GetScreenForWindow(this);
        if (screen is null)
        {
            return;
        }

        var targetBounds = expanded
            ? GetSnappedDockBoundsPixels(screen, _snappedFullBoundsPixels)
            : OverlayEdgeSnapService.GetSnapAutoHideCollapsedBoundsPixels(
                _currentSnapEdge,
                screen,
                _snappedFullBoundsPixels,
                SnapAutoHideVisibleStripPixels);

        _isApplyingPlacement = true;
        try
        {
            ApplyBoundsPixels(targetBounds);
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

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

        ClearSnapScreenIntegration();
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
            StopSnapAutoHide();
            _appBarRegistration.Unregister(this);
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
        StopSnapAutoHide();
        _appBarRegistration.Dispose();

        if (_providersCollection is not null)
        {
            _providersCollection.CollectionChanged -= ProvidersOnCollectionChanged;
            _providersCollection = null;
        }

        base.OnClosed(e);
    }

    // ── Card hover effects ──────────────────────────────────────────────────

    private static void EnsureCardTransforms(Border card)
    {
        if (card.RenderTransform is ScaleTransform) return;
        card.RenderTransform = new ScaleTransform(1, 1);
    }

    private void ProviderCard_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Border card || card.DataContext is not ProviderUsageCard provider)
        {
            return;
        }

        if (!string.Equals(provider.Name, KnownProviders.DeepSeek, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Handled = true;
        DeepSeekPeakOverrideRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ProviderCard_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Border card || DisplayMode == MiniDisplayMode) return;

        EnsureCardTransforms(card);
        if (card.RenderTransform is ScaleTransform scale)
        {
            var dur = TimeSpan.FromMilliseconds(160);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.035, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.035, dur) { EasingFunction = ease });
        }

        if (card.DataContext is ProviderUsageCard provider && provider.AccentBrush is SolidColorBrush accentSolid)
        {
            var c = accentSolid.Color;
            var glow = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, c.R, c.G, c.B));
            card.BorderBrush = glow;
            glow.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(System.Windows.Media.Color.FromArgb(200, c.R, c.G, c.B), TimeSpan.FromMilliseconds(160)));
        }

        var shimmer = FindCardShimmerOverlay(card);
        shimmer?.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)));
    }

    private void ProviderCard_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Border card || DisplayMode == MiniDisplayMode) return;

        if (card.RenderTransform is ScaleTransform scale)
        {
            var dur = TimeSpan.FromMilliseconds(300);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, dur) { EasingFunction = ease });
        }

        if (card.ReadLocalValue(BorderBrushProperty) is SolidColorBrush currentGlow)
        {
            var c = currentGlow.Color;
            var anim = new ColorAnimation(System.Windows.Media.Color.FromArgb(0, c.R, c.G, c.B), TimeSpan.FromMilliseconds(300));
            anim.Completed += (_, _) => card.ClearValue(BorderBrushProperty);
            currentGlow.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }

        var shimmer = FindCardShimmerOverlay(card);
        shimmer?.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300)));
    }

    private void ProviderCard_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Border card || DisplayMode == MiniDisplayMode) return;

        EnsureCardTransforms(card);
        var pos = e.GetPosition(card);
        var w = card.ActualWidth;
        var h = card.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Shimmer spotlight
        var shimmer = FindCardShimmerOverlay(card);
        if (shimmer is null) return;

        if (!_cardShimmerBrushCache.TryGetValue(card, out var brush))
        {
            brush = new RadialGradientBrush();
            brush.RadiusX = 0.7;
            brush.RadiusY = 0.7;
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.5));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
            _cardShimmerBrushCache[card] = brush;
            shimmer.Background = brush;
        }

        var ac = card.DataContext is ProviderUsageCard p && p.AccentBrush is SolidColorBrush ab
            ? ab.Color
            : System.Windows.Media.Colors.White;
        var relX = pos.X / w;
        var relY = pos.Y / h;

        brush.GradientOrigin = new System.Windows.Point(relX, relY);
        brush.Center         = new System.Windows.Point(relX, relY);
        brush.GradientStops[0].Color = System.Windows.Media.Color.FromArgb(38, ac.R, ac.G, ac.B);
        brush.GradientStops[1].Color = System.Windows.Media.Color.FromArgb(12, ac.R, ac.G, ac.B);
        brush.GradientStops[2].Color = System.Windows.Media.Color.FromArgb(0,  ac.R, ac.G, ac.B);
    }

    private static Border? FindCardShimmerOverlay(DependencyObject parent)
    {
        var n = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border b && "ShimmerOverlay".Equals(b.Tag)) return b;
            var found = FindCardShimmerOverlay(child);
            if (found != null) return found;
        }
        return null;
    }

}
