using AIUsageMonitor.Services;

namespace AIUsageMonitor;

public partial class App : System.Windows.Application
{
    private TrayIconService? _trayIconService;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        if (TryGetScreenshotPath(e.Args, out var screenshotPath))
        {
            ScreenshotService.SaveOverlayScreenshot(screenshotPath);
            Shutdown();
            return;
        }

        var logService = new AppLogService();
        var settingsService = new AppSettingsService();
        _trayIconService = new TrayIconService(new UsageAggregatorService(logService, settingsService), settingsService, logService);
        _trayIconService.ShowOverlay();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        base.OnExit(e);
    }

    private static bool TryGetScreenshotPath(string[] args, out string path)
    {
        path = string.Empty;

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--screenshot", StringComparison.OrdinalIgnoreCase))
            {
                path = args[index + 1];
                return !string.IsNullOrWhiteSpace(path);
            }
        }

        return false;
    }
}
