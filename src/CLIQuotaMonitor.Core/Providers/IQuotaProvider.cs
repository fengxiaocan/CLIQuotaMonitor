using CLIQuotaMonitor.Core.Models;

namespace CLIQuotaMonitor.Core.Providers;

public interface IQuotaProvider
{
    string Id { get; }

    string DisplayName { get; }

    Task<QuotaSnapshot> GetQuotaAsync(
        ProviderSettings settings,
        CancellationToken cancellationToken = default);
}
