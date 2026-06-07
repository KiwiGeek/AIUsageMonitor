namespace AIUsageMonitor.Models;

public sealed class AppSettings
{
    public const int DefaultUpdateIntervalMinutes = 20;
    public const int MinimumUpdateIntervalMinutes = 1;
    public const int MaximumUpdateIntervalMinutes = 1440;
    public const double DefaultCursorIncludedBudgetDollars = 20;
    public const double DefaultWaifuSquadOpacity = 0.4;
    public const double MinimumWaifuSquadOpacity = 0.05;
    public const double MaximumWaifuSquadOpacity = 1.0;
    public const string CursorUsageModePersonal = "PersonalSubscription";
    public const string CursorUsageModeTeamsApiKey = "TeamsApiKey";

    public int UpdateIntervalMinutes { get; set; } = DefaultUpdateIntervalMinutes;

    public Dictionary<string, bool> EnabledProviders { get; set; } = CreateDefaultEnabledProviders();

    public string CursorUsageMode { get; set; } = string.Empty;

    public string CursorApiKey { get; set; } = string.Empty;

    public double CursorIncludedBudgetDollars { get; set; } = DefaultCursorIncludedBudgetDollars;

    public string CursorDashboardCookieHeaderProtected { get; set; } = string.Empty;

    public DateTimeOffset? CursorDashboardCookiesCapturedAt { get; set; }

    public string DeepSeekApiKey { get; set; } = string.Empty;

    public Dictionary<string, decimal> DeepSeekLastBalances { get; set; } = [];

    public string GitHubCopilotApiKey { get; set; } = string.Empty;

    public bool WaifuSquadEnabled { get; set; }

    public double WaifuSquadOpacity { get; set; } = DefaultWaifuSquadOpacity;

    public bool ClaudeStatusExporterEnabled { get; set; } = true;

    public bool AutoRunAtLoginEnabled { get; set; }

    public bool OverlaySnapToScreenEnabled { get; set; } = true;

    public bool OverlaySnapReserveScreenSpaceEnabled { get; set; }

    public bool OverlaySnapAutoHideWhenSnappedEnabled { get; set; }

    public bool CloseToSystemTray { get; set; } = true;

    public bool MinimizeToSystemTray { get; set; } = true;

    public OverlayWindowPlacement OverlayWindowPlacement { get; set; } = new();

    public AppSettings Clone()
    {
        return new AppSettings
        {
            UpdateIntervalMinutes = UpdateIntervalMinutes,
            EnabledProviders = NormalizeEnabledProviders(EnabledProviders),
            CursorUsageMode = CursorUsageMode,
            CursorApiKey = CursorApiKey,
            CursorIncludedBudgetDollars = CursorIncludedBudgetDollars,
            CursorDashboardCookieHeaderProtected = CursorDashboardCookieHeaderProtected,
            CursorDashboardCookiesCapturedAt = CursorDashboardCookiesCapturedAt,
            DeepSeekApiKey = DeepSeekApiKey,
            DeepSeekLastBalances = new Dictionary<string, decimal>(DeepSeekLastBalances),
            GitHubCopilotApiKey = GitHubCopilotApiKey,
            WaifuSquadEnabled = WaifuSquadEnabled,
            WaifuSquadOpacity = WaifuSquadOpacity,
            ClaudeStatusExporterEnabled = ClaudeStatusExporterEnabled,
            AutoRunAtLoginEnabled = AutoRunAtLoginEnabled,
            OverlaySnapToScreenEnabled = OverlaySnapToScreenEnabled,
            OverlaySnapReserveScreenSpaceEnabled = OverlaySnapReserveScreenSpaceEnabled,
            OverlaySnapAutoHideWhenSnappedEnabled = OverlaySnapAutoHideWhenSnappedEnabled,
            CloseToSystemTray = CloseToSystemTray,
            MinimizeToSystemTray = MinimizeToSystemTray,
            OverlayWindowPlacement = OverlayWindowPlacement?.Clone() ?? new OverlayWindowPlacement()
        };
    }

    public bool IsProviderEnabled(string providerName)
    {
        return !EnabledProviders.TryGetValue(providerName, out var isEnabled) || isEnabled;
    }

    public void SetProviderEnabled(string providerName, bool isEnabled)
    {
        EnabledProviders[providerName] = isEnabled;
    }

