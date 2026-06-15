using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MahApps.Metro.Controls;
using SharpHook.Data;

namespace PoeAncientsPriceHelper;

public partial class MainWindow : MetroWindow
{
    private AppConfig _config = new();
    private PriceRepository? _repo;
    private IconCache? _icons;
    private ScanEngine? _engine;
    private readonly HttpClient _http = new();
    private bool _loading;
    
    // 日志
    private static readonly string LogDir = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string LogFilePath = Path.Combine(LogDir, "app.log");
    private static readonly object LogLock = new object();

    // F4 (calibrate) stays a Win32 hotkey — it's a rare, full-screen action that benefits from being
    // suppressed from the game. Start/Stop moved to the App-level SharpHook hook (configurable key).
    private const int HotkeyId = 1;
    private const int VK_F4 = 0x73;
    private IntPtr _hwnd;

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MainWindow()
    {
        InitializeComponent();
        Log("========================================");
        Log("应用程序启动");
        
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (v is not null) 
        {
            VersionLabel.Text = $"v{v.Major}.{v.Minor}.{v.Build}";
            Log($"版本: {v.Major}.{v.Minor}.{v.Build}");
        }
        
        Loaded += OnLoaded;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            if (!RegisterHotKey(_hwnd, HotkeyId, 0, VK_F4))
            {
                Log("[警告] F4 热键注册失败 (可能被其他程序占用)");
                Title += "  ⚠ F4 热键不可用";
            }
            else
            {
                Log("F4 热键注册成功");
            }
            HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        };
    }
    
    private static void Log(string message)
    {
        var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Console.WriteLine(logLine);
        try
        {
            lock (LogLock)
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogFilePath, logLine + Environment.NewLine);
            }
        }
        catch { }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId) { RunCalibration(); handled = true; }
        return IntPtr.Zero;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Log("窗口加载完成");
        _config = ConfigStore.Load();
        Log($"配置加载完成: 联赛={_config.LeagueName}, 已校准={_config.IsCalibrated}");
        PopulateFields();
        await StartupAsync();
    }

    private void PopulateFields()
    {
        _loading = true;
        LeagueBox.ItemsSource = _config.AvailableLeagues;
        LeagueBox.SelectedItem = _config.AvailableLeagues.Contains(_config.LeagueName)
            ? _config.LeagueName
            : _config.AvailableLeagues.FirstOrDefault();
        var key = HotkeyBinding.Parse(_config.StartStopHotkey);
        HotkeyLabel.Text = HotkeyBinding.Display(key);
        App.SetStartStopKey(key);
        
        // 显示校准热键
        var calibrateKey = HotkeyBinding.Parse(_config.CalibrateHotkey);
        CalibrateHotkeyLabel.Text = HotkeyBinding.Display(calibrateKey);
        
        UpdateRegionLabel();
        _loading = false;
    }

    private void UpdateRegionLabel()
    {
        RegionLabel.Text = _config.IsCalibrated
            ? $"x={_config.RegionX} y={_config.RegionY} {_config.RegionWidth}×{_config.RegionHeight}"
            : "未校准";
    }

    private async Task StartupAsync()
    {
        Log("开始启动...");
        StatusLabel.Text = "正在从 poe.ninja 获取价格...";
        StartStopButton.IsEnabled = false;

        _repo?.Dispose();
        _icons?.Dispose();

        _repo = new PriceRepository(_http);
        _repo.PricesUpdated += OnPricesUpdated;
        _icons = new IconCache(_http);

        try
        {
            Log("正在获取价格和图标...");
            await Task.WhenAll(
                _repo.InitialFetchAsync(_config),
                _icons.LoadAsync());
            Log("价格和图标获取完成");
        }
        catch (Exception ex)
        {
            Log($"[错误] 启动失败: {ex.Message}");
            StatusLabel.Text = $"启动失败: {ex.Message}";
        }

        _repo.StartAutoRefresh(_config);
        Log("自动同步已启动");

        UpdateStatusLabel();
        StartStopButton.IsEnabled = _config.IsCalibrated;
        Log("启动完成");
    }

    private void OnPricesUpdated() => Dispatcher.BeginInvoke(UpdateStatusLabel);

    private void UpdateStatusLabel()
    {
        if (_repo is null) return;
        string fetched = _repo.LastFetchedAt is { } t ? t.ToString("MM月dd日 HH:mm") : "从未";
        StatusLabel.Text = $"已加载 {_repo.ItemCount} 个物品  ·  上次获取 {fetched}";
        
        UpdateSyncStatus();
    }
    
    private void UpdateSyncStatus()
    {
        if (_repo is null) return;
        
        SyncStatusLabel.Text = _repo.SyncStatus;
        SyncStatusLabel.Foreground = _repo.IsSyncing 
            ? System.Windows.Media.Brushes.Yellow 
            : (_repo.LastFetchedAt.HasValue 
                ? System.Windows.Media.Brushes.LightGreen 
                : System.Windows.Media.Brushes.Gray);
        
        LastSyncLabel.Text = _repo.LastFetchedAt.HasValue 
            ? _repo.LastFetchedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") 
            : "-";
        
        SyncCountLabel.Text = _repo.SyncCount.ToString();
        ItemCountLabel.Text = _repo.ItemCount.ToString();
        
        SyncButton.IsEnabled = !_repo.IsSyncing;
        SyncButton.Content = _repo.IsSyncing ? "同步中..." : "手动同步价格";
    }

    private void DonateButton_Click(object sender, RoutedEventArgs e)
    {
        const string url = "https://www.paypal.com/donate/?business=pedro.levi.magic%40gmail.com&currency_code=USD&item_name=PoeAncientsPriceHelper";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            Log("打开捐赠页面");
        }
        catch (Exception ex)
        {
            Log($"[错误] 打开浏览器失败: {ex.Message}");
        }
    }

    private void RunCalibration()
    {
        Log("开始校准区域...");
        var rect = CalibrationOverlay.RunOnStaThread();
        if (rect is null) 
        {
            Log("校准取消");
            return;
        }
        _config.RegionRect = rect.Value;
        ConfigStore.Save(_config);
        Log($"校准完成: x={_config.RegionX} y={_config.RegionY} {_config.RegionWidth}×{_config.RegionHeight}");
        Dispatcher.Invoke(() =>
        {
            UpdateRegionLabel();
            StartStopButton.IsEnabled = _config.IsCalibrated;
        });
    }

    private void CalibrateButton_Click(object sender, RoutedEventArgs e) => RunCalibration();

    private void StartStopButton_Click(object sender, RoutedEventArgs e) => ToggleStartStop();

    internal void ToggleStartStop()
    {
        if (_engine is null)
        {
            if (!_config.IsCalibrated || _repo is null || _icons is null) 
            {
                Log("[警告] 无法启动: 未校准或数据未加载");
                return;
            }
            Log("启动扫描引擎...");
            _engine = new ScanEngine(_config, _repo, _icons);
            _engine.Start();
            StartStopButton.Content = "停止";
            StartStopButton.Background = System.Windows.Media.Brushes.DarkRed;
            Log("扫描引擎已启动");
        }
        else
        {
            Log("停止扫描引擎...");
            _engine.StopAndWait(TimeSpan.FromSeconds(2));
            _engine.Dispose();
            _engine = null;
            StartStopButton.Content = "启动";
            StartStopButton.Background = System.Windows.Media.Brushes.DarkGreen;
            Log("扫描引擎已停止");
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Log("应用程序关闭");
        UnregisterHotKey(_hwnd, HotkeyId);
        _engine?.StopAndWait(TimeSpan.FromSeconds(2));
        _engine?.Dispose();
        _repo?.Dispose();
        _icons?.Dispose();
        _http.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private async void LeagueBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading || LeagueBox.SelectedItem is not string league || league == _config.LeagueName) return;
        Log($"切换联赛: {_config.LeagueName} -> {league}");
        _config.LeagueName = league;
        ConfigStore.Save(_config);
        await StartupAsync();
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (_repo is null || _repo.IsSyncing) return;
        
        Log("手动同步触发");
        SyncButton.IsEnabled = false;
        SyncButton.Content = "同步中...";
        
        try
        {
            await _repo.InitialFetchAsync(_config);
            UpdateStatusLabel();
            Log("手动同步完成");
        }
        catch (Exception ex)
        {
            Log($"[错误] 手动同步失败: {ex.Message}");
            System.Windows.MessageBox.Show($"同步失败: {ex.Message}", "同步错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SyncButton.Content = "手动同步价格";
            SyncButton.IsEnabled = true;
        }
    }

    private void RebindButton_Click(object sender, RoutedEventArgs e)
    {
        Log("开始重新绑定启动/停止热键...");
        RebindButton.IsEnabled = false;
        RebindButton.Content = "请按键... (Esc 取消)";
        App.BeginHotkeyCapture(OnStartStopHotkeyCaptured);
    }
    
    private void RebindCalibrateButton_Click(object sender, RoutedEventArgs e)
    {
        Log("开始重新绑定校准热键...");
        RebindCalibrateButton.IsEnabled = false;
        RebindCalibrateButton.Content = "请按键... (Esc 取消)";
        App.BeginHotkeyCapture(OnCalibrateHotkeyCaptured);
    }

    private void OnStartStopHotkeyCaptured(App.CaptureOutcome outcome, KeyCode code)
    {
        switch (outcome)
        {
            case App.CaptureOutcome.Captured:
                Log($"启动/停止热键绑定: {HotkeyBinding.Display(code)}");
                _config.StartStopHotkey = HotkeyBinding.ToStorage(code);
                ConfigStore.Save(_config);
                App.SetStartStopKey(code);
                HotkeyLabel.Text = HotkeyBinding.Display(code);
                EndStartStopRebind();
                break;
            case App.CaptureOutcome.Reserved:
                RebindButton.Content = $"{HotkeyBinding.Display(code)} 已被占用 — 请换一个";
                break;
            case App.CaptureOutcome.Cancelled:
                EndStartStopRebind();
                break;
        }
    }
    
    private void OnCalibrateHotkeyCaptured(App.CaptureOutcome outcome, KeyCode code)
    {
        switch (outcome)
        {
            case App.CaptureOutcome.Captured:
                Log($"校准热键绑定: {HotkeyBinding.Display(code)}");
                _config.CalibrateHotkey = HotkeyBinding.ToStorage(code);
                ConfigStore.Save(_config);
                CalibrateHotkeyLabel.Text = HotkeyBinding.Display(code);
                EndCalibrateRebind();
                break;
            case App.CaptureOutcome.Reserved:
                RebindCalibrateButton.Content = $"{HotkeyBinding.Display(code)} 已被占用 — 请换一个";
                break;
            case App.CaptureOutcome.Cancelled:
                EndCalibrateRebind();
                break;
        }
    }

    private void EndStartStopRebind()
    {
        RebindButton.Content = "重新绑定";
        RebindButton.IsEnabled = true;
    }
    
    private void EndCalibrateRebind()
    {
        RebindCalibrateButton.Content = "重新绑定";
        RebindCalibrateButton.IsEnabled = true;
    }
}
