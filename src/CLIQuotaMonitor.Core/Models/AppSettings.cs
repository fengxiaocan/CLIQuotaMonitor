namespace CLIQuotaMonitor.Core.Models;

public sealed class AppSettings
{
    public int RefreshIntervalSeconds { get; set; } = 300;

    public bool AutoRefreshEnabled { get; set; } = true;

    public WorkTimeSettings WorkTime { get; set; } = new();

    public bool StartWithWindows { get; set; }

    public DisplayMode DisplayMode { get; set; } = DisplayMode.Compact;

    public string Theme { get; set; } = "CyberBlue";

    public WindowSettings Window { get; set; } = new();

    public NotificationSettings Notifications { get; set; } = new();

    public List<ProviderSettings> Providers { get; set; } =
    [
        new() { Id = "codex", SortOrder = 0 },
        new() { Id = "grok", SortOrder = 1 },
        new() { Id = "antigravity", SortOrder = 2 }
    ];
}

public sealed class WorkTimeSettings
{
    public bool Enabled { get; set; }

    public string Start { get; set; } = "09:00";

    public string End { get; set; } = "18:00";

    public List<DayOfWeek> Days { get; set; } =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday
    ];
}

public sealed class WindowSettings
{
    public double? Left { get; set; }

    public double? Top { get; set; }

    public bool Topmost { get; set; } = true;

    public bool Locked { get; set; }

    public bool ClickThrough { get; set; }

    public double Opacity { get; set; } = 0.85;

    public bool StartMinimized { get; set; }
}

public sealed class NotificationSettings
{
    public bool Enabled { get; set; } = true;

    public int LowQuotaThresholdPercent { get; set; } = 20;

    public bool NotifyOnReset { get; set; } = true;

    public string? QuietHoursStart { get; set; } = "22:00";

    public string? QuietHoursEnd { get; set; } = "08:00";
}
