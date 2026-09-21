namespace CLIQuotaMonitor.Core.Providers;

public interface IExecutableResolver
{
    string? Resolve(string commandName, string? configuredPath = null);
}
