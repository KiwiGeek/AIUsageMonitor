using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AIUsageMonitor.Models;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.ViewModels;

public sealed class UsageOverlayViewModel : INotifyPropertyChanged
{
    private string _generatedAtText = "Waiting for fake usage data";
    private string _sourcePathText = string.Empty;
    private string _autoRefreshText = "Auto refresh every 20 minutes";
    private string _logSummaryText = "No recent errors";
    private string _errorMessage = string.Empty;
    private bool _isErrorDismissed;
    private double _waifuBackgroundOpacity = 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProviderUsageCard> Providers { get; } = [];

    public string AppVersionText => AppMetadata.VersionText;

    public string GeneratedAtText
    {
        get => _generatedAtText;
        private set => SetField(ref _generatedAtText, value);
    }

    public string SourcePathText
    {
        get => _sourcePathText;
        private set => SetField(ref _sourcePathText, value);
    }

    public string AutoRefreshText
    {
        get => _autoRefreshText;
        private set => SetField(ref _autoRefreshText, value);
    }

    public string LogSummaryText
    {
        get => _logSummaryText;
        private set => SetField(ref _logSummaryText, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!string.IsNullOrEmpty(value))
            {
                _isErrorDismissed = false;
            }

            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool IsErrorDismissed
    {
        get => _isErrorDismissed;
        private set
        {
            if (SetField(ref _isErrorDismissed, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage) && !IsErrorDismissed;

    public void DismissError() { IsErrorDismissed = true; }

    public bool HasProviders => Providers.Count > 0;

    public double WaifuBackgroundOpacity
    {
        get => _waifuBackgroundOpacity;
        private set
        {
            var normalizedOpacity = Math.Clamp(value, 0, 1);
            SetField(ref _waifuBackgroundOpacity, normalizedOpacity);
        }
    }

    public void ApplyWaifuAppearance(bool waifuEnabled, double waifuOpacity)
    {
        WaifuBackgroundOpacity = waifuEnabled ? waifuOpacity : 1;

        foreach (var provider in Providers)
        {
            provider.ApplyWaifuAppearance(waifuEnabled);
        }
    }

    public void ApplySnapshot(UsageSnapshot snapshot, string dataPath, bool waifuEnabled = false)
    {
        Providers.Clear();

        foreach (var provider in snapshot.Providers)
        {
            Providers.Add(new ProviderUsageCard(provider, waifuEnabled));
        }

        OnPropertyChanged(nameof(HasProviders));
        var generatedAt = snapshot.GeneratedAt == default ? DateTimeOffset.Now : snapshot.GeneratedAt;
        GeneratedAtText = $"Updated {generatedAt.ToLocalTime():MMM d, yyyy h:mm tt}";
        SourcePathText = string.IsNullOrWhiteSpace(snapshot.Source) ? dataPath : snapshot.Source;
        ErrorMessage = string.Empty;
    }

    public void SetChecking(IEnumerable<string> providerNames)
    {
        var names = providerNames.ToList();
        if (names.Count == 0)
        {
            Providers.Clear();
            OnPropertyChanged(nameof(HasProviders));
            return;
        }

        if (ProviderListChanged(names))
        {
            Providers.Clear();
            foreach (var name in names)
            {
                var card = new ProviderUsageCard(new ProviderUsage
                {
                    Name = name,
                    Source = "Live/local collectors",
                    StatusMessage = "Checking usage data...",
                    IsUnavailable = true
                });
                card.SetChecking(true);
                Providers.Add(card);
            }

            OnPropertyChanged(nameof(HasProviders));
            return;
        }

        foreach (var provider in Providers)
        {
            provider.SetChecking(true);
        }
    }

    private bool ProviderListChanged(IReadOnlyCollection<string> providerNames)
    {
        if (Providers.Count != providerNames.Count)
        {
            return true;
        }

        var existingProviderNames = Providers
            .Select(provider => provider.ShortName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return providerNames.Any(providerName => !existingProviderNames.Contains(providerName));
    }

    public void SetCheckingSingle(string providerName)
    {
        var card = Providers.FirstOrDefault(p =>
            string.Equals(p.ShortName, providerName, StringComparison.OrdinalIgnoreCase));
        card?.SetChecking(true);
    }

    public void ApplyProviderUpdate(ProviderUsage usage, bool waifuEnabled = false)
    {
        var index = -1;
        for (var i = 0; i < Providers.Count; i++)
        {
            if (string.Equals(Providers[i].ShortName, usage.Name, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        var card = new ProviderUsageCard(usage, waifuEnabled);
        if (index >= 0)
        {
            Providers[index] = card;
        }
        else
        {
            Providers.Add(card);
            OnPropertyChanged(nameof(HasProviders));
        }
    }

    public void RemoveProvider(string providerName)
    {
        var card = Providers.FirstOrDefault(p =>
            string.Equals(p.ShortName, providerName, StringComparison.OrdinalIgnoreCase));
        if (card is null) return;
        Providers.Remove(card);
        OnPropertyChanged(nameof(HasProviders));
    }

    public void RefreshRelativeTimes()
    {
        foreach (var provider in Providers)
        {
            provider.RefreshCheckedText();
        }
    }

    public void SetError(string message, string dataPath)
    {
        ErrorMessage = message;
        SourcePathText = dataPath;
    }

    public void SetAutoRefreshInterval(int minutes)
    {
        AutoRefreshText = minutes == 1
            ? "Auto refresh every minute"
            : $"Auto refresh every {minutes} minutes";
    }

    public void SetLogSummary(int errorCount)
    {
        LogSummaryText = errorCount == 0
            ? "No recent errors"
            : $"{errorCount} recent error{(errorCount == 1 ? string.Empty : "s")}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
