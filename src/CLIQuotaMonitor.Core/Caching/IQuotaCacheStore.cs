using CLIQuotaMonitor.Core.Models;

namespace CLIQuotaMonitor.Core.Caching;

public interface IQuotaCacheStore
{
    Task<QuotaSnapshot?> GetAsync(
        string providerId,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        QuotaSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
