using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

/// <summary>
/// Single shared observable wrapper around the provider-specific subset of AppSettings.
/// All settings windows bind to this directly — no copies, no manual sync needed.
/// </summary>
public sealed class AppSettingsStore : INotifyPropertyChanged, IDisposable
{
    private readonly AppSettingsService _settingsService;
    private readonly AppLogService _logService;
    private readonly DispatcherTimer _saveTimer;
    private bool _disposed;

    private bool _deepSeekEnabled;
    private string _deepSeekApiKey = string.Empty;
    private string _deepSeekPeakBalanceText = string.Empty;
    private string _deepSeekCurrency = "USD";
    private string _deepSeekPeakValidationMessage = string.Empty;

    private bool _gitHubCopilotEnabled;
    private string _gitHubCopilotApiKey = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SettingsChanged;

    public AppSettingsStore(AppSettingsService settingsService, AppLogService logService)
    {
        _settingsService = settingsService;
        _logService = logService;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += SaveTimerOnTick;
        Load();
    }

    // ── DeepSeek ──────────────────────────────────────────────────────────────

    public bool DeepSeekEnabled
    {
        get => _deepSeekEnabled;
        set
        {
            if (_deepSeekEnabled == value) return;
            _deepSeekEnabled = value;
            SaveImmediate();
            OnPropertyChanged();
        }
    }

    public string DeepSeekApiKey
    {
        get => _deepSeekApiKey;
        set
        {
            if (_deepSeekApiKey == value) return;
            _deepSeekApiKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DeepSeekApiKeyStatus));
            QueueSave();
        }
    }

    public string DeepSeekPeakBalanceText
    {
        get => _deepSeekPeakBalanceText;
        set
        {
            if (_deepSeekPeakBalanceText == value) return;
            _deepSeekPeakBalanceText = value;
            OnPropertyChanged();
            ValidateDeepSeekPeak();
            QueueSave();
        }
    }

    public string DeepSeekCurrency => _deepSeekCurrency;
    public string DeepSeekPeakLabel => $"Peak balance ({_deepSeekCurrency})";
    public string DeepSeekApiKeyStatus => string.IsNullOrWhiteSpace(_deepSeekApiKey) ? "Not configured" : "Configured";

    public string DeepSeekPeakValidationMessage
    {
        get => _deepSeekPeakValidationMessage;
        private set
        {
            if (_deepSeekPeakValidationMessage == value) return;
            _deepSeekPeakValidationMessage = value;
            OnPropertyChanged();
        }
    }

    // ── GitHub Copilot ────────────────────────────────────────────────────────

    public bool GitHubCopilotEnabled
    {
        get => _gitHubCopilotEnabled;
        set
        {
            if (_gitHubCopilotEnabled == value) return;
            _gitHubCopilotEnabled = value;
            SaveImmediate();
            OnPropertyChanged();
        }
    }

    public string GitHubCopilotApiKey
    {
        get => _gitHubCopilotApiKey;
        set
        {
            if (_gitHubCopilotApiKey == value) return;
            _gitHubCopilotApiKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GitHubCopilotApiKeyStatus));
            QueueSave();
        }
    }

    public string GitHubCopilotApiKeyStatus => string.IsNullOrWhiteSpace(_gitHubCopilotApiKey) ? "Not configured" : "Configured";

    // ── Load / Save ───────────────────────────────────────────────────────────

    public void Load()
    {
        var s = _settingsService.Load();

        _deepSeekEnabled = s.IsProviderEnabled(KnownProviders.DeepSeek);
        _deepSeekApiKey = s.DeepSeekApiKey;
        _deepSeekCurrency = s.DeepSeekLastBalances.Keys.FirstOrDefault() ?? "USD";
        _deepSeekPeakBalanceText = s.DeepSeekLastBalances.TryGetValue(_deepSeekCurrency, out var peak) && peak > 0
            ? peak.ToString("0.00", CultureInfo.InvariantCulture)
            : string.Empty;

        _gitHubCopilotEnabled = s.IsProviderEnabled(KnownProviders.GitHubCopilot);
        _gitHubCopilotApiKey = s.GitHubCopilotApiKey;

        OnPropertyChanged(nameof(DeepSeekEnabled));
        OnPropertyChanged(nameof(DeepSeekApiKey));
        OnPropertyChanged(nameof(DeepSeekApiKeyStatus));
        OnPropertyChanged(nameof(DeepSeekPeakBalanceText));
        OnPropertyChanged(nameof(DeepSeekCurrency));
        OnPropertyChanged(nameof(DeepSeekPeakLabel));
        OnPropertyChanged(nameof(GitHubCopilotEnabled));
        OnPropertyChanged(nameof(GitHubCopilotApiKey));
        OnPropertyChanged(nameof(GitHubCopilotApiKeyStatus));
        ValidateDeepSeekPeak();
    }

    private void ValidateDeepSeekPeak()
    {
        if (string.IsNullOrWhiteSpace(_deepSeekPeakBalanceText))
        {
            DeepSeekPeakValidationMessage = string.Empty;
            return;
        }

        var text = _deepSeekPeakBalanceText.Trim().TrimStart('$');
        DeepSeekPeakValidationMessage =
            !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var peak) || peak <= 0
                ? "Enter a valid positive amount (e.g. 20.00)."
                : string.Empty;
    }

    private void QueueSave()
    {
        if (_disposed) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTimerOnTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        SaveImmediate();
    }

    private void SaveImmediate()
    {
        if (_disposed) return;

        try
        {
            var s = _settingsService.Load();
            s.SetProviderEnabled(KnownProviders.DeepSeek, _deepSeekEnabled);
            s.DeepSeekApiKey = _deepSeekApiKey.Trim();
            s.SetProviderEnabled(KnownProviders.GitHubCopilot, _gitHubCopilotEnabled);
            s.GitHubCopilotApiKey = _gitHubCopilotApiKey.Trim();

            var text = _deepSeekPeakBalanceText.Trim().TrimStart('$');
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var peak) && peak > 0)
                s.DeepSeekLastBalances[_deepSeekCurrency] = peak;

            _settingsService.Save(s);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logService.Warning("Settings", $"Could not save settings: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveTimer.Stop();
        _saveTimer.Tick -= SaveTimerOnTick;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
