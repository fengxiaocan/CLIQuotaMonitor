using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CLIQuotaMonitor.App.ViewModels;
using CLIQuotaMonitor.Core.Models;
using CLIQuotaMonitor.Core.Time;
using CLIQuotaMonitor.Infrastructure;
using Application = System.Windows.Application;

namespace CLIQuotaMonitor.App;

public partial class MainWindow : Window
{
    private const double NormalWindowWidth = 390;

    private readonly AppSettings _settings;
    private readonly JsonSettingsStore _settingsStore;
    private readonly IClock _clock;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _refreshTimer;
    private bool _hasInitializedPosition;
    private bool _allowApplicationExit;
    private HwndSource? _hwndSource;

    // Win32 Constants and P/Invoke for Mouse Click-Through, Hotkey & Robust Topmost
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_HIDEWINDOW = 0x0080;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    private const int HOTKEY_ID = 9001;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_Q = 0x51;
    private const int WM_ACTIVATEAPP = 0x001C;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_HOTKEY = 0x0312;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        AppSettings settings,
        JsonSettingsStore settingsStore,
        IClock clock)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _settings = settings;
        _settingsStore = settingsStore;
        _clock = clock;
        DataContext = viewModel;

        Icon = AppIconGenerator.GetAppImageSource();
        Topmost = settings.Window.Topmost;
        Opacity = settings.Window.Opacity;
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            ViewModel.Tick();
            if (_settings.Window.Topmost && IsVisible)
            {
                EnsureTopmost();
            }
        };
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(30, settings.RefreshIntervalSeconds))
        };
        _refreshTimer.Tick += async (_, _) => await RefreshIfNeededAsync();
    }

    public MainWindowViewModel ViewModel { get; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateWindowLayoutForMode();

        if (!_hasInitializedPosition)
        {
            RestorePosition();
            _hasInitializedPosition = true;
        }

        // Register global hotkey Ctrl + Alt + Q for toggling click-through
        var helper = new WindowInteropHelper(this);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(HwndHook);
        RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_Q);

        // Apply Click-Through state
        SetClickThrough(_settings.Window.ClickThrough);

        if (_settings.Window.Topmost)
        {
            EnsureTopmost();
        }

        _countdownTimer.Start();
        if (_settings.AutoRefreshEnabled)
        {
            _refreshTimer.Start();
        }

        _ = RefreshIfNeededAsync();
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            ToggleClickThrough();
            handled = true;
        }
        else if (msg == WM_WINDOWPOSCHANGING && _settings.Window.Topmost && IsVisible)
        {
            try
            {
                var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((wp.flags & SWP_HIDEWINDOW) == 0 && wp.hwndInsertAfter != HWND_TOPMOST)
                {
                    wp.hwndInsertAfter = HWND_TOPMOST;
                    Marshal.StructureToPtr(wp, lParam, false);
                }
            }
            catch
            {
                // Ignore any marshalling anomalies
            }
        }
        else if (msg == WM_ACTIVATEAPP && _settings.Window.Topmost && IsVisible)
        {
            EnsureTopmost();
        }

        return IntPtr.Zero;
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_settings.Window.Topmost && IsVisible)
        {
            EnsureTopmost();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowApplicationExit)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        var helper = new WindowInteropHelper(this);
        UnregisterHotKey(helper.Handle, HOTKEY_ID);
        _hwndSource?.RemoveHook(HwndHook);

        StopBackgroundActivity();
        SaveWindowState();
    }

    public void StopBackgroundActivity()
    {
        _countdownTimer.Stop();
        _refreshTimer.Stop();
    }

    public void CloseForApplicationExit()
    {
        if (Dispatcher.CheckAccess())
        {
            CloseForApplicationExitCore();
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Send,
            new Action(CloseForApplicationExitCore));
    }

    private void CloseForApplicationExitCore()
    {
        _allowApplicationExit = true;
        Hide();
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.Window.Locked)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The window may have been closed between the mouse event and DragMove.
        }
    }

    public void ApplySettings()
    {
        SetTopmostState(_settings.Window.Topmost);
        Opacity = Math.Clamp(_settings.Window.Opacity, 0.1, 1.0);
        ThemeManager.ApplyTheme(_settings.Theme);
        ViewModel.ApplySettings(_settings);
        UpdateWindowLayoutForMode();
        _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(30, _settings.RefreshIntervalSeconds));
        if (_settings.AutoRefreshEnabled)
        {
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
        }

        SetClickThrough(_settings.Window.ClickThrough);
    }

    public void SetTopmostState(bool enabled)
    {
        _settings.Window.Topmost = enabled;
        Topmost = enabled;

        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLong(handle, GWL_EXSTYLE);
        if (enabled)
        {
            if ((exStyle & WS_EX_TOPMOST) == 0)
            {
                SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TOPMOST);
            }

            SetWindowPos(
                handle,
                HWND_TOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
        else
        {
            if ((exStyle & WS_EX_TOPMOST) != 0)
            {
                SetWindowLong(handle, GWL_EXSTYLE, exStyle & ~WS_EX_TOPMOST);
            }

            SetWindowPos(
                handle,
                HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        _ = _settingsStore.SaveAsync(_settings);
    }

    public void EnsureTopmost()
    {
        if (!_settings.Window.Topmost || !IsVisible)
        {
            return;
        }

        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (!Topmost)
        {
            Topmost = true;
        }

        var exStyle = GetWindowLong(handle, GWL_EXSTYLE);
        if ((exStyle & WS_EX_TOPMOST) == 0)
        {
            SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TOPMOST);
        }

        SetWindowPos(
            handle,
            HWND_TOPMOST,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public void SetClickThrough(bool enabled)
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
        if (enabled)
        {
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }
        else
        {
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
        }

        _settings.Window.ClickThrough = enabled;
        _ = _settingsStore.SaveAsync(_settings);
        if (Application.Current is App app)
        {
            app.RefreshTrayMenu();
        }

        if (_settings.Window.Topmost)
        {
            EnsureTopmost();
        }
    }

    public void ToggleClickThrough()
    {
        SetClickThrough(!_settings.Window.ClickThrough);
    }

    private async Task RefreshIfNeededAsync()
    {
        if (WorkTimePolicy.ShouldRefreshAutomatically(_settings, _clock.Now) && !ViewModel.IsRefreshing)
        {
            await ViewModel.RefreshAsync();
        }
    }

    public Task RefreshIfAllowedAsync() => RefreshIfNeededAsync();

    private void RestorePosition()
    {
        if (_settings.Window.Left is double left && _settings.Window.Top is double top)
        {
            Left = left;
            Top = top;
            return;
        }

        var windowWidth = double.IsNaN(Width) ? 260 : Width;
        Left = SystemParameters.WorkArea.Right - windowWidth - 24;
        Top = SystemParameters.WorkArea.Top + 24;
    }

    private void SaveWindowState()
    {
        _settings.Window.Left = Left;
        _settings.Window.Top = Top;
        _settings.Window.Topmost = Topmost;
        _settings.Window.Opacity = Opacity;
        _settings.DisplayMode = ViewModel.DisplayMode;
        _settingsStore.SaveAsync(_settings).GetAwaiter().GetResult();
    }

    public void ApplyDisplayMode()
    {
        UpdateWindowLayoutForMode();
    }

    private void UpdateWindowLayoutForMode()
    {
        if (MainBorder is null)
        {
            return;
        }

        if (ViewModel.DisplayMode == DisplayMode.Minimal)
        {
            MaxWidth = NormalWindowWidth;
            Width = double.NaN;
            SizeToContent = SizeToContent.WidthAndHeight;
            MainBorder.Padding = new Thickness(12, 8, 12, 8);
            MainBorder.CornerRadius = new CornerRadius(10);
        }
        else
        {
            MaxWidth = double.PositiveInfinity;
            Width = NormalWindowWidth;
            SizeToContent = SizeToContent.Height;
            MainBorder.Padding = new Thickness(14, 12, 14, 12);
            MainBorder.CornerRadius = new CornerRadius(14);
        }
    }

}
