using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Providers;
using CLIQuotaMonitor.Core.Query;

namespace CLIQuotaMonitor.Providers;

public abstract class CliQuotaProviderBase : IQuotaProvider
{
    private readonly IQueryExecutor _queryExecutor;
    private readonly IExecutableResolver _executableResolver;
    private readonly IQuotaParser _parser;

    protected CliQuotaProviderBase(
        IQueryExecutor queryExecutor,
        IExecutableResolver executableResolver,
        IQuotaParser parser)
    {
        _queryExecutor = queryExecutor;
        _executableResolver = executableResolver;
        _parser = parser;
    }

    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    protected abstract string ExecutableName { get; }

    protected abstract string InteractiveCommand { get; }

    protected virtual string InteractiveExitCommand => "/exit";

    protected virtual int MinimumCommandTimeoutSeconds => 0;

    protected virtual bool AutoAcceptDirectoryTrustPrompt => false;

    protected virtual bool RetryOnParseError => false;

    public async Task<QuotaSnapshot> GetQuotaAsync(
        ProviderSettings settings,
        CancellationToken cancellationToken = default)
    {
        var executable = _executableResolver.Resolve(ExecutableName, settings.ExecutablePath);
        if (string.IsNullOrWhiteSpace(executable))
        {
            return CreateStatusSnapshot(
                ProviderStatus.NotInstalled,
                $"{DisplayName} executable was not found.");
        }

        var method = settings.QueryMethod == QueryMethod.Auto
            ? QueryMethod.Pty
            : settings.QueryMethod;
        var timeoutSeconds = settings.QueryTimeoutSeconds is > 0 and <= 60
            ? settings.QueryTimeoutSeconds
            : 10;
        if (method == QueryMethod.Command)
        {
            timeoutSeconds = Math.Max(timeoutSeconds, MinimumCommandTimeoutSeconds);
        }

        var request = new QueryRequest
        {
            ExecutablePath = executable,
            Arguments = method == QueryMethod.Command ? settings.CommandArguments : null,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            Method = method,
            InteractiveCommand = method == QueryMethod.Pty ? InteractiveCommand : null,
            InteractiveExitCommand = method == QueryMethod.Pty ? InteractiveExitCommand : null,
            AutoAcceptDirectoryTrustPrompt = method == QueryMethod.Pty && AutoAcceptDirectoryTrustPrompt
        };

        var result = await _queryExecutor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return CreateStatusSnapshot(result.Status, result.ErrorMessage);
        }

        var snapshot = _parser.Parse(result.StandardOutput, DateTimeOffset.Now);
        if (snapshot.Status == ProviderStatus.ParseError && RetryOnParseError)
        {
            var retryResult = await _queryExecutor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            if (!retryResult.Succeeded)
            {
                return CreateStatusSnapshot(retryResult.Status, retryResult.ErrorMessage);
            }

            snapshot = _parser.Parse(retryResult.StandardOutput, DateTimeOffset.Now);
        }

        return snapshot with
        {
            ProviderId = Id,
            ProviderName = DisplayName
        };
    }

    private QuotaSnapshot CreateStatusSnapshot(ProviderStatus status, string? errorMessage)
    {
        return new QuotaSnapshot
        {
            ProviderId = Id,
            ProviderName = DisplayName,
            Status = status,
            ErrorMessage = errorMessage,
            Quotas = Array.Empty<QuotaItem>()
        };
    }
}
