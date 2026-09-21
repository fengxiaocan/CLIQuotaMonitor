namespace CLIQuotaMonitor.Core.Models;

public sealed record QuotaItem
{
    public QuotaItem(
        string name,
        double? remainingPercent,
        DateTimeOffset? resetAt,
        decimal? remainingValue = null,
        string? unit = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Quota name is required.", nameof(name));
        }

        if (remainingPercent is double percent &&
            (!double.IsFinite(percent) || percent is < 0 or > 100))
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainingPercent),
                remainingPercent,
                "Remaining percent must be between 0 and 100.");
        }

        Name = name.Trim();
        RemainingPercent = remainingPercent;
        ResetAt = resetAt;
        RemainingValue = remainingValue;
        Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
    }

    public string Name { get; }

    public double? RemainingPercent { get; }

    public DateTimeOffset? ResetAt { get; }

    public decimal? RemainingValue { get; }

    public string? Unit { get; }

    public double? UsedPercent => RemainingPercent is null ? null : 100 - RemainingPercent.Value;

    public TimeSpan? GetResetAfter(DateTimeOffset now)
    {
        if (ResetAt is null)
        {
            return null;
        }

        var remaining = ResetAt.Value - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}
