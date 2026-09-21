using CLIQuotaMonitor.Core.Models;

namespace CLIQuotaMonitor.Core.Query;

public sealed record QueryResult
{
    public required bool Succeeded { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public TimeSpan Duration { get; init; }

    public ProviderStatus Status { get; init; } = ProviderStatus.Unknown;

    public string? ErrorMessage { get; init; }

    public static QueryResult Success(string standardOutput, TimeSpan duration)
    {
        return new QueryResult
        {
            Succeeded = true,
            StandardOutput = standardOutput,
            Duration = duration,
            Status = ProviderStatus.Ok
        };
    }

    public static QueryResult Failure(
        ProviderStatus status,
        string errorMessage,
        TimeSpan duration,
        string standardOutput = "",
        string standardError = "")
    {
        return new QueryResult
        {
            Succeeded = false,
            StandardOutput = standardOutput,
            StandardError = standardError,
            Duration = duration,
            Status = status,
            ErrorMessage = errorMessage
        };
    }
}
