using CLIQuotaMonitor.Core.Models;

namespace CLIQuotaMonitor.Core.Providers;

public interface IQuotaParser
{
    QuotaSnapshot Parse(string output, DateTimeOffset now);
}
