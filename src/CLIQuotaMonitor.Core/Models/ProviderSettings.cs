namespace CLIQuotaMonitor.Core.Models;

public sealed class ProviderSettings
{
    public required string Id { get; set; }

    public bool Enabled { get; set; } = true;

    public string? ExecutablePath { get; set; }

    public QueryMethod QueryMethod { get; set; } = QueryMethod.Auto;

    public string? CommandArguments { get; set; }

    public int QueryTimeoutSeconds { get; set; } = 10;

    public int SortOrder { get; set; }

    public List<string> VisibleQuotaNames { get; set; } = [];
}
