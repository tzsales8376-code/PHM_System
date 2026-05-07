// ============================================================================
// Tranzx.iVS4.App / Services / AppSettingsService.cs
// 全域偏好設定（單例）
//   - ViewMode (振動/水平/溫溼度)
//   - FontScale (字型大小)
//   - GravityMode (扣除/保留重力)
//   - StatisticsHz (時域統計頻率，預設 20Hz)
//   - StatisticsOverlapPct (Overlap 百分比，預設 30%)
//   - 圖表 X 軸/Y 軸設定（每模式各自）
// ============================================================================

using System;
using System.ComponentModel;
using System.Windows;

namespace Tranzx.iVS4.App.Services;

public enum AppViewMode { Vibration, Tilt, Env }

/// <summary>振動模式下的子顯示：Trend / Waveform / FFT</summary>
public enum VibrationSubMode { Trend, Waveform, Fft }
public enum FontScale  { Small, Normal, Large, XLarge }

/// <summary>
/// 重力處理模式（影響振動時域顯示）：
///   - RemoveGravity (預設)：扣除 DC 分量，靜置時 X/Y/Z 都 ≈ 0
///   - KeepGravity：保留 DC 分量，垂直軸靜置時 ≈ 1G
/// </summary>
public enum GravityMode { RemoveGravity, KeepGravity }

/// <summary>
/// 三軸角度顯示模式：
///   - Inclinometer (預設)：工程水平儀慣例，「該軸偏離水平面多少度」
///       公式：90 - acos(Ai / |g|)，範圍 -90 ~ +90
///       平放時 X=Y=0, Z=+90（Z 朝上垂直；X、Y 在水平面上）
///   - GravityVector：物理向量夾角，「軸正向與重力的夾角」
///       公式：acos(Ai / |g|)，範圍 0 ~ 180
///       平放時 X=Y=90, Z=0（Z 軸與重力夾 0°）
/// </summary>
public enum TiltAngleMode { Inclinometer, GravityVector }

public sealed class AppSettingsService : INotifyPropertyChanged
{
    public static AppSettingsService Instance { get; } = new();

