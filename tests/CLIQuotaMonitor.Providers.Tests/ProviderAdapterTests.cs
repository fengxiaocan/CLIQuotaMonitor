using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Providers;
using CLIQuotaMonitor.Core.Query;
using System.Reflection;

namespace CLIQuotaMonitor.Providers.Tests;

public sealed class ProviderAdapterTests
{
    [Fact]
    public async Task Codex_provider_reports_not_installed_without_starting_a_query()
    {
        var executor = new RecordingQueryExecutor(QueryResult.Success("unused", TimeSpan.Zero));
        var resolver = new FakeExecutableResolver(null);
        var provider = CreateProvider("CodexQuotaProvider", executor, resolver);

        var result = await provider.GetQuotaAsync(new ProviderSettings { Id = "codex" });

        Assert.Equal(ProviderStatus.NotInstalled, result.Status);
        Assert.Empty(executor.Requests);
    }

    [Fact]
    public async Task Codex_provider_uses_pty_status_and_parses_the_snapshot()
    {
        var output = "5h limit: 72% remaining, reset in 2h 43m\nWeekly limit: 43% remaining";
        var executor = new RecordingQueryExecutor(QueryResult.Success(output, TimeSpan.FromMilliseconds(10)));
        var resolver = new FakeExecutableResolver("C:\\Tools\\codex.exe");
        var provider = CreateProvider("CodexQuotaProvider", executor, resolver);

        var result = await provider.GetQuotaAsync(new ProviderSettings { Id = "codex" });

        Assert.Equal(ProviderStatus.Ok, result.Status);
        Assert.Equal(2, result.Quotas.Count);
        Assert.Single(executor.Requests);
        Assert.Equal(QueryMethod.Pty, executor.Requests[0].Method);
        Assert.Equal("/status", executor.Requests[0].InteractiveCommand);
        Assert.True(executor.Requests[0].AutoAcceptDirectoryTrustPrompt);
    }

    [Fact]
    public async Task Codex_provider_retries_once_when_status_refresh_has_no_quota_values()
    {
        var executor = new SequenceQueryExecutor(
            QueryResult.Success("Limits: refresh requested; run /status again shortly.", TimeSpan.Zero),
            QueryResult.Success("5h limit: 69% left\nWeekly limit: 78% left", TimeSpan.Zero));
        var resolver = new FakeExecutableResolver("C:\\Tools\\codex.exe");
        var provider = CreateProvider("CodexQuotaProvider", executor, resolver);

        var result = await provider.GetQuotaAsync(new ProviderSettings { Id = "codex" });

        Assert.Equal(ProviderStatus.Ok, result.Status);
        Assert.Equal(2, result.Quotas.Count);
        Assert.Equal(2, executor.Requests.Count);
    }

    [Fact]
    public async Task Antigravity_provider_keeps_parse_failure_distinct_from_zero_percent()
    {
        var executor = new RecordingQueryExecutor(QueryResult.Success("login required", TimeSpan.Zero));
        var resolver = new FakeExecutableResolver("C:\\Tools\\agy.exe");
        var provider = CreateProvider("AntigravityQuotaProvider", executor, resolver);

        var result = await provider.GetQuotaAsync(new ProviderSettings { Id = "antigravity" });

        Assert.Equal(ProviderStatus.ParseError, result.Status);
        Assert.Empty(result.Quotas);
        Assert.DoesNotContain(result.Quotas, quota => quota.RemainingPercent == 0);
    }

    [Fact]
    public async Task Antigravity_command_query_keeps_startup_grace_when_timeout_is_ten_seconds()
    {
        var executor = new RecordingQueryExecutor(QueryResult.Success("Weekly Limit: 78%", TimeSpan.Zero));
        var resolver = new FakeExecutableResolver("C:\\Tools\\agy.exe");
        var provider = CreateProvider("AntigravityQuotaProvider", executor, resolver);

        await provider.GetQuotaAsync(new ProviderSettings
        {
            Id = "antigravity",
            QueryMethod = QueryMethod.Command,
            CommandArguments = "-p /usage",
            QueryTimeoutSeconds = 10
        });

        Assert.Single(executor.Requests);
        Assert.Equal(TimeSpan.FromSeconds(15), executor.Requests[0].Timeout);
    }

    private sealed class FakeExecutableResolver : IExecutableResolver
    {
        private readonly string? _path;

        public FakeExecutableResolver(string? path)
        {
            _path = path;
        }

        public string? Resolve(string commandName, string? configuredPath = null)
        {
            return _path;
        }
    }

    private sealed class RecordingQueryExecutor : IQueryExecutor
    {
        private readonly QueryResult _result;

        public RecordingQueryExecutor(QueryResult result)
        {
            _result = result;
        }

        public List<QueryRequest> Requests { get; } = [];

        public Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }
    }

    private sealed class SequenceQueryExecutor : IQueryExecutor
    {
        private readonly Queue<QueryResult> _results;

        public SequenceQueryExecutor(params QueryResult[] results)
        {
            _results = new Queue<QueryResult>(results);
        }

        public List<QueryRequest> Requests { get; } = [];

        public Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek());
        }
    }

    private static IQuotaProvider CreateProvider(
        string typeName,
        IQueryExecutor executor,
        IExecutableResolver resolver)
    {
        var type = Assembly.Load("CLIQuotaMonitor.Providers")
            .GetType($"CLIQuotaMonitor.Providers.{typeName}");
        Assert.NotNull(type);
        var instance = Activator.CreateInstance(type!, executor, resolver);
        return Assert.IsAssignableFrom<IQuotaProvider>(instance);
    }
}
