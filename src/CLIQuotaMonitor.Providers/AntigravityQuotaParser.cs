using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Providers;

namespace CLIQuotaMonitor.Providers;

public sealed class AntigravityQuotaParser : IQuotaParser
{
    public QuotaSnapshot Parse(string output, DateTimeOffset now)
    {
        return QuotaOutputParser.Parse(output, "antigravity", "Antigravity", now);
    }
}
