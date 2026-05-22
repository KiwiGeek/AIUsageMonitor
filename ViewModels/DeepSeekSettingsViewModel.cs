using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AIUsageMonitor.Models;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.ViewModels;

public sealed class DeepSeekSettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppSettingsService _settingsService;
    private readonly AppLogService _logService;
    private readonly DispatcherTimer _saveTimer;
    private bool _disposed;

    private bool _isEnabled;
    private string _apiKey = string.Empty;
    private string _peakBalanceText = string.Empty;
    private string _currency = "USD";
    private string _peakValidationMessage = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SettingsSaved;

    public DeepSeekSettingsViewModel(AppSettingsService settingsService, AppLogService logService)
    {
        _settingsService = settingsService;
        _logService = logService;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += SaveTimerOnTick;
        LoadFromSettings();
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            SaveImmediate();
        }
    }

    public string ApiKey
    {
        get => _apiKey;
        set
        {
            if (_apiKey == value) return;
            _apiKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ApiKeyStatus));
            QueueSave();
        }
    }

    public string PeakBalanceText
    {
        get => _peakBalanceText;
        set
        {
            if (_peakBalanceText == value) return;
            _peakBalanceText = value;
            OnPropertyChanged();
            ValidatePeak();
            QueueSave();
        }
    }

    public string Currency => _currency;

    public string PeakLabel => $"Peak balance ({_currency})";

    public string ApiKeyStatus => string.IsNullOrWhiteSpace(_apiKey) ? "Not configured" : "Configured";

    public string PeakValidationMessage
    {
        get => _peakValidationMessage;
        private set
        {
            if (_peakValidationMessage == value) return;
            _peakValidationMessage = value;
            OnPropertyChanged();
        }
    }

    public void LoadFromSettings()
    {
        var settings = _settingsService.Load();
        _isEnabled = settings.IsProviderEnabled(KnownProviders.DeepSeek);
        _apiKey = settings.DeepSeekApiKey;
        _currency = settings.DeepSeekLastBalances.Keys.FirstOrDefault() ?? "USD";

        if (settings.DeepSeekLastBalances.TryGetValue(_currency, out var peak) && peak > 0)
            _peakBalanceText = peak.ToString("0.00", CultureInfo.InvariantCulture);
        else
            _peakBalanceText = string.Empty;

        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(ApiKey));
        OnPropertyChanged(nameof(PeakBalanceText));
        OnPropertyChanged(nameof(ApiKeyStatus));
        OnPropertyChanged(nameof(Currency));
        OnPropertyChanged(nameof(PeakLabel));
        ValidatePeak();
    }

    private void ValidatePeak()
    {
        if (string.IsNullOrWhiteSpace(_peakBalanceText))
        {
            PeakValidationMessage = string.Empty;
            return;
        }

        var text = _peakBalanceText.Trim().TrimStart('$');
        PeakValidationMessage =
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
            var settings = _settingsService.Load();
            settings.DeepSeekApiKey = _apiKey.Trim();
            settings.SetProviderEnabled(KnownProviders.DeepSeek, _isEnabled);

            var text = _peakBalanceText.Trim().TrimStart('$');
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var peak) && peak > 0)
                settings.DeepSeekLastBalances[_currency] = peak;

            _settingsService.Save(settings);
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logService.Warning("DeepSeek", $"Could not save settings: {ex.Message}");
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
