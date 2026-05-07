// ============================================================================
// Tranzx.iVS4.App / ViewModels / MainViewModel.cs
//
// Phase 5：Tab-based 多 Sensor 主視窗
//   - SensorTabs[]：每個 Sensor 一個 Tab
//   - ViewMode 全域切換（Vibration/Tilt/Env），影響所有 Sensor Tab 的圖表
//   - 同步控制：全部開始 / 全部停止
//   - 同步錄製：透過 CsvRecorder 一次錄所有作用 Sensor
//   - 個別錄製：每 Sensor Tab 自己有 Record toggle
// ============================================================================

using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tranzx.iVS4.App.Services;
using Tranzx.iVS4.App.Views;
using Tranzx.iVS4.Calibration;
using Tranzx.iVS4.Communication;
using Tranzx.iVS4.Communication.Discovery;
using Tranzx.iVS4.Communication.Sync;
using Tranzx.iVS4.Core.Models;

namespace Tranzx.iVS4.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MultiSensorManager Manager { get; }
    public TimeSyncService TimeSync { get; }
    public CsvRecorder Recorder { get; }

    public ObservableCollection<SensorTabViewModel> SensorTabs { get; } = new();

    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private SensorTabViewModel? selectedSensorTab;
    partial void OnSelectedSensorTabChanged(SensorTabViewModel? oldValue, SensorTabViewModel? newValue)
    {
        // 性能優化：只有當前選中的 Tab 跑 chart sweep，背景 Tab 完全停掉
        if (oldValue is not null) oldValue.IsActiveTab = false;
        if (newValue is not null) newValue.IsActiveTab = true;
    }
    [ObservableProperty] private int activeChannelCount;
    [ObservableProperty] private bool isAnyConnected;
    [ObservableProperty] private bool isSyncRecording;
    /// <summary>5-8c5：當前是「定時模式」錄製中（用於 toolbar 顯示倒數）</summary>
    [ObservableProperty] private bool isTimedRecording;
    /// <summary>5-8c5：toolbar 上顯示的倒數文字（如「⏱ 24s」）</summary>
    [ObservableProperty] private string recordingCountdownText = "";
    [ObservableProperty] private long recordedSamples;
    [ObservableProperty] private TimeSpan recordingDuration;
    [ObservableProperty] private string? sessionFolder;

    public LocalizationService Localization => LocalizationService.Instance;
    public AppSettingsService Settings => AppSettingsService.Instance;
    public Array FontScaleOptions => Enum.GetValues(typeof(FontScale));

    private string _statusKey = "Status.Ready";
    private object?[] _statusArgs = Array.Empty<object?>();
    private static LocalizationService Loc => LocalizationService.Instance;

    private System.Windows.Threading.DispatcherTimer? _recTimer;

    public MainViewModel()
    {
        var calStore = new CalibrationStore();
        Manager = new MultiSensorManager(calStore);
        TimeSync = new TimeSyncService(Manager, TimeSpan.FromSeconds(60));
        Recorder = new CsvRecorder();
        Recorder.OnSamplesWritten += n =>
            Application.Current?.Dispatcher.BeginInvoke(() => RecordedSamples = n);

        Manager.OnChannelAttached += (i, ch) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var chvm = new ChannelViewModel(i, ch);
                var tab = new SensorTabViewModel(chvm, Manager);
                tab.StatusRequested += (k, a) => SetStatus(k, a);
                SensorTabs.Add(tab);
                // ❗ Phase 5-8c：訂閱自動重連
                ReconnectService.Instance.Register(tab);
                // ❗ Phase 5-8c6：訂閱 Smart Log 監聽
                SmartLogMonitor.Instance.Register(chvm);
                ActiveChannelCount = SensorTabs.Count;
                if (SelectedSensorTab is null) SelectedSensorTab = tab;
            });
        };
        Manager.OnChannelDetached += i =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var t = SensorTabs.FirstOrDefault(s => s.Channel.Index == i);
                if (t is not null)
                {
                    t.Dispose();              // 停掉 sweep timer
                    SensorTabs.Remove(t);
                }
                ActiveChannelCount = SensorTabs.Count;
            });
        };

        Loc.LanguageChanged += _ => RefreshStatus();

        // ❗ Phase 5-8c：訂閱重連狀態 → 顯示在 status bar
        // (toast / errorlog 已由 ReconnectService.NotifyStatus 處理)
        ReconnectService.Instance.StatusChanged += (msg, phase) =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                StatusText = msg;
            });
        };

        SetStatus("Status.Ready");
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        StatusText = _statusArgs.Length == 0
            ? Loc[_statusKey]
            : Loc.Format(_statusKey, _statusArgs);
    }

    // ─────────────────────── Sensor 管理 ───────────────────────

    [RelayCommand]
    public void AddSensor()
    {
        if (SensorTabs.Count >= MultiSensorManager.MaxChannels)
        {
            SetStatus("Status.ChannelLimit");
            return;
        }
        var occupied = SensorTabs.Select(s => s.Channel.Index).ToHashSet();
        var freeSlots = Enumerable.Range(0, MultiSensorManager.MaxChannels)
                                  .Where(i => !occupied.Contains(i)).ToList();

        var dlg = new AddSensorDialog(freeSlots) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            var cfg = dlg.ViewModel.ToConfig();
            try
            {
                Manager.Attach(cfg.Index, cfg);
                SetStatus("Status.ChannelAddedFmt", cfg.DisplayName, cfg.PortName ?? "");
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Format("Error.AddChannelFailFmt", ex.Message),
                    Loc["Error.Title"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    public void RemoveSelectedSensor()
    {
        if (SelectedSensorTab is null) return;
        var idx = SelectedSensorTab.Channel.Index;
        Manager.Detach(idx);
        SetStatus("Status.ChannelRemovedFmt", idx + 1);
    }

    // ─────────────────────── 同步控制 ───────────────────────

    [RelayCommand]
    public async Task SyncStartAllAsync()
    {
        if (SensorTabs.Count == 0)
        {
            SetStatus("Status.NoChannels");
            return;
        }

        int connectedOk = 0;
        foreach (var s in SensorTabs)
        {
            try
            {
                if (!s.IsConnected)
                {
                    bool ok = await s.Channel.Channel.ConnectAsync();
                    if (ok)
                    {
                        await s.Channel.Channel.ApplyConfigAsync(verify: false);
                        s.IsConnected = true;
                        ReconnectService.Instance.NotifyUserConnect(s.Channel.Index);
                    }
                }
                if (s.IsConnected)
                {
                    s.IsPausedPlot = false;  // 連線後立即恢復繪圖
                    connectedOk++;
                }
            }
            catch { /* 個別失敗不影響其他 */ }
        }

        TimeSync.Start();
        IsAnyConnected = SensorTabs.Any(s => s.IsConnected);
        SetStatus("Status.SyncStartedFmt", connectedOk);
    }

    [RelayCommand]
    public async Task SyncStopAllAsync()
    {
        if (IsSyncRecording) StopSyncRecording();
        TimeSync.Stop();
        foreach (var s in SensorTabs)
        {
            try
            {
                s.IsPausedPlot = true;  // 圖表暫停（但仍可重連即恢復）
                ReconnectService.Instance.NotifyUserDisconnect(s.Channel.Index);
                if (s.IsConnected) await s.Channel.Channel.DisconnectAsync();
                s.IsConnected = false;
            }
            catch { }
        }
        IsAnyConnected = false;
        SetStatus("Status.SyncStopped");
    }

    // ─────────────────────── 同步錄製 ───────────────────────

    [RelayCommand]
    public void ToggleSyncRecording()
    {
        if (IsSyncRecording) StopSyncRecording();
        else StartSyncRecording();
    }

    private void StartSyncRecording()
    {
        var active = Manager.Active.ToList();
        if (active.Count == 0)
        {
            SetStatus("Status.NoRecord");
            return;
        }

        try
        {
            Recorder.Start(active);
            SessionFolder = Recorder.SessionFolder;
            IsSyncRecording = true;
            RecordedSamples = 0;
            RecordingDuration = TimeSpan.Zero;

            // 標記每個 Sensor 都在錄
            foreach (var s in SensorTabs)
                if (s.IsConnected) s.Channel.IsRecording = true;

            _recTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(500) };
            _recTimer.Tick += (_, _) =>
            {
                if (Recorder.StartedAt.HasValue)
                {
                    RecordingDuration = DateTime.Now - Recorder.StartedAt.Value;
                    // ❗ Phase 5-8c5：定時錄製到時自動停止 + 倒數
                    var s = AppSettingsService.Instance;
                    if (s.TimedRecordingEnabled)
                    {
                        int total = s.RecordingDurationSec;
                        double elapsed = RecordingDuration.TotalSeconds;
                        int remain = total - (int)Math.Floor(elapsed);
                        if (remain < 0) remain = 0;
                        RecordingCountdownText = FormatCountdown(remain);
                        if (elapsed >= total)
                        {
                            StopSyncRecording();
                            Services.LiveStatusFeed.Instance.Push(
                                Services.FeedKind.Success, "Recording",
                                string.Format(Loc["Recording.AutoStoppedFmt"], total));
                        }
                    }
                }
            };
            _recTimer.Start();

            // ❗ Phase 5-8c5：定時錄製啟動通知 + 設定 IsTimedRecording flag
            if (AppSettingsService.Instance.TimedRecordingEnabled)
            {
                int total = AppSettingsService.Instance.RecordingDurationSec;
                IsTimedRecording = true;
                RecordingCountdownText = FormatCountdown(total);
                Services.LiveStatusFeed.Instance.Push(
                    Services.FeedKind.Info, "Recording",
                    string.Format(Loc["Recording.TimedStartFmt"], total));
            }
            else if (AppSettingsService.Instance.ContinuousRecording)
            {
                Services.LiveStatusFeed.Instance.Push(
                    Services.FeedKind.Info, "Recording",
                    Loc["Recording.ContinuousStart"]);
            }

            SetStatus("Status.RecordStartedFmt", SessionFolder ?? "");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("Error.RecordStartFailFmt", ex.Message),
                Loc["Error.Title"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopSyncRecording()
    {
        if (!IsSyncRecording) return;
        _recTimer?.Stop();
        Recorder.Stop();
        IsSyncRecording = false;
        IsTimedRecording = false;
        RecordingCountdownText = "";
        foreach (var s in SensorTabs) s.Channel.IsRecording = false;
        SetStatus("Status.RecordStoppedFmt", RecordedSamples);
    }

    /// <summary>5-8c5：倒數文字格式（與 ChannelViewModel 一致）</summary>
    private static string FormatCountdown(int sec)
    {
        if (sec >= 3600)
        {
            int h = sec / 3600;
            int m = (sec % 3600) / 60;
            int s = sec % 60;
            return $"⏱ {h}:{m:D2}:{s:D2}";
        }
        if (sec >= 60)
        {
            int m = sec / 60;
            int s = sec % 60;
            return $"⏱ {m}:{s:D2}";
        }
        return $"⏱ {sec}s";
    }

    /// <summary>
    /// 個別 Sensor 的錄製 toggle（從 SensorTabView 的 Record 按鈕觸發）
    /// 簡化處理：實際上只支援單一同步錄製，個別錄製被視為「啟動同步錄製」
    /// <summary>
    /// 個別 Sensor 錄製按鈕：
    ///   LogScopeAll = true  → 切換所有 Sensor 一起錄
    ///   LogScopeAll = false → 只切換指定 Sensor
    /// </summary>
    [RelayCommand]
    public void ToggleSensorRecording(object? channelIndex)
    {
        if (AppSettingsService.Instance.LogScopeAll)
        {
            // 全部 Sensor 一起切換
            ToggleSyncRecording();
            return;
        }

        // 個別 Sensor 切換
        if (channelIndex is not int idx) return;
        var tab = SensorTabs.FirstOrDefault(t => t.Channel.Index == idx);
        if (tab is null) return;
        tab.Channel.IsRecording = !tab.Channel.IsRecording;
    }

    [RelayCommand]
    public void OpenSessionFolder()
    {
        if (string.IsNullOrEmpty(SessionFolder) || !Directory.Exists(SessionFolder)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SessionFolder,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public void OpenPreferences()
    {
        var dlg = new Views.PreferencesDialog { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    }

    /// <summary>Phase 5-8c：開啟多 Sensor Dashboard window（非 modal）</summary>
    [RelayCommand]
    public void OpenDashboard()
    {
        // 若已開啟同一個視窗則只 Activate（避免開多份）
        foreach (Window w in Application.Current.Windows)
        {
            if (w is Views.SensorDashboardWindow) { w.Activate(); return; }
        }
        var win = new Views.SensorDashboardWindow(SensorTabs)
        {
            Owner = Application.Current.MainWindow
        };
        win.Show();
    }

    /// <summary>Phase 5-8c：開啟歷史警報統計畫面</summary>
    [RelayCommand]
    public void OpenAlarmStats()
    {
        foreach (Window w in Application.Current.Windows)
        {
            if (w is Views.AlarmStatsWindow) { w.Activate(); return; }
        }
        var win = new Views.AlarmStatsWindow
        {
            Owner = Application.Current.MainWindow
        };
        win.Show();
    }

    /// <summary>Phase 5-8c7：開啟振動統計數據視窗</summary>
    [RelayCommand]
    public void OpenVibrationStats()
    {
        foreach (Window w in Application.Current.Windows)
        {
            if (w is Views.VibrationStatsWindow) { w.Activate(); return; }
        }
        var win = new Views.VibrationStatsWindow(SensorTabs)
        {
            Owner = Application.Current.MainWindow
        };
        win.Show();
    }

    /// <summary>Phase 5-8c9：開啟歷史分析視窗（載入 CSV 畫圖）</summary>
    [RelayCommand]
    public void OpenHistoryAnalysis()
    {
        foreach (Window w in Application.Current.Windows)
        {
            if (w is Views.HistoryAnalysisWindow) { w.Activate(); return; }
        }
        var win = new Views.HistoryAnalysisWindow
        {
            Owner = Application.Current.MainWindow
        };
        win.Show();
    }

    /// <summary>Phase 5-9 (A)：開啟 3D 動態回放視窗</summary>
    [RelayCommand]
    public void OpenMotion3D()
    {
        try
        {
            foreach (Window w in Application.Current.Windows)
            {
                if (w is Views.Motion3DWindow) { w.Activate(); return; }
            }
            var win = new Views.Motion3DWindow
            {
                Owner = Application.Current.MainWindow
            };
            win.Show();
        }
        catch (Exception ex)
        {
            try { Services.ErrorLogService.Instance.Error("Motion3D", $"open failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); } catch { }
            MessageBox.Show($"無法開啟 3D 動態回放：\n\n{ex.GetType().Name}: {ex.Message}",
                "3D Motion Replay", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Phase 5-8c2：開啟軟體狀況監控（事件 log）</summary>
    [RelayCommand]
    public void OpenEventLog()
    {
        foreach (Window w in Application.Current.Windows)
        {
            if (w is Views.EventLogWindow) { w.Activate(); return; }
        }
        var win = new Views.EventLogWindow
        {
            Owner = Application.Current.MainWindow
        };
        win.Show();
    }
}
