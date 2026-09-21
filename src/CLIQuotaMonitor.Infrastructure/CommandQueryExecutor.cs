using System.Diagnostics;
using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Query;

namespace CLIQuotaMonitor.Infrastructure;

public sealed class CommandQueryExecutor : IQueryExecutor
{
    public async Task<QueryResult> ExecuteAsync(
        QueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(request.ExecutablePath) ||
            !File.Exists(request.ExecutablePath))
        {
            return QueryResult.Failure(
                ProviderStatus.NotInstalled,
                $"Executable was not found: {request.ExecutablePath}",
                stopwatch.Elapsed);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            Arguments = request.Arguments ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
                ? Environment.CurrentDirectory
                : request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        if (IsBatchFile(request.ExecutablePath))
        {
            var command = $"\"{request.ExecutablePath}\"";
            if (!string.IsNullOrWhiteSpace(request.Arguments))
            {
                command += $" {request.Arguments}";
            }

            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.Arguments = $"/d /s /c \"{command}\"";
        }

        if (request.Environment is not null)
        {
            foreach (var (key, value) in request.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                return QueryResult.Failure(
                    ProviderStatus.Error,
                    "The query process could not be started.",
                    stopwatch.Elapsed);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return QueryResult.Failure(
                ProviderStatus.NotInstalled,
                exception.Message,
                stopwatch.Elapsed);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : request.Timeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            stopwatch.Stop();

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode == 0)
            {
                return QueryResult.Success(stdout, stopwatch.Elapsed) with { StandardError = stderr };
            }

            return QueryResult.Failure(
                ProviderStatus.Error,
                string.IsNullOrWhiteSpace(stderr)
                    ? $"Query process exited with code {process.ExitCode}."
                    : LogRedactor.Redact(stderr.Trim()),
                stopwatch.Elapsed,
                LogRedactor.Redact(stdout),
                LogRedactor.Redact(stderr));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await DrainAfterKillAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            stopwatch.Stop();
            return QueryResult.Failure(
                ProviderStatus.Timeout,
                $"Query timed out after {request.Timeout.TotalSeconds:0.#} seconds.",
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await DrainAfterKillAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            stopwatch.Stop();
            return QueryResult.Failure(
                ProviderStatus.Error,
                "Query was cancelled.",
                stopwatch.Elapsed);
        }
    }

    private static async Task DrainAfterKillAsync(Task<string> stdoutTask, Task<string> stderrTask)
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (Exception)
        {
            // The process has already been terminated. Output is not used on failure.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
    }

    private static bool IsBatchFile(string path)
    {
        return string.Equals(Path.GetExtension(path), ".cmd", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(path), ".bat", StringComparison.OrdinalIgnoreCase);
    }
}
