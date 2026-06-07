using System.Windows;
using AIUsageMonitor.Models;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.Views;

public partial class LogWindow : FluentDialogWindow
{
    private readonly AppLogService _logService;

    public event EventHandler? LogsCleared;

    public LogWindow(AppLogService logService)
    {
        InitializeComponent();
        _logService = logService;
        DataContext = logService.Entries;
        LogPathTextBlock.Text = logService.LogPath;
    }

    private void AddFakeEntriesButtonOnClick(object sender, RoutedEventArgs e)
    {
        _logService.AddSampleEntries();
    }

    private void ClearButtonOnClick(object sender, RoutedEventArgs e)
    {
        _logService.Clear();
        LogsCleared?.Invoke(this, EventArgs.Empty);
    }

    private void CopySelectedButtonOnClick(object sender, RoutedEventArgs e)
    {
        var selectedEntries = LogsDataGrid.SelectedItems
            .OfType<AppLogEntry>()
            .ToList();

        CopyEntries(selectedEntries.Count == 0 ? _logService.Entries : selectedEntries);
    }

    private void CopyAllButtonOnClick(object sender, RoutedEventArgs e)
    {
        CopyEntries(_logService.Entries);
    }

    private static void CopyEntries(IEnumerable<AppLogEntry> entries)
    {
        var lines = entries.Select(entry =>
            $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss zzz}\t{entry.Level}\t{entry.Source}\t{entry.Message}");
        var text = "Time\tLevel\tSource\tMessage" + Environment.NewLine + string.Join(Environment.NewLine, lines);

        if (!string.IsNullOrWhiteSpace(text))
        {
            System.Windows.Clipboard.SetText(text);
        }
    }
}
