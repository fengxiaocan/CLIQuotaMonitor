using System.Reflection;
using CLIQuotaMonitor.Core.Models;

namespace CLIQuotaMonitor.Core.Tests;

public sealed class WorkTimePolicyTests
{
    [Fact]
    public void New_settings_use_a_disabled_weekday_schedule_by_default()
    {
        var settings = new AppSettings();
        var workTime = GetWorkTime(settings);

        Assert.False(GetProperty<bool>(workTime, "Enabled"));
        Assert.Equal("09:00", GetProperty<string>(workTime, "Start"));
        Assert.Equal("18:00", GetProperty<string>(workTime, "End"));
        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            GetProperty<IEnumerable<DayOfWeek>>(workTime, "Days"));
    }

    [Fact]
    public void Allows_refresh_at_the_start_of_a_selected_workday_window()
    {
        var settings = CreateSettings(true, "09:00", "18:00", DayOfWeek.Monday);

        Assert.True(IsWithinWorkTime(settings, At(2026, 9, 21, 9, 0)));
    }

    [Fact]
    public void Rejects_refresh_at_the_end_of_a_workday_window()
    {
        var settings = CreateSettings(true, "09:00", "18:00", DayOfWeek.Monday);

        Assert.False(IsWithinWorkTime(settings, At(2026, 9, 21, 18, 0)));
    }

    [Fact]
    public void Rejects_refresh_on_a_day_that_is_not_selected()
    {
        var settings = CreateSettings(true, "09:00", "18:00", DayOfWeek.Monday);

        Assert.False(IsWithinWorkTime(settings, At(2026, 9, 22, 10, 0)));
    }

    [Fact]
    public void Disabled_work_time_limit_allows_refresh_at_any_time()
    {
        var settings = CreateSettings(false, "09:00", "18:00", DayOfWeek.Monday);

        Assert.True(IsWithinWorkTime(settings, At(2026, 9, 22, 23, 30)));
    }

    [Fact]
    public void Invalid_enabled_schedule_rejects_automatic_refresh()
    {
        var settings = CreateSettings(true, "18:00", "09:00");

        Assert.False(IsWithinWorkTime(settings, At(2026, 9, 21, 10, 0)));
    }

    [Fact]
    public void Validation_rejects_a_schedule_without_selected_days()
    {
        var settings = CreateSettings(false, "09:00", "18:00");
        var workTime = GetWorkTime(settings);
        var policyType = typeof(AppSettings).Assembly.GetType("CLIQuotaMonitor.Core.Time.WorkTimePolicy");
        Assert.NotNull(policyType);

        var method = policyType!.GetMethod("TryValidate", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var arguments = new object?[] { workTime, null };

        var valid = Assert.IsType<bool>(method!.Invoke(null, arguments));

        Assert.False(valid);
        Assert.Equal("请至少选择一个工作日。", arguments[1]);
    }

    [Fact]
    public void Automatic_refresh_requires_both_auto_refresh_and_work_time_to_be_allowed()
    {
        var settings = CreateSettings(true, "09:00", "18:00", DayOfWeek.Monday);

        Assert.True(ShouldRefreshAutomatically(settings, At(2026, 9, 21, 10, 0)));

        settings.AutoRefreshEnabled = false;
        Assert.False(ShouldRefreshAutomatically(settings, At(2026, 9, 21, 10, 0)));

        settings.AutoRefreshEnabled = true;
        Assert.False(ShouldRefreshAutomatically(settings, At(2026, 9, 21, 19, 0)));
    }

    private static AppSettings CreateSettings(
        bool enabled,
        string start,
        string end,
        params DayOfWeek[] days)
    {
        var settings = new AppSettings();
        var workTime = GetWorkTime(settings);
        SetProperty(workTime, "Enabled", enabled);
        SetProperty(workTime, "Start", start);
        SetProperty(workTime, "End", end);

        var selectedDays = GetProperty<IList<DayOfWeek>>(workTime, "Days");
        selectedDays.Clear();
        foreach (var day in days)
        {
            selectedDays.Add(day);
        }

        return settings;
    }

    private static bool IsWithinWorkTime(AppSettings settings, DateTimeOffset now)
    {
        var workTime = GetWorkTime(settings);
        var policyType = typeof(AppSettings).Assembly.GetType("CLIQuotaMonitor.Core.Time.WorkTimePolicy");
        Assert.NotNull(policyType);

        var method = policyType!.GetMethod("IsWithinWorkTime", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        return Assert.IsType<bool>(method!.Invoke(null, [workTime, now]));
    }

    private static bool ShouldRefreshAutomatically(AppSettings settings, DateTimeOffset now)
    {
        var policyType = typeof(AppSettings).Assembly.GetType("CLIQuotaMonitor.Core.Time.WorkTimePolicy");
        Assert.NotNull(policyType);

        var method = policyType!.GetMethod("ShouldRefreshAutomatically", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        return Assert.IsType<bool>(method!.Invoke(null, [settings, now]));
    }

    private static object GetWorkTime(AppSettings settings)
    {
        var property = typeof(AppSettings).GetProperty("WorkTime");
        Assert.NotNull(property);
        return property!.GetValue(settings)!;
    }

    private static T GetProperty<T>(object target, string name)
    {
        var property = target.GetType().GetProperty(name);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<T>(property!.GetValue(target));
    }

    private static void SetProperty(object target, string name, object value)
    {
        var property = target.GetType().GetProperty(name);
        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute = 0)
    {
        return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(8));
    }
}
