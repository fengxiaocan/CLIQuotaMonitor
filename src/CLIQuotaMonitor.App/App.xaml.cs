using System.Windows;
using System.Diagnostics;
using System.IO;
using CLIQuotaMonitor.App.ViewModels;
using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Providers;
using CLIQuotaMonitor.Core.Services;
using CLIQuotaMonitor.Core.Time;
using CLIQuotaMonitor.Infrastructure;
using CLIQuotaMonitor.Providers;
using Forms = System.Windows.Forms;

namespace CLIQuotaMonitor.App;

public partial class App : System.Windows.Application
{
    private readonly HashSet<string> _notifiedLowQuotas = new(StringComparer.OrdinalIgnoreCase);
    private JsonSettingsStore? _settingsStore;
    private AppSettings? _settings;
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _viewModel;
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private WindowsStartupManager? _startupManager;

    public bool IsExiting { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CLIQuotaMonitor");
            Directory.CreateDirectory(dataDirectory);

            _settingsStore = new JsonSettingsStore(Path.Combine(dataDirectory, "config.json"));
            _settings = await _settingsStore.LoadAsync();
            ThemeManager.ApplyTheme(_settings.Theme);
            var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                _startupManager = new WindowsStartupManager("CLIQuotaMonitor", executablePath, "--startup");
                _startupManager.SetEnabled(_settings.StartWithWindows);
            }
            var cacheStore = new JsonQuotaCacheStore(Path.Combine(dataDirectory, "cache.json"));
            var resolver = new ExecutableResolver();
            var queryRouter = new QueryExecutorRouter(
                new CommandQueryExecutor(),
                new ConPtyQueryExecutor());
            var providers = new IQuotaProvider[]
            {
                new CodexQuotaProvider(queryRouter, resolver),
                new GrokQuotaProvider(queryRouter, resolver),
                new AntigravityQuotaProvider(queryRouter, resolver)
            };
            var clock = new SystemClock();
            var manager = new QuotaManager(providers, cacheStore, clock);
            _viewModel = new MainWindowViewModel(manager, _settings, clock);
            foreach (var card in _viewModel.ProviderCards)
            {
                card.IsVisible = _settings.Providers.FirstOrDefault(provider =>
                    string.Equals(provider.Id, card.ProviderId, StringComparison.OrdinalIgnoreCase))?.Enabled ?? true;
            }

