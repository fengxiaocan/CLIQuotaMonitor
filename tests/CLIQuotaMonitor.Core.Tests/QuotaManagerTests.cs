using System.Reflection;
using CLIQuotaMonitor.Core.Caching;
using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Providers;
using CLIQuotaMonitor.Core.Time;

namespace CLIQuotaMonitor.Core.Tests;

public sealed class QuotaManagerTests
{
    [Fact]
    public async Task Failed_refresh_keeps_the_last_successful_snapshot_as_stale()
    {
        var updatedAt = new DateTimeOffset(2026, 9, 20, 20, 0, 0, TimeSpan.FromHours(8));
        var provider = new SequenceProvider(
            new QuotaSnapshot
            {
                ProviderId = "codex",
                ProviderName = "Codex",
                Status = ProviderStatus.Ok,
                LastUpdated = updatedAt,
                Quotas = [new QuotaItem("5H", 72, updatedAt.AddHours(2))]
            },
            new QuotaSnapshot
            {
                ProviderId = "codex",
                ProviderName = "Codex",
                Status = ProviderStatus.Timeout,
                ErrorMessage = "timed out"
            });
        var cache = new MemoryQuotaCacheStore();
        var manager = CreateManager(provider, cache, new FixedClock(updatedAt.AddMinutes(5)));
        var settings = new ProviderSettings { Id = "codex" };

        await Refresh(manager, "codex", settings);
        var result = await Refresh(manager, "codex", settings);

        Assert.Equal(ProviderStatus.Timeout, result.Status);
        Assert.True(result.IsStale);
        Assert.Equal(72, result.Quotas.Single().RemainingPercent);
        Assert.Equal(updatedAt, result.LastUpdated);
    }

    [Fact]
    public async Task Concurrent_refreshes_for_one_provider_share_a_single_query()
    {
        var provider = new SequenceProvider(
            [new QuotaSnapshot
            {
                ProviderId = "grok",
                ProviderName = "Grok",
                Status = ProviderStatus.Ok,
                LastUpdated = DateTimeOffset.UtcNow,
                Quotas = [new QuotaItem("Limit", 53, null)]
            }],
            delay: TimeSpan.FromMilliseconds(100));
        var manager = CreateManager(provider, new MemoryQuotaCacheStore(), new FixedClock(DateTimeOffset.UtcNow));
        var settings = new ProviderSettings { Id = "grok" };

        var first = Refresh(manager, "grok", settings);
        var second = Refresh(manager, "grok", settings);
        await Task.WhenAll(first, second);

        Assert.Equal(1, provider.CallCount);
    }

    private static object CreateManager(
        IQuotaProvider provider,
        IQuotaCacheStore cache,
        IClock clock)
    {
        var type = Assembly.Load("CLIQuotaMonitor.Core")
            .GetType("CLIQuotaMonitor.Core.Services.QuotaManager");
        Assert.NotNull(type);
        return Activator.CreateInstance(type!, [new[] { provider }, cache, clock])!;
    }

    private static async Task<QuotaSnapshot> Refresh(
        object manager,
        string providerId,
        ProviderSettings settings)
    {
        var method = manager.GetType().GetMethod("RefreshProviderAsync");
        Assert.NotNull(method);
        var task = (Task)method!.Invoke(manager, [providerId, settings, CancellationToken.None])!;
        await task;
        return (QuotaSnapshot)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; }
    }

    private sealed class MemoryQuotaCacheStore : IQuotaCacheStore
    {
        private readonly Dictionary<string, QuotaSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

        public Task<QuotaSnapshot?> GetAsync(string providerId, CancellationToken cancellationToken = default)
        {
            _snapshots.TryGetValue(providerId, out var snapshot);
            return Task.FromResult(snapshot);
        }

        public Task SetAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _snapshots[snapshot.ProviderId] = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class SequenceProvider : IQuotaProvider
    {
        private readonly Queue<QuotaSnapshot> _responses;
        private readonly TimeSpan _delay;

        public SequenceProvider(params QuotaSnapshot[] responses)
            : this(responses, TimeSpan.Zero)
        {
        }

        public SequenceProvider(QuotaSnapshot[] responses, TimeSpan delay)
        {
            _responses = new Queue<QuotaSnapshot>(responses);
            _delay = delay;
        }

        public string Id => _responses.Peek().ProviderId;

        public string DisplayName => _responses.Peek().ProviderName;

        public int CallCount { get; private set; }

        public async Task<QuotaSnapshot> GetQuotaAsync(
            ProviderSettings settings,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            return _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        }
    }
}
