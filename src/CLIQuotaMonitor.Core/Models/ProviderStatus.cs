namespace CLIQuotaMonitor.Core.Models;

public enum ProviderStatus
{
    Unknown,
    Refreshing,
    Ok,
    NotInstalled,
    NotLoggedIn,
    Timeout,
    ParseError,
    UnsupportedVersion,
    Offline,
    Error
}
