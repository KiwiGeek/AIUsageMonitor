using System.ComponentModel;
using System.Runtime.CompilerServices;
using AIUsageMonitor.Models;
using MediaBrush = System.Windows.Media.Brush;

namespace AIUsageMonitor.ViewModels;

public sealed class ProviderUsageCard : INotifyPropertyChanged
{
    private bool _isChecking;
    private string _checkedText = string.Empty;

    public ProviderUsageCard(ProviderUsage usage)
    {
        Name = FormatDisplayName(usage.Name, usage.PlanName);
        Source = usage.Source;
        StatusMessage = usage.StatusMessage;
        IsUnavailable = usage.IsUnavailable;
        LastCheckedAt = usage.LastCheckedAt;
        AccentBrush = UsageBrushes.ProviderAccent(usage.Name);
        Windows = usage.Windows.Select(window => new UsageWindowDisplay(window)).ToList();

        var status = usage.IsUnavailable || Windows.Count == 0
            ? UsageStatus.Unavailable
            : UsageStatus.FromRemainingPercent(Windows.Min(window => window.RemainingPercent));
        OverallStatusLabel = status.Label;
        OverallStatusBrush = status.Foreground;
        OverallStatusBackground = status.Background;
        RefreshCheckedText();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string Source { get; }

    public string StatusMessage { get; }

    public bool IsUnavailable { get; }

    public DateTimeOffset? LastCheckedAt { get; }

    public bool IsChecking
    {
        get => _isChecking;
        private set => SetField(ref _isChecking, value);
    }

    public string CheckedText
    {
        get => _checkedText;
        private set => SetField(ref _checkedText, value);
    }

    public bool HasWindows => Windows.Count > 0;

    public MediaBrush AccentBrush { get; }

    public IReadOnlyList<UsageWindowDisplay> Windows { get; }

    public string OverallStatusLabel { get; }

    public MediaBrush OverallStatusBrush { get; }

    public MediaBrush OverallStatusBackground { get; }

    public void SetChecking(bool isChecking)
    {
        IsChecking = isChecking;
        RefreshCheckedText();
    }

    public void RefreshCheckedText()
    {
        if (IsChecking)
        {
            CheckedText = "Checking...";
            return;
        }

        if (LastCheckedAt is null)
        {
            CheckedText = "Not checked yet";
            return;
        }

        var elapsed = DateTimeOffset.Now - LastCheckedAt.Value;
        CheckedText = elapsed.TotalSeconds switch
        {
            < 45 => "Checked now",
            < 90 => "Checked 1m ago",
            < 3600 => $"Checked {(int)Math.Round(elapsed.TotalMinutes)}m ago",
            < 5400 => "Checked 1h ago",
            < 86400 => $"Checked {(int)Math.Round(elapsed.TotalHours)}h ago",
            _ => $"Checked {(int)Math.Round(elapsed.TotalDays)}d ago"
        };
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private static string FormatDisplayName(string providerName, string planName)
    {
        if (string.IsNullOrWhiteSpace(planName) ||
            providerName.Contains(planName, StringComparison.OrdinalIgnoreCase))
        {
            return providerName;
        }

        return $"{providerName} ({planName})";
    }
}
