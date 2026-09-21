using Microsoft.Win32;
using System.Runtime.Versioning;

namespace CLIQuotaMonitor.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class WindowsStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;
    private readonly string _executablePath;
    private readonly string? _arguments;

    public WindowsStartupManager(string valueName, string executablePath)
        : this(valueName, executablePath, null)
    {
    }

    public WindowsStartupManager(string valueName, string executablePath, string? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(valueName))
        {
            throw new ArgumentException("A startup value name is required.", nameof(valueName));
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("An executable path is required.", nameof(executablePath));
        }

        _valueName = valueName.Trim();
        _executablePath = executablePath.Trim();
        _arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(_valueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null)
        {
            throw new InvalidOperationException("Unable to open the Windows startup registry key.");
        }

        if (enabled)
        {
            var command = QuoteExecutablePath(_executablePath);
            if (_arguments is not null)
            {
                command = $"{command} {_arguments}";
            }

            key.SetValue(_valueName, command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
        }
    }

    private static string QuoteExecutablePath(string path)
    {
        if (path.StartsWith('"') && path.EndsWith('"'))
        {
            return path;
        }

        return $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
