namespace CLIQuotaMonitor.Core.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
