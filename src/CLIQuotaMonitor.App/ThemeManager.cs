using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace CLIQuotaMonitor.App;

public sealed record ThemeDefinition(
    string Key,
    string DisplayName,
    string Description,
    string Accent,
    string AccentDark,
    string WindowBackground,
    string WindowBorder,
    string CardBackground,
    string CardBorder
);

public static class ThemeManager
{
    public static readonly IReadOnlyList<ThemeDefinition> Themes =
    [
        new("CyberBlue", "科技蓝", "经典深空灰蓝搭配青蓝点缀", "#38BDF8", "#2563EB", "#F20F172A", "#334155", "#261E293B", "#2E3F57"),
        new("AuroraEmerald", "极光绿", "清新极光墨绿与翡翠色调", "#10B981", "#059669", "#F2071A15", "#1B4336", "#26112C24", "#1E4D3E"),
        new("NeonViolet", "幻彩紫", "赛博霓虹深紫与优雅罗兰色", "#A855F7", "#7C3AED", "#F2130F24", "#3D2B59", "#26231A3B", "#422E68"),
        new("SunsetAmber", "日落金", "温暖黑曜底色与暖金琥珀", "#F59E0B", "#D97706", "#F21C150D", "#4B3A23", "#262E2316", "#503D24"),
        new("ObsidianDark", "暗夜黑", "纯粹极简深黑与冰霜灰蓝", "#60A5FA", "#475569", "#F405070B", "#262A33", "#26161922", "#29303D")
    ];

    public static ThemeDefinition GetTheme(string? key)
    {
        return Themes.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? Themes[0];
    }

    public static void ApplyTheme(string? key)
    {
        if (Application.Current is null)
        {
            return;
        }

        var theme = GetTheme(key);
        var res = Application.Current.Resources;

        var accentColor = (Color)ColorConverter.ConvertFromString(theme.Accent);
        var accentDarkColor = (Color)ColorConverter.ConvertFromString(theme.AccentDark);
        var winBg = (Color)ColorConverter.ConvertFromString(theme.WindowBackground);
        var winBorder = (Color)ColorConverter.ConvertFromString(theme.WindowBorder);
        var cardBg = (Color)ColorConverter.ConvertFromString(theme.CardBackground);
        var cardBorder = (Color)ColorConverter.ConvertFromString(theme.CardBorder);

        res["ThemeAccentBrush"] = new SolidColorBrush(accentColor);
        res["ThemeAccentDarkBrush"] = new SolidColorBrush(accentDarkColor);
        res["ThemeAccentColor"] = accentColor;
        res["ThemeWindowBackgroundBrush"] = new SolidColorBrush(winBg);
        res["ThemeWindowBorderBrush"] = new SolidColorBrush(winBorder);
        res["ThemeCardBackgroundBrush"] = new SolidColorBrush(cardBg);
        res["ThemeCardBorderBrush"] = new SolidColorBrush(cardBorder);
    }
}
