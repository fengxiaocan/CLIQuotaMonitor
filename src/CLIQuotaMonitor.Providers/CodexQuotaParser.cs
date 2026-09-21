using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Providers;

namespace CLIQuotaMonitor.Providers;

public sealed class CodexQuotaParser : IQuotaParser
{
    public QuotaSnapshot Parse(string output, DateTimeOffset now)
    {
        return QuotaOutputParser.Parse(output, "codex", "Codex", now);
    }
}
