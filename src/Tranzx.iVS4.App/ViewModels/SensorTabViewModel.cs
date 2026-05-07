// ============================================================================
// Tranzx.iVS4.App / ViewModels / SensorTabViewModel.cs
//
// Phase 5-2：三模式 sweep 同時在背景累積（依使用者選 Q3 第一項）
//   - 振動 / 水平 / 溫溼度 各自有 _vibT / _tiltT / _envT 狀態
//   - OnRefresh 每次 tick 三個都跑（資料持續累積）
//   - ApplyViewMode 切換時只切 series IsVisible 與軸範圍，不清空資料
//   - 圖表 X/Y 軸大小綁 AppSettingsService（ChartSettingsChanged 事件 → 重套軸）
//   - 水平歸零 toggle Command：按下 Channel.ToggleTiltZero + 清空水平 sweep 從 0 重畫
//
// Phase 5-2.1：水平改三軸對重力夾角（X°/Y°/Z°，定義 A），不再用 Pitch/Roll/Total
// ============================================================================

using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Tranzx.iVS4.App.Services;
using Tranzx.iVS4.Communication;

namespace Tranzx.iVS4.App.ViewModels;

public partial class SensorTabViewModel : ObservableObject, IDisposable
{
    private const double FsLimit = 16.0;
    private const double TiltSampleSec  = 0.5;
    private const double EnvSampleSec   = 1.0;

