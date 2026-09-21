using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CLIQuotaMonitor.Core.Models;
using Binding = System.Windows.Data.Binding;

namespace CLIQuotaMonitor.App;

public sealed class DisplayModeVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var expected = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(expected))
        {
            return Visibility.Visible;
        }

        var mode = value is DisplayMode displayMode ? displayMode.ToString() : string.Empty;

        // Support "!Minimal" for negation
        if (expected.StartsWith('!'))
        {
            return !string.Equals(mode, expected[1..], StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Support comma separated list, e.g. "Detailed,Compact"
        var modes = expected.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return modes.Any(m => string.Equals(mode, m, StringComparison.OrdinalIgnoreCase))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
