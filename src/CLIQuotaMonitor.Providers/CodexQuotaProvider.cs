using CLIQuotaMonitor.Core.Providers;
using CLIQuotaMonitor.Core.Query;

namespace CLIQuotaMonitor.Providers;

public sealed class CodexQuotaProvider : CliQuotaProviderBase
{
    public CodexQuotaProvider(IQueryExecutor queryExecutor, IExecutableResolver executableResolver)
        : base(queryExecutor, executableResolver, new CodexQuotaParser())
    {
    }

    public override string Id => "codex";

    public override string DisplayName => "Codex";

    protected override string ExecutableName => "codex";

    protected override string InteractiveCommand => "/status";

    protected override bool AutoAcceptDirectoryTrustPrompt => true;

    protected override bool RetryOnParseError => true;
}