            _viewModel.RefreshCompleted += NotifyLowQuotas;
            _viewModel.RefreshCompleted += _ => RefreshTrayMenu();
            _mainWindow = new MainWindow(_viewModel, _settings, _settingsStore, clock);
            _mainWindow.Closed += MainWindow_Closed;
            CreateTrayIcon();
            _mainWindow.Show();
            if (_settings.Window.StartMinimized || e.Args.Any(argument =>
                    string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase)))
            {
                _mainWindow.Hide();
            }
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                LogRedactor.Redact(exception.Message),
                "CLI Quota Monitor 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    public void ShowSettings()
    {
        if (_mainWindow is null || _settings is null || _settingsStore is null)
        {
            return;
        }

        var dialog = new SettingsWindow(_settings)
        {
            Owner = _mainWindow,
            Icon = AppIconGenerator.GetAppImageSource()
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                _startupManager?.SetEnabled(_settings.StartWithWindows);
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show(
                    LogRedactor.Redact(exception.Message),
                    "无法更新开机启动设置",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            _mainWindow.ApplySettings();
            _ = _settingsStore.SaveAsync(_settings);
            RefreshTrayMenu();
            _ = _mainWindow.RefreshIfAllowedAsync();
        }
    }

    public void ShowOverlay()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    public void HideOverlay() => _mainWindow?.Hide();

    public void RequestExit()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RequestExit));
            return;
        }

        if (IsExiting)
        {
            return;
        }

        IsExiting = true;
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        _mainWindow?.StopBackgroundActivity();
        _ = CompleteExitAsync();
    }

    private async Task CompleteExitAsync()
    {
        try
        {
            if (_viewModel is not null)
            {
                await _viewModel.StopAsync();
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Failed to stop refresh services during exit: {exception}");
        }
        finally
        {
            if (_mainWindow is null)
            {
                Shutdown();
            }
            else
            {
                _mainWindow.CloseForApplicationExit();
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        _trayMenu?.Dispose();
        base.OnExit(e);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (IsExiting)
        {
            Shutdown();
        }
    }

    private void CreateTrayIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = AppIconGenerator.GetAppIcon(),
            Text = "CLI Quota Monitor",
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => _ = _viewModel?.RefreshAsync();
        RefreshTrayMenu();
    }

    public void RefreshTrayMenu()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var previousMenu = _trayMenu;
        _trayMenu = BuildTrayMenu();
        _notifyIcon.ContextMenuStrip = _trayMenu;
        previousMenu?.Dispose();
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(15, 23, 42),
            ForeColor = System.Drawing.Color.FromArgb(226, 232, 240),
            Renderer = new Forms.ToolStripProfessionalRenderer(new DarkTrayMenuColorTable()),
            ShowImageMargin = false,
            ShowCheckMargin = true
        };
        menu.Items.Add("显示悬浮窗", null, (_, _) => ShowOverlay());
        menu.Items.Add("刷新全部", null, (_, _) => _ = _viewModel?.RefreshAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());

        AddProviderMenu(menu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        AddDisplayModeMenu(menu);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var clickThroughItem = new Forms.ToolStripMenuItem("鼠标穿透 (Ctrl+Alt+Q)")
        {
            CheckOnClick = true,
            Checked = _settings?.Window.ClickThrough ?? false
        };
        clickThroughItem.Click += (_, _) =>
        {
            _mainWindow?.SetClickThrough(clickThroughItem.Checked);
            RefreshTrayMenu();
        };
        menu.Items.Add(clickThroughItem);

        var topmostItem = new Forms.ToolStripMenuItem("始终置顶")
        {
            CheckOnClick = true,
            Checked = _mainWindow?.Topmost ?? _settings?.Window.Topmost ?? true
        };
        topmostItem.Click += (_, _) =>
        {
            if (_settings is not null)
            {
                _settings.Window.Topmost = topmostItem.Checked;
                if (_mainWindow is not null)
                {
                    _mainWindow.Topmost = topmostItem.Checked;
                }

                _ = _settingsStore?.SaveAsync(_settings);
            }

            RefreshTrayMenu();
        };
        menu.Items.Add(topmostItem);

        var lockedItem = new Forms.ToolStripMenuItem("锁定位置")
        {
            CheckOnClick = true,
            Checked = _settings?.Window.Locked ?? false
        };
        lockedItem.Click += (_, _) =>
        {
            if (_settings is not null)
            {
                _settings.Window.Locked = lockedItem.Checked;
                _ = _settingsStore?.SaveAsync(_settings);
            }

            RefreshTrayMenu();
        };
        menu.Items.Add(lockedItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("设置", null, (_, _) => ShowSettings());
        menu.Items.Add("隐藏", null, (_, _) => HideOverlay());
        menu.Items.Add("退出", null, (_, _) => RequestExit());
        return menu;
    }

    private void AddProviderMenu(Forms.ContextMenuStrip menu)
    {
        var providersMenu = new Forms.ToolStripMenuItem("显示 Provider");
        foreach (var card in _viewModel?.ProviderCards ?? [])
        {
            var provider = _settings?.Providers.FirstOrDefault(item =>
                string.Equals(item.Id, card.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (provider is null)
            {
                continue;
            }

            var providerItem = new Forms.ToolStripMenuItem(card.DisplayName)
            {
                CheckOnClick = true,
                Checked = provider.Enabled
            };
            providerItem.Click += (_, _) =>
            {
                provider.Enabled = providerItem.Checked;
                card.IsVisible = providerItem.Checked;
                _ = _settingsStore?.SaveAsync(_settings!);
                RefreshTrayMenu();
            };

            if (card.AvailableQuotaNames.Count > 0)
            {
                var quotaMenu = new Forms.ToolStripMenuItem("显示额度");
                foreach (var quotaName in card.AvailableQuotaNames)
                {
                    var quotaItem = new Forms.ToolStripMenuItem(quotaName)
                    {
                        CheckOnClick = true,
                        Checked = provider.VisibleQuotaNames.Count == 0 ||
                                  provider.VisibleQuotaNames.Contains(quotaName, StringComparer.OrdinalIgnoreCase)
                    };
                    quotaItem.Click += (_, _) =>
                    {
                        UpdateVisibleQuotaNames(provider, card, quotaName, quotaItem.Checked);
                        RefreshTrayMenu();
                    };
                    quotaMenu.DropDownItems.Add(quotaItem);
                }

                providerItem.DropDownItems.Add(quotaMenu);
            }

            providersMenu.DropDownItems.Add(providerItem);
        }

        menu.Items.Add(providersMenu);
    }

    private void UpdateVisibleQuotaNames(
        ProviderSettings provider,
        ProviderCardViewModel card,
        string quotaName,
        bool isChecked)
    {
        if (provider.VisibleQuotaNames.Count == 0)
        {
            provider.VisibleQuotaNames.AddRange(card.AvailableQuotaNames);
        }

        if (isChecked)
        {
            if (!provider.VisibleQuotaNames.Contains(quotaName, StringComparer.OrdinalIgnoreCase))
            {
                provider.VisibleQuotaNames.Add(quotaName);
            }
        }
        else
        {
            provider.VisibleQuotaNames.RemoveAll(name =>
                string.Equals(name, quotaName, StringComparison.OrdinalIgnoreCase));
        }

        if (provider.VisibleQuotaNames.Count >= card.AvailableQuotaNames.Count &&
            card.AvailableQuotaNames.All(name => provider.VisibleQuotaNames.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            provider.VisibleQuotaNames.Clear();
        }

        card.ApplyVisibleQuotaNames(provider.VisibleQuotaNames, DateTimeOffset.Now);
        _ = _settingsStore?.SaveAsync(_settings!);
    }

    private void AddDisplayModeMenu(Forms.ContextMenuStrip menu)
    {
        AddDisplayModeItem(menu, "详细模式", DisplayMode.Detailed);
        AddDisplayModeItem(menu, "紧凑模式", DisplayMode.Compact);
        AddDisplayModeItem(menu, "极简模式", DisplayMode.Minimal);
    }

    private void AddDisplayModeItem(
        Forms.ContextMenuStrip menu,
        string header,
        DisplayMode mode)
    {
        var item = new Forms.ToolStripMenuItem(header)
        {
            CheckOnClick = true,
            Checked = _viewModel?.DisplayMode == mode
        };
        item.Click += (_, _) =>
        {
            if (_settings is not null && _viewModel is not null)
            {
                _viewModel.DisplayMode = mode;
                _settings.DisplayMode = mode;
                _mainWindow?.ApplyDisplayMode();
                _ = _settingsStore?.SaveAsync(_settings);
            }

            RefreshTrayMenu();
        };
        menu.Items.Add(item);
    }

    private sealed class DarkTrayMenuColorTable : Forms.ProfessionalColorTable
    {
        private static readonly System.Drawing.Color DarkBackground =
            System.Drawing.Color.FromArgb(15, 23, 42);
        private static readonly System.Drawing.Color HoverBackground =
            System.Drawing.Color.FromArgb(37, 99, 235);
        private static readonly System.Drawing.Color DarkBorder =
            System.Drawing.Color.FromArgb(51, 65, 85);

        public override System.Drawing.Color MenuBorder => DarkBorder;

        public override System.Drawing.Color MenuItemBorder => HoverBackground;

        public override System.Drawing.Color MenuItemSelected => HoverBackground;

        public override System.Drawing.Color MenuItemSelectedGradientBegin => HoverBackground;

        public override System.Drawing.Color MenuItemSelectedGradientEnd => HoverBackground;

        public override System.Drawing.Color MenuItemPressedGradientBegin => HoverBackground;

        public override System.Drawing.Color MenuItemPressedGradientEnd => HoverBackground;

        public override System.Drawing.Color ToolStripDropDownBackground => DarkBackground;

        public override System.Drawing.Color ImageMarginGradientBegin => DarkBackground;

        public override System.Drawing.Color ImageMarginGradientMiddle => DarkBackground;

        public override System.Drawing.Color ImageMarginGradientEnd => DarkBackground;

        public override System.Drawing.Color SeparatorDark => DarkBorder;

        public override System.Drawing.Color SeparatorLight => DarkBorder;
    }

    private void NotifyLowQuotas(IReadOnlyList<QuotaSnapshot> snapshots)
    {
        if (_notifyIcon is null || _settings is null || !_settings.Notifications.Enabled || IsQuietHours())
        {
            return;
        }

        foreach (var snapshot in snapshots)
        {
            foreach (var quota in snapshot.Quotas)
            {
                if (quota.RemainingPercent is null)
                {
                    continue;
                }

                var key = $"{snapshot.ProviderId}:{quota.Name}";
                if (quota.RemainingPercent <= _settings.Notifications.LowQuotaThresholdPercent)
                {
                    if (_notifiedLowQuotas.Add(key))
                    {
                        _notifyIcon.ShowBalloonTip(
                            5000,
                            $"{snapshot.ProviderName} 额度不足",
                            $"{quota.Name} 仅剩 {quota.RemainingPercent:0}%{FormatReset(quota)}",
                            Forms.ToolTipIcon.Warning);
                    }
                }
                else
                {
                    _notifiedLowQuotas.Remove(key);
                }
            }
        }
    }

    private bool IsQuietHours()
    {
        if (_settings?.Notifications.QuietHoursStart is not string startText ||
            _settings.Notifications.QuietHoursEnd is not string endText ||
            !TimeSpan.TryParse(startText, out var start) ||
            !TimeSpan.TryParse(endText, out var end))
        {
            return false;
        }

        var now = DateTime.Now.TimeOfDay;
        return start <= end ? now >= start && now < end : now >= start || now < end;
    }

    private static string FormatReset(QuotaItem quota)
    {
        if (quota.ResetAt is not DateTimeOffset resetAt)
        {
            return string.Empty;
        }

        return $"，将在 {resetAt:HH:mm} 恢复";
    }
}
