using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Query;

namespace CLIQuotaMonitor.Infrastructure;

public sealed class QueryExecutorRouter : IQueryExecutor
{
    private readonly IQueryExecutor _commandExecutor;
    private readonly IQueryExecutor _ptyExecutor;

    public QueryExecutorRouter(
        IQueryExecutor commandExecutor,
        IQueryExecutor ptyExecutor)
    {
        _commandExecutor = commandExecutor;
        _ptyExecutor = ptyExecutor;
    }

    public Task<QueryResult> ExecuteAsync(
        QueryRequest request,
        CancellationToken cancellationToken = default)
    {
        return request.Method == QueryMethod.Pty
            ? _ptyExecutor.ExecuteAsync(request, cancellationToken)
            : _commandExecutor.ExecuteAsync(request, cancellationToken);
    }
}
