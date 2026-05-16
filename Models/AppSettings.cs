namespace AIUsageMonitor.Models;

public sealed class AppSettings
{
    public const int DefaultUpdateIntervalMinutes = 20;
    public const int MinimumUpdateIntervalMinutes = 1;
    public const int MaximumUpdateIntervalMinutes = 1440;
    public const double DefaultCursorIncludedBudgetDollars = 20;

    public int UpdateIntervalMinutes { get; set; } = DefaultUpdateIntervalMinutes;

    public string CursorApiKey { get; set; } = string.Empty;

    public double CursorIncludedBudgetDollars { get; set; } = DefaultCursorIncludedBudgetDollars;

    public string CursorDashboardCookieHeaderProtected { get; set; } = string.Empty;

    public DateTimeOffset? CursorDashboardCookiesCapturedAt { get; set; }

    public bool ClaudeStatusExporterEnabled { get; set; } = true;

    public bool AutoRunAtLoginEnabled { get; set; }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            UpdateIntervalMinutes = UpdateIntervalMinutes,
            CursorApiKey = CursorApiKey,
            CursorIncludedBudgetDollars = CursorIncludedBudgetDollars,
            CursorDashboardCookieHeaderProtected = CursorDashboardCookieHeaderProtected,
            CursorDashboardCookiesCapturedAt = CursorDashboardCookiesCapturedAt,
            ClaudeStatusExporterEnabled = ClaudeStatusExporterEnabled,
            AutoRunAtLoginEnabled = AutoRunAtLoginEnabled
        };
    }

    public void Normalize()
    {
        UpdateIntervalMinutes = Math.Clamp(
            UpdateIntervalMinutes,
            MinimumUpdateIntervalMinutes,
            MaximumUpdateIntervalMinutes);

        CursorApiKey = CursorApiKey.Trim();

        if (CursorIncludedBudgetDollars <= 0 || double.IsNaN(CursorIncludedBudgetDollars) || double.IsInfinity(CursorIncludedBudgetDollars))
        {
            CursorIncludedBudgetDollars = DefaultCursorIncludedBudgetDollars;
        }
    }
}
