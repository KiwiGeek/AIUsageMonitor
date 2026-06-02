using System.IO;
using System.Text.Json;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

public sealed class AppSettingsService
{
    private const string FileName = "monitor.settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public string SettingsPath { get; }

    public AppSettingsService()
    {
        var currentDirectoryPath = Path.Combine(Environment.CurrentDirectory, FileName);
        var outputDirectoryPath = Path.Combine(AppContext.BaseDirectory, FileName);

        SettingsPath = File.Exists(outputDirectoryPath)
            ? outputDirectoryPath
            : currentDirectoryPath;
    }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            var defaultSettings = new AppSettings();
            Save(defaultSettings);
            return defaultSettings;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (JsonException)
        {
            return RecoverFromInvalidSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private AppSettings RecoverFromInvalidSettings()
    {
        TryBackupInvalidSettingsFile();

        var defaultSettings = new AppSettings();
        Save(defaultSettings);
        return defaultSettings;
    }

    private void TryBackupInvalidSettingsFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(SettingsPath);
            var extension = Path.GetExtension(SettingsPath);
            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            var backupFileName = $"{fileNameWithoutExtension}.invalid.{timestamp}{extension}";
            var backupPath = directory is null
                ? backupFileName
                : Path.Combine(directory, backupFileName);

            File.Copy(SettingsPath, backupPath, overwrite: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
