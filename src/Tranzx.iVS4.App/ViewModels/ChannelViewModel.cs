// ============================================================================
// Tranzx.iVS4.App / ViewModels / ChannelViewModel.cs
//
// Phase 5-2.1：
//   - 取代 Pitch/Roll/Total，改用「三軸對重力夾角」(定義 A)
//       AngleX = acos(Ax / |g|) [°]，靜置平放時 ≈ 90°
//       AngleY = acos(Ay / |g|) [°]，靜置平放時 ≈ 90°
//       AngleZ = acos(Az / |g|) [°]，靜置平放時 ≈ 0°
//   - DC 算傾角 + 1-pole LPF（時間常數可調）
//       y[k] = α·x[k] + (1-α)·y[k-1]，α = 1 - exp(-Δt/τ)
//       Δt = 1/StatsHz；τ = TiltLpfSec
//   - 歸零 toggle：第一次按歸零、第二次按取消歸零（IsTiltZeroed 旗標）
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Tranzx.iVS4.Analysis;
using Tranzx.iVS4.App.Models;
using Tranzx.iVS4.App.Services;
using Tranzx.iVS4.Communication;
using Tranzx.iVS4.Communication.Transport;
using Tranzx.iVS4.Core.Models;

namespace Tranzx.iVS4.App.ViewModels;

public partial class ChannelViewModel : ObservableObject, IDisposable
{
    public int Index { get; }
    public SensorChannel Channel { get; }

    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private string sensorId = "";
    [ObservableProperty] private string portName = "";
    [ObservableProperty] private TransportState state = TransportState.Disconnected;
    [ObservableProperty] private bool isCalibrated;
    [ObservableProperty] private string calibrationLabel = "";

    // 即時環境
    [ObservableProperty] private double temperatureC;
    [ObservableProperty] private double humidityPercent;

    // 即時 RMS / Peak / P-P
    [ObservableProperty] private double rmsX;
    [ObservableProperty] private double rmsY;
    [ObservableProperty] private double rmsZ;
    [ObservableProperty] private double peakX;
    [ObservableProperty] private double peakY;
    [ObservableProperty] private double peakZ;

    // ❗ Phase 5-8c：Dashboard 顯示用衍生屬性 — 各軸 alarm level 對應的 brush
    public Brush AccentBrush => ChannelColor;
    public Brush XAlarmBrush => LevelBrush(WorstAxisLevel(Thresholds.XPeak.Level(PeakX), Thresholds.XRms.Level(RmsX)));
    public Brush YAlarmBrush => LevelBrush(WorstAxisLevel(Thresholds.YPeak.Level(PeakY), Thresholds.YRms.Level(RmsY)));
    public Brush ZAlarmBrush => LevelBrush(WorstAxisLevel(Thresholds.ZPeak.Level(PeakZ), Thresholds.ZRms.Level(RmsZ)));

    /// <summary>所有量值中最嚴重的等級（用於 dashboard 卡片底部 chip）</summary>
    public AlarmLevel WorstAlarmLevel
    {
        get
        {
            var lv = AlarmLevel.Green;
            void Up(AlarmLevel l) { if (l > lv) lv = l; }
            Up(Thresholds.XPeak.Level(PeakX)); Up(Thresholds.YPeak.Level(PeakY)); Up(Thresholds.ZPeak.Level(PeakZ));
            Up(Thresholds.XRms.Level(RmsX));   Up(Thresholds.YRms.Level(RmsY));   Up(Thresholds.ZRms.Level(RmsZ));
            Up(Thresholds.AngleX.Level(Math.Abs(AngleX))); Up(Thresholds.AngleY.Level(Math.Abs(AngleY))); Up(Thresholds.AngleZ.Level(Math.Abs(AngleZ)));
            return lv;
        }
    }
    public Brush WorstAlarmBrush => LevelBrush(WorstAlarmLevel);
    public string WorstAlarmText => WorstAlarmLevel switch
    {
        AlarmLevel.Red    => "RED",
        AlarmLevel.Yellow => "YELLOW",
        _                 => "OK"
    };

