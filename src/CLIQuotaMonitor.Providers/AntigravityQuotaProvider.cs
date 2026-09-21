using CLIQuotaMonitor.Core.Providers;
using CLIQuotaMonitor.Core.Query;

namespace CLIQuotaMonitor.Providers;

public sealed class AntigravityQuotaProvider : CliQuotaProviderBase
{
    public AntigravityQuotaProvider(IQueryExecutor queryExecutor, IExecutableResolver executableResolver)
        : base(queryExecutor, executableResolver, new AntigravityQuotaParser())
    {
    }

    public override string Id => "antigravity";

    public override string DisplayName => "Antigravity";

    protected override string ExecutableName => "agy";

    protected override string InteractiveCommand => "/usage";

    protected override int MinimumCommandTimeoutSeconds => 15;
}