    public ChannelViewModel Channel { get; }
    public MultiSensorManager Manager { get; }

    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private bool isPausedPlot;
    [ObservableProperty] private bool hpfEnabled;
    [ObservableProperty] private bool unitG = true;
    /// <summary>是否為當前選中的 Sensor Tab（影響背景 Sensor 是否跑 sweep 計算）</summary>
    [ObservableProperty] private bool isActiveTab = true;
    partial void OnIsActiveTabChanged(bool value)
    {
        if (value)
        {
            // 從背景切回前景：重設 sweep 狀態，從 0 重新累積（避免一次跳一堆點）
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ResetSweepForCurrentMode();
                ChartModel.InvalidatePlot(true);
            });
        }
    }

    // ViewMode 旗標（給 UI XAML DataTrigger 用，跟著全域 ViewMode 同步）
    [ObservableProperty] private bool isVibrationMode = true;
    [ObservableProperty] private bool isTiltMode;
    [ObservableProperty] private bool isEnvMode;

    /// <summary>暴露全域 Settings 給 XAML 綁 ShowDiagnostics 等</summary>
    public AppSettingsService AppSettings => AppSettingsService.Instance;
    public string UnitText => UnitG ? "G" : "m/s²";
    partial void OnUnitGChanged(bool value) => OnPropertyChanged(nameof(UnitText));

    public bool IsRecording => Channel.IsRecording;

    public PlotModel ChartModel { get; }
    public IPlotController ChartController { get; }
    private readonly LinearAxis _xAxis, _yAxis;
    private readonly LineSeries _sx, _sy, _sz;          // 0-P trend (實線) — 1.5px
    private readonly LineSeries _sRmsX, _sRmsY, _sRmsZ; // RMS trend (虛線) — 1.2px

    // Phase 5-8：振動 sub-mode 的 series
    private readonly LineSeries _wavX, _wavY, _wavZ;    // Waveform：原始時域波形
    private readonly LineSeries _fftX, _fftY, _fftZ;    // FFT：單邊振幅譜
    private readonly LineSeries _sAngleX, _sAngleY, _sAngleZ;
    private readonly LineSeries _sTemp, _sHum;

    [ObservableProperty] private bool showX = true;
    [ObservableProperty] private bool showY = true;
    [ObservableProperty] private bool showZ = true;
    [ObservableProperty] private bool showAngleX = true;
    [ObservableProperty] private bool showAngleY = true;
    [ObservableProperty] private bool showAngleZ = true;
    [ObservableProperty] private bool showTemp = true;
    [ObservableProperty] private bool showHum = true;
    partial void OnShowXChanged(bool value)      => ApplySeriesVisibility();
    partial void OnShowYChanged(bool value)      => ApplySeriesVisibility();
    partial void OnShowZChanged(bool value)      => ApplySeriesVisibility();
    partial void OnShowAngleXChanged(bool value) => ApplySeriesVisibility();
    partial void OnShowAngleYChanged(bool value) => ApplySeriesVisibility();
    partial void OnShowAngleZChanged(bool value) => ApplySeriesVisibility();
    partial void OnShowTempChanged(bool value)   => ApplySeriesVisibility();
    partial void OnShowHumChanged(bool value)    => ApplySeriesVisibility();

    // 三模式各自的 sweep 狀態
    private double _vibT;
    // (Phase 5-7c：_vibLastVP / _vibStrideCounter 不再使用 — 振動改 stats-based)
    private double _tiltT;
    private DateTime? _tiltLastSample;
    private double _envT;
    private DateTime? _envLastSample;

    private AppViewMode _activeMode = AppViewMode.Vibration;

    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private static LocalizationService Loc => LocalizationService.Instance;
    private static AppSettingsService Settings => AppSettingsService.Instance;

    public event Action<string, object?[]>? StatusRequested;

    /// <summary>顯示在圖表上方的標籤：「Sensor 1 — COM7」等</summary>
    public string SensorHeaderText
    {
        get
        {
            string num = $"Sensor {Channel.Index + 1}";
            string name = string.IsNullOrWhiteSpace(Channel.DisplayName) ? "" : $" — {Channel.DisplayName}";
            string port = string.IsNullOrWhiteSpace(Channel.PortName) ? "" : $" ({Channel.PortName})";
            return num + name + port;
        }
    }

    public SensorTabViewModel(ChannelViewModel ch, MultiSensorManager mgr)
    {
        Channel = ch;
        Manager = mgr;

        // ❗ 5-8c8：訂閱節能模式恢復事件 → 強制重畫一次
        Services.PowerSaverService.Instance.ChartsPausedChanged += OnPowerSaverChanged;

        ch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ch.State))
                IsConnected = ch.State == Communication.Transport.TransportState.Connected;
            if (e.PropertyName == nameof(ch.DisplayName) || e.PropertyName == nameof(ch.PortName))
                OnPropertyChanged(nameof(SensorHeaderText));
        };

        // PlotModel
        ChartModel = new PlotModel
        {
            Background = OxyColors.Transparent,
            TextColor = OxyColors.LightGray,
            PlotAreaBorderColor = OxyColor.FromArgb(40, 200, 200, 220),
            PlotMargins = new OxyThickness(58, 12, 12, 36)
        };

        // 圖表互動控制：左鍵框選 = box zoom，右鍵拖曳 = pan，滾輪 = zoom
        // ❗ Phase 5-8c：Shift+左鍵 = P-P 圈選計算
        var pc = new PlotController();
        pc.UnbindAll();
        pc.BindMouseDown(OxyMouseButton.Left, PlotCommands.ZoomRectangle);
        pc.BindMouseDown(OxyMouseButton.Left, OxyModifierKeys.Shift,
            OxyPlotExt.CustomPlotCommands.SelectPp);
        pc.BindMouseDown(OxyMouseButton.Right, PlotCommands.PanAt);
        pc.BindMouseWheel(PlotCommands.ZoomWheel);
        pc.BindMouseEnter(PlotCommands.HoverPointsOnlyTrack);
        ChartController = pc;

        _xAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            TitleFontSize = 12, FontSize = 11,
            AxislineColor = OxyColors.Gray, TextColor = OxyColors.LightGray,
            MajorGridlineColor = OxyColor.FromArgb(30, 200, 200, 200),
            MajorGridlineStyle = LineStyle.Dot,
            IsZoomEnabled = true, IsPanEnabled = true
        };
        _yAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            TitleFontSize = 12, FontSize = 11,
            AxislineColor = OxyColors.Gray, TextColor = OxyColors.LightGray,
            MajorGridlineColor = OxyColor.FromArgb(30, 200, 200, 200),
            MajorGridlineStyle = LineStyle.Dot,
            IsZoomEnabled = true, IsPanEnabled = true
        };
        ChartModel.Axes.Add(_xAxis); ChartModel.Axes.Add(_yAxis);

        // ❗ Phase 5-2.5：LineSeries 加 Decimator + 抗鋸齒關閉，渲染快數倍
        // Decimator.Decimate 在每個畫面寬度像素只保留 min/max 2 個點，視覺等效但點數大幅減少
        // ❗ Phase 5-5：設定 TrackerFormatString 控制小數位顯示
        // {0}=Title, {2:F2}=X 值（時間秒）, {4:F3}=Y 值
        // 振動 trend 用 vibPeakTracker / vibRmsTracker（下方定義）
        const string angTracker  = "{0}\nTime: {2:F2} s\nAngle: {4:F2}°";
        const string tempTracker = "Temp\nTime: {2:F2} s\n{4:F1} °C";
        const string humTracker  = "Hum\nTime: {2:F2} s\n{4:F0} %";

        const string vibPeakTracker = "{0:l} 0-P\nTime: {2:F2} s\n{4:F3} G";
        const string vibRmsTracker  = "{0:l} RMS\nTime: {2:F2} s\n{4:F3} G";

        _sx = new LineSeries { Title = "X-0P", Color = OxyColor.FromRgb(0xE7, 0x4C, 0x3C),
            StrokeThickness = 1.5, EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = vibPeakTracker };
        _sy = new LineSeries { Title = "Y-0P", Color = OxyColor.FromRgb(0x1A, 0xBC, 0x9C),
            StrokeThickness = 1.5, EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = vibPeakTracker };
        _sz = new LineSeries { Title = "Z-0P", Color = OxyColor.FromRgb(0x52, 0x94, 0xE2),
            StrokeThickness = 1.5, EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = vibPeakTracker };
        _sRmsX = new LineSeries { Title = "X-RMS", Color = OxyColor.FromRgb(0xE7, 0x4C, 0x3C),
            StrokeThickness = 1.2, LineStyle = LineStyle.Dash,
            EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = vibRmsTracker };
        _sRmsY = new LineSeries { Title = "Y-RMS", Color = OxyColor.FromRgb(0x1A, 0xBC, 0x9C),
            StrokeThickness = 1.2, LineStyle = LineStyle.Dash,
            EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = vibRmsTracker };
        _sRmsZ = new LineSeries { Title = "Z-RMS", Color = OxyColor.FromRgb(0x52, 0x94, 0xE2),
            StrokeThickness = 1.2, LineStyle = LineStyle.Dash,
            EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = vibRmsTracker };
        ChartModel.Series.Add(_sx); ChartModel.Series.Add(_sy); ChartModel.Series.Add(_sz);
        ChartModel.Series.Add(_sRmsX); ChartModel.Series.Add(_sRmsY); ChartModel.Series.Add(_sRmsZ);

        // ❗ Phase 5-8：Waveform（原始時域波形，與 trend 同色但更細，stride 後可達 ~1000 點）
        const string wavTracker = "{0:l}\nTime: {2:F3} s\n{4:F3} G";
        _wavX = new LineSeries { Title = "X", Color = OxyColor.FromRgb(0xE7, 0x4C, 0x3C),
            StrokeThickness = 1, Decimator = OxyPlot.Decimator.Decimate,
            EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = wavTracker };
        _wavY = new LineSeries { Title = "Y", Color = OxyColor.FromRgb(0x1A, 0xBC, 0x9C),
            StrokeThickness = 1, Decimator = OxyPlot.Decimator.Decimate,
            EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = wavTracker };
        _wavZ = new LineSeries { Title = "Z", Color = OxyColor.FromRgb(0x52, 0x94, 0xE2),
            StrokeThickness = 1, Decimator = OxyPlot.Decimator.Decimate,
            EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = wavTracker };
        ChartModel.Series.Add(_wavX); ChartModel.Series.Add(_wavY); ChartModel.Series.Add(_wavZ);

        // ❗ Phase 5-8：FFT 單邊振幅譜
        const string fftTracker = "{0:l}\nFreq: {2:F1} Hz\n{4:F4} G";
        _fftX = new LineSeries { Title = "X", Color = OxyColor.FromRgb(0xE7, 0x4C, 0x3C),
            StrokeThickness = 1.2, Decimator = OxyPlot.Decimator.Decimate,
            EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = fftTracker };
        _fftY = new LineSeries { Title = "Y", Color = OxyColor.FromRgb(0x1A, 0xBC, 0x9C),
            StrokeThickness = 1.2, Decimator = OxyPlot.Decimator.Decimate,
            EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = fftTracker };
        _fftZ = new LineSeries { Title = "Z", Color = OxyColor.FromRgb(0x52, 0x94, 0xE2),
            StrokeThickness = 1.2, Decimator = OxyPlot.Decimator.Decimate,
            EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = fftTracker };
        ChartModel.Series.Add(_fftX); ChartModel.Series.Add(_fftY); ChartModel.Series.Add(_fftZ);

        _sAngleX = new LineSeries { Title = "X°", Color = OxyColor.FromRgb(0xE7, 0x4C, 0x3C),
            StrokeThickness = 1.8, EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = angTracker };
        _sAngleY = new LineSeries { Title = "Y°", Color = OxyColor.FromRgb(0x1A, 0xBC, 0x9C),
            StrokeThickness = 1.8, EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = angTracker };
        _sAngleZ = new LineSeries { Title = "Z°", Color = OxyColor.FromRgb(0x52, 0x94, 0xE2),
            StrokeThickness = 1.8, EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = angTracker };
        ChartModel.Series.Add(_sAngleX); ChartModel.Series.Add(_sAngleY); ChartModel.Series.Add(_sAngleZ);

        _sTemp = new LineSeries { Title = "Temp", Color = OxyColor.FromRgb(0xE7, 0x4C, 0x3C),
            StrokeThickness = 1.8, EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = tempTracker };
        _sHum  = new LineSeries { Title = "Hum",  Color = OxyColor.FromRgb(0x52, 0x94, 0xE2),
            StrokeThickness = 1.8, LineStyle = LineStyle.Dash,
            EdgeRenderingMode = EdgeRenderingMode.PreferSpeed,
            TrackerFormatString = humTracker };
        ChartModel.Series.Add(_sTemp); ChartModel.Series.Add(_sHum);

        // 訂閱事件
        Settings.ViewModeChanged += OnGlobalViewModeChanged;
        Settings.GravityModeChanged += OnGravityModeChanged;
        Settings.ChartSettingsChanged += OnChartSettingsChanged;
        Settings.TiltAngleModeChanged += OnTiltAngleModeChanged;
        Settings.VibrationSubModeChanged += OnVibrationSubModeChanged;
        // ❗ Phase 5-7c：監聽 Channel.PeakX 變更 → push 6 條振動 trend series 點
        Channel.PropertyChanged += OnChannelPropertyChanged;

        // 套用初始
        var initMode = Settings.ViewMode;
        _activeMode = initMode;
        IsVibrationMode = initMode == AppViewMode.Vibration;
        IsTiltMode = initMode == AppViewMode.Tilt;
        IsEnvMode = initMode == AppViewMode.Env;
        ApplyAxesForMode(initMode);
        ApplySeriesVisibility();

        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, Settings.ChartRefreshHz))
        };
        _refreshTimer.Tick += OnRefresh;
        _refreshTimer.Start();

        Settings.PerformanceSettingsChanged += OnPerformanceSettingsChanged;
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        Settings.ViewModeChanged -= OnGlobalViewModeChanged;
        Settings.GravityModeChanged -= OnGravityModeChanged;
        Settings.ChartSettingsChanged -= OnChartSettingsChanged;
        Settings.TiltAngleModeChanged -= OnTiltAngleModeChanged;
        Settings.PerformanceSettingsChanged -= OnPerformanceSettingsChanged;
        Channel.Dispose();
    }

    private void OnPerformanceSettingsChanged()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _refreshTimer.Interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, Settings.ChartRefreshHz));
            // 點數上限變了 → 清空從新 stride 重畫（避免新舊點密度不一致）
            ResetSweepForCurrentMode();
            ChartModel.InvalidatePlot(true);
        });
    }

    // ─────────────────────── 事件 ───────────────────────

    private void OnGlobalViewModeChanged(AppViewMode m)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _activeMode = m;
            IsVibrationMode = m == AppViewMode.Vibration;
            IsTiltMode = m == AppViewMode.Tilt;
            IsEnvMode = m == AppViewMode.Env;
            ApplyAxesForMode(m);
            ApplySeriesVisibility();
        });
    }

    private void OnGravityModeChanged(GravityMode m)
    {
        // 振動模式重新累積（DC 處理變了）
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _sx.Points.Clear();    _sy.Points.Clear();    _sz.Points.Clear();
            _sRmsX.Points.Clear(); _sRmsY.Points.Clear(); _sRmsZ.Points.Clear();
            _vibT = 0;
            ChartModel.InvalidatePlot(true);
        });
    }

    private void OnTiltAngleModeChanged(TiltAngleMode m)
    {
        // 角度模式變了：清空水平 sweep + 重套 Y 軸範圍
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _sAngleX.Points.Clear(); _sAngleY.Points.Clear(); _sAngleZ.Points.Clear();
            _tiltT = 0;
            _tiltLastSample = null;
            ApplyAxesForMode(_activeMode);
            ChartModel.InvalidatePlot(true);
        });
    }

    private void OnChartSettingsChanged()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // 設定變了：清空當前模式的點（X 軸長度可能變），重新套軸
            ResetSweepForCurrentMode();
            ApplyAxesForMode(_activeMode);
            ChartModel.InvalidatePlot(true);
        });
    }

    private void ResetSweepForCurrentMode()
    {
        _sx.Points.Clear();    _sy.Points.Clear();    _sz.Points.Clear();
        _sRmsX.Points.Clear(); _sRmsY.Points.Clear(); _sRmsZ.Points.Clear();
        _sAngleX.Points.Clear(); _sAngleY.Points.Clear(); _sAngleZ.Points.Clear();
        _sTemp.Points.Clear(); _sHum.Points.Clear();
        _wavX.Points.Clear(); _wavY.Points.Clear(); _wavZ.Points.Clear();
        _fftX.Points.Clear(); _fftY.Points.Clear(); _fftZ.Points.Clear();
        // 清除 P-P 圈選 annotation（5-8c）
        var stale = ChartModel.Annotations
            .Where(a => Equals(a.Tag, "PP")).ToList();
        foreach (var a in stale) ChartModel.Annotations.Remove(a);
        _vibT = 0;
        _tiltT = 0; _tiltLastSample = null;
        _envT = 0; _envLastSample = null;
    }

    private void ApplyAxesForMode(AppViewMode mode)
    {
        // 重置 ActualMin/Max（清除使用者 box zoom 殘留）
        _xAxis.Reset();
        _yAxis.Reset();
        switch (mode)
        {
            case AppViewMode.Vibration:
                switch (Settings.VibrationSubMode)
                {
                    case VibrationSubMode.Trend:
                        _xAxis.Title = "Time (s)";
                        _yAxis.Title = "Acceleration |G|";
                        _xAxis.Minimum = 0; _xAxis.Maximum = Settings.VibXSec;
                        _xAxis.AbsoluteMinimum = 0; _xAxis.AbsoluteMaximum = Settings.VibXSec;
                        if (Settings.VibYMaxG > 0)
                        {
                            _yAxis.Minimum = 0; _yAxis.Maximum = Settings.VibYMaxG;
                            _yAxis.AbsoluteMinimum = 0; _yAxis.AbsoluteMaximum = FsLimit;
                        }
                        else
                        {
                            _yAxis.Minimum = 0; _yAxis.Maximum = double.NaN;
                            _yAxis.AbsoluteMinimum = 0; _yAxis.AbsoluteMaximum = FsLimit;
                        }
                        _yAxis.MinimumRange = 0.5;
                        break;

                    case VibrationSubMode.Waveform:
                        // 原始波形：Y 軸 ±WaveformYMaxG，X 軸用 WaveformSec（5-8c2 獨立可調）
                        _xAxis.Title = "Time (s)";
                        _yAxis.Title = "Acceleration (G)";
                        _xAxis.Minimum = 0; _xAxis.Maximum = Settings.WaveformSec;
                        _xAxis.AbsoluteMinimum = 0; _xAxis.AbsoluteMaximum = Settings.WaveformSec;
                        double yWavMax = Settings.WaveformYMaxG > 0 ? Settings.WaveformYMaxG : FsLimit;
                        _yAxis.Minimum = -yWavMax; _yAxis.Maximum = yWavMax;
                        _yAxis.AbsoluteMinimum = -FsLimit; _yAxis.AbsoluteMaximum = FsLimit;
                        _yAxis.MinimumRange = 0.5;
                        break;

                    case VibrationSubMode.Fft:
                        // FFT：X 用 FftFreqMax；Y 用 FftYMax (0=auto)
                        _xAxis.Title = "Frequency (Hz)";
                        _yAxis.Title = "Amplitude (G)";
                        _xAxis.Minimum = 0; _xAxis.Maximum = Settings.FftFreqMax;
                        _xAxis.AbsoluteMinimum = 0;
                        _xAxis.AbsoluteMaximum = Settings.FftFreqMax;
                        if (Settings.FftYMax > 0)
                        {
                            _yAxis.Minimum = 0; _yAxis.Maximum = Settings.FftYMax;
                        }
                        else
                        {
                            _yAxis.Minimum = 0; _yAxis.Maximum = double.NaN;  // Auto
                        }
                        _yAxis.AbsoluteMinimum = 0; _yAxis.AbsoluteMaximum = FsLimit;
                        _yAxis.MinimumRange = 0.001;
                        break;
                }
                break;

            case AppViewMode.Tilt:
                _xAxis.Title = "Time (s)";
                _yAxis.Title = "Angle (°)";
                _xAxis.Minimum = 0; _xAxis.Maximum = Settings.TiltXSec;
                _xAxis.AbsoluteMinimum = 0; _xAxis.AbsoluteMaximum = Settings.TiltXSec;
                if (Channel.IsTiltZeroed)
                {
                    // 歸零後顯示偏移：±range
                    _yAxis.Minimum = -Settings.TiltYRangeDeg;
                    _yAxis.Maximum = Settings.TiltYRangeDeg;
                }
                else if (Settings.TiltAngleMode == TiltAngleMode.Inclinometer)
                {
                    // B：水平儀模式，平放 X=Y=0、Z=+90，範圍 -90 ~ +90
                    _yAxis.Minimum = -90;
                    _yAxis.Maximum = 90;
                }
                else
                {
                    // A：向量夾角，平放 X=Y=90、Z=0，範圍 0 ~ 180
                    _yAxis.Minimum = 0;
                    _yAxis.Maximum = 180;
                }
                _yAxis.AbsoluteMinimum = -180; _yAxis.AbsoluteMaximum = 180;
                _yAxis.MinimumRange = 5;
                break;

            case AppViewMode.Env:
                _xAxis.Title = "Time (s)";
                _yAxis.Title = "Temp (°C) / Humidity (%)";
                _xAxis.Minimum = 0; _xAxis.Maximum = Settings.EnvXSec;
                _xAxis.AbsoluteMinimum = 0; _xAxis.AbsoluteMaximum = Settings.EnvXSec;
                _yAxis.Minimum = Settings.EnvYMin; _yAxis.Maximum = Settings.EnvYMax;
                _yAxis.AbsoluteMinimum = -50; _yAxis.AbsoluteMaximum = 200;
                _yAxis.MinimumRange = Math.Max(5, (Settings.EnvYMax - Settings.EnvYMin) * 0.1);
                break;
        }
        ChartModel.InvalidatePlot(true);
    }

    private void ApplySeriesVisibility()
    {
        bool isVib = _activeMode == AppViewMode.Vibration;
        var sub = Settings.VibrationSubMode;
        bool trendOn = isVib && sub == VibrationSubMode.Trend;
        bool wavOn   = isVib && sub == VibrationSubMode.Waveform;
        bool fftOn   = isVib && sub == VibrationSubMode.Fft;

        // Trend (5-7c)：每軸 0-P + RMS
        _sx.IsVisible    = trendOn && ShowX;
        _sRmsX.IsVisible = trendOn && ShowX;
        _sy.IsVisible    = trendOn && ShowY;
        _sRmsY.IsVisible = trendOn && ShowY;
        _sz.IsVisible    = trendOn && ShowZ;
        _sRmsZ.IsVisible = trendOn && ShowZ;

        // Waveform (5-8)
        _wavX.IsVisible = wavOn && ShowX;
        _wavY.IsVisible = wavOn && ShowY;
        _wavZ.IsVisible = wavOn && ShowZ;

        // FFT (5-8)
        _fftX.IsVisible = fftOn && ShowX;
        _fftY.IsVisible = fftOn && ShowY;
        _fftZ.IsVisible = fftOn && ShowZ;

        _sAngleX.IsVisible = _activeMode == AppViewMode.Tilt && ShowAngleX;
        _sAngleY.IsVisible = _activeMode == AppViewMode.Tilt && ShowAngleY;
        _sAngleZ.IsVisible = _activeMode == AppViewMode.Tilt && ShowAngleZ;
        _sTemp.IsVisible   = _activeMode == AppViewMode.Env && ShowTemp;
        _sHum.IsVisible    = _activeMode == AppViewMode.Env && ShowHum;
        ChartModel.InvalidatePlot(false);
    }

    /// <summary>振動 sub-mode 切換 handler</summary>
    private void OnVibrationSubModeChanged(VibrationSubMode m)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // 切 sub-mode：清空 series + 套對應軸
            _wavX.Points.Clear(); _wavY.Points.Clear(); _wavZ.Points.Clear();
            _fftX.Points.Clear(); _fftY.Points.Clear(); _fftZ.Points.Clear();
            ApplyAxesForMode(_activeMode);
            ApplySeriesVisibility();
            ChartModel.InvalidatePlot(true);
        });
    }

    // ─────────────────────── Refresh tick：三模式都跑 ───────────────────────

    private void OnRefresh(object? sender, EventArgs e)
    {
        if (!IsConnected) return;
        if (IsPausedPlot) return;

        // 背景 Sensor Tab：完全跳過 chart sweep 計算（看不到的圖不更新）
        if (!IsActiveTab) return;

        // 依當前 mode + sub-mode 決定要更新哪一組
        switch (_activeMode)
        {
            case AppViewMode.Vibration:
                switch (Settings.VibrationSubMode)
                {
                    // Trend 由 Channel.PropertyChanged push，不在這裡動
                    case VibrationSubMode.Waveform: UpdateWaveform(); break;
                    case VibrationSubMode.Fft:      UpdateFft();      break;
                }
                break;
            case AppViewMode.Tilt: UpdateTiltSweep(); break;
            case AppViewMode.Env:  UpdateEnvSweep();  break;
        }
    }

    /// <summary>
    /// Phase 5-7c：振動趨勢圖改用 stats（每次 Channel.PeakX 更新時 push 6 條 series 的點）
    /// 此函式由 OnRefresh tick 呼叫，但不再做 sample-by-sample；只做 sweep clear 維護。
    /// 真正的 push 由 Channel.PropertyChanged 觸發，避免重複/錯位。
    /// </summary>
    private void UpdateVibrationSweep()
    {
        // 由 Channel.PropertyChanged → OnChannelStatsTick 觸發加點
        // 這裡只保留 sweep clear 的安全網（_vibT 在背景值更新時逐步推進）
        // 為簡化、實際 clear 邏輯也搬到 OnChannelStatsTick 內
    }

    /// <summary>
    /// Phase 5-8：Waveform — 從 ring buffer 取最近 WaveformSec 秒 sample，原始時域波形
    /// 不取 abs，可正可負。每次刷新整段重畫（不做 sweep）。
    /// Phase 5-8b：ring buffer 容量擴大到 ~60s，可顯示長時間波形。
    /// </summary>
    /// <summary>5-8c8：包一層 InvalidatePlot 讓節能模式可以暫停渲染</summary>
    private void InvalidateChart(bool updateData)
    {
        if (Services.PowerSaverService.Instance.ChartsPaused) return;
        ChartModel.InvalidatePlot(updateData);
    }

    /// <summary>5-8c8：節能模式恢復時呼叫一次強制重畫（從 ctor 訂閱）</summary>
    private void OnPowerSaverChanged(bool paused)
    {
        if (!paused)
        {
            try { ChartModel.InvalidatePlot(true); } catch { }
        }
    }

    private void UpdateWaveform()
    {
        var ch = Channel.Channel;
        double sps = ch.Sps.Current > 100 ? ch.Sps.Current : 3332;
        int waveSec = Math.Max(1, Settings.WaveformSec);
        int totalSamples = (int)Math.Ceiling(sps * waveSec);
        if (totalSamples < 16) totalSamples = 16;
        if (totalSamples > ch.Buffer.Capacity) totalSamples = ch.Buffer.Capacity;

        bool removeGravity = Settings.GravityMode == GravityMode.RemoveGravity;
        int maxPts = Settings.ChartMaxPoints;
        int stride = Math.Max(1, totalSamples / maxPts);

        Task.Run(() =>
        {
            var (x, y, z, _) = ch.Buffer.Snapshot(totalSamples);
            if (x.Length == 0) return;

            // 去 DC（簡單平均）
            double dcX = 0, dcY = 0, dcZ = 0;
            if (removeGravity)
            {
                double sxSum = 0, sySum = 0, szSum = 0;
                for (int i = 0; i < x.Length; i++) { sxSum += x[i]; sySum += y[i]; szSum += z[i]; }
                dcX = sxSum / x.Length;
                dcY = sySum / y.Length;
                dcZ = szSum / z.Length;
            }

            double dt = 1.0 / sps;
            int outCap = (x.Length / stride) + 4;
            var ptsX = new List<DataPoint>(outCap);
            var ptsY = new List<DataPoint>(outCap);
            var ptsZ = new List<DataPoint>(outCap);
            for (int k = 0; k < x.Length; k += stride)
            {
                double t = k * dt;
                ptsX.Add(new DataPoint(t, x[k] - dcX));
                ptsY.Add(new DataPoint(t, y[k] - dcY));
                ptsZ.Add(new DataPoint(t, z[k] - dcZ));
            }

            Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                (Action)(() =>
                {
                    _wavX.Points.Clear(); _wavY.Points.Clear(); _wavZ.Points.Clear();
                    _wavX.Points.AddRange(ptsX);
                    _wavY.Points.AddRange(ptsY);
                    _wavZ.Points.AddRange(ptsZ);
                    if (_activeMode == AppViewMode.Vibration
                        && Settings.VibrationSubMode == VibrationSubMode.Waveform)
                    {
                        ChartModel.InvalidatePlot(true);
                    }
                }));
        });
    }

    /// <summary>
    /// Phase 5-8：FFT — 從 ring buffer 取最近 FftN 個 sample，算單邊振幅譜
    /// Phase 5-8b：可選 N (1024/2048/4096/8192) + 窗函數
    /// </summary>
    private void UpdateFft()
    {
        var ch = Channel.Channel;
        double sps = ch.Sps.Current > 100 ? ch.Sps.Current : 3332;
        int fftN = Settings.FftN;
        var win = Settings.FftWindow;

        Task.Run(() =>
        {
            var (x, y, z, _) = ch.Buffer.Snapshot(fftN);
            if (x.Length < 64) return;  // 太少不算

            // 去 DC
            double dcX = 0, dcY = 0, dcZ = 0;
            for (int i = 0; i < x.Length; i++) { dcX += x[i]; dcY += y[i]; dcZ += z[i]; }
            dcX /= x.Length; dcY /= x.Length; dcZ /= x.Length;
            var xx = new double[x.Length];
            var yy = new double[x.Length];
            var zz = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                xx[i] = x[i] - dcX;
                yy[i] = y[i] - dcY;
                zz[i] = z[i] - dcZ;
            }

            var (freq, ampX) = Tranzx.iVS4.Analysis.FftAnalyzer.ComputeAmplitudeSpectrum(xx, sps, win);
            var (_,    ampY) = Tranzx.iVS4.Analysis.FftAnalyzer.ComputeAmplitudeSpectrum(yy, sps, win);
            var (_,    ampZ) = Tranzx.iVS4.Analysis.FftAnalyzer.ComputeAmplitudeSpectrum(zz, sps, win);
            if (freq.Length == 0) return;

            // 只保留 freqMax 範圍內的點，避免畫超出（雖然軸已經限制）
            int freqMax = Settings.FftFreqMax;
            int kMax = freq.Length;
            for (int k = 0; k < freq.Length; k++)
            {
                if (freq[k] > freqMax) { kMax = k; break; }
            }

            int maxPts = Settings.ChartMaxPoints;
            int stride = Math.Max(1, kMax / maxPts);

            int outCap = kMax / stride + 4;
            var ptsX = new List<DataPoint>(outCap);
            var ptsY = new List<DataPoint>(outCap);
            var ptsZ = new List<DataPoint>(outCap);
            // 跳過 DC bin (k=0)
            for (int k = 1; k < kMax; k += stride)
            {
                ptsX.Add(new DataPoint(freq[k], ampX[k]));
                ptsY.Add(new DataPoint(freq[k], ampY[k]));
                ptsZ.Add(new DataPoint(freq[k], ampZ[k]));
            }

            Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                (Action)(() =>
                {
                    _fftX.Points.Clear(); _fftY.Points.Clear(); _fftZ.Points.Clear();
                    _fftX.Points.AddRange(ptsX);
                    _fftY.Points.AddRange(ptsY);
                    _fftZ.Points.AddRange(ptsZ);
                    if (_activeMode == AppViewMode.Vibration
                        && Settings.VibrationSubMode == VibrationSubMode.Fft)
                    {
                        ChartModel.InvalidatePlot(true);
                    }
                }));
        });
    }

    /// <summary>
    /// 監聽 Channel.PeakX 變更（每個 stats tick 會同時更新 6 個值，用 PeakX 作觸發代表）
    /// 每次 push 一個時間點到 6 條 series（X/Y/Z × 0-P/RMS），sweep 滿了清空從 0 重新累積。
    /// </summary>
    private void OnChannelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChannelViewModel.PeakX)) return;
        if (!IsConnected || IsPausedPlot) return;
        if (!IsActiveTab) return;  // 背景 tab 不畫
        if (_activeMode != AppViewMode.Vibration) return;

        double window = Settings.VibXSec;
        double dt = 1.0 / Math.Max(1, Settings.StatisticsHz);

        if (_vibT >= window)
        {
            _sx.Points.Clear();    _sy.Points.Clear();    _sz.Points.Clear();
            _sRmsX.Points.Clear(); _sRmsY.Points.Clear(); _sRmsZ.Points.Clear();
            _vibT = 0;
        }

        double t = _vibT;
        _sx.Points.Add(new DataPoint(t, Channel.PeakX));
        _sy.Points.Add(new DataPoint(t, Channel.PeakY));
        _sz.Points.Add(new DataPoint(t, Channel.PeakZ));
        _sRmsX.Points.Add(new DataPoint(t, Channel.RmsX));
        _sRmsY.Points.Add(new DataPoint(t, Channel.RmsY));
        _sRmsZ.Points.Add(new DataPoint(t, Channel.RmsZ));
        _vibT = t + dt;

        ChartModel.InvalidatePlot(false);
    }

    private void UpdateTiltSweep()
    {
        var now = DateTime.Now;
        if (_tiltLastSample is null) _tiltLastSample = now;
        double elapsed = (now - _tiltLastSample.Value).TotalSeconds;
        if (elapsed < TiltSampleSec) return;
        _tiltLastSample = now;

        double window = Settings.TiltXSec;
        if (_tiltT >= window)
        {
            _sAngleX.Points.Clear(); _sAngleY.Points.Clear(); _sAngleZ.Points.Clear();
            _tiltT = 0;
        }
        _sAngleX.Points.Add(new DataPoint(_tiltT, Channel.AngleX));
        _sAngleY.Points.Add(new DataPoint(_tiltT, Channel.AngleY));
        _sAngleZ.Points.Add(new DataPoint(_tiltT, Channel.AngleZ));
        _tiltT += elapsed;

        if (_activeMode == AppViewMode.Tilt)
            ChartModel.InvalidatePlot(true);
    }

    private void UpdateEnvSweep()
    {
        var now = DateTime.Now;
        if (_envLastSample is null) _envLastSample = now;
        double elapsed = (now - _envLastSample.Value).TotalSeconds;
        if (elapsed < EnvSampleSec) return;
        _envLastSample = now;

        double window = Settings.EnvXSec;
        if (_envT >= window)
        {
            _sTemp.Points.Clear(); _sHum.Points.Clear();
            _envT = 0;
        }
        _sTemp.Points.Add(new DataPoint(_envT, Channel.TemperatureC));
        _sHum.Points.Add(new DataPoint(_envT, Channel.HumidityPercent));
        _envT += elapsed;

        if (_activeMode == AppViewMode.Env)
            ChartModel.InvalidatePlot(true);
    }

    // ─────────────────────── Commands ───────────────────────

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            if (IsConnected)
            {
                // ❗ Phase 5-8c2：使用者主動斷線 → 取消重連
                Services.ReconnectService.Instance.NotifyUserDisconnect(Channel.Index);
                await Channel.Channel.DisconnectAsync();
                IsConnected = false;
                StatusRequested?.Invoke("Status.SensorDisconnectedFmt", new object?[] { Channel.DisplayName });
            }
            else
            {
                bool ok = await Channel.Channel.ConnectAsync();
                if (ok)
                {
                    await Channel.Channel.ApplyConfigAsync(verify: false);
                    IsConnected = true;
                    IsPausedPlot = false;
                    // ❗ Phase 5-8c2：標記為使用者主動連線 → 啟用斷線後自動重連
                    Services.ReconnectService.Instance.NotifyUserConnect(Channel.Index);
                    // 連線成功 → 重置三模式 sweep 狀態，從 0 開始累積
                    ResetSweepForCurrentMode();
                    ChartModel.InvalidatePlot(true);
                    StatusRequested?.Invoke("Status.SensorConnectedFmt", new object?[] { Channel.DisplayName });
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("Error.ConnectFailFmt", ex.Message),
                Loc["Error.Title"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand] private void Start() => IsPausedPlot = false;
    [RelayCommand] private void Stop() => IsPausedPlot = true;
    [RelayCommand] private void ToggleMeasure() => IsPausedPlot = !IsPausedPlot;
    [RelayCommand] private void ToggleUnit() => UnitG = !UnitG;

    /// <summary>還原縮放（取消使用者 box zoom）</summary>
    [RelayCommand]
    private void ResetZoom()
    {
        ApplyAxesForMode(_activeMode);
        ChartModel.InvalidatePlot(false);
    }

    /// <summary>重新刷新圖表（清空 sweep 從 0 重畫）</summary>
    [RelayCommand]
    private void RefreshChart()
    {
        ResetSweepForCurrentMode();
        ApplyAxesForMode(_activeMode);
        ChartModel.InvalidatePlot(true);
    }

    /// <summary>
    /// 水平歸零 toggle：
    ///   - 第一次按 → 把當前 X/Y/Z 角度當成 0° 基準
    ///   - 第二次按 → 取消歸零，恢復絕對角度
    /// </summary>
    [RelayCommand]
    private void TiltZero()
    {
        Channel.ToggleTiltZero();
        // 歸零狀態變了 → Y 軸範圍切換（歸零後 ±range，未歸零 0~180）
        ApplyAxesForMode(_activeMode);
        // 清空水平 sweep 從 0 重畫
        _sAngleX.Points.Clear(); _sAngleY.Points.Clear(); _sAngleZ.Points.Clear();
        _tiltT = 0;
        _tiltLastSample = null;
        ChartModel.InvalidatePlot(true);
        StatusRequested?.Invoke(
            Channel.IsTiltZeroed ? "Status.TiltZeroedFmt" : "Status.TiltZeroResetFmt",
            new object?[] { Channel.DisplayName });
    }

    [RelayCommand]
    private void OpenAlarmDialog(string key)
    {
        var dlg = new Views.AlarmThresholdDialog(Channel, key)
        {
            Owner = Application.Current.MainWindow
        };
        dlg.ShowDialog();
    }

    /// <summary>暴露 AlarmLogger 給 XAML 綁定今日計數</summary>
    public AlarmLogger AlarmLogger => AlarmLogger.Instance;

    /// <summary>清除今日警報計數（5-8c4：手動 reset）</summary>
    [RelayCommand]
    private void ResetAlarmCount()
    {
        AlarmLogger.Instance.ResetTodayCounters();
        Services.LiveStatusFeed.Instance.Push(
            Services.FeedKind.Info,
            Channel.DisplayName,
            LocalizationService.Instance["Alarm.ResetDone"]);
    }

    /// <summary>瀏覽 alarm 紀錄資料夾（換 folder）</summary>
    [RelayCommand]
    private void BrowseAlarmFolder()
    {
        // WPF 沒原生 folder picker，用 OpenFileDialog 變通：選資料夾內任一檔案，取它的目錄
        // 或用 WindowsAPICodePack — 為避免新增依賴，我用 Microsoft.Win32 + ValidateNames=false 的 hack
        // 最乾淨：直接用 System.Windows.Forms.FolderBrowserDialog（已參考 WindowsBase，不需額外）
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = LocalizationService.Instance["Alarm.LogFolder"],
            InitialDirectory = AppSettingsService.Instance.AlarmLogFolder
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.FolderName))
        {
            AppSettingsService.Instance.AlarmLogFolder = dlg.FolderName;
        }
    }

    /// <summary>用檔案總管打開 alarm 紀錄資料夾</summary>
    [RelayCommand]
    private void OpenAlarmFolder()
    {
        try
        {
            var folder = AppSettingsService.Instance.AlarmLogFolder;
            if (string.IsNullOrEmpty(folder)) return;
            System.IO.Directory.CreateDirectory(folder);  // 確保存在
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenAlarmFolder] {ex.Message}");
        }
    }

    /// <summary>瀏覽 trend 紀錄資料夾</summary>
    [RelayCommand]
    private void BrowseTrendFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = LocalizationService.Instance["Trend.LogFolder"],
            InitialDirectory = AppSettingsService.Instance.TrendLogFolder
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.FolderName))
        {
            AppSettingsService.Instance.TrendLogFolder = dlg.FolderName;
        }
    }

    /// <summary>用檔案總管打開 trend 紀錄資料夾</summary>
    [RelayCommand]
    private void OpenTrendFolder()
    {
        try
        {
            var folder = AppSettingsService.Instance.TrendLogFolder;
            if (string.IsNullOrEmpty(folder)) return;
            System.IO.Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenTrendFolder] {ex.Message}");
        }
    }

    /// <summary>開啟錄製設定對話框（資料夾 / 切割時間 / 保留天數）</summary>
    [RelayCommand]
    private void OpenRecordingSettings()
    {
        var dlg = new Views.RecordingSettingsDialog
        {
            Owner = Application.Current.MainWindow
        };
        dlg.ShowDialog();
    }

    [RelayCommand]
    private void OpenChartSettings()
    {
        // ❗ Phase 5-8b：振動 mode 開「振動量測設定」對話框（含 trend / waveform / FFT）
        // 其他 mode 仍開原本的圖表 X/Y 設定
        if (AppSettingsService.Instance.ViewMode == AppViewMode.Vibration)
        {
            var dlg = new Views.VibrationMeasurementSettingsDialog
            {
                Owner = Application.Current.MainWindow
            };
            dlg.ShowDialog();
        }
        else
        {
            var dlg = new Views.ChartSettingsDialog { Owner = Application.Current.MainWindow };
            dlg.ShowDialog();
        }
    }

    [RelayCommand]
    private void OpenTimeDomainSettings()
    {
        var dlg = new Views.TimeDomainSettingsDialog { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    }

    [RelayCommand]
    private void LoadCalibration()
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc["Channel.LoadCalibration.Tip"] + " — " + Channel.DisplayName,
            Filter = "Calibration|*.tzcal;*.sr|TZ Calibration|*.tzcal|SR Format|*.sr|All|*.*",
            CheckFileExists = true,
            InitialDirectory = Manager.CalibrationStore.RootFolder
        };
        if (dlg.ShowDialog() == true)
        {
            var cal = Manager.CalibrationStore.LoadFromPath(dlg.FileName);
            if (cal is null)
            {
                MessageBox.Show(Loc.Format("Error.LoadCalibrationFailFmt", dlg.FileName),
                    Loc["Error.Title"], MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            Channel.Channel.Calibration.Load(cal);
            StatusRequested?.Invoke("Status.CalibrationLoadedFmt",
                new object?[] { Channel.DisplayName, cal.SensorId });
        }
    }

    [RelayCommand]
    private void OpenCsvFolder()
    {
        try
        {
            var root = Path.Combine(AppContext.BaseDirectory, "Records");
            if (Directory.Exists(root))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = root,
                    UseShellExecute = true
                });
        }
        catch { }
    }
}
