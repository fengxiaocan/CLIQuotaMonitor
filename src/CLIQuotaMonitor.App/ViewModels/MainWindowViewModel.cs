using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Services;
using CLIQuotaMonitor.Core.Time;

namespace CLIQuotaMonitor.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly QuotaManager _quotaManager;
    private readonly IClock _clock;
    private AppSettings _settings;
    private bool _isRefreshing;
    private string _lastRefreshText = "尚未刷新";
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _refreshSync = new();
    private Task? _activeRefreshTask;
    private bool _isStopping;

    public MainWindowViewModel(
        QuotaManager quotaManager,
        AppSettings settings,
        IClock clock)
    {
        _quotaManager = quotaManager;
        _settings = settings;
        _clock = clock;
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(), () => !IsRefreshing);

        foreach (var provider in settings.Providers.OrderBy(provider => provider.SortOrder))
        {
            ProviderCards.Add(new ProviderCardViewModel(provider.Id, GetDisplayName(provider.Id)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<IReadOnlyList<QuotaSnapshot>>? RefreshCompleted;

    public ObservableCollection<ProviderCardViewModel> ProviderCards { get; } = [];

    public ICommand RefreshCommand { get; }

    public DisplayMode DisplayMode
    {
        get => _settings.DisplayMode;
        set
        {
            if (_settings.DisplayMode == value)
            {
                return;
            }

            _settings.DisplayMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayModeText));
        }
    }

    public string DisplayModeText => DisplayMode switch
    {
        DisplayMode.Detailed => "详细模式",
        DisplayMode.Compact => "紧凑模式",
        DisplayMode.Minimal => "极简模式",
        _ => "详细模式"
    };

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (_isRefreshing == value)
            {
                return;
            }

            _isRefreshing = value;
            OnPropertyChanged();
            if (RefreshCommand is AsyncRelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public string LastRefreshText
    {
        get => _lastRefreshText;
        private set
        {
            if (_lastRefreshText == value)
            {
                return;
            }

            _lastRefreshText = value;
            OnPropertyChanged();
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_refreshSync)
        {
            if (IsRefreshing || _isStopping)
            {
                return Task.CompletedTask;
            }

            var refreshTask = RefreshCoreAsync(cancellationToken);
            _activeRefreshTask = refreshTask;
            return refreshTask;
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        IsRefreshing = true;
        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token,
                cancellationToken);
            var snapshots = await _quotaManager
                .RefreshAllAsync(_settings, linkedCancellation.Token)
                .ConfigureAwait(true);
            foreach (var snapshot in snapshots)
            {
                var card = ProviderCards.FirstOrDefault(item =>
                    string.Equals(item.ProviderId, snapshot.ProviderId, StringComparison.OrdinalIgnoreCase));
                var providerSettings = _settings.Providers.FirstOrDefault(item =>
                    string.Equals(item.Id, snapshot.ProviderId, StringComparison.OrdinalIgnoreCase));
                card?.Update(snapshot, _clock.Now, providerSettings?.VisibleQuotaNames);
            }

            LastRefreshText = $"更新于 {_clock.Now:HH:mm:ss}";
            RefreshCompleted?.Invoke(snapshots);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public async Task StopAsync()
    {
        Task? activeRefresh;
        lock (_refreshSync)
        {
            if (!_isStopping)
            {
                _isStopping = true;
                _lifetimeCancellation.Cancel();
            }

            activeRefresh = _activeRefreshTask;
        }

        if (activeRefresh is null)
        {
            return;
        }

        try
        {
            await activeRefresh.ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Cancellation is the expected completion path during application exit.
        }
    }

    public void Tick()
    {
        foreach (var card in ProviderCards)
        {
            card.Tick(_clock.Now);
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        foreach (var card in ProviderCards)
        {
            var provider = settings.Providers.FirstOrDefault(item =>
                string.Equals(item.Id, card.ProviderId, StringComparison.OrdinalIgnoreCase));
            card.IsVisible = provider?.Enabled ?? true;
            card.ApplyVisibleQuotaNames(provider?.VisibleQuotaNames, _clock.Now);
        }

        OnPropertyChanged(nameof(DisplayMode));
    }

    private static string GetDisplayName(string id)
    {
        return id.ToLowerInvariant() switch
        {
            "codex" => "Codex",
            "grok" => "Grok",
            "antigravity" => "Antigravity",
            _ => id
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ProviderCardViewModel : INotifyPropertyChanged
{
    private QuotaSnapshot _snapshot = new()
    {
        ProviderId = "unknown",
        ProviderName = "Unknown",
        Status = ProviderStatus.Unknown
    };
    private bool _isVisible = true;
    private DateTimeOffset _lastUpdated = DateTimeOffset.Now;
    private IReadOnlyCollection<string>? _visibleQuotaNames;

    public ProviderCardViewModel(string providerId, string displayName)
    {
        ProviderId = providerId;
        DisplayName = displayName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProviderId { get; }

    public string DisplayName { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
            {
                return;
            }

            _isVisible = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<QuotaRowViewModel> Quotas { get; } = [];

    public IReadOnlyList<string> AvailableQuotaNames => _snapshot.Quotas
        .Select(quota => quota.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public string? Account => _snapshot.Account;

    public string? SessionUsage => _snapshot.SessionUsage;

    public ProviderStatus Status => _snapshot.Status;

    public bool IsStale => _snapshot.IsStale;

    public string StatusText => _snapshot.Status switch
    {
        ProviderStatus.Ok => _snapshot.IsStale ? "缓存 · 刷新失败" : "正常",
        ProviderStatus.Refreshing => "刷新中",
        ProviderStatus.NotInstalled => "未安装",
        ProviderStatus.NotLoggedIn => "未登录",
        ProviderStatus.Timeout => "查询超时",
        ProviderStatus.ParseError => "无法解析",
        ProviderStatus.UnsupportedVersion => "版本不兼容",
        ProviderStatus.Offline => "离线",
        ProviderStatus.Error => "查询失败",
        _ => "未查询"
    };

    public string? ErrorMessage => _snapshot.ErrorMessage;

    public string BrandColor => ProviderId.ToLowerInvariant() switch
    {
        "codex" => "#10A37F",
        "grok" => "#38BDF8",
        "antigravity" => "#818CF8",
        _ => "#64748B"
    };

    public string BrandBadgeText => ProviderId.ToLowerInvariant() switch
    {
        "codex" => "CODEX",
        "grok" => "GROK",
        "antigravity" => "AGY",
        _ => DisplayName.ToUpperInvariant()
    };

    public string StatusColor => Status switch
    {
        ProviderStatus.Ok => IsStale ? "#F59E0B" : "#10B981",
        ProviderStatus.Refreshing => "#38BDF8",
        ProviderStatus.NotLoggedIn or ProviderStatus.NotInstalled or ProviderStatus.UnsupportedVersion => "#F59E0B",
        ProviderStatus.Timeout or ProviderStatus.ParseError or ProviderStatus.Error or ProviderStatus.Offline => "#EF4444",
        _ => "#94A3B8"
    };

    public string StatusBgColor => Status switch
    {
        ProviderStatus.Ok => IsStale ? "#24F59E0B" : "#2410B981",
        ProviderStatus.Refreshing => "#2438BDF8",
        ProviderStatus.NotLoggedIn or ProviderStatus.NotInstalled or ProviderStatus.UnsupportedVersion => "#24F59E0B",
        ProviderStatus.Timeout or ProviderStatus.ParseError or ProviderStatus.Error or ProviderStatus.Offline => "#24EF4444",
        _ => "#2494A3B8"
    };

    public bool HasAccount => !string.IsNullOrWhiteSpace(Account);

    public bool HasError => IsStale || (!string.IsNullOrWhiteSpace(ErrorMessage) && Status != ProviderStatus.Ok);

    private bool HasQuotaDisplayError =>
        string.Equals(ProviderId, "grok", StringComparison.OrdinalIgnoreCase) &&
        ((Status != ProviderStatus.Ok &&
          Status != ProviderStatus.Unknown &&
          Status != ProviderStatus.Refreshing) ||
         (Status == ProviderStatus.Ok &&
          !Quotas.Any(quota => quota.RemainingPercent is not null)));

    public string CompactText => HasQuotaDisplayError
        ? "ERROR"
        : Quotas.Count == 0
            ? StatusText
            : string.Join(
                "  │  ",
                Quotas
                    .OrderBy(quota => GetQuotaSortOrder(quota.Name))
                    .Select(quota => $"{quota.Name} {quota.RemainingText}"));

    public string MinimalPrefix => ProviderId.ToLowerInvariant() switch
    {
        "antigravity" => "agy",
        _ => ProviderId.ToLowerInvariant()
    };

    private static string FormatMinimalQuotaName(string providerId, string name)
    {
        if (name.Equals("Five Hour", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("FiveHour", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("5-Hour", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("5 Hour", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("5h", StringComparison.OrdinalIgnoreCase))
        {
            return "5h";
        }

        if (name.Equals("Weekly", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Weekly Limit", StringComparison.OrdinalIgnoreCase))
        {
            return "Weekly";
        }

        if (string.Equals(providerId, "grok", StringComparison.OrdinalIgnoreCase) &&
            name.Equals("Limit", StringComparison.OrdinalIgnoreCase))
        {
            return "Weekly";
        }

        return name;
    }

    private static int GetQuotaSortOrder(string name)
    {
        if (name.Contains("5h", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Five", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (name.Contains("Week", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    public string MinimalQuotasText => HasQuotaDisplayError
        ? "ERROR"
        : Quotas.Count == 0
            ? StatusText
            : string.Join(" | ", Quotas
                .OrderBy(q => GetQuotaSortOrder(q.Name))
                .Select(quota => $"{FormatMinimalQuotaName(ProviderId, quota.Name)}:{quota.RemainingText}"));

    public string MinimalText => $"{MinimalPrefix} {MinimalQuotasText}";

    public void Update(
        QuotaSnapshot snapshot,
        DateTimeOffset now,
        IReadOnlyCollection<string>? visibleQuotaNames = null)
    {
        _snapshot = snapshot;
        _lastUpdated = now;
        _visibleQuotaNames = visibleQuotaNames;
        RebuildQuotas();
        RaiseAll();
    }

    public void ApplyVisibleQuotaNames(
        IReadOnlyCollection<string>? visibleQuotaNames,
        DateTimeOffset? now = null)
    {
        _visibleQuotaNames = visibleQuotaNames;
        if (now is DateTimeOffset updated)
        {
            _lastUpdated = updated;
        }

        RebuildQuotas();
        RaiseAll();
    }

    private void RebuildQuotas()
    {
        Quotas.Clear();
        var visible = _visibleQuotaNames is { Count: > 0 }
            ? _visibleQuotaNames.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        foreach (var quota in _snapshot.Quotas
                     .Where(item => visible is null || visible.Contains(item.Name))
                     .OrderBy(item => GetQuotaSortOrder(item.Name)))
        {
            Quotas.Add(new QuotaRowViewModel(quota, _lastUpdated));
        }
    }

    public void Tick(DateTimeOffset now)
    {
        foreach (var quota in Quotas)
        {
            quota.Tick(now);
        }
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Account));
        OnPropertyChanged(nameof(SessionUsage));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(StatusBgColor));
        OnPropertyChanged(nameof(BrandColor));
        OnPropertyChanged(nameof(BrandBadgeText));
        OnPropertyChanged(nameof(HasAccount));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(CompactText));
        OnPropertyChanged(nameof(MinimalPrefix));
        OnPropertyChanged(nameof(MinimalQuotasText));
        OnPropertyChanged(nameof(MinimalText));
        OnPropertyChanged(nameof(AvailableQuotaNames));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class QuotaRowViewModel : INotifyPropertyChanged
{
    private readonly QuotaItem _quota;
    private string _resetText;

    public QuotaRowViewModel(QuotaItem quota, DateTimeOffset now)
    {
        _quota = quota;
        _resetText = FormatReset(quota.GetResetAfter(now), quota.ResetAt);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => _quota.Name;

    public double? RemainingPercent => _quota.RemainingPercent;

    public string RemainingText => _quota.RemainingPercent is not null
        ? $"{_quota.RemainingPercent:0}%"
        : _quota.RemainingValue is not null
            ? $"{_quota.RemainingValue:0.##}{_quota.Unit ?? string.Empty}"
            : "—";

    public string ResetText => _resetText;

    public bool HasResetText => !string.IsNullOrWhiteSpace(_resetText);

    public string AccentColor => _quota.RemainingPercent switch
    {
        null => "#94A3B8",
        <= 20 => "#EF4444",
        < 50 => "#F59E0B",
        _ => "#10B981"
    };

    public void Tick(DateTimeOffset now)
    {
        var next = FormatReset(_quota.GetResetAfter(now), _quota.ResetAt);
        if (_resetText == next)
        {
            return;
        }

        _resetText = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResetText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasResetText)));
    }

    private static string FormatReset(TimeSpan? remaining, DateTimeOffset? resetAt)
    {
        if (remaining is null)
        {
            return string.Empty;
        }

        var value = remaining.Value;
        var relative = value.TotalDays >= 1
            ? $"{(int)value.TotalDays}d {value.Hours}h"
            : value.TotalHours >= 1
                ? $"{(int)value.TotalHours}h {value.Minutes}m"
                : $"{value.Minutes}m {value.Seconds}s";
        return resetAt is null ? relative : $"{resetAt:HH:mm} · {relative}";
    }
}
