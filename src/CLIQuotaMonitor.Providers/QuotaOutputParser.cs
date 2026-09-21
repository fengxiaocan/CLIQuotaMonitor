using System.Globalization;
using System.Text.RegularExpressions;
using CLIQuotaMonitor.Core.Models;

namespace CLIQuotaMonitor.Providers;

internal static partial class QuotaOutputParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public static QuotaSnapshot Parse(
        string output,
        string providerId,
        string providerName,
        DateTimeOffset now,
        bool parseSessionUsage = false)
    {
        var quotas = new List<QuotaItem>();
        string? account = null;
        string? sessionUsage = null;
        string? pendingGrokLimitName = null;

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = StripAnsi(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var accountMatch = AccountPattern().Match(line);
            if (accountMatch.Success)
            {
                account = accountMatch.Groups["value"].Value.Trim();
                continue;
            }

            if (string.Equals(providerId, "antigravity", StringComparison.OrdinalIgnoreCase) &&
                TryParseAntigravityTabularQuota(line, out var tabularQuota))
            {
                quotas.Add(tabularQuota);
                continue;
            }

            if (string.Equals(providerId, "grok", StringComparison.OrdinalIgnoreCase))
            {
                var grokLimitMatch = GrokLimitPercentPattern().Match(line);
                if (grokLimitMatch.Success &&
                    double.TryParse(
                        grokLimitMatch.Groups["percent"].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var grokPercent) &&
                    grokPercent is >= 0 and <= 100)
                {
                    var name = NormalizeQuotaName(grokLimitMatch.Groups["label"].Value);
                    var resetAt = ParseResetAt(grokLimitMatch.Groups["rest"].Value, now);
                    quotas.Add(new QuotaItem(name, ToRemainingPercent(providerId, grokPercent), resetAt));
                    pendingGrokLimitName = null;
                    continue;
                }

                var grokLimitLabelMatch = GrokLimitLabelPattern().Match(line);
                if (grokLimitLabelMatch.Success)
                {
                    pendingGrokLimitName = NormalizeQuotaName(grokLimitLabelMatch.Groups["label"].Value);
                    continue;
                }

                if (pendingGrokLimitName is not null)
                {
                    var standalonePercentMatch = StandalonePercentPattern().Match(line);
                    if (standalonePercentMatch.Success &&
                        double.TryParse(
                            standalonePercentMatch.Groups["percent"].Value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var standalonePercent) &&
                        standalonePercent is >= 0 and <= 100)
                    {
                        var resetAt = ParseResetAt(standalonePercentMatch.Groups["rest"].Value, now);
                        quotas.Add(new QuotaItem(
                            pendingGrokLimitName,
                            ToRemainingPercent(providerId, standalonePercent),
                            resetAt));
                        pendingGrokLimitName = null;
                        continue;
                    }
                }
            }

            var percentMatch = PercentPattern().Match(line);
            if (percentMatch.Success &&
                double.TryParse(
                    percentMatch.Groups["percent"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var percent) &&
                percent is >= 0 and <= 100)
            {
                var name = NormalizeQuotaName(providerId, percentMatch.Groups["label"].Value);
                var resetAt = ParseResetAt(percentMatch.Groups["rest"].Value, now);
                quotas.Add(new QuotaItem(name, ToRemainingPercent(providerId, percent), resetAt));
                continue;
            }

            if (!string.Equals(providerId, "grok", StringComparison.OrdinalIgnoreCase))
            {
                var valueMatch = ValuePattern().Match(line);
                if (valueMatch.Success &&
                    decimal.TryParse(
                        valueMatch.Groups["value"].Value.Replace(",", string.Empty),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var value))
                {
                    var name = NormalizeQuotaName(providerId, valueMatch.Groups["label"].Value);
                    var unit = valueMatch.Groups["unit"].Success
                        ? valueMatch.Groups["unit"].Value
                        : null;
                    var resetAt = ParseResetAt(valueMatch.Groups["rest"].Value, now);
                    quotas.Add(new QuotaItem(name, null, resetAt, value, unit));
                    continue;
                }
            }

            if (parseSessionUsage)
            {
                var sessionMatch = SessionPattern().Match(line);
                if (sessionMatch.Success)
                {
                    sessionUsage = sessionMatch.Groups["usage"].Value.Trim();
                }
            }
        }

        return new QuotaSnapshot
        {
            ProviderId = providerId,
            ProviderName = providerName,
            Account = account,
            Quotas = quotas,
            SessionUsage = sessionUsage,
            LastUpdated = now,
            Status = quotas.Count == 0 ? ProviderStatus.ParseError : ProviderStatus.Ok,
            ErrorMessage = quotas.Count == 0 ? "No quota values were found in CLI output." : null
        };
    }

    private static bool TryParseAntigravityTabularQuota(string line, out QuotaItem quota)
    {
        var fields = line.Split('\t', StringSplitOptions.TrimEntries);
        if (fields.Length < 3 ||
            string.IsNullOrWhiteSpace(fields[0]) ||
            string.IsNullOrWhiteSpace(fields[1]) ||
            !string.Equals(fields[0], "Gemini Models", StringComparison.OrdinalIgnoreCase))
        {
            quota = null!;
            return false;
        }

        var percentText = fields[2].Trim().TrimEnd('%');
        if (!double.TryParse(
                percentText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var percent) ||
            percent is < 0 or > 100)
        {
            quota = null!;
            return false;
        }

        var period = Regex.Replace(
                fields[1],
                @"\s+limit\s+remaining$",
                string.Empty,
                RegexOptions.IgnoreCase,
                RegexTimeout)
            .Trim();
        var name = period.Equals("Five Hour", StringComparison.OrdinalIgnoreCase)
            ? "5h"
            : period.Equals("Weekly", StringComparison.OrdinalIgnoreCase)
                ? "Weekly"
                : period;
        DateTimeOffset? resetAt = null;
        if (fields.Length > 3 &&
            DateTimeOffset.TryParse(
                fields[3],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsedResetAt))
        {
            resetAt = parsedResetAt;
        }

        quota = new QuotaItem(name, percent, resetAt);
        return true;
    }

    private static string StripAnsi(string value)
    {
        return AnsiPattern().Replace(value, string.Empty);
    }

    private static string NormalizeQuotaName(string value)
    {
        var name = Regex.Replace(
                value.Trim(),
                @"\s+limit(?:\s*\([^)]*\))?$",
                string.Empty,
                RegexOptions.IgnoreCase,
                RegexTimeout)
            .Trim();
        return string.IsNullOrWhiteSpace(name) ? "Limit" : name;
    }

    private static string NormalizeQuotaName(string providerId, string value)
    {
        var name = NormalizeQuotaName(value);
        if (string.Equals(providerId, "antigravity", StringComparison.OrdinalIgnoreCase) &&
            name.Equals("Five Hour", StringComparison.OrdinalIgnoreCase))
        {
            return "5h";
        }

        return name;
    }

    private static double ToRemainingPercent(string providerId, double percent)
    {
        return string.Equals(providerId, "grok", StringComparison.OrdinalIgnoreCase)
            ? 100 - percent
            : percent;
    }

    private static DateTimeOffset? ParseResetAt(string value, DateTimeOffset now)
    {
        var resetText = value.Trim().TrimEnd('.', ',', ';', ')');
        var relativeMatch = RelativeResetPattern().Match(resetText);
        if (relativeMatch.Success && TryParseDuration(relativeMatch.Groups["duration"].Value, out var duration))
        {
            return now.Add(duration);
        }

        var absoluteMatch = AbsoluteResetPattern().Match(resetText);
        if (absoluteMatch.Success &&
            DateTimeOffset.TryParse(
                absoluteMatch.Groups["value"].Value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var absolute))
        {
            return absolute;
        }

        return null;
    }

    private static bool TryParseDuration(string text, out TimeSpan duration)
    {
        var match = DurationPattern().Match(text);
        if (!match.Success || string.IsNullOrWhiteSpace(match.Value))
        {
            duration = default;
            return false;
        }

        var days = ParsePart(match.Groups["days"].Value);
        var hours = ParsePart(match.Groups["hours"].Value);
        var minutes = ParsePart(match.Groups["minutes"].Value);
        var seconds = ParsePart(match.Groups["seconds"].Value);
        duration = new TimeSpan(days, hours, minutes, seconds);
        return true;
    }

    private static int ParsePart(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    [GeneratedRegex(@"(?<label>[\p{L}\d][^:\r\n]*?)\s*:\s*(?:\[[^\]\r\n]*\]\s*)?(?<percent>\d{1,3}(?:\.\d+)?)\s*%\s*(?<rest>.*)$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"(?<![A-Za-z])(?<label>[A-Za-z][A-Za-z]*(?:\s+[A-Za-z][A-Za-z]*)*\s+limit(?:\s*\([^)]*\))?)\s*[^\p{L}\d%\r\n]*(?<percent>\d{1,3}(?:\.\d+)?)\s*%\s*(?<rest>[^│\r\n]*)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex GrokLimitPercentPattern();

    [GeneratedRegex(@"(?:^|[│|])\s*(?<label>[A-Za-z][A-Za-z]*(?:\s+[A-Za-z][A-Za-z]*)*\s+limit(?:\s*\([^)]*\))?)\s*(?:[│|])?\s*$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex GrokLimitLabelPattern();

    [GeneratedRegex(@"(?<percent>\d{1,3}(?:\.\d+)?)\s*%\s*(?<rest>.*)$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex StandalonePercentPattern();

    [GeneratedRegex(@"^(?<label>[^:]+?)\s*:\s*(?<value>\d[\d,.]*)(?:\s+(?<unit>[A-Za-z]+))?(?:\s+(?<rest>.*))?$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex ValuePattern();

    [GeneratedRegex(@"^Account\s*:\s*(?<value>.+)$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex AccountPattern();

    [GeneratedRegex(@"(?<usage>\d+(?:\.\d+)?\s*[KMGTP]?\s+tokens)\b", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex SessionPattern();

    [GeneratedRegex(@"(?:reset|refresh)(?:s|es)?\s+(?:in|after)\s+(?<duration>[^,;)]*)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex RelativeResetPattern();

    [GeneratedRegex(@"(?:reset|refresh)(?:s|es)?\s+(?:at|on)\s+(?<value>.+)$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex AbsoluteResetPattern();

    [GeneratedRegex(@"(?:(?<days>\d+)\s*d(?:ays?)?\s*)?(?:(?<hours>\d+)\s*h(?:ours?)?\s*)?(?:(?<minutes>\d+)\s*m(?:in(?:utes?)?)?\s*)?(?:(?<seconds>\d+)\s*s(?:ec(?:onds?)?)?\s*)?", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.None, "en-US")]
    private static partial Regex AnsiPattern();
}
