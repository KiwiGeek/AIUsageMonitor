using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

public sealed class AppLogService
{
    private const string FileName = "monitor.log.jsonl";
    private const int MaxEntriesInMemory = 300;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    public AppLogService()
    {
        LogPath = Path.Combine(Environment.CurrentDirectory, FileName);
        LoadRecentEntries();
    }

    public string LogPath { get; }

    public ObservableCollection<AppLogEntry> Entries { get; } = [];

    public int RecentErrorCount => Entries.Count(entry => entry.Level is "Error" or "Warning");

    public void Info(string source, string message) => Add("Info", source, message);

    public void Warning(string source, string message) => Add("Warning", source, message);

    public void Error(string source, string message) => Add("Error", source, message);

    public void AddSampleEntries()
    {
        Info("App", "Sample info entry for log UI testing.");
        Warning("OpenAI", "Rate limit approaching: 85% of 5h window used.");
        Error("Anthropic", "Collector returned HTTP 503; will retry on next refresh.");
        Info("Cursor", "Dashboard session refreshed successfully.");
        Warning("Gemini", "Quota check skipped: API key not configured.");
    }

    public void Clear()
    {
        Entries.Clear();

        try
        {
            File.WriteAllText(LogPath, string.Empty);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Add(string level, string source, string message)
    {
        var entry = new AppLogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Source = source,
            Message = message
        };

        Entries.Insert(0, entry);

        while (Entries.Count > MaxEntriesInMemory)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }

        try
        {
            File.AppendAllText(LogPath, JsonSerializer.Serialize(entry, SerializerOptions) + Environment.NewLine);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void LoadRecentEntries()
    {
        if (!File.Exists(LogPath))
        {
            return;
        }

        try
        {
            var lines = File.ReadLines(LogPath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .TakeLast(MaxEntriesInMemory)
                .ToList();

            for (var index = lines.Count - 1; index >= 0; index--)
            {
                var entry = JsonSerializer.Deserialize<AppLogEntry>(lines[index]);
                if (entry is not null)
                {
                    Entries.Add(entry);
                }
            }
        }
        catch
        {
            Entries.Clear();
        }
    }
}
