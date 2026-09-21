using System.Globalization;
using CLIQuotaMonitor.Core.Models;

namespace CLIQuotaMonitor.Core.Time;

public static class WorkTimePolicy
{
    public static bool ShouldRefreshAutomatically(AppSettings? settings, DateTimeOffset now)
    {
        return settings is not null &&
               settings.AutoRefreshEnabled &&
               IsWithinWorkTime(settings.WorkTime, now);
    }

    public static bool IsWithinWorkTime(WorkTimeSettings? settings, DateTimeOffset now)
    {
        if (settings is null || !settings.Enabled)
        {
            return true;
        }

        if (!TryValidate(settings, out _) ||
            settings.Days is null ||
            !settings.Days.Contains(now.DayOfWeek))
        {
            return false;
        }

        var start = TimeSpan.ParseExact(settings.Start, @"hh\:mm", CultureInfo.InvariantCulture);
        var end = TimeSpan.ParseExact(settings.End, @"hh\:mm", CultureInfo.InvariantCulture);
        return now.TimeOfDay >= start && now.TimeOfDay < end;
    }

    public static bool TryValidate(WorkTimeSettings? settings, out string errorMessage)
    {
        if (settings is null)
        {
            errorMessage = "工作时间设置不存在。";
            return false;
        }

        if (settings.Days is null || settings.Days.Count == 0)
        {
            errorMessage = "请至少选择一个工作日。";
            return false;
        }

        if (!TryParse(settings.Start, out var start) || !TryParse(settings.End, out var end))
        {
            errorMessage = "工作时间必须使用 HH:mm 格式。";
            return false;
        }

        if (start >= end)
        {
            errorMessage = "结束时间必须晚于开始时间。";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool TryParse(string? value, out TimeSpan result)
    {
        return TimeSpan.TryParseExact(
            value,
            @"hh\:mm",
            CultureInfo.InvariantCulture,
            out result);
    }
}
