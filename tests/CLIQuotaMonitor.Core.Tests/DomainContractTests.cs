using System.Reflection;

namespace CLIQuotaMonitor.Core.Tests;

public sealed class DomainContractTests
{
    [Fact]
    public void Core_exposes_a_dynamic_quota_item_contract()
    {
        var quotaItemType = Assembly.Load("CLIQuotaMonitor.Core")
            .GetType("CLIQuotaMonitor.Core.Models.QuotaItem");

        Assert.NotNull(quotaItemType);
        Assert.NotNull(quotaItemType!.GetProperty("Name"));
        Assert.NotNull(quotaItemType.GetProperty("RemainingPercent"));
        Assert.NotNull(quotaItemType.GetProperty("ResetAt"));
    }

    [Fact]
    public void Core_exposes_a_snapshot_with_provider_status_and_quotas()
    {
        var assembly = Assembly.Load("CLIQuotaMonitor.Core");
        var snapshotType = assembly.GetType("CLIQuotaMonitor.Core.Models.QuotaSnapshot");
        var statusType = assembly.GetType("CLIQuotaMonitor.Core.Models.ProviderStatus");

        Assert.NotNull(snapshotType);
        Assert.NotNull(statusType);
        Assert.NotNull(snapshotType!.GetProperty("ProviderId"));
        Assert.NotNull(snapshotType.GetProperty("Quotas"));
        Assert.NotNull(snapshotType.GetProperty("Status"));
        Assert.True(statusType!.IsEnum);
    }

    [Fact]
    public void Core_exposes_refresh_and_provider_settings()
    {
        var assembly = Assembly.Load("CLIQuotaMonitor.Core");
        var appSettingsType = assembly.GetType("CLIQuotaMonitor.Core.Models.AppSettings");
        var providerSettingsType = assembly.GetType("CLIQuotaMonitor.Core.Models.ProviderSettings");
        var queryMethodType = assembly.GetType("CLIQuotaMonitor.Core.Models.QueryMethod");

        Assert.NotNull(appSettingsType);
        Assert.NotNull(providerSettingsType);
        Assert.NotNull(queryMethodType);
        Assert.NotNull(appSettingsType!.GetProperty("RefreshIntervalSeconds"));
        Assert.NotNull(appSettingsType.GetProperty("Providers"));
        Assert.NotNull(providerSettingsType!.GetProperty("ExecutablePath"));
        Assert.NotNull(providerSettingsType.GetProperty("QueryMethod"));
        Assert.True(queryMethodType!.IsEnum);
    }

    [Fact]
    public void Core_exposes_a_cancellable_query_executor_contract()
    {
        var assembly = Assembly.Load("CLIQuotaMonitor.Core");
        var executorType = assembly.GetType("CLIQuotaMonitor.Core.Query.IQueryExecutor");
        var requestType = assembly.GetType("CLIQuotaMonitor.Core.Query.QueryRequest");
        var resultType = assembly.GetType("CLIQuotaMonitor.Core.Query.QueryResult");

        Assert.NotNull(executorType);
        Assert.True(executorType!.IsInterface);
        Assert.NotNull(requestType);
        Assert.NotNull(resultType);
    }

    [Fact]
    public void Core_exposes_provider_and_parser_contracts()
    {
        var assembly = Assembly.Load("CLIQuotaMonitor.Core");
        var providerType = assembly.GetType("CLIQuotaMonitor.Core.Providers.IQuotaProvider");
        var parserType = assembly.GetType("CLIQuotaMonitor.Core.Providers.IQuotaParser");

        Assert.NotNull(providerType);
        Assert.NotNull(parserType);
        Assert.True(providerType!.IsInterface);
        Assert.True(parserType!.IsInterface);
    }

    [Fact]
    public void Core_exposes_an_executable_resolver_seam()
    {
        var resolverType = Assembly.Load("CLIQuotaMonitor.Core")
            .GetType("CLIQuotaMonitor.Core.Providers.IExecutableResolver");

        Assert.NotNull(resolverType);
        Assert.True(resolverType!.IsInterface);
    }

    [Fact]
    public void Core_exposes_a_quota_cache_store_seam()
    {
        var cacheType = Assembly.Load("CLIQuotaMonitor.Core")
            .GetType("CLIQuotaMonitor.Core.Caching.IQuotaCacheStore");

        Assert.NotNull(cacheType);
        Assert.True(cacheType!.IsInterface);
    }

}