    private static AlarmLevel WorstAxisLevel(AlarmLevel a, AlarmLevel b) => a > b ? a : b;
    private static Brush LevelBrush(AlarmLevel lv) => lv switch
    {
        AlarmLevel.Red    => new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
        AlarmLevel.Yellow => new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
        _                 => new SolidColorBrush(Color.FromRgb(0x1A, 0xBC, 0x9C)),
    };

    // 觸發 dashboard 屬性更新（每次 Peak/RMS/Angle 變動）
    partial void OnPeakXChanged(double value) => RaiseDashboardX();
    partial void OnPeakYChanged(double value) => RaiseDashboardY();
    partial void OnPeakZChanged(double value) => RaiseDashboardZ();
    partial void OnRmsXChanged(double value)  => RaiseDashboardX();
    partial void OnRmsYChanged(double value)  => RaiseDashboardY();
    partial void OnRmsZChanged(double value)  => RaiseDashboardZ();
    partial void OnAngleXChanged(double value) => RaiseWorst();
    partial void OnAngleYChanged(double value) => RaiseWorst();
    partial void OnAngleZChanged(double value) => RaiseWorst();
    private void RaiseDashboardX() { OnPropertyChanged(nameof(XAlarmBrush)); RaiseWorst(); }
    private void RaiseDashboardY() { OnPropertyChanged(nameof(YAlarmBrush)); RaiseWorst(); }
    private void RaiseDashboardZ() { OnPropertyChanged(nameof(ZAlarmBrush)); RaiseWorst(); }
    private void RaiseWorst()
    {
        OnPropertyChanged(nameof(WorstAlarmLevel));
        OnPropertyChanged(nameof(WorstAlarmBrush));
        OnPropertyChanged(nameof(WorstAlarmText));
    }

    [ObservableProperty] private double ppX;
    [ObservableProperty] private double ppY;
    [ObservableProperty] private double ppZ;

    // Crest factor
    [ObservableProperty] private double crestX;
    [ObservableProperty] private double crestY;
    [ObservableProperty] private double crestZ;

    // 三軸對重力夾角（顯示用，已套 LPF + 歸零 offset）
    [ObservableProperty] private double angleX;
    [ObservableProperty] private double angleY;
    [ObservableProperty] private double angleZ;

    /// <summary>歸零是否啟用（true = 顯示相對基準的偏移；false = 顯示絕對角度）</summary>
    [ObservableProperty] private bool isTiltZeroed;

    // 採樣統計
    [ObservableProperty] private double sps;
    [ObservableProperty] private long lostPackets;
    [ObservableProperty] private long validPackets;

    [ObservableProperty] private bool isRecording;

    /// <summary>5-8c10：當前錄製是由 Smart Log 觸發（影響檔案路徑與切檔行為）</summary>
    public bool IsSmartLogRecording { get; set; }

    /// <summary>5-8c10：環境警告 hysteresis state — 避免邊界震盪每包都推</summary>
    private AlarmLevel _lastTempLevel = AlarmLevel.Green;
    private AlarmLevel _lastHumidLevel = AlarmLevel.Green;

    private void CheckEnvWarning()
    {
        var s = AppSettingsService.Instance;
        if (!s.EnvWarnEnabled) return;

        // 溫度
        AlarmLevel tempLv = TemperatureC >= s.TempRed ? AlarmLevel.Red
                          : TemperatureC >= s.TempYellow ? AlarmLevel.Yellow
                          : AlarmLevel.Green;
        if (tempLv != _lastTempLevel)
        {
            // 升級（更嚴重）才推訊息；降級回綠不推
            if ((int)tempLv > (int)_lastTempLevel)
            {
                Services.WarningFeed.Instance.Push(
                    tempLv == AlarmLevel.Red ? Services.FeedKind.Error : Services.FeedKind.Warn,
                    DisplayName,
                    $"🌡 溫度 {TemperatureC:F1}°C → {tempLv}");
            }
            _lastTempLevel = tempLv;
        }

        // 濕度
        AlarmLevel humLv = HumidityPercent >= s.HumidRed ? AlarmLevel.Red
                         : HumidityPercent >= s.HumidYellow ? AlarmLevel.Yellow
                         : AlarmLevel.Green;
        if (humLv != _lastHumidLevel)
        {
            if ((int)humLv > (int)_lastHumidLevel)
            {
                Services.WarningFeed.Instance.Push(
                    humLv == AlarmLevel.Red ? Services.FeedKind.Error : Services.FeedKind.Warn,
                    DisplayName,
                    $"💧 濕度 {HumidityPercent:F0}% → {humLv}");
            }
            _lastHumidLevel = humLv;
        }
    }

