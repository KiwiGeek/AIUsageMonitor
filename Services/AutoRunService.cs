using Microsoft.Win32;

namespace AIUsageMonitor.Services;

public static class AutoRunService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return HasStartupValue(key, AppMetadata.StartupEntryName);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            key.SetValue(AppMetadata.StartupEntryName, QuoteExecutablePath(GetExecutablePath()), RegistryValueKind.String);
            return;
        }

        key.DeleteValue(AppMetadata.StartupEntryName, throwOnMissingValue: false);
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
    }

    private static bool HasStartupValue(RegistryKey? key, string valueName)
    {
        var value = key?.GetValue(valueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string QuoteExecutablePath(string path)
    {
        return path.StartsWith('"') ? path : $"\"{path}\"";
    }
}
