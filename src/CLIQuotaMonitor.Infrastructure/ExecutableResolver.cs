using CLIQuotaMonitor.Core.Providers;

namespace CLIQuotaMonitor.Infrastructure;

public sealed class ExecutableResolver : IExecutableResolver
{
    public string? Resolve(string commandName, string? configuredPath = null)
    {
        foreach (var candidate in GetConfiguredCandidates(configuredPath))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var candidateNames = GetCandidateNames(commandName).ToArray();
        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in pathEntries.Concat(GetCommonInstallDirectories()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var candidateName in candidateNames)
            {
                var candidate = Path.Combine(directory, candidateName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> GetConfiguredCandidates(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            yield break;
        }

        var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));
        yield return expanded;
    }

    private static IEnumerable<string> GetCandidateNames(string commandName)
    {
        var normalized = commandName.Trim().Trim('"');
        if (Path.GetExtension(normalized).Length > 0)
        {
            yield return normalized;
            yield break;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return $"{normalized}.exe";
            yield return $"{normalized}.cmd";
            yield return $"{normalized}.bat";
            yield return normalized;
            yield break;
        }

        yield return normalized;
        yield return $"{normalized}.exe";
        yield return $"{normalized}.cmd";
        yield return $"{normalized}.bat";
    }

    private static IEnumerable<string> GetCommonInstallDirectories()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var npmPrefix = Environment.GetEnvironmentVariable("NPM_CONFIG_PREFIX");

        var directories = new List<string>
        {
            Path.Combine(appData, "npm"),
            Path.Combine(localAppData, "npm"),
            Path.Combine(localAppData, "Microsoft", "WindowsApps"),
            Path.Combine(localAppData, "Programs", "nodejs"),
            Path.Combine(programFiles, "nodejs"),
            Path.Combine(programFilesX86, "nodejs")
        };

        if (!string.IsNullOrWhiteSpace(npmPrefix))
        {
            directories.Add(npmPrefix);
            directories.Add(Path.Combine(npmPrefix, "npm"));
        }

        return directories.Where(Directory.Exists);
    }
}