    public void Normalize()
    {
        UpdateIntervalMinutes = Math.Clamp(
            UpdateIntervalMinutes,
            MinimumUpdateIntervalMinutes,
            MaximumUpdateIntervalMinutes);

        EnabledProviders = NormalizeEnabledProviders(EnabledProviders);
        CursorUsageMode = NormalizeCursorUsageMode();

        CursorApiKey = CursorApiKey.Trim();
        DeepSeekApiKey = DeepSeekApiKey.Trim();
        DeepSeekLastBalances ??= [];
        GitHubCopilotApiKey = GitHubCopilotApiKey.Trim();

        if (CursorIncludedBudgetDollars <= 0 || double.IsNaN(CursorIncludedBudgetDollars) || double.IsInfinity(CursorIncludedBudgetDollars))
        {
            CursorIncludedBudgetDollars = DefaultCursorIncludedBudgetDollars;
        }

        if (OverlaySnapAutoHideWhenSnappedEnabled && OverlaySnapReserveScreenSpaceEnabled)
        {
            OverlaySnapAutoHideWhenSnappedEnabled = false;
        }

        if (WaifuSquadOpacity < MinimumWaifuSquadOpacity ||
            WaifuSquadOpacity > MaximumWaifuSquadOpacity ||
            double.IsNaN(WaifuSquadOpacity) ||
            double.IsInfinity(WaifuSquadOpacity))
        {
            WaifuSquadOpacity = DefaultWaifuSquadOpacity;
        }

        OverlayWindowPlacement ??= new OverlayWindowPlacement();
        OverlayWindowPlacement.Normalize();
    }

    private string NormalizeCursorUsageMode()
    {
        if (string.Equals(CursorUsageMode, CursorUsageModePersonal, StringComparison.OrdinalIgnoreCase))
        {
            return CursorUsageModePersonal;
        }

        if (string.Equals(CursorUsageMode, CursorUsageModeTeamsApiKey, StringComparison.OrdinalIgnoreCase))
        {
            return CursorUsageModeTeamsApiKey;
        }

        if (!string.IsNullOrWhiteSpace(CursorDashboardCookieHeaderProtected))
        {
            return CursorUsageModePersonal;
        }

        return string.IsNullOrWhiteSpace(CursorApiKey)
            ? CursorUsageModePersonal
            : CursorUsageModeTeamsApiKey;
    }

    private static Dictionary<string, bool> CreateDefaultEnabledProviders()
    {
        return KnownProviders.All.ToDictionary(
            providerName => providerName,
            _ => true,
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, bool> NormalizeEnabledProviders(Dictionary<string, bool>? enabledProviders)
    {
        var normalizedProviders = CreateDefaultEnabledProviders();

        if (enabledProviders is not null)
        {
            foreach (var pair in enabledProviders)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    normalizedProviders[pair.Key.Trim()] = pair.Value;
                }
            }
        }

        return normalizedProviders;
    }
}

public sealed class OverlayWindowPlacement
{
    public double? Left { get; set; }

    public double? Top { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }

    /// <summary>Last free-floating window width (DIP) before edge snap.</summary>
    public double? FloatingWidth { get; set; }

    /// <summary>Last free-floating window height (DIP) before edge snap.</summary>
    public double? FloatingHeight { get; set; }

    /// <summary>Last free-floating window left (DIP) before edge snap.</summary>
    public double? FloatingLeft { get; set; }

    /// <summary>Last free-floating window top (DIP) before edge snap.</summary>
    public double? FloatingTop { get; set; }

    public OverlayEdgeSnap SnapEdge { get; set; } = OverlayEdgeSnap.None;

    public string? SnapMonitorDeviceName { get; set; }

    public OverlayWindowPlacement Clone()
    {
        return new OverlayWindowPlacement
        {
            Left = Left,
            Top = Top,
            Width = Width,
            Height = Height,
            FloatingWidth = FloatingWidth,
            FloatingHeight = FloatingHeight,
            FloatingLeft = FloatingLeft,
            FloatingTop = FloatingTop,
            SnapEdge = SnapEdge,
            SnapMonitorDeviceName = SnapMonitorDeviceName
        };
    }

    public void Normalize()
    {
        Left = NormalizeFiniteValue(Left);
        Top = NormalizeFiniteValue(Top);
        Width = NormalizePositiveValue(Width);
        Height = NormalizePositiveValue(Height);
        FloatingWidth = NormalizePositiveValue(FloatingWidth);
        FloatingHeight = NormalizePositiveValue(FloatingHeight);
        FloatingLeft = NormalizeFiniteValue(FloatingLeft);
        FloatingTop = NormalizeFiniteValue(FloatingTop);
        SnapMonitorDeviceName = string.IsNullOrWhiteSpace(SnapMonitorDeviceName)
            ? null
            : SnapMonitorDeviceName.Trim();

        if (SnapEdge == OverlayEdgeSnap.None)
        {
            SnapMonitorDeviceName = null;
        }
    }

    private static double? NormalizePositiveValue(double? value)
    {
        var normalizedValue = NormalizeFiniteValue(value);
        return normalizedValue is > 0 ? normalizedValue : null;
    }

    private static double? NormalizeFiniteValue(double? value)
    {
        return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
            ? value
            : null;
    }
}