    // ─────────── ViewMode ───────────
    private AppViewMode _viewMode = AppViewMode.Vibration;
    public AppViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode == value) return;
            _viewMode = value;
            PropertyChanged?.Invoke(this, new(nameof(ViewMode)));
            ViewModeChanged?.Invoke(value);
        }
    }

    // ─────────── VibrationSubMode（振動模式下的子顯示）───────────
    private VibrationSubMode _vibSubMode = VibrationSubMode.Trend;
    public VibrationSubMode VibrationSubMode
    {
        get => _vibSubMode;
        set
        {
            if (_vibSubMode == value) return;
            _vibSubMode = value;
            PropertyChanged?.Invoke(this, new(nameof(VibrationSubMode)));
            VibrationSubModeChanged?.Invoke(value);
        }
    }
    public event Action<VibrationSubMode>? VibrationSubModeChanged;

    // ─── Phase 5-8b：振動量測設定（波形 + FFT）──────────────────
    public static int[] WaveformSecOptions => new[] { 1, 2, 5, 10, 20, 30, 60 };
    /// <summary>波形 Y 軸最大（±N G）；0 = Auto；預設 2G</summary>
    public static double[] WaveformYMaxOptions => new[] { 0.0, 0.5, 1, 2, 5, 16 };
    public static int[] FftFreqMaxOptions => new[] { 150, 250, 500, 1000, 1600 };
    /// <summary>0 = Auto；其他為固定上限 (G)</summary>
    public static double[] FftYMaxOptions => new[] { 0.0, 0.01, 0.05, 0.1, 0.5, 1.0, 5.0, 10.0 };
    public static int[] FftNOptions => new[] { 1024, 2048, 4096, 8192 };
    public static Tranzx.iVS4.Analysis.WindowFunction[] FftWindowOptions =>
        new[] {
            Tranzx.iVS4.Analysis.WindowFunction.Hanning,
            Tranzx.iVS4.Analysis.WindowFunction.Hamming,
            Tranzx.iVS4.Analysis.WindowFunction.Blackman,
            Tranzx.iVS4.Analysis.WindowFunction.FlatTop,
            Tranzx.iVS4.Analysis.WindowFunction.Rectangular,
        };

    private int _waveformSec = 5;
    public int WaveformSec
    {
        get => _waveformSec;
        set { if (_waveformSec == value) return; _waveformSec = value;
              PropertyChanged?.Invoke(this, new(nameof(WaveformSec)));
              ChartSettingsChanged?.Invoke(); }
    }

    private double _waveformYMaxG = 2.0;
    public double WaveformYMaxG
    {
        get => _waveformYMaxG;
        set { if (Math.Abs(_waveformYMaxG - value) < 1e-9) return; _waveformYMaxG = value;
              PropertyChanged?.Invoke(this, new(nameof(WaveformYMaxG)));
              ChartSettingsChanged?.Invoke(); }
    }

    private int _fftFreqMax = 250;
    public int FftFreqMax
    {
        get => _fftFreqMax;
        set { if (_fftFreqMax == value) return; _fftFreqMax = value;
              PropertyChanged?.Invoke(this, new(nameof(FftFreqMax)));
              ChartSettingsChanged?.Invoke(); }
    }

    private double _fftYMax;  // 0 = Auto
    public double FftYMax
    {
        get => _fftYMax;
        set { if (Math.Abs(_fftYMax - value) < 1e-9) return; _fftYMax = value;
              PropertyChanged?.Invoke(this, new(nameof(FftYMax)));
              ChartSettingsChanged?.Invoke(); }
    }

    private Tranzx.iVS4.Analysis.WindowFunction _fftWindow = Tranzx.iVS4.Analysis.WindowFunction.Hanning;
    public Tranzx.iVS4.Analysis.WindowFunction FftWindow
    {
        get => _fftWindow;
        set { if (_fftWindow == value) return; _fftWindow = value;
              PropertyChanged?.Invoke(this, new(nameof(FftWindow))); }
    }

    private int _fftN = 2048;
    public int FftN
    {
        get => _fftN;
        set { if (_fftN == value) return; _fftN = value;
              PropertyChanged?.Invoke(this, new(nameof(FftN))); }
    }

    // ─────────── FontScale ───────────
    private FontScale _fontScale = FontScale.Normal;
    public FontScale FontScale
    {
        get => _fontScale;
        set
        {
            if (_fontScale == value) return;
            _fontScale = value;
            ApplyFontScale();
            PropertyChanged?.Invoke(this, new(nameof(FontScale)));
        }
    }

    // ─────────── GravityMode ───────────
    private GravityMode _gravityMode = GravityMode.RemoveGravity;
    public GravityMode GravityMode
    {
        get => _gravityMode;
        set
        {
            if (_gravityMode == value) return;
            _gravityMode = value;
            PropertyChanged?.Invoke(this, new(nameof(GravityMode)));
            GravityModeChanged?.Invoke(value);
        }
    }

    // ─────────── Statistics（時域統計設定）───────────
    /// <summary>時域統計取樣頻率 (Hz)，預設 20Hz = 每 50ms 算一次 batch 的 Peak/RMS/Crest</summary>
    private double _statisticsHz = 20;
    public double StatisticsHz
    {
        get => _statisticsHz;
        set
        {
            if (Math.Abs(_statisticsHz - value) < 1e-9) return;
            _statisticsHz = value;
            PropertyChanged?.Invoke(this, new(nameof(StatisticsHz)));
            StatisticsSettingsChanged?.Invoke();
        }
    }

    /// <summary>Overlap 百分比，預設 30%</summary>
    private double _statisticsOverlapPct = 30;
    public double StatisticsOverlapPct
    {
        get => _statisticsOverlapPct;
        set
        {
            if (Math.Abs(_statisticsOverlapPct - value) < 1e-9) return;
            _statisticsOverlapPct = value;
            PropertyChanged?.Invoke(this, new(nameof(StatisticsOverlapPct)));
            StatisticsSettingsChanged?.Invoke();
        }
    }

    public static double[] StatsHzOptions => new[] { 1.0, 2, 5, 10, 20, 40, 50, 100, 200 };
    public static double[] OverlapPctOptions => new[] { 0.0, 10, 25, 30, 50, 75 };

    // ─────────── Tilt LPF（DC 算傾角的低通濾波）───────────
    /// <summary>水平 LPF 啟用，預設 true</summary>
    private bool _tiltLpfEnabled = true;
    public bool TiltLpfEnabled
    {
        get => _tiltLpfEnabled;
        set
        {
            if (_tiltLpfEnabled == value) return;
            _tiltLpfEnabled = value;
            PropertyChanged?.Invoke(this, new(nameof(TiltLpfEnabled)));
            TiltLpfChanged?.Invoke();
        }
    }

    /// <summary>水平 LPF 時間常數（秒），預設 1.0</summary>
    private double _tiltLpfSec = 1.0;
    public double TiltLpfSec
    {
        get => _tiltLpfSec;
        set
        {
            if (Math.Abs(_tiltLpfSec - value) < 1e-9) return;
            _tiltLpfSec = value;
            PropertyChanged?.Invoke(this, new(nameof(TiltLpfSec)));
            TiltLpfChanged?.Invoke();
        }
    }

    public static double[] TiltLpfSecOptions => new[] { 0.2, 0.5, 1.0, 2.0, 5.0 };

    // ─────────── 三軸角度顯示模式 ───────────
    private TiltAngleMode _tiltAngleMode = TiltAngleMode.Inclinometer;  // 預設工程水平儀
    public TiltAngleMode TiltAngleMode
    {
        get => _tiltAngleMode;
        set
        {
            if (_tiltAngleMode == value) return;
            _tiltAngleMode = value;
            PropertyChanged?.Invoke(this, new(nameof(TiltAngleMode)));
            TiltAngleModeChanged?.Invoke(value);
        }
    }

    // ─────────── 效能調校 ───────────
    /// <summary>圖表 refresh 頻率 (Hz)。預設 10Hz（100ms），可選 5/8/10/15/20/30</summary>
    private double _chartRefreshHz = 10;
    public double ChartRefreshHz
    {
        get => _chartRefreshHz;
        set
        {
            if (Math.Abs(_chartRefreshHz - value) < 1e-9) return;
            _chartRefreshHz = value;
            PropertyChanged?.Invoke(this, new(nameof(ChartRefreshHz)));
            PerformanceSettingsChanged?.Invoke();
        }
    }
    public static double[] ChartRefreshHzOptions => new[] { 5.0, 8, 10, 15, 20, 30 };

    /// <summary>每個 series 圖表點數上限。預設 1000，可選 500/800/1000/1500/2000</summary>
    private int _chartMaxPoints = 1000;
    public int ChartMaxPoints
    {
        get => _chartMaxPoints;
        set
        {
            if (_chartMaxPoints == value) return;
            _chartMaxPoints = value;
            PropertyChanged?.Invoke(this, new(nameof(ChartMaxPoints)));
            PerformanceSettingsChanged?.Invoke();
        }
    }
    public static int[] ChartMaxPointsOptions => new[] { 500, 800, 1000, 1500, 2000 };

    // ─── Phase 5-8c：自動重連設定 ─────────────────────
    public static int[] ReconnectAttemptsOptions => new[] { 0, 3, 5, 10, 20, 50 };  // 0 = 停用
    public static int[] ReconnectIntervalSecOptions => new[] { 5, 10, 15, 30, 60 };

    /// <summary>斷線後最大重試次數，預設 5；0 = 不自動重連</summary>
    private int _reconnectAttempts = 5;
    public int ReconnectAttempts
    {
        get => _reconnectAttempts;
        set { if (_reconnectAttempts == value) return; _reconnectAttempts = value;
              PropertyChanged?.Invoke(this, new(nameof(ReconnectAttempts))); Save(); }
    }

    /// <summary>每次重試之間的間隔秒數，預設 10</summary>
    private int _reconnectIntervalSec = 10;
    public int ReconnectIntervalSec
    {
        get => _reconnectIntervalSec;
        set { if (_reconnectIntervalSec == value) return; _reconnectIntervalSec = value;
              PropertyChanged?.Invoke(this, new(nameof(ReconnectIntervalSec))); Save(); }
    }

    /// <summary>是否顯示診斷面板（SPS / Bytes / Packets / Hex preview），預設關閉</summary>
    private bool _showDiagnostics = false;
    public bool ShowDiagnostics
    {
        get => _showDiagnostics;
        set
        {
            if (_showDiagnostics == value) return;
            _showDiagnostics = value;
            PropertyChanged?.Invoke(this, new(nameof(ShowDiagnostics)));
            Save();
        }
    }

    /// <summary>警報紀錄資料夾，預設 %LocalAppData%\Tranzx.iVS4\Alarms</summary>
    private string _alarmLogFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tranzx PHM", "Alarms");
    public string AlarmLogFolder
    {
        get => _alarmLogFolder;
        set
        {
            if (_alarmLogFolder == value) return;
            _alarmLogFolder = value ?? string.Empty;
            PropertyChanged?.Invoke(this, new(nameof(AlarmLogFolder)));
            Save();
        }
    }

    /// <summary>Trend CSV 紀錄資料夾，預設 %LocalAppData%\Tranzx.iVS4\Trends</summary>
    private string _trendLogFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tranzx PHM", "Trends");
    public string TrendLogFolder
    {
        get => _trendLogFolder;
        set
        {
            if (_trendLogFolder == value) return;
            _trendLogFolder = value ?? string.Empty;
            PropertyChanged?.Invoke(this, new(nameof(TrendLogFolder)));
            Save();
        }
    }

    /// <summary>啟用 Raw Data 紀錄（3300 sps 全紀錄，檔案會很大）</summary>
    private bool _rawDataEnabled = false;
    public bool RawDataEnabled
    {
        get => _rawDataEnabled;
        set
        {
            if (_rawDataEnabled == value) return;
            _rawDataEnabled = value;
            PropertyChanged?.Invoke(this, new(nameof(RawDataEnabled)));
            Save();
        }
    }

    // ─── Phase 5-7c：錄製範圍 + 每個 mode 是否錄 ───
    /// <summary>true = 點任一 Sensor 開始錄製 → 全部 Sensor 一起錄；false = 只錄該 Sensor</summary>
    private bool _logScopeAll = true;
    public bool LogScopeAll
    {
        get => _logScopeAll;
        set { if (_logScopeAll == value) return; _logScopeAll = value;
              PropertyChanged?.Invoke(this, new(nameof(LogScopeAll))); Save(); }
    }

    private bool _logVibration = true;
    public bool LogVibration
    {
        get => _logVibration;
        set { if (_logVibration == value) return; _logVibration = value;
              PropertyChanged?.Invoke(this, new(nameof(LogVibration))); Save(); }
    }

    private bool _logTilt = true;
    public bool LogTilt
    {
        get => _logTilt;
        set { if (_logTilt == value) return; _logTilt = value;
              PropertyChanged?.Invoke(this, new(nameof(LogTilt))); Save(); }
    }

    private bool _logEnv = true;
    public bool LogEnv
    {
        get => _logEnv;
        set { if (_logEnv == value) return; _logEnv = value;
              PropertyChanged?.Invoke(this, new(nameof(LogEnv))); Save(); }
    }

    /// <summary>警報音效（黃/紅燈觸發時播放系統音），預設關閉</summary>
    private bool _alarmSoundEnabled = false;
    public bool AlarmSoundEnabled
    {
        get => _alarmSoundEnabled;
        set
        {
            if (_alarmSoundEnabled == value) return;
            _alarmSoundEnabled = value;
            PropertyChanged?.Invoke(this, new(nameof(AlarmSoundEnabled)));
            Save();
        }
    }

    // ─── Phase 5-8c2：Alarm Toast 視覺通知開關 + 等級過濾 ───
    /// <summary>主開關：黃/紅燈是否彈出視覺通知（預設 On）</summary>
    private bool _alarmToastEnabled = true;
    public bool AlarmToastEnabled
    {
        get => _alarmToastEnabled;
        set { if (_alarmToastEnabled == value) return; _alarmToastEnabled = value;
              PropertyChanged?.Invoke(this, new(nameof(AlarmToastEnabled))); Save(); }
    }

    /// <summary>是否在黃燈時也彈 toast（預設 false：只 Red 才彈）</summary>
    private bool _alarmToastOnYellow = false;
    public bool AlarmToastOnYellow
    {
        get => _alarmToastOnYellow;
        set { if (_alarmToastOnYellow == value) return; _alarmToastOnYellow = value;
              PropertyChanged?.Invoke(this, new(nameof(AlarmToastOnYellow))); Save(); }
    }

    /// <summary>紅燈時彈 toast（預設 true）</summary>
    private bool _alarmToastOnRed = true;
    public bool AlarmToastOnRed
    {
        get => _alarmToastOnRed;
        set { if (_alarmToastOnRed == value) return; _alarmToastOnRed = value;
              PropertyChanged?.Invoke(this, new(nameof(AlarmToastOnRed))); Save(); }
    }

    // ─── Phase 5-7b：CSV 切割時間 + 保留天數（Trend / Raw 獨立）───
    public static int[] SegmentMinutesOptions => new[] { 5, 10, 30, 60, 120, 360 };
    public static int[] RetentionDaysOptions  => new[] { 7, 30, 60, 90, 180, 365 };

    private int _trendSegmentMinutes = 60;
    public int TrendSegmentMinutes
    {
        get => _trendSegmentMinutes;
        set { if (_trendSegmentMinutes == value) return; _trendSegmentMinutes = value;
              PropertyChanged?.Invoke(this, new(nameof(TrendSegmentMinutes))); Save(); }
    }

    private int _trendRetentionDays = 90;
    public int TrendRetentionDays
    {
        get => _trendRetentionDays;
        set { if (_trendRetentionDays == value) return; _trendRetentionDays = value;
              PropertyChanged?.Invoke(this, new(nameof(TrendRetentionDays))); Save(); }
    }

    private int _rawSegmentMinutes = 60;
    public int RawSegmentMinutes
    {
        get => _rawSegmentMinutes;
        set { if (_rawSegmentMinutes == value) return; _rawSegmentMinutes = value;
              PropertyChanged?.Invoke(this, new(nameof(RawSegmentMinutes))); Save(); }
    }

    private int _rawRetentionDays = 90;
    public int RawRetentionDays
    {
        get => _rawRetentionDays;
        set { if (_rawRetentionDays == value) return; _rawRetentionDays = value;
              PropertyChanged?.Invoke(this, new(nameof(RawRetentionDays))); Save(); }
    }

    // ─── Phase 5-8c5：定時 / 持續錄製 ───────────────
    /// <summary>啟用定時錄製（true：到時間自動停止；false：手動停止）</summary>
    private bool _timedRecordingEnabled = false;
    public bool TimedRecordingEnabled
    {
        get => _timedRecordingEnabled;
        set { if (_timedRecordingEnabled == value) return; _timedRecordingEnabled = value;
              PropertyChanged?.Invoke(this, new(nameof(TimedRecordingEnabled))); Save(); }
    }

    /// <summary>定時錄製秒數（10~86400），預設 10 秒</summary>
    private int _recordingDurationSec = 10;
    public int RecordingDurationSec
    {
        get => _recordingDurationSec;
        set
        {
            int v = Math.Max(10, Math.Min(86400, value));
            if (_recordingDurationSec == v) return;
            _recordingDurationSec = v;
            PropertyChanged?.Invoke(this, new(nameof(RecordingDurationSec)));
            Save();
        }
    }

    /// <summary>持續不切檔錄製（勾選時 CSV 切割時間被忽略，整段錄到停止才切檔）</summary>
    private bool _continuousRecording = false;
    public bool ContinuousRecording
    {
        get => _continuousRecording;
        set { if (_continuousRecording == value) return; _continuousRecording = value;
              PropertyChanged?.Invoke(this, new(nameof(ContinuousRecording))); Save(); }
    }

    // ─── Phase 5-8c6：Smart Log 智慧錄製（事件式）─────
    /// <summary>啟用 Smart Log（依振動閾值自動 start/stop）</summary>
    private bool _smartLogEnabled = false;
    public bool SmartLogEnabled
    {
        get => _smartLogEnabled;
        set
        {
            if (_smartLogEnabled == value) return;
            _smartLogEnabled = value;
            PropertyChanged?.Invoke(this, new(nameof(SmartLogEnabled)));
            Save();
            // 關閉時把所有錄製中的 Sensor 都停下
            if (!value)
            {
                try { SmartLogMonitor.Instance.StopAllIfActive(); } catch { }
            }
        }
    }

    /// <summary>啟動閾值（任一軸 Peak 超過此值持續 StartHoldSec 秒 → 開始錄製）</summary>
    private double _smartStartG = 0.05;
    public double SmartStartG
    {
        get => _smartStartG;
        set { if (Math.Abs(_smartStartG - value) < 1e-9) return; _smartStartG = Math.Max(0.001, value);
              PropertyChanged?.Invoke(this, new(nameof(SmartStartG))); Save(); }
    }

    /// <summary>啟動條件持續秒數</summary>
    private double _smartStartHoldSec = 1.0;
    public double SmartStartHoldSec
    {
        get => _smartStartHoldSec;
        set { if (Math.Abs(_smartStartHoldSec - value) < 1e-9) return;
              _smartStartHoldSec = Math.Max(0.1, value);
              PropertyChanged?.Invoke(this, new(nameof(SmartStartHoldSec))); Save(); }
    }

    /// <summary>停止閾值（所有軸 Peak 都低於此值持續 StopHoldSec 秒 → 停止錄製）</summary>
    private double _smartStopG = 0.02;
    public double SmartStopG
    {
        get => _smartStopG;
        set { if (Math.Abs(_smartStopG - value) < 1e-9) return; _smartStopG = Math.Max(0.001, value);
              PropertyChanged?.Invoke(this, new(nameof(SmartStopG))); Save(); }
    }

    /// <summary>停止條件持續秒數</summary>
    private double _smartStopHoldSec = 3.0;
    public double SmartStopHoldSec
    {
        get => _smartStopHoldSec;
        set { if (Math.Abs(_smartStopHoldSec - value) < 1e-9) return;
              _smartStopHoldSec = Math.Max(0.1, value);
              PropertyChanged?.Invoke(this, new(nameof(SmartStopHoldSec))); Save(); }
    }

    /// <summary>最短事件時長（事件低於此值不算合法事件）</summary>
    private double _smartMinRecordSec = 1.0;
    public double SmartMinRecordSec
    {
        get => _smartMinRecordSec;
        set { if (Math.Abs(_smartMinRecordSec - value) < 1e-9) return;
              _smartMinRecordSec = Math.Max(0.1, value);
              PropertyChanged?.Invoke(this, new(nameof(SmartMinRecordSec))); Save(); }
    }

    // ─── Phase 5-8c7：振動統計數據分頁 ─────
    public static int[] StatsWindowSecOptions => new[] { 1, 2, 5, 10, 30, 60, 120, 300 };
    public static int[] StatsOverlapPctOptions => new[] { 0, 15, 30, 50, 75 };

    /// <summary>統計視窗長度（秒），預設 5 秒</summary>
    private int _statsWindowSec = 5;
    public int StatsWindowSec
    {
        get => _statsWindowSec;
        set { if (_statsWindowSec == value) return;
              _statsWindowSec = Math.Max(1, Math.Min(300, value));
              PropertyChanged?.Invoke(this, new(nameof(StatsWindowSec))); Save(); }
    }

    /// <summary>視窗重疊百分比（0~75），預設 30%</summary>
    private int _statsOverlapPct = 30;
    public int StatsOverlapPct
    {
        get => _statsOverlapPct;
        set { if (_statsOverlapPct == value) return;
              _statsOverlapPct = Math.Max(0, Math.Min(75, value));
              PropertyChanged?.Invoke(this, new(nameof(StatsOverlapPct))); Save(); }
    }

    /// <summary>統計 CSV 輸出資料夾</summary>
    private string _statsCsvFolder = "";
    public string StatsCsvFolder
    {
        get => string.IsNullOrEmpty(_statsCsvFolder)
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           "Tranzx PHM", "Stats")
            : _statsCsvFolder;
        set { if (_statsCsvFolder == value) return; _statsCsvFolder = value;
              PropertyChanged?.Invoke(this, new(nameof(StatsCsvFolder))); Save(); }
    }

    // ─── Phase 5-8c8：節能模式（VMS 2.0 風格）─────
    public static int[] PowerSaverIdleMinOptions => new[] { 1, 3, 5, 10, 15, 30, 60 };

    /// <summary>啟用節能模式（無操作 N 分鐘後暫停圖表渲染與計算）</summary>
    private bool _powerSaverEnabled = true;
    public bool PowerSaverEnabled
    {
        get => _powerSaverEnabled;
        set { if (_powerSaverEnabled == value) return; _powerSaverEnabled = value;
              PropertyChanged?.Invoke(this, new(nameof(PowerSaverEnabled))); Save(); }
    }

    /// <summary>節能模式閾值（分鐘）</summary>
    private int _powerSaverIdleMin = 5;
    public int PowerSaverIdleMin
    {
        get => _powerSaverIdleMin;
        set { if (_powerSaverIdleMin == value) return;
              _powerSaverIdleMin = Math.Max(1, Math.Min(120, value));
              PropertyChanged?.Invoke(this, new(nameof(PowerSaverIdleMin))); Save(); }
    }

    /// <summary>5-8c9：把統計每筆寫進 Event Log（預設 ON，可在錄製設定關閉）</summary>
    private bool _statsToEventLog = true;
    public bool StatsToEventLog
    {
        get => _statsToEventLog;
        set { if (_statsToEventLog == value) return; _statsToEventLog = value;
              PropertyChanged?.Invoke(this, new(nameof(StatsToEventLog))); Save(); }
    }

    // ─── Phase 5-8c10：環境警告（溫濕度）─────
    /// <summary>啟用環境警告（溫度 / 濕度三段警報），預設關閉</summary>
    private bool _envWarnEnabled = false;
    public bool EnvWarnEnabled
    {
        get => _envWarnEnabled;
        set { if (_envWarnEnabled == value) return; _envWarnEnabled = value;
              PropertyChanged?.Invoke(this, new(nameof(EnvWarnEnabled))); Save(); }
    }

    /// <summary>溫度黃燈閾值（°C）— 預設 25</summary>
    private double _tempYellow = 25.0;
    public double TempYellow
    {
        get => _tempYellow;
        set { if (Math.Abs(_tempYellow - value) < 1e-9) return; _tempYellow = value;
              PropertyChanged?.Invoke(this, new(nameof(TempYellow))); Save(); }
    }

    /// <summary>溫度紅燈閾值（°C）— 預設 35</summary>
    private double _tempRed = 35.0;
    public double TempRed
    {
        get => _tempRed;
        set { if (Math.Abs(_tempRed - value) < 1e-9) return; _tempRed = value;
              PropertyChanged?.Invoke(this, new(nameof(TempRed))); Save(); }
    }

    /// <summary>濕度黃燈閾值（%）— 預設 55</summary>
    private double _humidYellow = 55.0;
    public double HumidYellow
    {
        get => _humidYellow;
        set { if (Math.Abs(_humidYellow - value) < 1e-9) return; _humidYellow = value;
              PropertyChanged?.Invoke(this, new(nameof(HumidYellow))); Save(); }
    }

    /// <summary>濕度紅燈閾值（%）— 預設 65</summary>
    private double _humidRed = 65.0;
    public double HumidRed
    {
        get => _humidRed;
        set { if (Math.Abs(_humidRed - value) < 1e-9) return; _humidRed = value;
              PropertyChanged?.Invoke(this, new(nameof(HumidRed))); Save(); }
    }

    // ─────────── JSON 持久化 ───────────
    // 設定檔位置：%LocalAppData%\Tranzx.iVS4\settings.json
    // 為避免循環呼叫（每個 setter 都呼叫 Save 又 PropertyChanged），用一個 _suspendSave 旗標 + debounce
    private static readonly string SettingsFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tranzx.iVS4", "settings.json");

    private bool _suspendSave;

    /// <summary>儲存的設定 schema（增減欄位請同步 record，避免破壞向下相容）</summary>
    private record SettingsSnapshot(
        string? AlarmLogFolder = null,
        string? TrendLogFolder = null,
        bool RawDataEnabled = false,
        bool AlarmSoundEnabled = false,
        bool ShowDiagnostics = false,
        int TrendSegmentMinutes = 60,
        int TrendRetentionDays = 90,
        int RawSegmentMinutes = 60,
        int RawRetentionDays = 90,
        bool LogScopeAll = true,
        bool LogVibration = true,
        bool LogTilt = true,
        bool LogEnv = true,
        int ReconnectAttempts = 5,
        int ReconnectIntervalSec = 10,
        bool AlarmToastEnabled = true,
        bool AlarmToastOnYellow = false,
        bool AlarmToastOnRed = true,
        double WaveformYMaxG = 2.0,
        bool TimedRecordingEnabled = false,
        int RecordingDurationSec = 10,
        bool ContinuousRecording = false,
        bool SmartLogEnabled = false,
        double SmartStartG = 0.05,
        double SmartStartHoldSec = 1.0,
        double SmartStopG = 0.02,
        double SmartStopHoldSec = 3.0,
        double SmartMinRecordSec = 1.0,
        int StatsWindowSec = 5,
        int StatsOverlapPct = 30,
        string StatsCsvFolder = "",
        bool PowerSaverEnabled = true,
        int PowerSaverIdleMin = 5,
        bool StatsToEventLog = true,
        bool EnvWarnEnabled = false,
        double TempYellow = 25.0,
        double TempRed = 35.0,
        double HumidYellow = 55.0,
        double HumidRed = 65.0);

    private void Save()
    {
        if (_suspendSave) return;
        try
        {
            var snap = new SettingsSnapshot(
                AlarmLogFolder, TrendLogFolder, RawDataEnabled, AlarmSoundEnabled, ShowDiagnostics,
                TrendSegmentMinutes, TrendRetentionDays, RawSegmentMinutes, RawRetentionDays,
                LogScopeAll, LogVibration, LogTilt, LogEnv,
                ReconnectAttempts, ReconnectIntervalSec,
                AlarmToastEnabled, AlarmToastOnYellow, AlarmToastOnRed, WaveformYMaxG,
                TimedRecordingEnabled, RecordingDurationSec, ContinuousRecording,
                SmartLogEnabled, SmartStartG, SmartStartHoldSec,
                SmartStopG, SmartStopHoldSec, SmartMinRecordSec,
                StatsWindowSec, StatsOverlapPct, _statsCsvFolder ?? "",
                PowerSaverEnabled, PowerSaverIdleMin, StatsToEventLog,
                EnvWarnEnabled, TempYellow, TempRed, HumidYellow, HumidRed);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFilePath)!);
            string json = System.Text.Json.JsonSerializer.Serialize(snap,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings.Save] {ex.Message}");
        }
    }

    /// <summary>啟動時呼叫一次（在 App.OnStartup）</summary>
    public void Load()
    {
        try
        {
            if (!System.IO.File.Exists(SettingsFilePath)) return;
            string json = System.IO.File.ReadAllText(SettingsFilePath);
            var snap = System.Text.Json.JsonSerializer.Deserialize<SettingsSnapshot>(json);
            if (snap is null) return;
            _suspendSave = true;
            try
            {
                // 5-8c10：自動 migrate 舊路徑 Tranzx.iVS4 → Tranzx PHM
                if (!string.IsNullOrEmpty(snap.AlarmLogFolder))
                    AlarmLogFolder = snap.AlarmLogFolder.Replace(@"\Tranzx.iVS4\", @"\Tranzx PHM\");
                if (!string.IsNullOrEmpty(snap.TrendLogFolder))
                    TrendLogFolder = snap.TrendLogFolder.Replace(@"\Tranzx.iVS4\", @"\Tranzx PHM\");
                RawDataEnabled = snap.RawDataEnabled;
                AlarmSoundEnabled = snap.AlarmSoundEnabled;
                ShowDiagnostics = snap.ShowDiagnostics;
                if (snap.TrendSegmentMinutes > 0) TrendSegmentMinutes = snap.TrendSegmentMinutes;
                if (snap.TrendRetentionDays > 0)  TrendRetentionDays  = snap.TrendRetentionDays;
                if (snap.RawSegmentMinutes > 0)   RawSegmentMinutes   = snap.RawSegmentMinutes;
                if (snap.RawRetentionDays > 0)    RawRetentionDays    = snap.RawRetentionDays;
                LogScopeAll = snap.LogScopeAll;
                LogVibration = snap.LogVibration;
                LogTilt = snap.LogTilt;
                LogEnv = snap.LogEnv;
                if (snap.ReconnectAttempts >= 0)    ReconnectAttempts = snap.ReconnectAttempts;
                if (snap.ReconnectIntervalSec > 0)  ReconnectIntervalSec = snap.ReconnectIntervalSec;
                AlarmToastEnabled = snap.AlarmToastEnabled;
                AlarmToastOnYellow = snap.AlarmToastOnYellow;
                AlarmToastOnRed = snap.AlarmToastOnRed;
                if (snap.WaveformYMaxG > 0) WaveformYMaxG = snap.WaveformYMaxG;
                TimedRecordingEnabled = snap.TimedRecordingEnabled;
                if (snap.RecordingDurationSec >= 10) RecordingDurationSec = snap.RecordingDurationSec;
                ContinuousRecording = snap.ContinuousRecording;
                SmartLogEnabled = snap.SmartLogEnabled;
                if (snap.SmartStartG > 0)        SmartStartG = snap.SmartStartG;
                if (snap.SmartStartHoldSec > 0)  SmartStartHoldSec = snap.SmartStartHoldSec;
                if (snap.SmartStopG > 0)         SmartStopG = snap.SmartStopG;
                if (snap.SmartStopHoldSec > 0)   SmartStopHoldSec = snap.SmartStopHoldSec;
                if (snap.SmartMinRecordSec > 0)  SmartMinRecordSec = snap.SmartMinRecordSec;
                if (snap.StatsWindowSec > 0)     StatsWindowSec = snap.StatsWindowSec;
                StatsOverlapPct = snap.StatsOverlapPct;
                if (!string.IsNullOrEmpty(snap.StatsCsvFolder))
                    _statsCsvFolder = snap.StatsCsvFolder.Replace(@"\Tranzx.iVS4\", @"\Tranzx PHM\");
                PowerSaverEnabled = snap.PowerSaverEnabled;
                if (snap.PowerSaverIdleMin > 0) PowerSaverIdleMin = snap.PowerSaverIdleMin;
                StatsToEventLog = snap.StatsToEventLog;
                EnvWarnEnabled = snap.EnvWarnEnabled;
                if (snap.TempYellow != 0)  TempYellow  = snap.TempYellow;
                if (snap.TempRed != 0)     TempRed     = snap.TempRed;
                if (snap.HumidYellow != 0) HumidYellow = snap.HumidYellow;
                if (snap.HumidRed != 0)    HumidRed    = snap.HumidRed;
            }
            finally { _suspendSave = false; }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings.Load] {ex.Message}");
        }
    }

    // ─────────── 圖表 X 軸時間長度（秒）───────────
    private double _vibXSec = 20;
    public double VibXSec { get => _vibXSec; set => SetChart(ref _vibXSec, value, nameof(VibXSec)); }
    private double _tiltXSec = 60;
    public double TiltXSec { get => _tiltXSec; set => SetChart(ref _tiltXSec, value, nameof(TiltXSec)); }
    private double _envXSec = 600;
    public double EnvXSec { get => _envXSec; set => SetChart(ref _envXSec, value, nameof(EnvXSec)); }

    public static double[] VibXSecOptions => new[] { 5.0, 10, 20, 60, 120 };
    public static double[] TiltXSecOptions => new[] { 30.0, 60, 120, 300 };
    public static double[] EnvXSecOptions => new[] { 60.0, 300, 600, 1800, 3600 };

    // ─────────── 圖表 Y 軸範圍 ───────────
    /// <summary>振動 Y 軸上限 (G)。0 = Auto fit。下限永遠 0（取絕對值）</summary>
    private double _vibYMaxG = 0;  // Auto
    public double VibYMaxG { get => _vibYMaxG; set => SetChart(ref _vibYMaxG, value, nameof(VibYMaxG)); }
    public static double[] VibYMaxOptions => new[] { 0.0, 0.5, 1, 2, 5, 16 };  // 0 = Auto

    /// <summary>水平 Y 軸範圍 (°)，例 90 表示 -90~+90</summary>
    private double _tiltYRangeDeg = 90;
    public double TiltYRangeDeg { get => _tiltYRangeDeg; set => SetChart(ref _tiltYRangeDeg, value, nameof(TiltYRangeDeg)); }
    public static double[] TiltYRangeOptions => new[] { 10.0, 30, 90, 180 };

    /// <summary>溫溼度 Y 軸下限 (% / °C)，預設 0</summary>
    private double _envYMin = 0;
    public double EnvYMin { get => _envYMin; set => SetChart(ref _envYMin, value, nameof(EnvYMin)); }
    /// <summary>溫溼度 Y 軸上限 (% / °C)，預設 100</summary>
    private double _envYMax = 100;
    public double EnvYMax { get => _envYMax; set => SetChart(ref _envYMax, value, nameof(EnvYMax)); }

    private void SetChart<T>(ref T field, T value, string name)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
        ChartSettingsChanged?.Invoke();
    }

    // ─────────── 事件 ───────────
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<AppViewMode>? ViewModeChanged;
    public event Action<GravityMode>? GravityModeChanged;
    public event Action? StatisticsSettingsChanged;
    public event Action? ChartSettingsChanged;
    public event Action? TiltLpfChanged;
    public event Action<TiltAngleMode>? TiltAngleModeChanged;
    public event Action? PerformanceSettingsChanged;

    // ─────────── Font scale apply ───────────
    public static double FontSizeFor(FontScale s) => s switch
    {
        FontScale.Small  => 11,
        FontScale.Normal => 13,
        FontScale.Large  => 15,
        FontScale.XLarge => 17,
        _ => 13
    };

    public void ApplyFontScale()
    {
        if (Application.Current is null) return;
        double baseSize = FontSizeFor(_fontScale);
        Application.Current.Resources["RootFontSize"]      = baseSize;
        Application.Current.Resources["LargeFontSize"]     = baseSize + 3;
        Application.Current.Resources["SmallFontSize"]     = Math.Max(9, baseSize - 2);
        Application.Current.Resources["TinyFontSize"]      = Math.Max(8, baseSize - 3);
        Application.Current.Resources["DataValueFontSize"] = baseSize + 6;
    }
}
