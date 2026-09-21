using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Query;

namespace CLIQuotaMonitor.Infrastructure;

public sealed class ConPtyQueryExecutor : IQueryExecutor
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint ProcThreadAttributePseudoConsole = 0x00020016;
    private const int StartfUseStdHandles = 0x00000100;
    private const uint WaitTimeout = 0x00000102;

    public async Task<QueryResult> ExecuteAsync(
        QueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!OperatingSystem.IsWindows())
        {
            return QueryResult.Failure(
                ProviderStatus.Error,
                "ConPTY is only available on Windows.",
                stopwatch.Elapsed);
        }

        if (string.IsNullOrWhiteSpace(request.ExecutablePath) || !File.Exists(request.ExecutablePath))
        {
            return QueryResult.Failure(
                ProviderStatus.NotInstalled,
                $"Executable was not found: {request.ExecutablePath}",
                stopwatch.Elapsed);
        }

        IntPtr inputRead = IntPtr.Zero;
        IntPtr inputWrite = IntPtr.Zero;
        IntPtr outputRead = IntPtr.Zero;
        IntPtr outputWrite = IntPtr.Zero;
        IntPtr pseudoConsole = IntPtr.Zero;
        IntPtr processHandle = IntPtr.Zero;
        IntPtr threadHandle = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        Task? outputTask = null;
        StringBuilder? outputBuilder = null;

        try
        {
            ThrowIfFalse(CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0), "CreatePipe(input)");
            ThrowIfFalse(CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0), "CreatePipe(output)");

            var size = new Coord { X = 160, Y = 50 };
            ThrowIfHResultFailed(CreatePseudoConsole(size, inputRead, outputWrite, 0, out pseudoConsole), "CreatePseudoConsole");

            var attributeSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
            attributeList = Marshal.AllocHGlobal(attributeSize);
            ThrowIfFalse(
                InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeSize),
                "InitializeProcThreadAttributeList");
            ThrowIfFalse(
                UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    (IntPtr)ProcThreadAttributePseudoConsole,
                    pseudoConsole,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero),
                "UpdateProcThreadAttribute");

            var startupInfo = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Cb = Marshal.SizeOf<StartupInfoEx>(),
                    Flags = StartfUseStdHandles,
                    StandardInput = IntPtr.Zero,
                    StandardOutput = IntPtr.Zero,
                    StandardError = IntPtr.Zero
                },
                AttributeList = attributeList
            };

            var commandLine = BuildCommandLine(request);

            ThrowIfFalse(
                CreateProcess(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ExtendedStartupInfoPresent,
                    IntPtr.Zero,
                    string.IsNullOrWhiteSpace(request.WorkingDirectory)
                        ? Environment.CurrentDirectory
                        : request.WorkingDirectory,
                    ref startupInfo,
                    out var processInformation),
                "CreateProcess");
            processHandle = processInformation.ProcessHandle;
            threadHandle = processInformation.ThreadHandle;
            CloseHandle(inputRead);
            inputRead = IntPtr.Zero;
            CloseHandle(outputWrite);
            outputWrite = IntPtr.Zero;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(request.Timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : request.Timeout);
            outputBuilder = new StringBuilder();
            outputTask = ReadOutputAsync(outputRead, outputBuilder, timeout.Token);

            await Task.Delay(TimeSpan.FromMilliseconds(1500), timeout.Token).ConfigureAwait(false);
            if (WaitForSingleObject(processHandle, 0) == WaitTimeout &&
                !string.IsNullOrWhiteSpace(request.InteractiveCommand))
            {
                if (request.AutoAcceptDirectoryTrustPrompt &&
                    await WaitForDirectoryTrustPromptAsync(outputBuilder, timeout.Token).ConfigureAwait(false))
                {
                    await WriteLineAsync(inputWrite, "1", timeout.Token).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token).ConfigureAwait(false);
                }

                await WriteLineAsync(inputWrite, request.InteractiveCommand, timeout.Token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(5000), timeout.Token).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(request.InteractiveExitCommand) &&
                WaitForSingleObject(processHandle, 0) == WaitTimeout)
            {
                await WriteLineAsync(inputWrite, request.InteractiveExitCommand, timeout.Token).ConfigureAwait(false);
            }

            var processExited = await WaitForProcessAsync(
                processHandle,
                TimeSpan.FromSeconds(1),
                timeout.Token).ConfigureAwait(false);
            if (!processExited)
            {
                TryTerminate(processHandle);
            }
            else
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token).ConfigureAwait(false);
            }
            ClosePseudoConsole(pseudoConsole);
            pseudoConsole = IntPtr.Zero;
            CloseHandle(inputWrite);
            inputWrite = IntPtr.Zero;
            if (!await DrainOutputAsync(outputTask).ConfigureAwait(false))
            {
                CloseHandle(outputRead);
                outputRead = IntPtr.Zero;
                await DrainOutputAsync(outputTask).ConfigureAwait(false);
            }
            else
            {
                CloseHandle(outputRead);
                outputRead = IntPtr.Zero;
            }
            stopwatch.Stop();
            return QueryResult.Success(GetOutput(outputBuilder), stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminate(processHandle);
            CloseCommunicationHandles(ref pseudoConsole, ref inputWrite, ref outputRead);
            await DrainOutputAsync(outputTask).ConfigureAwait(false);
            stopwatch.Stop();
            return QueryResult.Failure(
                ProviderStatus.Timeout,
                $"PTY query timed out after {request.Timeout.TotalSeconds:0.#} seconds.",
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(processHandle);
            CloseCommunicationHandles(ref pseudoConsole, ref inputWrite, ref outputRead);
            await DrainOutputAsync(outputTask).ConfigureAwait(false);
            stopwatch.Stop();
            return QueryResult.Failure(
                ProviderStatus.Error,
                "PTY query was cancelled.",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or Win32Exception)
        {
            TryTerminate(processHandle);
            CloseCommunicationHandles(ref pseudoConsole, ref inputWrite, ref outputRead);
            await DrainOutputAsync(outputTask).ConfigureAwait(false);
            stopwatch.Stop();
            return QueryResult.Failure(
                ProviderStatus.Error,
                LogRedactor.Redact(exception.Message),
                stopwatch.Elapsed);
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (threadHandle != IntPtr.Zero)
            {
                CloseHandle(threadHandle);
            }

            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
            }

            if (pseudoConsole != IntPtr.Zero)
            {
                ClosePseudoConsole(pseudoConsole);
            }

            CloseHandle(inputRead);
            CloseHandle(inputWrite);
            CloseHandle(outputRead);
            CloseHandle(outputWrite);
        }
    }

    private static async Task<bool> WaitForDirectoryTrustPromptAsync(
        StringBuilder outputBuilder,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (ContainsDirectoryTrustPrompt(outputBuilder))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        return ContainsDirectoryTrustPrompt(outputBuilder);
    }

    private static bool ContainsDirectoryTrustPrompt(StringBuilder outputBuilder)
    {
        string output;
        lock (outputBuilder)
        {
            output = outputBuilder.ToString();
        }

        return output.Contains(
            "Do you trust the contents of this directory?",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetOutput(StringBuilder outputBuilder)
    {
        lock (outputBuilder)
        {
            return outputBuilder.ToString();
        }
    }

    private static Task ReadOutputAsync(
        IntPtr outputHandle,
        StringBuilder builder,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var buffer = new byte[4096];
            try
            {
                while (!cancellationToken.IsCancellationRequested && outputHandle != IntPtr.Zero)
                {
                    if (!ReadFile(outputHandle, buffer, buffer.Length, out var bytesRead, IntPtr.Zero) || bytesRead == 0)
                    {
                        return;
                    }

                    lock (builder)
                    {
                        builder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Disposing the output stream after process exit is the normal EOF path for ConPTY.
            }
        }, CancellationToken.None);
    }

    private static async Task<bool> DrainOutputAsync(Task? outputTask)
    {
        if (outputTask is null)
        {
            return true;
        }

        try
        {
            await outputTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task<bool> WaitForProcessAsync(
        IntPtr processHandle,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        var milliseconds = waitTimeout.TotalMilliseconds >= uint.MaxValue
            ? uint.MaxValue
            : (uint)Math.Max(1, waitTimeout.TotalMilliseconds);
        var waitTask = Task.Run(() => WaitForSingleObject(processHandle, milliseconds));
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var completed = await Task.WhenAny(waitTask, cancellationTask).ConfigureAwait(false);
        if (completed != waitTask)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        var result = await waitTask.ConfigureAwait(false);
        if (result == WaitTimeout)
        {
            return false;
        }

        if (result == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WaitForSingleObject");
        }

        return true;
    }

    private static Task WriteLineAsync(IntPtr inputHandle, string command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Encoding.UTF8.GetBytes($"{command.Trim()}\r");
        ThrowIfFalse(WriteFile(inputHandle, bytes, bytes.Length, out _, IntPtr.Zero), "WriteFile");
        return Task.CompletedTask;
    }

    private static void CloseCommunicationHandles(
        ref IntPtr pseudoConsole,
        ref IntPtr inputWrite,
        ref IntPtr outputRead)
    {
        if (pseudoConsole != IntPtr.Zero)
        {
            ClosePseudoConsole(pseudoConsole);
            pseudoConsole = IntPtr.Zero;
        }

        CloseHandle(inputWrite);
        inputWrite = IntPtr.Zero;
        CloseHandle(outputRead);
        outputRead = IntPtr.Zero;
    }

    private static void TryTerminate(IntPtr processHandle)
    {
        if (processHandle != IntPtr.Zero && WaitForSingleObject(processHandle, 0) == WaitTimeout)
        {
            TerminateProcess(processHandle, 1);
        }
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static StringBuilder BuildCommandLine(QueryRequest request)
    {
        if (IsBatchFile(request.ExecutablePath))
        {
            var commandLine = new StringBuilder();
            var comSpec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            commandLine.Append(Quote(comSpec)).Append(" /d /s /c \"");
            commandLine.Append(Quote(request.ExecutablePath));
            if (!string.IsNullOrWhiteSpace(request.Arguments))
            {
                commandLine.Append(' ').Append(request.Arguments);
            }

            commandLine.Append('"');
            return commandLine;
        }

        var normalCommandLine = new StringBuilder(
            request.ExecutablePath.Contains(' ', StringComparison.Ordinal)
                ? Quote(request.ExecutablePath)
                : request.ExecutablePath);
        if (!string.IsNullOrWhiteSpace(request.Arguments))
        {
            normalCommandLine.Append(' ').Append(request.Arguments);
        }

        return normalCommandLine;
    }

    private static bool IsBatchFile(string path)
    {
        return string.Equals(Path.GetExtension(path), ".cmd", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(path), ".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static void ThrowIfFalse(bool result, string operation)
    {
        if (!result)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
        }
    }

    private static void ThrowIfHResultFailed(int result, string operation)
    {
        if (result != 0)
        {
            throw new Win32Exception(result, operation);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved3;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out IntPtr readPipe,
        out IntPtr writePipe,
        IntPtr securityAttributes,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output, uint flags, out IntPtr pseudoConsole);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr pseudoConsole);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        int attributeCount,
        int flags,
        ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        IntPtr attribute,
        IntPtr value,
        IntPtr size,
        IntPtr previousValue,
        IntPtr returnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr fileHandle,
        byte[] buffer,
        int numberOfBytesToRead,
        out int numberOfBytesRead,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        IntPtr fileHandle,
        byte[] buffer,
        int numberOfBytesToWrite,
        out int numberOfBytesWritten,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);

}
