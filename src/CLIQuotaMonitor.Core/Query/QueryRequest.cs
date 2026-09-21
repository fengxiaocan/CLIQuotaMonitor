using CLIQuotaMonitor.Core.Models;

namespace CLIQuotaMonitor.Core.Query;

public sealed record QueryRequest
{
    public required string ExecutablePath { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    public QueryMethod Method { get; init; } = QueryMethod.Auto;

    public string? InteractiveCommand { get; init; }

    public string? InteractiveExitCommand { get; init; } = "/exit";

    public bool AutoAcceptDirectoryTrustPrompt { get; init; }
}
