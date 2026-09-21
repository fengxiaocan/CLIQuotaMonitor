using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Time;
using CLIQuotaMonitor.Infrastructure;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using TextBox = System.Windows.Controls.TextBox;

namespace CLIQuotaMonitor.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly double _initialOpacity;
    private readonly string _initialTheme;
    private string _selectedThemeKey;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _initialOpacity = settings.Window.Opacity;
        _initialTheme = string.IsNullOrWhiteSpace(settings.Theme) ? "CyberBlue" : settings.Theme;
        _selectedThemeKey = _initialTheme;

        // Theme Palette Selection
        ThemeListBox.ItemsSource = ThemeManager.Themes;
        ThemeListBox.SelectedItem = ThemeManager.GetTheme(_initialTheme);

        Icon = AppIconGenerator.GetAppImageSource();

        // General & Window
        AutoRefreshCheckBox.IsChecked = settings.AutoRefreshEnabled;
        var workTime = settings.WorkTime ?? new WorkTimeSettings();
        settings.WorkTime = workTime;
        WorkTimeEnabledCheckBox.IsChecked = workTime.Enabled;
        WorkTimeStartTextBox.Text = workTime.Start;
        WorkTimeEndTextBox.Text = workTime.End;
        SetWorkDayCheckboxes(workTime.Days);
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        StartMinimizedCheckBox.IsChecked = settings.Window.StartMinimized;
        LockedCheckBox.IsChecked = settings.Window.Locked;
        TopmostCheckBox.IsChecked = settings.Window.Topmost;
        ClickThroughCheckBox.IsChecked = settings.Window.ClickThrough;
        OpacitySlider.Value = settings.Window.Opacity;
        UpdateOpacityLabel(settings.Window.Opacity);

        SelectByTag(RefreshIntervalComboBox, settings.RefreshIntervalSeconds.ToString());
        SelectByTag(DisplayModeComboBox, settings.DisplayMode.ToString());

        // Notifications
        NotificationEnabledCheckBox.IsChecked = settings.Notifications.Enabled;
        NotifyOnResetCheckBox.IsChecked = settings.Notifications.NotifyOnReset;
        SelectByTag(NotificationThresholdComboBox, settings.Notifications.LowQuotaThresholdPercent.ToString());
        QuietHoursStartTextBox.Text = settings.Notifications.QuietHoursStart ?? "22:00";
        QuietHoursEndTextBox.Text = settings.Notifications.QuietHoursEnd ?? "08:00";

        // Provider Executables & Arguments
        CodexPathTextBox.Text = GetProviderPath("codex", "codex.cmd");
        GrokPathTextBox.Text = GetProviderPath("grok", "grok.cmd");
        AntigravityPathTextBox.Text = GetProviderPath("antigravity", "agy.cmd");
        CodexCommandArgumentsTextBox.Text = FindProvider("codex")?.CommandArguments ?? string.Empty;
        GrokCommandArgumentsTextBox.Text = FindProvider("grok")?.CommandArguments ?? string.Empty;
        AntigravityCommandArgumentsTextBox.Text = FindProvider("antigravity")?.CommandArguments ?? string.Empty;
    }

    private void ThemeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeListBox.SelectedItem is ThemeDefinition theme)
        {
            _selectedThemeKey = theme.Key;
            ThemeManager.ApplyTheme(theme.Key);
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateOpacityLabel(e.NewValue);
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.Opacity = Math.Clamp(e.NewValue, 0.1, 1.0);
        }
    }

    private void PresetOpacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            double.TryParse(button.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
        {
            OpacitySlider.Value = val;
        }
    }

    private void UpdateOpacityLabel(double opacity)
    {
        if (OpacityValueText != null)
        {
            var percent = (int)Math.Round(opacity * 100);
            OpacityValueText.Text = percent switch
            {
                85 => "85% (推荐)",
                100 => "100% (实色)",
                60 => "60% (极透)",
                _ => $"{percent}%"
            };
        }
    }

    private void AutoDetectCli_Click(object sender, RoutedEventArgs e)
    {
        var resolver = new ExecutableResolver();
        var foundCount = 0;

        var codex = resolver.Resolve("codex");
        if (!string.IsNullOrWhiteSpace(codex))
        {
            CodexPathTextBox.Text = codex;
            foundCount++;
        }

        var grok = resolver.Resolve("grok");
        if (!string.IsNullOrWhiteSpace(grok))
        {
            GrokPathTextBox.Text = grok;
            foundCount++;
        }

        var agy = resolver.Resolve("agy");
        if (!string.IsNullOrWhiteSpace(agy))
        {
            AntigravityPathTextBox.Text = agy;
            foundCount++;
        }

        AutoDetectStatusText.Text = foundCount > 0
            ? $"✓ 扫描完成，已自动填入 {foundCount} 个 CLI 路径！"
            : "ℹ 未能在常用目录发现 CLI，可手动指定或留空使用 PATH。";
        AutoDetectStatusText.Visibility = Visibility.Visible;
    }

    private void BrowseCodex_Click(object sender, RoutedEventArgs e)
    {
        BrowseExecutable(CodexPathTextBox, "选择 Codex CLI 可执行文件");
    }

    private void BrowseGrok_Click(object sender, RoutedEventArgs e)
    {
        BrowseExecutable(GrokPathTextBox, "选择 Grok CLI 可执行文件");
    }

    private void BrowseAntigravity_Click(object sender, RoutedEventArgs e)
    {
        BrowseExecutable(AntigravityPathTextBox, "选择 Antigravity (agy) CLI 可执行文件");
    }

    private static void BrowseExecutable(TextBox targetTextBox, string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "可执行文件 (*.cmd;*.exe;*.bat;*.ps1)|*.cmd;*.exe;*.bat;*.ps1|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            targetTextBox.Text = dialog.FileName;
        }
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        AutoRefreshCheckBox.IsChecked = true;
        WorkTimeEnabledCheckBox.IsChecked = false;
        WorkTimeStartTextBox.Text = "09:00";
        WorkTimeEndTextBox.Text = "18:00";
        SetWorkDayCheckboxes(
        [
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday
        ]);
        StartWithWindowsCheckBox.IsChecked = false;
        StartMinimizedCheckBox.IsChecked = false;
        LockedCheckBox.IsChecked = false;
        TopmostCheckBox.IsChecked = true;
        ClickThroughCheckBox.IsChecked = false;
        OpacitySlider.Value = 0.85;
        SelectByTag(RefreshIntervalComboBox, "300");
        SelectByTag(DisplayModeComboBox, "Compact");

        ThemeListBox.SelectedItem = ThemeManager.GetTheme("CyberBlue");

        NotificationEnabledCheckBox.IsChecked = true;
        NotifyOnResetCheckBox.IsChecked = true;
        SelectByTag(NotificationThresholdComboBox, "20");
        QuietHoursStartTextBox.Text = "22:00";
        QuietHoursEndTextBox.Text = "08:00";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        RevertLiveChanges();
        DialogResult = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DialogResult != true)
        {
            RevertLiveChanges();
        }

        base.OnClosed(e);
    }

    private void RevertLiveChanges()
    {
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.Opacity = _initialOpacity;
        }

        ThemeManager.ApplyTheme(_initialTheme);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var workTime = ReadWorkTimeSettings();
        if (!WorkTimePolicy.TryValidate(workTime, out var workTimeError))
        {
            MessageBox.Show(
                this,
                workTimeError,
                "工作时间设置无效",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _settings.Theme = _selectedThemeKey;
        _settings.AutoRefreshEnabled = AutoRefreshCheckBox.IsChecked == true;
        _settings.WorkTime = workTime;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _settings.Window.StartMinimized = StartMinimizedCheckBox.IsChecked == true;
        _settings.Window.Locked = LockedCheckBox.IsChecked == true;
        _settings.Window.Topmost = TopmostCheckBox.IsChecked == true;
        _settings.Window.ClickThrough = ClickThroughCheckBox.IsChecked == true;
        _settings.Window.Opacity = OpacitySlider.Value;

        if (RefreshIntervalComboBox.SelectedItem is ComboBoxItem refreshItem &&
            int.TryParse(refreshItem.Tag?.ToString(), out var refreshInterval))
        {
            _settings.RefreshIntervalSeconds = refreshInterval;
        }

        if (DisplayModeComboBox.SelectedItem is ComboBoxItem displayItem &&
            Enum.TryParse<DisplayMode>(displayItem.Tag?.ToString(), out var displayMode))
        {
            _settings.DisplayMode = displayMode;
        }

        // Save Notifications
        _settings.Notifications.Enabled = NotificationEnabledCheckBox.IsChecked == true;
        _settings.Notifications.NotifyOnReset = NotifyOnResetCheckBox.IsChecked == true;
        if (NotificationThresholdComboBox.SelectedItem is ComboBoxItem thresholdItem &&
            int.TryParse(thresholdItem.Tag?.ToString(), out var threshold))
        {
            _settings.Notifications.LowQuotaThresholdPercent = threshold;
        }

        _settings.Notifications.QuietHoursStart = EmptyToNull(QuietHoursStartTextBox.Text);
        _settings.Notifications.QuietHoursEnd = EmptyToNull(QuietHoursEndTextBox.Text);

        // Save Providers
        SaveProvider("codex", CodexPathTextBox.Text, CodexCommandArgumentsTextBox.Text);
        SaveProvider("grok", GrokPathTextBox.Text, GrokCommandArgumentsTextBox.Text);
        SaveProvider("antigravity", AntigravityPathTextBox.Text, AntigravityCommandArgumentsTextBox.Text);

        DialogResult = true;
    }

    private void SaveProvider(string id, string? executablePath, string? commandArguments)
    {
        var provider = FindProvider(id)!;
        provider.ExecutablePath = EmptyToNull(executablePath);
        provider.CommandArguments = EmptyToNull(commandArguments);
        provider.QueryMethod = provider.CommandArguments is null ? QueryMethod.Auto : QueryMethod.Command;
    }

    private string GetProviderPath(string id, string npmCommandFileName)
    {
        var configuredPath = FindProvider(id)?.ExecutablePath;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var npmDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm");
        return Path.Combine(npmDirectory, npmCommandFileName);
    }

    private ProviderSettings? FindProvider(string id)
    {
        var provider = _settings.Providers.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (provider is not null)
        {
            return provider;
        }

        provider = new ProviderSettings { Id = id, SortOrder = _settings.Providers.Count };
        _settings.Providers.Add(provider);
        return provider;
    }

    private static void SelectByTag(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private WorkTimeSettings ReadWorkTimeSettings()
    {
        var days = new List<DayOfWeek>();
        AddSelectedDay(WorkDayMondayCheckBox, DayOfWeek.Monday, days);
        AddSelectedDay(WorkDayTuesdayCheckBox, DayOfWeek.Tuesday, days);
        AddSelectedDay(WorkDayWednesdayCheckBox, DayOfWeek.Wednesday, days);
        AddSelectedDay(WorkDayThursdayCheckBox, DayOfWeek.Thursday, days);
        AddSelectedDay(WorkDayFridayCheckBox, DayOfWeek.Friday, days);
        AddSelectedDay(WorkDaySaturdayCheckBox, DayOfWeek.Saturday, days);
        AddSelectedDay(WorkDaySundayCheckBox, DayOfWeek.Sunday, days);

        return new WorkTimeSettings
        {
            Enabled = WorkTimeEnabledCheckBox.IsChecked == true,
            Start = WorkTimeStartTextBox.Text.Trim(),
            End = WorkTimeEndTextBox.Text.Trim(),
            Days = days
        };
    }

    private void SetWorkDayCheckboxes(IEnumerable<DayOfWeek>? days)
    {
        var selected = (days ?? []).ToHashSet();
        WorkDayMondayCheckBox.IsChecked = selected.Contains(DayOfWeek.Monday);
        WorkDayTuesdayCheckBox.IsChecked = selected.Contains(DayOfWeek.Tuesday);
        WorkDayWednesdayCheckBox.IsChecked = selected.Contains(DayOfWeek.Wednesday);
        WorkDayThursdayCheckBox.IsChecked = selected.Contains(DayOfWeek.Thursday);
        WorkDayFridayCheckBox.IsChecked = selected.Contains(DayOfWeek.Friday);
        WorkDaySaturdayCheckBox.IsChecked = selected.Contains(DayOfWeek.Saturday);
        WorkDaySundayCheckBox.IsChecked = selected.Contains(DayOfWeek.Sunday);
    }

    private static void AddSelectedDay(
        CheckBox checkBox,
        DayOfWeek day,
        ICollection<DayOfWeek> selectedDays)
    {
        if (checkBox.IsChecked == true)
        {
            selectedDays.Add(day);
        }
    }
}
