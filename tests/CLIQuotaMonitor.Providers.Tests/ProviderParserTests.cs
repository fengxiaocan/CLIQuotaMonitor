using System.Collections;
using System.Reflection;
using CLIQuotaMonitor.Core.Models;

namespace CLIQuotaMonitor.Providers.Tests;

public sealed class ProviderParserTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 20, 20, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Codex_parser_reads_dynamic_percentages_and_relative_reset()
    {
        const string output = """
            Codex Status
            Account: developer@example.com
            5h limit: 72% remaining, reset in 2h 43m
            Weekly limit: 43% remaining, reset at 2026-09-23T04:12:00+08:00
            Credits: 120
            """;

        var snapshot = Parse("CodexQuotaParser", output);
        var quotas = GetProperty<IEnumerable>(snapshot, "Quotas").Cast<object>().ToList();

        Assert.Equal("codex", GetProperty<string>(snapshot, "ProviderId"));
        Assert.Equal(3, quotas.Count);
        Assert.Equal(72d, GetProperty<double?>(quotas[0], "RemainingPercent"));
        Assert.Equal(43d, GetProperty<double?>(quotas[1], "RemainingPercent"));
        Assert.Equal(120m, GetProperty<decimal?>(quotas[2], "RemainingValue"));
    }

    [Fact]
    public void Codex_parser_reads_percentages_from_terminal_progress_bars()
    {
        const string output = """
            5h limit: [██████████████████░░] 89% left (resets 16:51)
            Weekly limit: [████████████████░░░░] 81% left (resets 14:38 on 27 Sep)
            """;

        var snapshot = Parse("CodexQuotaParser", output);
        var quotas = GetProperty<IEnumerable>(snapshot, "Quotas").Cast<object>().ToList();

        Assert.Equal(2, quotas.Count);
        Assert.Equal("5h", GetProperty<string>(quotas[0], "Name"));
        Assert.Equal(89d, GetProperty<double?>(quotas[0], "RemainingPercent"));
        Assert.Equal(81d, GetProperty<double?>(quotas[1], "RemainingPercent"));
    }

    [Fact]
    public void Codex_parser_does_not_treat_session_identifier_as_a_quota_value()
    {
        const string output = """
            Session: 01a0c252-5f64-78e1-97a4-155dccd14b95
            5h limit: [██████████████████░░] 88% left (resets 16:51)
            """;

        var snapshot = Parse("CodexQuotaParser", output);
        var quotas = GetProperty<IEnumerable>(snapshot, "Quotas").Cast<object>().ToList();

        Assert.Single(quotas);
        Assert.Equal("5h", GetProperty<string>(quotas[0], "Name"));
    }

    [Fact]
    public void Grok_parser_keeps_account_limit_separate_from_session_usage()
    {
        const string output = """
            Account Usage
            Limit: 53% used; resets in 4h 11m
            Current Session
            2.3M tokens
            """;

        var snapshot = Parse("GrokQuotaParser", output);

        Assert.Equal("grok", GetProperty<string>(snapshot, "ProviderId"));
        Assert.Equal("2.3M tokens", GetProperty<string?>(snapshot, "SessionUsage"));
        var quotas = GetProperty<IEnumerable>(snapshot, "Quotas").Cast<object>().ToList();
        Assert.Single(quotas);
        Assert.Equal(47d, GetProperty<double?>(quotas[0], "RemainingPercent"));
    }

    [Fact]
    public void Grok_parser_reads_usage_modal_limit_without_a_colon()
    {
        const string output = """
            Weekly limit (SuperGrok)
            ███████████████████████░░░░░░░78%
            Resets: September 24, 17:14
            Session usage: no model calls yet in this session.
            """;

        var snapshot = Parse("GrokQuotaParser", output);
        var quotas = GetProperty<IEnumerable>(snapshot, "Quotas").Cast<object>().ToList();

        Assert.Single(quotas);
        Assert.Equal("Weekly", GetProperty<string>(quotas[0], "Name"));
        Assert.Equal(22d, GetProperty<double?>(quotas[0], "RemainingPercent"));
    }

    [Fact]
    public void Grok_parser_reads_limit_when_terminal_redraw_joins_label_and_progress_bar()
    {
        const string output =
            "ContextusageUsage limitSession info\u0008⠙2\u00083⠹4⠸6⠼7Weekly limit (SuperGrok)" +
            "███████████████████████░░░░░░░78%Resets: September 24, 17:14";

        var snapshot = Parse("GrokQuotaParser", output);
        var quotas = GetProperty<IEnumerable>(snapshot, "Quotas").Cast<object>().ToList();

        Assert.Single(quotas);
        Assert.Equal("Weekly", GetProperty<string>(quotas[0], "Name"));
        Assert.Equal(22d, GetProperty<double?>(quotas[0], "RemainingPercent"));
    }

    [Fact]
    public void Grok_parser_does_not_treat_non_percentage_values_as_quota()
    {
        const string output = "Desktop: 90 tokens";

        var snapshot = Parse("GrokQuotaParser", output);
        var quotas = GetProperty<IEnumerable>(snapshot, "Quotas").Cast<object>().ToList();

        Assert.Empty(quotas);
        Assert.Equal(ProviderStatus.ParseError, GetProperty<ProviderStatus>(snapshot, "Status"));
    }

    [Fact]
    public void Antigravity_parser_recognizes_five_hour_and_weekly_windows()
    {
        const string output = """
            Weekly Limit: 78% remaining, refreshes in 3d 4h
            Five Hour Limit: 91% remaining, refreshes in 4h 55m
            """;

        var snapshot = Parse("AntigravityQuotaParser", output);
        var quotas = GetProperty<IEnumerable>(snapshot, "Quotas").Cast<object>().ToList();

        Assert.Equal("antigravity", GetProperty<string>(snapshot, "ProviderId"));
        Assert.Equal(2, quotas.Count);
        Assert.Equal("Weekly", GetProperty<string>(quotas[0], "Name"));
        Assert.Equal("5h", GetProperty<string>(quotas[1], "Name"));
        Assert.Equal(91d, GetProperty<double?>(quotas[1], "RemainingPercent"));
    }

    [Fact]
    public void Antigravity_parser_reads_tabular_usage_output()
    {
        const string output = """
            Gemini Models	Weekly Limit Remaining	1%	2026-09-23T04:13:58Z
            Gemini Models	Five Hour Limit Remaining	100%	2026-09-21T09:29:34Z
            Claude and GPT models	Weekly Limit Remaining	100%	2026-09-28T04:29:34Z
            Claude and GPT models	Five Hour Limit Remaining	100%	2026-09-21T09:29:34Z
            """;

        var snapshot = Parse("AntigravityQuotaParser", output);
        var quotas = GetProperty<IEnumerable>(snapshot, "Quotas").Cast<object>().ToList();

        Assert.Equal(2, quotas.Count);
        Assert.Equal("Weekly", GetProperty<string>(quotas[0], "Name"));
        Assert.Equal(1d, GetProperty<double?>(quotas[0], "RemainingPercent"));
        Assert.Equal("5h", GetProperty<string>(quotas[1], "Name"));
        Assert.Equal(100d, GetProperty<double?>(quotas[1], "RemainingPercent"));
        Assert.DoesNotContain(quotas, quota =>
            GetProperty<string>(quota, "Name").Contains("Claude", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            new DateTimeOffset(2026, 9, 23, 4, 13, 58, TimeSpan.Zero),
            GetProperty<DateTimeOffset?>(quotas[0], "ResetAt"));
    }

    private static object Parse(string parserTypeName, string output)
    {
        var type = Assembly.Load("CLIQuotaMonitor.Providers")
            .GetType($"CLIQuotaMonitor.Providers.{parserTypeName}");
        Assert.NotNull(type);

        var parser = Activator.CreateInstance(type!);
        Assert.NotNull(parser);
        var parseMethod = type!.GetMethod("Parse");
        Assert.NotNull(parseMethod);

        var snapshot = parseMethod!.Invoke(parser, [output, Now]);
        Assert.NotNull(snapshot);
        return snapshot!;
    }

    private static T GetProperty<T>(object target, string name)
    {
        var property = target.GetType().GetProperty(name);
        Assert.NotNull(property);
        return (T)property!.GetValue(target)!;
    }
}
