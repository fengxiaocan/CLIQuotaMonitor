using CLIQuotaMonitor.Core.Providers;
using CLIQuotaMonitor.Core.Query;

namespace CLIQuotaMonitor.Providers;

public sealed class GrokQuotaProvider : CliQuotaProviderBase
{
    public GrokQuotaProvider(IQueryExecutor queryExecutor, IExecutableResolver executableResolver)
        : base(queryExecutor, executableResolver, new GrokQuotaParser())
    {
    }

    public override string Id => "grok";

    public override string DisplayName => "Grok";

    protected override string ExecutableName => "grok";

    protected override string InteractiveCommand => "/usage";
}
