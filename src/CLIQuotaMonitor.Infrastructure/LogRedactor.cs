using System.Text.RegularExpressions;

namespace CLIQuotaMonitor.Infrastructure;

public static partial class LogRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var result = BearerTokenPattern().Replace(value, "$1[REDACTED]");
        result = ApiKeyPattern().Replace(result, "$1[REDACTED]");
        return SecretKeyPattern().Replace(result, "[REDACTED]");
    }

    [GeneratedRegex(@"(Bearer\s+)[^\s,;]+", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(@"((?:api[_-]?key|token|secret)\s*[=:]\s*)[^\s,;]+", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_-]+\b", RegexOptions.None, "en-US")]
    private static partial Regex SecretKeyPattern();
}
