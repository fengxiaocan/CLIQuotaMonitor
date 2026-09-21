namespace CLIQuotaMonitor.Core.Query;

public interface IQueryExecutor
{
    Task<QueryResult> ExecuteAsync(
        QueryRequest request,
        CancellationToken cancellationToken = default);
}