    /// <summary>5-8c5：定時錄製用 timer（個別 Sensor）</summary>
    private DispatcherTimer? _recAutoStopTimer;
    private DateTime _recStartTime;

    /// <summary>5-8c5：當前正在「定時模式」錄製中（true 才顯示倒數）</summary>
    [ObservableProperty] private bool isRecordingTimed;

    /// <summary>5-8c5：倒數文字（如「⏱ 24s 停」），錄製結束後清空</summary>
    [ObservableProperty] private string recordingCountdownText = "";

    partial void OnIsRecordingChanged(bool value)
    {
        if (value)
        {
            // FullScale enum 值 = G 數（G2=2, G16=16）；ODR enum 值 = Hz
            int rangeG = (int)Channel.Config.FullScale;
            int freqRange = (int)Channel.Config.Odr;
            // 並行開三個 trend csv，外加可選 raw
            // 5-8c10：Smart Log 觸發時走專屬資料夾 + 強制 1h 切檔
            TrendLogger.Instance.StartRecording(
                Index, DisplayName, rangeG, freqRange, Settings.RawDataEnabled,
                isSmartLog: IsSmartLogRecording);

            // ❗ 5-8c5：定時模式 → 啟動 auto-stop timer
            var s = AppSettingsService.Instance;
            if (s.TimedRecordingEnabled)
            {
                _recStartTime = DateTime.Now;
                IsRecordingTimed = true;
                int total = s.RecordingDurationSec;
                RecordingCountdownText = FormatCountdown(total);
                _recAutoStopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _recAutoStopTimer.Tick += (_, _) =>
                {
                    double elapsed = (DateTime.Now - _recStartTime).TotalSeconds;
                    int remain = total - (int)Math.Floor(elapsed);
                    if (remain < 0) remain = 0;
                    RecordingCountdownText = FormatCountdown(remain);
                    if (elapsed >= total)
                    {
                        _recAutoStopTimer?.Stop();
                        _recAutoStopTimer = null;
                        IsRecording = false;
                        Services.LiveStatusFeed.Instance.Push(
                            Services.FeedKind.Success, DisplayName,
                            string.Format(Loc["Recording.AutoStoppedFmt"], total));
                    }
                };
                _recAutoStopTimer.Start();
                Services.LiveStatusFeed.Instance.Push(
                    Services.FeedKind.Info, DisplayName,
                    string.Format(Loc["Recording.TimedStartFmt"], total));
            }
            else if (s.ContinuousRecording)
            {
                Services.LiveStatusFeed.Instance.Push(
                    Services.FeedKind.Info, DisplayName,
                    Loc["Recording.ContinuousStart"]);
            }
        }
        else
        {
            TrendLogger.Instance.StopRecording(Index);
            _recAutoStopTimer?.Stop();
            _recAutoStopTimer = null;
            IsRecordingTimed = false;
            RecordingCountdownText = "";
            IsSmartLogRecording = false;  // 5-8c10：清除 Smart Log 旗標
        }
    }

    /// <summary>倒數文字 — 大於 60 秒顯示 m:ss，否則 Ns</summary>
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

    // Raw 診斷
    [ObservableProperty] private long rawBytesReceived;
    [ObservableProperty] private string rawHexPreview = "";
    // ❗ 5-8c4 診斷：每包 raw int16 平均 + 當前套用的 ScaleFactor (mG/LSB)
    [ObservableProperty] private double rawAvgX;
    [ObservableProperty] private double rawAvgY;
    [ObservableProperty] private double rawAvgZ;
    [ObservableProperty] private double scaleFactorMgPerLsb;

    public Brush ChannelColor { get; }

    /// <summary>本 Sensor 的所有量值警報閾值（每量值獨立可調）</summary>
    public ChannelAlarmThresholds Thresholds { get; } = new();

    private static LocalizationService Loc => LocalizationService.Instance;
    private static AppSettingsService Settings => AppSettingsService.Instance;

