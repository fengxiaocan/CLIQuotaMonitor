using System.Collections.Concurrent;
using CLIQuotaMonitor.Core.Caching;
using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Providers;
using CLIQuotaMonitor.Core.Time;

namespace CLIQuotaMonitor.Core.Services;

public sealed class QuotaManager
{
    private readonly IReadOnlyDictionary<string, IQuotaProvider> _providers;
    private readonly IQuotaCacheStore _cacheStore;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _providerGates = new(StringComparer.OrdinalIgnoreCase);

    public QuotaManager(
        IEnumerable<IQuotaProvider> providers,
        IQuotaCacheStore cacheStore,
        IClock clock)
    {
        _providers = providers.ToDictionary(provider => provider.Id, StringComparer.OrdinalIgnoreCase);
        _cacheStore = cacheStore;
        _clock = clock;
    }

    public async Task<QuotaSnapshot> RefreshProviderAsync(
        string providerId,
        ProviderSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!_providers.TryGetValue(providerId, out var provider))
        {
            return new QuotaSnapshot
            {
                ProviderId = providerId,
                ProviderName = providerId,
                Status = ProviderStatus.Unknown,
                ErrorMessage = "Provider is not registered."
            };
        }

        var gate = _providerGates.GetOrAdd(providerId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            var cachedWhileRefreshing = await _cacheStore
                .GetAsync(providerId, cancellationToken)
                .ConfigureAwait(false);
            return cachedWhileRefreshing ?? new QuotaSnapshot
            {
                ProviderId = provider.Id,
                ProviderName = provider.DisplayName,
                Status = ProviderStatus.Refreshing,
                ErrorMessage = "A refresh is already in progress."
            };
        }

        try
        {
            var fresh = await provider.GetQuotaAsync(settings, cancellationToken).ConfigureAwait(false);
            if (fresh.Status == ProviderStatus.Ok && fresh.Quotas.Count > 0)
            {
                var normalized = fresh with
                {
                    LastUpdated = fresh.LastUpdated ?? _clock.Now,
                    IsStale = false
                };
                await _cacheStore.SetAsync(normalized, cancellationToken).ConfigureAwait(false);
                return normalized;
            }

            var cached = await _cacheStore.GetAsync(providerId, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return cached with
                {
                    Status = fresh.Status,
                    ErrorMessage = fresh.ErrorMessage,
                    IsStale = true
                };
            }

            return fresh;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<QuotaSnapshot>> RefreshAllAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var results = new List<QuotaSnapshot>();
        var providerSettings = settings.Providers
            .Where(provider => provider.Enabled)
            .OrderBy(provider => provider.SortOrder)
            .ToArray();

        for (var index = 0; index < providerSettings.Length; index++)
        {
            var providerSetting = providerSettings[index];
            results.Add(await RefreshProviderAsync(
                    providerSetting.Id,
                    providerSetting,
                    cancellationToken)
                .ConfigureAwait(false));

            if (index < providerSettings.Length - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            }
        }

        return results;
    }
}
