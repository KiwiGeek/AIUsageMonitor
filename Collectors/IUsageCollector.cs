using AIUsageMonitor.Models;

namespace AIUsageMonitor.Collectors;

public interface IUsageCollector
{
    string ProviderName { get; }

    Task<ProviderUsage> CollectAsync(CancellationToken cancellationToken);
}