    private readonly DispatcherTimer _statsTimer;
    private readonly DispatcherTimer _diagTimer;

    // LPF 狀態（用於三軸夾角）
    private double _lpfAngleX;
    private double _lpfAngleY;
    private double _lpfAngleZ;
    private bool _lpfPrimed;

    // 歸零 offset（絕對角度 - offset = 顯示值）
    private double _zeroAngleX;
    private double _zeroAngleY;
    private double _zeroAngleZ;

    public ChannelViewModel(int index, SensorChannel channel)
    {
        Index = index;
        Channel = channel;
        DisplayName = channel.Config.DisplayName;
        SensorId = channel.Config.SensorId;
        PortName = channel.Config.PortName ?? channel.Transport.Identifier;

        ChannelColor = index switch
        {
            0 => new SolidColorBrush(Color.FromRgb(0x1A, 0xBC, 0x9C)),
            1 => new SolidColorBrush(Color.FromRgb(0x52, 0x94, 0xE2)),
            2 => new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
            _ => new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6)),
        };

        channel.Calibration.OnCalibrationChanged += _ => RefreshCalibrationLabel();
        channel.OnStateChanged += (_, s) => State = s;
        channel.OnPacketReceived += OnPacket;

        Loc.LanguageChanged += _ =>
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                RefreshCalibrationLabel();
                OnPropertyChanged(nameof(State));
            });
        };

        RefreshCalibrationLabel();

        _statsTimer = new DispatcherTimer();
        ApplyStatsTimerInterval();
        _statsTimer.Tick += (_, _) => ComputeStatistics();
        _statsTimer.Start();

        Settings.StatisticsSettingsChanged += OnStatsSettingsChanged;
        Settings.TiltLpfChanged += OnTiltLpfChanged;
        Settings.TiltAngleModeChanged += OnTiltAngleModeChanged;

        _diagTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _diagTimer.Tick += (_, _) =>
        {
            RawBytesReceived = Channel.RawBytesReceived;
            RawHexPreview = Channel.GetLastBytesHex(16);
            ValidPackets = Channel.Parser.ValidPackets;
            LostPackets = Channel.Parser.LostPackets;
            Sps = Channel.Sps.Current;
        };
        _diagTimer.Start();
    }

    public void Dispose()
    {
        _statsTimer.Stop();
        _diagTimer.Stop();
        Settings.StatisticsSettingsChanged -= OnStatsSettingsChanged;
        Settings.TiltLpfChanged -= OnTiltLpfChanged;
        Settings.TiltAngleModeChanged -= OnTiltAngleModeChanged;
    }

    private void OnStatsSettingsChanged()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(ApplyStatsTimerInterval);
    }

    private void OnTiltLpfChanged()
    {
        // LPF 設定變了 → 強制重新 prime（避免舊 state 殘留導致跳動）
        _lpfPrimed = false;
    }

    private void OnTiltAngleModeChanged(TiltAngleMode m)
    {
        // 模式切換：公式不同，原本的 offset / LPF state 都失效
        _lpfPrimed = false;
        _zeroAngleX = 0;
        _zeroAngleY = 0;
        _zeroAngleZ = 0;
        IsTiltZeroed = false;
    }

    private void ApplyStatsTimerInterval()
    {
        double hz = Settings.StatisticsHz;
        if (hz < 0.1) hz = 0.1;
        _statsTimer.Interval = TimeSpan.FromSeconds(1.0 / hz);
    }

    private void RefreshCalibrationLabel()
    {
        var cal = Channel.Calibration.Loaded;
        IsCalibrated = cal is not null;
        CalibrationLabel = cal is null
            ? Loc["Channel.Uncalibrated"]
            : Loc.Format("Channel.CalibratedFmt", cal.SensorId);
    }

    private void OnPacket(SensorChannel ch, SensorPacket pkt)
    {
        TemperatureC = pkt.Env.TemperatureC;
        HumidityPercent = pkt.Env.HumidityPercent;

        // 5-8c10：環境警告（溫度 / 濕度）— 帶 hysteresis 避免邊界震盪
        CheckEnvWarning();

        // ❗ 5-8c4 診斷：raw int16 + 套用的 ScaleFactor，可驗證裝置回傳是否與 UI 設定一致
        if (pkt.AccSamples.Length > 0)
        {
            long sx = 0, sy = 0, sz = 0;
            for (int i = 0; i < pkt.AccSamples.Length; i++)
            {
                sx += pkt.AccSamples[i].RawX;
                sy += pkt.AccSamples[i].RawY;
                sz += pkt.AccSamples[i].RawZ;
            }
            RawAvgX = (double)sx / pkt.AccSamples.Length;
            RawAvgY = (double)sy / pkt.AccSamples.Length;
            RawAvgZ = (double)sz / pkt.AccSamples.Length;
            ScaleFactorMgPerLsb = ch.Parser.ScaleFactor;
        }

        // ❗ Phase 5-7b：Raw Data 紀錄（每個 sample 一行）
        if (IsRecording && Settings.RawDataEnabled && pkt.AccSamples.Length > 0)
        {
            for (int i = 0; i < pkt.AccSamples.Length; i++)
            {
                var s = pkt.AccSamples[i];
                TrendLogger.Instance.WriteRawSample(Index, s.PcTime, s.X_G, s.Y_G, s.Z_G);
            }
        }
    }

    private void ComputeStatistics()
    {
        if (State != TransportState.Connected) return;

        // 取出計算需要的 snapshot 與設定（在 UI thread 快速完成）
        var ch = Channel;
        double currentSps = ch.Sps.Current > 100 ? ch.Sps.Current : 3332;
        double hz = Settings.StatisticsHz;
        double overlapPct = Settings.StatisticsOverlapPct;
        bool lpfOn = Settings.TiltLpfEnabled;
        double tau = Math.Max(0.05, Settings.TiltLpfSec);
        var angleMode = Settings.TiltAngleMode;

        int n = (int)Math.Ceiling(currentSps / hz * (1.0 + overlapPct / 100.0));
        if (n < 8) n = 8;
        if (n > 4096) n = 4096;

        // ── 重點：Snapshot + 統計運算搬到 ThreadPool，避免阻塞 UI thread ──
        // ring buffer Snapshot 內部有 lock，跨 thread 安全
        Task.Run(() =>
        {
            var (x, y, z, _) = ch.Buffer.Snapshot(n);
            if (x.Length == 0) return;

            var sx = VibrationStats.Compute(x);
            var sy = VibrationStats.Compute(y);
            var sz = VibrationStats.Compute(z);

            // 三軸對重力夾角（依 TiltAngleMode）
            double dcX = sx.Mean, dcY = sy.Mean, dcZ = sz.Mean;
            double mag = Math.Sqrt(dcX * dcX + dcY * dcY + dcZ * dcZ);
            if (mag < 0.05) return;

            double cosX = Clamp(dcX / mag, -1, 1);
            double cosY = Clamp(dcY / mag, -1, 1);
            double cosZ = Clamp(dcZ / mag, -1, 1);
            double rawAngleX, rawAngleY, rawAngleZ;
            if (angleMode == TiltAngleMode.Inclinometer)
            {
                rawAngleX = 90.0 - Math.Acos(cosX) * 180.0 / Math.PI;
                rawAngleY = 90.0 - Math.Acos(cosY) * 180.0 / Math.PI;
                rawAngleZ = 90.0 - Math.Acos(cosZ) * 180.0 / Math.PI;
            }
            else
            {
                rawAngleX = Math.Acos(cosX) * 180.0 / Math.PI;
                rawAngleY = Math.Acos(cosY) * 180.0 / Math.PI;
                rawAngleZ = Math.Acos(cosZ) * 180.0 / Math.PI;
            }

            // LPF 計算（state 也在 background thread 上更新；只有此 timer 會碰，無 race）
            double lpfX, lpfY, lpfZ;
            if (lpfOn)
            {
                double dt = 1.0 / hz;
                double alpha = 1.0 - Math.Exp(-dt / tau);
                if (!_lpfPrimed)
                {
                    _lpfAngleX = rawAngleX;
                    _lpfAngleY = rawAngleY;
                    _lpfAngleZ = rawAngleZ;
                    _lpfPrimed = true;
                }
                else
                {
                    _lpfAngleX += alpha * (rawAngleX - _lpfAngleX);
                    _lpfAngleY += alpha * (rawAngleY - _lpfAngleY);
                    _lpfAngleZ += alpha * (rawAngleZ - _lpfAngleZ);
                }
                lpfX = _lpfAngleX; lpfY = _lpfAngleY; lpfZ = _lpfAngleZ;
            }
            else
            {
                _lpfAngleX = rawAngleX;
                _lpfAngleY = rawAngleY;
                _lpfAngleZ = rawAngleZ;
                _lpfPrimed = true;
                lpfX = rawAngleX; lpfY = rawAngleY; lpfZ = rawAngleZ;
            }

            double zX = _zeroAngleX, zY = _zeroAngleY, zZ = _zeroAngleZ;
            double crestX_ = sx.Rms > 1e-9 ? sx.Peak / sx.Rms : 0;
            double crestY_ = sy.Rms > 1e-9 ? sy.Peak / sy.Rms : 0;
            double crestZ_ = sz.Rms > 1e-9 ? sz.Peak / sz.Rms : 0;

            // 把所有結果一次性 marshal 回 UI thread 更新 ObservableProperty
            // ❗ 用 Background priority：input/render 優先，避免塞爆 dispatcher queue 導致 UI 卡頓
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                (Action)(() =>
                {
                    RmsX = sx.Rms; PeakX = sx.Peak; PpX = sx.PeakToPeak;
                    RmsY = sy.Rms; PeakY = sy.Peak; PpY = sy.PeakToPeak;
                    RmsZ = sz.Rms; PeakZ = sz.Peak; PpZ = sz.PeakToPeak;
                    CrestX = crestX_; CrestY = crestY_; CrestZ = crestZ_;
                    AngleX = lpfX - zX;
                    AngleY = lpfY - zY;
                    AngleZ = lpfZ - zZ;

                    // ❗ Phase 5-6：alarm 等級轉變偵測 + CSV 紀錄
                    AlarmLogger.Instance.CheckChannel(Index, DisplayName, Thresholds,
                        new Dictionary<string, double>
                        {
                            ["XPeak"]  = PeakX,  ["YPeak"]  = PeakY,  ["ZPeak"]  = PeakZ,
                            ["XRms"]   = RmsX,   ["YRms"]   = RmsY,   ["ZRms"]   = RmsZ,
                            ["AngleX"] = AngleX, ["AngleY"] = AngleY, ["AngleZ"] = AngleZ,
                            ["Temp"]   = TemperatureC, ["Hum"] = HumidityPercent,
                        });

                    // ❗ Phase 5-7b：並行寫三個 trend csv（Vib/Tilt/Env 同時）
                    if (IsRecording)
                    {
                        TrendLogger.Instance.WriteVibration(Index,
                            PeakX, PeakY, PeakZ, RmsX, RmsY, RmsZ);
                        TrendLogger.Instance.WriteTilt(Index, AngleX, AngleY, AngleZ);
                        TrendLogger.Instance.WriteEnv(Index, TemperatureC, HumidityPercent);
                    }
                }));
        });
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

    /// <summary>
    /// 切換歸零狀態：
    ///   - 未歸零 → 把當前角度當基準（offset = 當前 LPF 值），IsTiltZeroed = true
    ///   - 已歸零 → 還原為絕對角度（offset = 0），IsTiltZeroed = false
    /// </summary>
    public void ToggleTiltZero()
    {
        if (IsTiltZeroed)
        {
            // 取消歸零
            _zeroAngleX = 0;
            _zeroAngleY = 0;
            _zeroAngleZ = 0;
            IsTiltZeroed = false;
        }
        else
        {
            // 啟用歸零：用當前 LPF 後的角度（不是顯示值，避免疊加）作 offset
            _zeroAngleX = _lpfAngleX;
            _zeroAngleY = _lpfAngleY;
            _zeroAngleZ = _lpfAngleZ;
            IsTiltZeroed = true;
        }
        // 立即更新顯示
        AngleX = _lpfAngleX - _zeroAngleX;
        AngleY = _lpfAngleY - _zeroAngleY;
        AngleZ = _lpfAngleZ - _zeroAngleZ;
    }
}
