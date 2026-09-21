using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Query;
using CLIQuotaMonitor.Infrastructure;

namespace CLIQuotaMonitor.Core.Tests;

public sealed class InfrastructureBoundaryTests
{
    [Fact]
    public async Task Command_query_returns_stdout_from_an_independent_process()
    {
        var type = Assembly.Load("CLIQuotaMonitor.Infrastructure")
            .GetType("CLIQuotaMonitor.Infrastructure.CommandQueryExecutor");
        Assert.NotNull(type);

        var executor = Activator.CreateInstance(type!);
        var request = new QueryRequest
        {
            ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/c echo cli-quota-test",
            Timeout = TimeSpan.FromSeconds(10),
            Method = QueryMethod.Command
        };

        var method = type!.GetMethod("ExecuteAsync");
        Assert.NotNull(method);
        var task = (Task)method!.Invoke(executor, [request, CancellationToken.None])!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var succeeded = (bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!;
        var stdout = (string)result.GetType().GetProperty("StandardOutput")!.GetValue(result)!;

        Assert.True(succeeded);
        Assert.Contains("cli-quota-test", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Command_query_redacts_sensitive_stderr_in_failures()
    {
        var type = Assembly.Load("CLIQuotaMonitor.Infrastructure")
            .GetType("CLIQuotaMonitor.Infrastructure.CommandQueryExecutor");
        Assert.NotNull(type);

        var executor = Activator.CreateInstance(type!);
        var request = new QueryRequest
        {
            ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/c echo Authorization: Bearer secret-token 1>&2 & exit /b 1",
            Timeout = TimeSpan.FromSeconds(10),
            Method = QueryMethod.Command
        };

        var task = (Task)type!.GetMethod("ExecuteAsync")!.Invoke(executor, [request, CancellationToken.None])!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var error = (string?)result.GetType().GetProperty("ErrorMessage")!.GetValue(result);

        Assert.DoesNotContain("secret-token", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Json_settings_store_round_trips_app_settings()
    {
        var type = Assembly.Load("CLIQuotaMonitor.Infrastructure")
            .GetType("CLIQuotaMonitor.Infrastructure.JsonSettingsStore");
        Assert.NotNull(type);

        var root = Path.Combine(Path.GetTempPath(), "CLIQuotaMonitorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var filePath = Path.Combine(root, "config.json");
            var store = Activator.CreateInstance(type!, filePath);
            var settings = new AppSettings { RefreshIntervalSeconds = 90 };
            var save = (Task)type!.GetMethod("SaveAsync")!.Invoke(store, [settings, CancellationToken.None])!;
            await save;
            var load = (Task)type.GetMethod("LoadAsync")!.Invoke(store, [CancellationToken.None])!;
            await load;
            var loaded = load.GetType().GetProperty("Result")!.GetValue(load)!;

            Assert.Equal(90, ((AppSettings)loaded).RefreshIntervalSeconds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    #pragma warning disable xUnit1031
    [Fact]
    public async Task Json_settings_store_save_does_not_require_the_calling_context_to_pump()
    {
        var root = Path.Combine(Path.GetTempPath(), "CLIQuotaMonitorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "config.json");
        var store = new JsonSettingsStore(filePath);
        var synchronizationContext = new QueuedSynchronizationContext();
        var completion = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            try
            {
                store.SaveAsync(new AppSettings())
                    .GetAwaiter()
                    .GetResult();
                completion.SetResult(null);
            }
            catch (Exception exception)
            {
                completion.SetResult(exception);
            }
        })
        {
            IsBackground = true
        };

        try
        {
            thread.Start();
            var completedWithoutPumping = await Task.WhenAny(
                completion.Task,
                Task.Delay(TimeSpan.FromSeconds(2))) == completion.Task;
            if (!completedWithoutPumping)
            {
                synchronizationContext.Drain();
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }

            Assert.True(
                completedWithoutPumping,
                "A synchronous caller must not deadlock when the save captures its calling context.");
            Assert.Null(await completion.Task);
        }
        finally
        {
            synchronizationContext.Drain();
            if (!completion.Task.IsCompleted)
            {
                try
                {
                    await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (TimeoutException)
                {
                    // The assertion above reports the deadlock; cleanup remains best effort.
                }
            }

            Directory.Delete(root, recursive: true);
        }
    }
    #pragma warning restore xUnit1031

    [Fact]
    public void Log_redactor_removes_bearer_tokens_and_api_keys()
    {
        var type = Assembly.Load("CLIQuotaMonitor.Infrastructure")
            .GetType("CLIQuotaMonitor.Infrastructure.LogRedactor");
        Assert.NotNull(type);

        var redact = type!.GetMethod("Redact", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(redact);
        var result = (string)redact!.Invoke(null, ["Authorization: Bearer secret-token api_key=sk-secret123"] )!;

        Assert.DoesNotContain("secret-token", result);
        Assert.DoesNotContain("sk-secret123", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void Executable_resolver_finds_a_command_on_the_windows_path()
    {
        var type = Assembly.Load("CLIQuotaMonitor.Infrastructure")
            .GetType("CLIQuotaMonitor.Infrastructure.ExecutableResolver");
        Assert.NotNull(type);

        var resolver = Activator.CreateInstance(type!);
        var resolve = type!.GetMethod("Resolve");
        Assert.NotNull(resolve);
        var path = (string?)resolve!.Invoke(resolver, ["cmd.exe", null]);

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Executable_resolver_prioritizes_windows_launchers_over_extensionless_shims()
    {
        var type = Assembly.Load("CLIQuotaMonitor.Infrastructure")
            .GetType("CLIQuotaMonitor.Infrastructure.ExecutableResolver");
        Assert.NotNull(type);

        var getCandidateNames = type!.GetMethod(
            "GetCandidateNames",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(getCandidateNames);

        var candidates = ((IEnumerable<string>)getCandidateNames!.Invoke(null, ["sample-cli"])!).ToArray();

        Assert.Equal(
            new[] { "sample-cli.exe", "sample-cli.cmd", "sample-cli.bat", "sample-cli" },
            candidates);
    }

    [Fact]
    public void Windows_startup_manager_round_trips_a_user_run_entry()
    {
        var type = Assembly.Load("CLIQuotaMonitor.Infrastructure")
            .GetType("CLIQuotaMonitor.Infrastructure.WindowsStartupManager");
        Assert.NotNull(type);

        var valueName = $"CLIQuotaMonitorTest_{Guid.NewGuid():N}";
        var manager = Activator.CreateInstance(type!, valueName, "C:\\Tools\\cli-quota-monitor.exe");
        Assert.NotNull(manager);
        try
        {
            var isEnabled = type!.GetMethod("IsEnabled");
            var setEnabled = type.GetMethod("SetEnabled");
            Assert.NotNull(isEnabled);
            Assert.NotNull(setEnabled);

            Assert.False((bool)isEnabled!.Invoke(manager, null)!);
            setEnabled!.Invoke(manager, [true]);
            Assert.True((bool)isEnabled.Invoke(manager, null)!);
            setEnabled.Invoke(manager, [false]);
            Assert.False((bool)isEnabled.Invoke(manager, null)!);
        }
        finally
        {
            CleanupStartupEntry(type!, manager!);
        }

        static void CleanupStartupEntry(Type type, object manager)
        {
            type.GetMethod("SetEnabled")?.Invoke(manager, [false]);
        }
    }

    [Fact]
    public void Conpty_executor_is_exposed_as_a_query_executor()
    {
        var type = Assembly.Load("CLIQuotaMonitor.Infrastructure")
            .GetType("CLIQuotaMonitor.Infrastructure.ConPtyQueryExecutor");
        Assert.NotNull(type);
        Assert.Contains(typeof(IQueryExecutor), type!.GetInterfaces());
    }

    [Fact]
    public async Task Json_quota_cache_round_trips_the_last_successful_snapshot()
    {
        var type = Assembly.Load("CLIQuotaMonitor.Infrastructure")
            .GetType("CLIQuotaMonitor.Infrastructure.JsonQuotaCacheStore");
        Assert.NotNull(type);

        var root = Path.Combine(Path.GetTempPath(), "CLIQuotaMonitorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = Activator.CreateInstance(type!, Path.Combine(root, "cache.json"));
            var snapshot = new QuotaSnapshot
            {
                ProviderId = "codex",
                ProviderName = "Codex",
                Status = ProviderStatus.Ok,
                LastUpdated = DateTimeOffset.UtcNow,
                Quotas = [new QuotaItem("5H", 72, null)]
            };
            var save = (Task)type!.GetMethod("SetAsync")!.Invoke(store, [snapshot, CancellationToken.None])!;
            await save;
            var load = (Task)type.GetMethod("GetAsync")!.Invoke(store, ["codex", CancellationToken.None])!;
            await load;
            var loaded = (QuotaSnapshot?)load.GetType().GetProperty("Result")!.GetValue(load);

            Assert.NotNull(loaded);
            Assert.Equal(72, loaded!.Quotas.Single().RemainingPercent);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Query_router_dispatches_command_requests_to_the_command_executor()
    {
        var type = Assembly.Load("CLIQuotaMonitor.Infrastructure")
            .GetType("CLIQuotaMonitor.Infrastructure.QueryExecutorRouter");
        Assert.NotNull(type);

        var commandExecutor = new RecordingExecutor(QueryResult.Success("command", TimeSpan.Zero));
        var ptyExecutor = new RecordingExecutor(QueryResult.Success("pty", TimeSpan.Zero));
        var router = Activator.CreateInstance(type!, commandExecutor, ptyExecutor);
        var request = new QueryRequest
        {
            ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Method = QueryMethod.Command
        };
        var execute = type!.GetMethod("ExecuteAsync");
        Assert.NotNull(execute);
        var task = (Task)execute!.Invoke(router, [request, CancellationToken.None])!;
        await task;
        var result = (QueryResult)task.GetType().GetProperty("Result")!.GetValue(task)!;

        Assert.Equal("command", result.StandardOutput);
        Assert.Single(commandExecutor.Requests);
        Assert.Empty(ptyExecutor.Requests);
    }

    [Fact]
    public async Task Conpty_query_captures_output_from_a_short_lived_command()
    {
        var executor = new global::CLIQuotaMonitor.Infrastructure.ConPtyQueryExecutor();
        var request = new QueryRequest
        {
            ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/k",
            Timeout = TimeSpan.FromSeconds(10),
            Method = QueryMethod.Pty,
            InteractiveCommand = "echo conpty-quota-test",
            InteractiveExitCommand = "exit"
        };

        var result = await executor.ExecuteAsync(request);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(
            result.StandardOutput.Contains("conpty-quota-test", StringComparison.OrdinalIgnoreCase),
            $"Output=[{result.StandardOutput.Replace("\u001b", "<ESC>", StringComparison.Ordinal)}]");
    }

    [Fact]
    public async Task Conpty_query_returns_output_when_interactive_cli_does_not_exit_after_query()
    {
        var executor = new global::CLIQuotaMonitor.Infrastructure.ConPtyQueryExecutor();
        var request = new QueryRequest
        {
            ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/k",
            Timeout = TimeSpan.FromSeconds(10),
            Method = QueryMethod.Pty,
            InteractiveCommand = "echo quota: 72%",
            InteractiveExitCommand = "rem"
        };

        var result = await executor.ExecuteAsync(request);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Contains("quota: 72%", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Conpty_query_accepts_a_directory_trust_prompt_before_sending_the_query()
    {
        var executor = new global::CLIQuotaMonitor.Infrastructure.ConPtyQueryExecutor();
        var request = new QueryRequest
        {
            ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/V:ON /k \"echo Do you trust the contents of this directory? & set /p trust= & if \"!trust!\"==\"1\" echo quota: 72%\"",
            Timeout = TimeSpan.FromSeconds(10),
            Method = QueryMethod.Pty,
            InteractiveCommand = "/status",
            InteractiveExitCommand = "exit",
            AutoAcceptDirectoryTrustPrompt = true
        };

        var result = await executor.ExecuteAsync(request);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(
            result.StandardOutput.Contains("quota: 72%", StringComparison.OrdinalIgnoreCase),
            $"Output={result.StandardOutput.Replace("\u001b", "<ESC>", StringComparison.Ordinal)}");
    }

    [Fact]
    public async Task Conpty_cancellation_terminates_the_child_process_tree()
    {
        var executor = new global::CLIQuotaMonitor.Infrastructure.ConPtyQueryExecutor();
        var pidFile = Path.Combine(
            Path.GetTempPath(),
            $"CLIQuotaMonitor-OrphanProbe-{Guid.NewGuid():N}.txt");
        using var cancellation = new CancellationTokenSource();
        Task<QueryResult>? queryTask = null;
        var childPid = 0;

        try
        {
            var request = new QueryRequest
            {
                ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/k powershell.exe -NoProfile -Command \"Set-Content -LiteralPath '{pidFile}' -Value $PID; Start-Sleep -Seconds 60\"",
                Timeout = TimeSpan.FromSeconds(30),
                Method = QueryMethod.Pty,
                InteractiveCommand = "echo ready",
                InteractiveExitCommand = "exit"
            };

            queryTask = executor.ExecuteAsync(request, cancellation.Token);
            childPid = await WaitForPidFileAsync(pidFile, TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            var result = await queryTask;

            Assert.Equal(ProviderStatus.Error, result.Status);
            await WaitForProcessExitAsync(childPid, TimeSpan.FromSeconds(3));
            Assert.False(IsProcessRunning(childPid));
        }
        finally
        {
            cancellation.Cancel();
            if (queryTask is not null)
            {
                try
                {
                    await queryTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception)
                {
                    // Cleanup below is the important part of this process-tree test.
                }
            }

            TryKillProcessTree(childPid);
            File.Delete(pidFile);
        }
    }

    [Fact]
    public async Task Conpty_query_waits_for_delayed_interactive_output_before_terminating_query()
    {
        var executor = new global::CLIQuotaMonitor.Infrastructure.ConPtyQueryExecutor();
        var request = new QueryRequest
        {
            ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/k",
            Timeout = TimeSpan.FromSeconds(10),
            Method = QueryMethod.Pty,
            InteractiveCommand = "powershell.exe -NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 3; Write-Output ('delayed-quota-' + (2+3))\"",
            InteractiveExitCommand = "rem"
        };

        var result = await executor.ExecuteAsync(request);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Contains("delayed-quota-5", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingExecutor : IQueryExecutor
    {
        private readonly QueryResult _result;

        public RecordingExecutor(QueryResult result)
        {
            _result = result;
        }

        public List<QueryRequest> Requests { get; } = [];

        public Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _work = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            _work.Enqueue((d, state));
        }

        public void Drain()
        {
            while (_work.TryDequeue(out var item))
            {
                item.Callback(item.State);
            }
        }
    }

    private static async Task<int> WaitForPidFileAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path) &&
                int.TryParse(File.ReadAllText(path), out var processId) &&
                processId > 0)
            {
                return processId;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"The child process PID file was not created: {path}");
    }

    private static async Task WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && IsProcessRunning(processId))
        {
            await Task.Delay(50);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryKillProcessTree(int processId)
    {
        if (processId <= 0 || !IsProcessRunning(processId))
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // The child can exit between the check and cleanup.
        }
    }
}
