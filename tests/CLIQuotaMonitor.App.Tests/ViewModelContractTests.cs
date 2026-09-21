using System.Reflection;
using CLIQuotaMonitor.App.ViewModels;
using CLIQuotaMonitor.Core.Caching;
using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Providers;
using CLIQuotaMonitor.Core.Services;
using CLIQuotaMonitor.Core.Time;

namespace CLIQuotaMonitor.App.Tests;

public sealed class ViewModelContractTests
{
    [Fact]
    public void App_exposes_an_overlay_view_model_with_refresh_and_cards()
    {
        var viewModelType = Assembly.Load("CLIQuotaMonitor.App")
            .GetType("CLIQuotaMonitor.App.ViewModels.MainWindowViewModel");

        Assert.NotNull(viewModelType);
        Assert.NotNull(viewModelType!.GetProperty("ProviderCards"));
        Assert.NotNull(viewModelType.GetMethod("RefreshAsync"));
    }

    [Fact]
    public async Task View_model_stop_cancels_an_in_flight_refresh()
    {
        var provider = new BlockingProvider();
        var manager = new QuotaManager(
            [provider],
            new EmptyCacheStore(),
            new FixedClock(DateTimeOffset.UtcNow));
        var settings = new AppSettings
        {
            AutoRefreshEnabled = false,
            Providers = [new ProviderSettings { Id = "blocking" }]
        };
        var viewModel = new MainWindowViewModel(manager, settings, new FixedClock(DateTimeOffset.UtcNow));
        var stopMethod = viewModel.GetType().GetMethod("StopAsync");

        Assert.NotNull(stopMethod);

        var refreshTask = viewModel.RefreshAsync();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopTask = (Task)stopMethod!.Invoke(viewModel, null)!;
        await stopTask;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await refreshTask);
        Assert.True(provider.CancellationObserved);
        Assert.False(viewModel.IsRefreshing);
    }

    [Fact]
    public void Provider_card_filters_quota_rows_when_visible_names_are_configured()
    {
        var cardType = Assembly.Load("CLIQuotaMonitor.App")
            .GetType("CLIQuotaMonitor.App.ViewModels.ProviderCardViewModel");
        Assert.NotNull(cardType);

        var card = Activator.CreateInstance(cardType!, "codex", "Codex");
        var update = cardType!.GetMethod("Update");
        Assert.NotNull(update);
        var snapshot = new QuotaSnapshot
        {
            ProviderId = "codex",
            ProviderName = "Codex",
            Status = ProviderStatus.Ok,
            Quotas =
            [
                new QuotaItem("5H", 72, null),
                new QuotaItem("Weekly", 43, null)
            ]
        };

        update!.Invoke(card, [snapshot, DateTimeOffset.UtcNow, new[] { "5H" }]);

        var quotas = (System.Collections.IEnumerable)cardType.GetProperty("Quotas")!.GetValue(card)!;
        Assert.Single(quotas.Cast<object>());
        Assert.Equal("5H", quotas.Cast<object>().Single().GetType().GetProperty("Name")!.GetValue(quotas.Cast<object>().Single()));
    }

    [Fact]
    public void App_exposes_a_dark_tray_menu_color_table()
    {
        var appType = Assembly.Load("CLIQuotaMonitor.App")
            .GetType("CLIQuotaMonitor.App.App");
        Assert.NotNull(appType);

        var colorTableType = appType!.GetNestedType(
            "DarkTrayMenuColorTable",
            BindingFlags.NonPublic);

        Assert.NotNull(colorTableType);
        Assert.Equal(
            "System.Windows.Forms.ProfessionalColorTable",
            colorTableType!.BaseType?.FullName);
    }

    [Fact]
    public void Main_window_exposes_an_explicit_application_exit_path()
    {
        var windowType = Assembly.Load("CLIQuotaMonitor.App")
            .GetType("CLIQuotaMonitor.App.MainWindow");

        Assert.NotNull(windowType);
        Assert.NotNull(windowType!.GetMethod("CloseForApplicationExit"));
    }

    [Fact]
    public void Main_window_binds_read_only_quota_percentage_one_way()
    {
        var xamlPath = FindSourceFile("src", "CLIQuotaMonitor.App", "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains(
            "Value=\"{Binding RemainingPercent, Mode=OneWay}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Floating_window_only_keeps_display_and_drag_interactions()
    {
        var xamlPath = FindSourceFile("src", "CLIQuotaMonitor.App", "MainWindow.xaml");
        var codePath = FindSourceFile("src", "CLIQuotaMonitor.App", "MainWindow.xaml.cs");
        var xaml = File.ReadAllText(xamlPath);
        var code = File.ReadAllText(codePath);

        Assert.DoesNotContain("Header Toolbar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HeaderIconButtonStyle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MouseDoubleClick=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Footer Hint", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Window_MouseDoubleClick", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ContextMenu = menu", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Tray_double_click_refreshes_instead_of_only_showing_the_overlay()
    {
        var appPath = FindSourceFile("src", "CLIQuotaMonitor.App", "App.xaml.cs");
        var appCode = File.ReadAllText(appPath);

        Assert.Contains(
            "_notifyIcon.DoubleClick += (_, _) => _ = _viewModel?.RefreshAsync();",
            appCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_notifyIcon.DoubleClick += (_, _) => ShowOverlay();",
            appCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Tray_menu_contains_the_overlay_operations()
    {
        var appPath = FindSourceFile("src", "CLIQuotaMonitor.App", "App.xaml.cs");
        var appCode = File.ReadAllText(appPath);

        foreach (var menuLabel in new[]
                 {
                     "显示 Provider",
                     "显示额度",
                     "详细模式",
                     "紧凑模式",
                     "极简模式",
                     "始终置顶",
                     "锁定位置",
                     "设置",
                     "隐藏",
                     "退出"
                 })
        {
            Assert.Contains(menuLabel, appCode, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Provider_card_formats_minimal_text_correctly()
    {
        var cardType = Assembly.Load("CLIQuotaMonitor.App")
            .GetType("CLIQuotaMonitor.App.ViewModels.ProviderCardViewModel");
        Assert.NotNull(cardType);

        // Codex test: codex 5h:80% | Weekly:40%
        var codexCard = Activator.CreateInstance(cardType!, "codex", "Codex");
        var update = cardType!.GetMethod("Update");
        var codexSnapshot = new QuotaSnapshot
        {
            ProviderId = "codex",
            ProviderName = "Codex",
            Status = ProviderStatus.Ok,
            Quotas =
            [
                new QuotaItem("5h", 80, null),
                new QuotaItem("Weekly", 40, null)
            ]
        };
        update!.Invoke(codexCard, [codexSnapshot, DateTimeOffset.UtcNow, null]);
        var codexText = cardType.GetProperty("MinimalText")!.GetValue(codexCard);
        Assert.Equal("codex 5h:80% | Weekly:40%", codexText);

        // Grok test: grok Weekly:50%
        var grokCard = Activator.CreateInstance(cardType!, "grok", "Grok");
        var grokSnapshot = new QuotaSnapshot
        {
            ProviderId = "grok",
            ProviderName = "Grok",
            Status = ProviderStatus.Ok,
            Quotas = [new QuotaItem("Weekly", 50, null)]
        };
        update!.Invoke(grokCard, [grokSnapshot, DateTimeOffset.UtcNow, null]);
        var grokText = cardType.GetProperty("MinimalText")!.GetValue(grokCard);
        Assert.Equal("grok Weekly:50%", grokText);

        // Antigravity test: agy 5h:20% | weekly:20% (sorting 5h before weekly)
        var agyCard = Activator.CreateInstance(cardType!, "antigravity", "Antigravity");
        var agySnapshot = new QuotaSnapshot
        {
            ProviderId = "antigravity",
            ProviderName = "Antigravity",
            Status = ProviderStatus.Ok,
            Quotas =
            [
                new QuotaItem("Weekly", 20, null),
                new QuotaItem("Five Hour", 20, null)
            ]
        };
        update!.Invoke(agyCard, [agySnapshot, DateTimeOffset.UtcNow, null]);
        var agyText = cardType.GetProperty("MinimalText")!.GetValue(agyCard);
        Assert.Equal("agy 5h:20% | Weekly:20%", agyText);
    }

    [Fact]
    public void Provider_card_shows_ERROR_when_refresh_did_not_produce_a_percentage()
    {
        var cardType = Assembly.Load("CLIQuotaMonitor.App")
            .GetType("CLIQuotaMonitor.App.ViewModels.ProviderCardViewModel");
        Assert.NotNull(cardType);

        var card = Activator.CreateInstance(cardType!, "grok", "Grok");
        var update = cardType!.GetMethod("Update");
        Assert.NotNull(update);
        var snapshot = new QuotaSnapshot
        {
            ProviderId = "grok",
            ProviderName = "Grok",
            Status = ProviderStatus.ParseError,
            Quotas = [new QuotaItem("Desktop", null, null, 90, "tokens")]
        };

        update!.Invoke(card, [snapshot, DateTimeOffset.UtcNow, null]);

        Assert.Equal("ERROR", cardType.GetProperty("CompactText")!.GetValue(card));
        Assert.Equal("ERROR", cardType.GetProperty("MinimalQuotasText")!.GetValue(card));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("antigravity")]
    public void Provider_card_keeps_cached_percentages_for_non_grok_refresh_errors(string providerId)
    {
        var cardType = Assembly.Load("CLIQuotaMonitor.App")
            .GetType("CLIQuotaMonitor.App.ViewModels.ProviderCardViewModel");
        Assert.NotNull(cardType);

        var card = Activator.CreateInstance(cardType!, providerId, providerId);
        var update = cardType!.GetMethod("Update");
        Assert.NotNull(update);
        var snapshot = new QuotaSnapshot
        {
            ProviderId = providerId,
            ProviderName = providerId,
            Status = ProviderStatus.Timeout,
            IsStale = true,
            Quotas =
            [
                new QuotaItem("5h", 72, null),
                new QuotaItem("Weekly", 43, null)
            ]
        };

        update!.Invoke(card, [snapshot, DateTimeOffset.UtcNow, null]);

        Assert.DoesNotContain("ERROR", (string)cardType.GetProperty("CompactText")!.GetValue(card)!);
        Assert.DoesNotContain("ERROR", (string)cardType.GetProperty("MinimalQuotasText")!.GetValue(card)!);
        Assert.Contains("72%", (string)cardType.GetProperty("CompactText")!.GetValue(card)!);
    }

    [Fact]
    public void Main_window_bounds_minimal_content_to_the_normal_window_width()
    {
        var codePath = FindSourceFile("src", "CLIQuotaMonitor.App", "MainWindow.xaml.cs");
        var code = File.ReadAllText(codePath);

        Assert.Contains("MaxWidth", code, StringComparison.Ordinal);
        Assert.Contains("390", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Antigravity_card_orders_five_hour_before_weekly_in_all_layouts()
    {
        var cardType = Assembly.Load("CLIQuotaMonitor.App")
            .GetType("CLIQuotaMonitor.App.ViewModels.ProviderCardViewModel");
        Assert.NotNull(cardType);

        var card = Activator.CreateInstance(cardType!, "antigravity", "Antigravity");
        var update = cardType!.GetMethod("Update");
        Assert.NotNull(update);
        var snapshot = new QuotaSnapshot
        {
            ProviderId = "antigravity",
            ProviderName = "Antigravity",
            Status = ProviderStatus.Ok,
            Quotas =
            [
                new QuotaItem("Weekly", 20, null),
                new QuotaItem("5h", 20, null)
            ]
        };

        update!.Invoke(card, [snapshot, DateTimeOffset.UtcNow, null]);

        Assert.Equal(
            "5h 20%  │  Weekly 20%",
            cardType.GetProperty("CompactText")!.GetValue(card));
        var rows = ((System.Collections.IEnumerable)cardType.GetProperty("Quotas")!.GetValue(card)!)
            .Cast<object>()
            .ToList();
        Assert.Equal("5h", rows[0].GetType().GetProperty("Name")!.GetValue(rows[0]));
        Assert.Equal("Weekly", rows[1].GetType().GetProperty("Name")!.GetValue(rows[1]));
    }

    private static string FindSourceFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the source file.", Path.Combine(segments));
    }

    private sealed class BlockingProvider : IQuotaProvider
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved { get; private set; }

        public string Id => "blocking";

        public string DisplayName => "Blocking";

        public async Task<QuotaSnapshot> GetQuotaAsync(
            ProviderSettings settings,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }

            throw new InvalidOperationException("The blocking provider should only finish by cancellation.");
        }
    }

    private sealed class EmptyCacheStore : IQuotaCacheStore
    {
        public Task<QuotaSnapshot?> GetAsync(
            string providerId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<QuotaSnapshot?>(null);
        }

        public Task SetAsync(
            QuotaSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; }
    }
}
