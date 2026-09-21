namespace CLIQuotaMonitor.Core.Models;

public sealed record QuotaSnapshot
{
    public required string ProviderId { get; init; }

    public required string ProviderName { get; init; }

    public string? Account { get; init; }

    public string? Plan { get; init; }

    public IReadOnlyList<QuotaItem> Quotas { get; init; } = Array.Empty<QuotaItem>();

    public DateTimeOffset? LastUpdated { get; init; }

    public ProviderStatus Status { get; init; } = ProviderStatus.Unknown;

    public string? ErrorMessage { get; init; }

    public string? CliVersion { get; init; }

    public string? SessionUsage { get; init; }

    public bool IsStale { get; init; }

    public QuotaSnapshot WithStatus(ProviderStatus status, string? errorMessage = null)
    {
        return this with
        {
            Status = status,
            ErrorMessage = errorMessage
        };
    }
}
