namespace AIUsageMonitor.Models;

public sealed class ProviderCardEventArgs : EventArgs
{
    public ProviderCardEventArgs(string providerName) => ProviderName = providerName;
    public string ProviderName { get; }
}
