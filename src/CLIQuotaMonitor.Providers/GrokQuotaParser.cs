using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Providers;

namespace CLIQuotaMonitor.Providers;

public sealed class GrokQuotaParser : IQuotaParser
{
    public QuotaSnapshot Parse(string output, DateTimeOffset now)
    {
        return QuotaOutputParser.Parse(output, "grok", "Grok", now, parseSessionUsage: true);
    }
}
