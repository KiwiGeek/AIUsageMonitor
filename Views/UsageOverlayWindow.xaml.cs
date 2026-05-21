using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
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
    private const double FullMinimumVerticalInset = 175;
    private const double FullMinimumRowHeight = 265;
    private const double CompactMinimumVerticalInset = 36;
    private const double CompactMinimumRowHeight = 100;
    private const double MiniMinimumVerticalInset = 22;
    private const double MiniMinimumRowHeight = 36;
    private const double WindowHorizontalGutter = 36;
    private const double BodyHorizontalGutter = 12;
    private const double CardHostHorizontalFudge = 10;
    private const double MiniHorizontalChromeInset = WindowHorizontalGutter + BodyHorizontalGutter;
    private const double CompactHorizontalChromeInset = WindowHorizontalGutter + BodyHorizontalGutter;
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
        providerCount = Math.Max(0, providerCount);
        var displayMode = GetDisplayMode(width, height, providerCount);
        var showCompactButtons = displayMode == CompactDisplayMode && providerCount == 0;

        if (displayMode == CompactDisplayMode && providerCount > 0)
        {
            var withButtons = CalculateCardGrid(displayMode, width, height, providerCount, true);
            showCompactButtons = withButtons.Columns > 1;
        }

        var grid = CalculateCardGrid(displayMode, width, height, providerCount, showCompactButtons);
        return new ResponsiveLayout(displayMode, grid.Rows, grid.CardSlotWidth, grid.ProvidersListWidth, showCompactButtons);
    }

    private static double GetMinimumWindowHeight(string displayMode, int rows)
    {
        if (rows <= 0)
        {
            return displayMode switch
            {
                MiniDisplayMode => MiniEmptyMinimumHeight,
                CompactDisplayMode => CompactEmptyMinimumHeight,
                _ => FullEmptyMinimumHeight
            };
        }

        return displayMode switch
        {
            MiniDisplayMode => MiniMinimumVerticalInset + rows * MiniMinimumRowHeight,
            CompactDisplayMode => CompactMinimumVerticalInset + rows * CompactMinimumRowHeight,
            _ => FullMinimumVerticalInset + rows * FullMinimumRowHeight
        };
    }

    private static int ChooseColumnsForLayout(int providerCount, int maxColumns)
    {
        var bestColumns = 1;
        var bestRows = int.MaxValue;

        for (var columns = 1; columns <= maxColumns; columns++)
        {
            var rows = (int)Math.Ceiling(providerCount / (double)columns);

            if (rows < bestRows)
            {
                bestColumns = columns;
                bestRows = rows;
            }
        }

        return bestColumns;
    }

    private static string GetDisplayMode(double width, double height, int providerCount)
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
        if (providerCount <= 0)
        {
            return new CardGrid(0, 0, availableWidth, availableWidth);
        }

        var minimumCardWidth = displayMode switch
        {
            MiniDisplayMode => MiniMinimumCardWidth,
            CompactDisplayMode => CompactMinimumCardWidth,
            _ => FullMinimumCardWidth
        };
        var maxColumns = Math.Clamp((int)Math.Floor(availableWidth / minimumCardWidth), 1, providerCount);
        var columns = ChooseColumnsForLayout(providerCount, maxColumns);
        var rows = (int)Math.Ceiling(providerCount / (double)columns);
        var cardSlotWidth = Math.Max(1, Math.Floor(availableWidth / columns));

        return new CardGrid(columns, rows, cardSlotWidth, cardSlotWidth * columns);
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
        int Rows,
        double CardSlotWidth,
        double ProvidersListWidth,
        bool ShowCompactButtons);

    private readonly record struct CardGrid(
        int Columns,
        int Rows,
        double CardSlotWidth,
        double ProvidersListWidth);

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
