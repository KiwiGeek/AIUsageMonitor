using System.Globalization;
using AIUsageMonitor.Services;

namespace AIUsageMonitor;

public partial class App : System.Windows.Application
{
    private TrayIconService? _trayIconService;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        if (TryGetScreenshotOptions(e.Args, out var screenshotPath, out var screenshotWidth, out var screenshotHeight))
        {
            try
            {
                ScreenshotService.SaveOverlayScreenshot(screenshotPath, screenshotWidth, screenshotHeight);
                Shutdown();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Could not save screenshot: {exception.Message}");
                Shutdown(1);
            }

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

    private static bool TryGetScreenshotOptions(string[] args, out string path, out double? width, out double? height)
    {
        path = string.Empty;
        width = null;
        height = null;

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--screenshot", StringComparison.OrdinalIgnoreCase))
            {
                path = args[index + 1];
                index++;
                continue;
            }

            if (string.Equals(args[index], "--screenshot-size", StringComparison.OrdinalIgnoreCase) &&
                TryParseScreenshotSize(args[index + 1], out var parsedWidth, out var parsedHeight))
            {
                width = parsedWidth;
                height = parsedHeight;
                index++;
                continue;
            }

            if (string.Equals(args[index], "--screenshot-width", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWidthOnly))
            {
                width = parsedWidthOnly;
                index++;
                continue;
            }

            if (string.Equals(args[index], "--screenshot-height", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedHeightOnly))
            {
                height = parsedHeightOnly;
                index++;
            }
        }

        return !string.IsNullOrWhiteSpace(path);
    }

    private static bool TryParseScreenshotSize(string value, out double width, out double height)
    {
        width = 0;
        height = 0;
        var separatorIndex = value.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
        {
            return false;
        }

        return double.TryParse(value[..separatorIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out width) &&
               double.TryParse(value[(separatorIndex + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out height);
    }
}
