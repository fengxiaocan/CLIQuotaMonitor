namespace CLIQuotaMonitor.Core.Time;

public interface IClock
{
    DateTimeOffset Now { get; }
}
