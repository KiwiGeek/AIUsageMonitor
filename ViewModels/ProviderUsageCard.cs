using System.ComponentModel;
using System.Runtime.CompilerServices;
using AIUsageMonitor.Models;
using MediaBrush = System.Windows.Media.Brush;

namespace AIUsageMonitor.ViewModels;

public sealed class ProviderUsageCard : INotifyPropertyChanged
{
    private bool _isChecking;
    private string _checkedText = string.Empty;
    private readonly string _summaryText;

    public ProviderUsageCard(ProviderUsage usage, bool waifuEnabled = false)
    {
        ShortName = string.IsNullOrWhiteSpace(usage.Name) ? "Provider" : usage.Name.Trim();
        Name = FormatDisplayName(ShortName, usage.PlanName);
        Source = usage.Source;
        StatusMessage = usage.StatusMessage;
        IsUnavailable = usage.IsUnavailable;
        LastCheckedAt = usage.LastCheckedAt;
        AccentBrush = UsageBrushes.ProviderAccent(usage.Name);
        BackgroundImage = waifuEnabled ? ShortName switch
        {
            "Anthropic"      => "pack://application:,,,/Assets/claude-girl.png",
            "Cursor"         => "pack://application:,,,/Assets/cursor-girl.png",
            "Gemini"         => "pack://application:,,,/Assets/gemini-girl.png",
            "OpenAI"         => "pack://application:,,,/Assets/codex-girl.png",
            "DeepSeek"       => "pack://application:,,,/Assets/deepseek-girl.png",
            "GitHub Copilot" => "pack://application:,,,/Assets/copilot-girl.png",
            _                => null
        } : null;
        Windows = usage.Windows.Select(window => new UsageWindowDisplay(window)).ToList();
        // Use the minimum remaining percent across non-exhausted windows so that a single
        // exhausted bucket (e.g. Gemini Pro at 0%) doesn't mark the whole provider as
        // Exhausted when other buckets (e.g. Flash) still have quota. Only show Exhausted
        // when every window is at zero.
        var nonExhaustedWindows = Windows.Where(w => w.RemainingPercent > 0).ToList();
        PrimaryRemainingPercent = nonExhaustedWindows.Count > 0
            ? nonExhaustedWindows.Min(w => w.RemainingPercent)
            : 0;

        var status = usage.IsUnavailable || Windows.Count == 0
            ? UsageStatus.Unavailable
            : UsageStatus.FromRemainingPercent(PrimaryRemainingPercent);
        OverallStatusLabel = status.Label;
        OverallStatusBrush = status.Foreground;
        OverallStatusBackground = status.Background;
        SummaryProgressBrush = status.Foreground;
        _summaryText = usage.IsUnavailable || Windows.Count == 0
            ? $"{ShortName} - unavailable"
            : $"{ShortName} - {PrimaryRemainingPercent:0}%";
        RefreshCheckedText();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string ShortName { get; }

    public string Source { get; }

    public string StatusMessage { get; }

    public bool IsUnavailable { get; }

    public DateTimeOffset? LastCheckedAt { get; }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetField(ref _isChecking, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SummaryText)));
            }
        }
    }

    public string CheckedText
    {
        get => _checkedText;
        private set => SetField(ref _checkedText, value);
    }

    public bool HasWindows => Windows.Count > 0;

    public double PrimaryRemainingPercent { get; }

    public string SummaryText => IsChecking ? $"{ShortName} - checking" : _summaryText;

    public MediaBrush SummaryProgressBrush { get; }

    public MediaBrush AccentBrush { get; }

    public string? BackgroundImage { get; }

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
