// ============================================================================
// Tranzx.iVS4.App / Views / VibrationStatsWindow.xaml.cs
//
// Phase 5-8c7：振動統計數據分頁
//   - 每 (WindowSec × (1-Overlap)) 秒對每個 Sensor 三軸計算一次擴充統計
//   - DataGrid 顯示最近 1000 筆（滾動）
//   - 自動寫 CSV 到指定資料夾（每天一個檔）
//   - 用 RingBuffer.Snapshot 抓最近 N 秒原始資料，不影響原 UI 統計
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using Tranzx.iVS4.Analysis;
using Tranzx.iVS4.App.Services;
using Tranzx.iVS4.App.ViewModels;

namespace Tranzx.iVS4.App.Views;

public partial class VibrationStatsWindow : Window
{
    private readonly System.Collections.ObjectModel.ObservableCollection<SensorTabViewModel> _tabs;
    private readonly ObservableCollection<StatRow> _rows = new();
    private readonly DispatcherTimer _timer = new();
    private bool _running;
    private DateTime _nextTickAt;
    private int _writtenCount;
    private const int MaxRowsKept = 500;

    // 5-8c8：趨勢圖表
    private readonly PlotModel _chartModel = new();
    private readonly Dictionary<(int sensorIdx, string axis), LineSeries> _seriesMap = new();
    private string _chartMetric = "RMS";
    private int _chartMaxPoints = 200;
    private DateTimeAxis? _xAxis;
    // 手動軸範圍（false = 自動）
    private bool _xAxisManual;
    private bool _yAxisManual;

    public VibrationStatsWindow(System.Collections.ObjectModel.ObservableCollection<SensorTabViewModel> tabs)
    {
        InitializeComponent();
        _tabs = tabs;
        dgStats.ItemsSource = _rows;

        var s = AppSettingsService.Instance;
        cmbWindowSec.ItemsSource = AppSettingsService.StatsWindowSecOptions;
        cmbWindowSec.SelectedItem = AppSettingsService.StatsWindowSecOptions
            .Contains(s.StatsWindowSec) ? s.StatsWindowSec : 5;
        cmbOverlap.ItemsSource = AppSettingsService.StatsOverlapPctOptions;
        cmbOverlap.SelectedItem = AppSettingsService.StatsOverlapPctOptions
            .Contains(s.StatsOverlapPct) ? s.StatsOverlapPct : 30;

        cmbWindowSec.SelectionChanged += (_, _) =>
        {
            if (cmbWindowSec.SelectedItem is int v) AppSettingsService.Instance.StatsWindowSec = v;
            RescheduleNextTick();
        };
        cmbOverlap.SelectionChanged += (_, _) =>
        {
            if (cmbOverlap.SelectedItem is int v) AppSettingsService.Instance.StatsOverlapPct = v;
            RescheduleNextTick();
        };

        UpdateFolderLabel();
        InitChart();

        _timer.Interval = TimeSpan.FromMilliseconds(250); // 顯示倒數用
        _timer.Tick += OnTimerTick;

        Closed += (_, _) => Stop();
    }

    private void InitChart()
    {
        _chartModel.Background = OxyColors.Transparent;
        _chartModel.TextColor = OxyColor.FromRgb(0xC0, 0xC0, 0xCF);
        _chartModel.PlotAreaBorderColor = OxyColor.FromArgb(50, 200, 200, 220);
        _chartModel.PlotMargins = new OxyThickness(58, 12, 12, 36);

        _xAxis = new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            StringFormat = "HH:mm:ss",
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(50, 200, 200, 220),
            TextColor = OxyColor.FromRgb(0xA0, 0xA0, 0xB8),
        };
        _chartModel.Axes.Add(_xAxis);
        _chartModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(50, 200, 200, 220),
            TextColor = OxyColor.FromRgb(0xA0, 0xA0, 0xB8),
            Title = "RMS (G)",
            TitleColor = OxyColor.FromRgb(0xA0, 0xA0, 0xB8),
            TitleFontSize = 11,
            Minimum = 0,
        });
        var legend = new Legend
        {
            LegendPosition = LegendPosition.TopRight,
            LegendBackground = OxyColor.FromArgb(180, 30, 30, 46),
            LegendBorder = OxyColor.FromArgb(80, 200, 200, 220),
            LegendTextColor = OxyColor.FromRgb(0xC0, 0xC0, 0xCF),
            LegendFontSize = 10,
        };
        _chartModel.Legends.Add(legend);
        trendChart.Model = _chartModel;

        // metric ComboBox
        var metrics = new[] { "RMS", "Peak", "P-P", "Mean", "Median", "StdDev", "Crest", "Min", "Max" };
        cmbChartMetric.ItemsSource = metrics;
        cmbChartMetric.SelectedItem = "RMS";

        cmbChartMaxPoints.ItemsSource = new[] { 50, 100, 200, 500, 1000 };
        cmbChartMaxPoints.SelectedItem = 200;

        // X 時間範圍：自動 / 30s / 1m / 5m / 15m / 1h
        cmbXSpan.ItemsSource = new[] { "Auto", "30s", "1m", "5m", "15m", "1h" };
        cmbXSpan.SelectedItem = "Auto";
    }

    private void OnApplyAxisClick(object sender, RoutedEventArgs e)
    {
        var inv = CultureInfo.InvariantCulture;
        if (double.TryParse(txtYMin.Text, NumberStyles.Float, inv, out double ymin)
            && double.TryParse(txtYMax.Text, NumberStyles.Float, inv, out double ymax)
            && ymax > ymin)
        {
            if (_chartModel.Axes.Count >= 2 && _chartModel.Axes[1] is LinearAxis ya)
            {
                ya.Minimum = ymin;
                ya.Maximum = ymax;
                _yAxisManual = true;
                _chartModel.InvalidatePlot(false);
            }
        }
    }

    private void OnAutoAxisClick(object sender, RoutedEventArgs e)
    {
        _yAxisManual = false;
        if (_chartModel.Axes.Count >= 2 && _chartModel.Axes[1] is LinearAxis ya)
        {
            ya.Minimum = 0;          // 維持從 0 起算（振動量不為負）
            ya.Maximum = double.NaN;
            ya.Reset();
        }
        _chartModel.InvalidatePlot(true);
    }

    private void OnAxisRangeKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) OnApplyAxisClick(sender, new RoutedEventArgs());
    }

    private void OnXSpanChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (cmbXSpan.SelectedItem is not string sel || _xAxis is null) return;
        if (sel == "Auto")
        {
            _xAxisManual = false;
            _xAxis.Minimum = double.NaN;
            _xAxis.Maximum = double.NaN;
            _xAxis.Reset();
        }
        else
        {
            int sec = sel switch
            {
                "30s" => 30, "1m" => 60, "5m" => 300, "15m" => 900, "1h" => 3600,
                _ => 60
            };
            _xAxisManual = true;
            var now = DateTime.Now;
            _xAxis.Minimum = DateTimeAxis.ToDouble(now.AddSeconds(-sec));
            _xAxis.Maximum = DateTimeAxis.ToDouble(now);
        }
        _chartModel.InvalidatePlot(false);
    }

    private LineSeries GetOrCreateSeries(int sensorIdx, string sensorName, string axis)
    {
        var key = (sensorIdx, axis);
        if (_seriesMap.TryGetValue(key, out var series)) return series;

        // 通道色：Sensor1=Teal, Sensor2=Blue, Sensor3=Amber, Sensor4=Purple
        // 軸色微調：X 偏紅、Y 中色、Z 偏藍。但 sensor 之間用不同色系區隔，所以混合策略：
        //   sensor 主色 + 軸虛線樣式
        OxyColor sensorColor = sensorIdx switch
        {
            0 => OxyColor.FromRgb(0x1A, 0xBC, 0x9C),  // Teal
            1 => OxyColor.FromRgb(0x52, 0x94, 0xE2),  // Blue
            2 => OxyColor.FromRgb(0xF3, 0x9C, 0x12),  // Amber
            3 => OxyColor.FromRgb(0x9B, 0x59, 0xB6),  // Purple
            _ => OxyColor.FromRgb(0xE7, 0x4C, 0x3C),  // Red
        };
        // X / Y / Z 用不同 dash pattern + brightness
        LineStyle ls = axis switch { "X" => LineStyle.Solid, "Y" => LineStyle.Dash, _ => LineStyle.Dot };

        series = new LineSeries
        {
            Title = $"{sensorName} {axis}",
            Color = sensorColor,
            StrokeThickness = 1.6,
            LineStyle = ls,
            MarkerType = MarkerType.None,
            CanTrackerInterpolatePoints = false,
        };
        _seriesMap[key] = series;
        _chartModel.Series.Add(series);
        return series;
    }

    private double ExtractMetric(StatRow row)
    {
        return _chartMetric switch
        {
            "RMS" => row.Rms,
            "Peak" => row.Peak,
            "P-P" => row.Pp,
            "Mean" => row.Mean,
            "Median" => row.Median,
            "StdDev" => row.StdDev,
            "Crest" => row.Crest,
            "Min" => row.Min,
            "Max" => row.Max,
            _ => row.Rms,
        };
    }

    private int FindSensorIdx(string sensorName)
    {
        for (int i = 0; i < _tabs.Count; i++)
            if (_tabs[i].Channel.DisplayName == sensorName) return i;
        return 0;
    }

    private void PushPoint(StatRow row)
    {
        // 軸開關
        bool show = row.Axis switch
        {
            "X" => cbAxisX.IsChecked == true,
            "Y" => cbAxisY.IsChecked == true,
            "Z" => cbAxisZ.IsChecked == true,
            _ => true,
        };
        if (!show) return;

        int idx = FindSensorIdx(row.SensorName);
        var series = GetOrCreateSeries(idx, row.SensorName, row.Axis);
        double y = ExtractMetric(row);
        series.Points.Add(new DataPoint(DateTimeAxis.ToDouble(row.Time), y));
        // FIFO
        while (series.Points.Count > _chartMaxPoints) series.Points.RemoveAt(0);
    }

    private void RebuildChart()
    {
        // 清空所有 series + 重建
        foreach (var s in _seriesMap.Values) s.Points.Clear();
        // 從 _rows 倒序取最近 _chartMaxPoints × 軸數
        // 每個 (sensor, axis) 各自 FIFO，但簡單做：把所有 row 按時間正向 push
        var ordered = _rows.OrderBy(r => r.Time).ToList();
        foreach (var r in ordered) PushPoint(r);

        // 更新 Y 軸 Title + 強制重設範圍讓 OxyPlot 自動套到新 metric
        if (_chartModel.Axes.Count >= 2 && _chartModel.Axes[1] is LinearAxis ya)
        {
            ya.Title = $"{_chartMetric} (G)";
            // 沒手動指定範圍時 → reset 讓自動縮放
            if (!_yAxisManual) ya.Reset();
        }
        if (_xAxis is not null && !_xAxisManual) _xAxis.Reset();

        _chartModel.InvalidatePlot(true);
    }

    private void OnChartMetricChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (cmbChartMetric.SelectedItem is string m)
        {
            _chartMetric = m;
            RebuildChart();
        }
    }

    private void OnChartMaxPointsChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (cmbChartMaxPoints.SelectedItem is int n)
        {
            _chartMaxPoints = n;
            RebuildChart();
        }
    }

    private void OnAxisToggle(object sender, RoutedEventArgs e)
    {
        RebuildChart();
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (_running) Stop();
        else Start();
    }

    private void Start()
    {
        if (_running) return;
        _running = true;
        _writtenCount = 0;
        btnToggle.Content = LocalizationService.Instance["StatsWin.Stop"];
        btnToggle.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
        RescheduleNextTick();
        _timer.Start();
        SetStatusText(string.Format(
            LocalizationService.Instance["StatsWin.RunningFmt"],
            AppSettingsService.Instance.StatsWindowSec,
            AppSettingsService.Instance.StatsOverlapPct));
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        _timer.Stop();
        btnToggle.Content = LocalizationService.Instance["StatsWin.Start"];
        btnToggle.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x1A, 0xBC, 0x9C));
        lblNextIn.Text = "";
        SetStatusText(string.Format(
            LocalizationService.Instance["StatsWin.StoppedFmt"], _writtenCount));
    }

    private void RescheduleNextTick()
    {
        var s = AppSettingsService.Instance;
        double advanceSec = s.StatsWindowSec * (1.0 - s.StatsOverlapPct / 100.0);
        if (advanceSec < 0.1) advanceSec = 0.1;
        _nextTickAt = DateTime.Now.AddSeconds(advanceSec);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_running) return;
        var now = DateTime.Now;
        var remain = _nextTickAt - now;
        if (remain.TotalMilliseconds > 0)
        {
            lblNextIn.Text = string.Format(
                LocalizationService.Instance["StatsWin.NextInFmt"],
                remain.TotalSeconds);
            return;
        }
        // 觸發計算
        ComputeAllSensors(now);
        RescheduleNextTick();
    }

    private void ComputeAllSensors(DateTime now)
    {
        var s = AppSettingsService.Instance;
        int windowSec = s.StatsWindowSec;

        foreach (var tab in _tabs)
        {
            if (tab.Channel.State != Tranzx.iVS4.Communication.Transport.TransportState.Connected) continue;
            int sps = Math.Max(1, tab.Channel.Sps > 0 ? (int)tab.Channel.Sps : (int)tab.Channel.Channel.Config.Odr);
            int n = sps * windowSec;
            if (n < 8) n = 8;
            // 上限：RingBuffer 容量 204800
            if (n > 204_800) n = 204_800;

            var (xs, ys, zs, _) = tab.Channel.Channel.Buffer.Snapshot(n);
            if (xs.Length < 4) continue; // 資料不足

            var sx = ExtendedVibrationStats.Compute(xs);
            var sy = ExtendedVibrationStats.Compute(ys);
            var sz = ExtendedVibrationStats.Compute(zs);

            AppendRow(now, tab.Channel.DisplayName, "X", xs.Length, sx);
            AppendRow(now, tab.Channel.DisplayName, "Y", ys.Length, sy);
            AppendRow(now, tab.Channel.DisplayName, "Z", zs.Length, sz);
        }
    }

    private void AppendRow(DateTime time, string sensor, string axis, int n, ExtendedVibrationStats st)
    {
        var row = new StatRow
        {
            Time = time, SensorName = sensor, Axis = axis, N = n,
            Min = st.Min, Max = st.Max, Mean = st.Mean, Median = st.Median,
            StdDev = st.StdDev, Rms = st.Rms, Peak = st.Peak, Pp = st.PeakToPeak,
            Crest = st.CrestFactor
        };
        // UI thread 添加
        _rows.Insert(0, row);
        while (_rows.Count > MaxRowsKept) _rows.RemoveAt(_rows.Count - 1);

        // 5-8c8：推到圖表（每筆都 invalidate，OxyPlot 內部會節流）
        PushPoint(row);
        // 若 X 軸是「最近 N 秒」滑動模式 → 更新範圍跟著時間走
        if (_xAxisManual && _xAxis is not null && cmbXSpan.SelectedItem is string sel && sel != "Auto")
        {
            int sec = sel switch
            {
                "30s" => 30, "1m" => 60, "5m" => 300, "15m" => 900, "1h" => 3600,
                _ => 60
            };
            _xAxis.Minimum = DateTimeAxis.ToDouble(time.AddSeconds(-sec));
            _xAxis.Maximum = DateTimeAxis.ToDouble(time);
        }
        _chartModel.InvalidatePlot(true);

        // 寫 CSV（每日一個檔）
        try
        {
            WriteCsvRow(row);
            _writtenCount++;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StatsCsv] {ex.Message}");
        }

        // 5-8c9：可選寫入 Event Log（讓客戶稽核）
        if (AppSettingsService.Instance.StatsToEventLog)
        {
            try
            {
                ErrorLogService.Instance.Info(sensor,
                    $"Stats[{axis}] N={n} RMS={st.Rms:F4} Peak={st.Peak:F4} P-P={st.PeakToPeak:F4} " +
                    $"Min={st.Min:F4} Max={st.Max:F4} Mean={st.Mean:F4} Median={st.Median:F4} StdDev={st.StdDev:F4}");
            }
            catch { }
        }
    }

    private static StreamWriter? _csvWriter;
    private static string _csvDay = "";
    private static readonly object _csvLock = new();

    private static void WriteCsvRow(StatRow row)
    {
        var s = AppSettingsService.Instance;
        var folder = s.StatsCsvFolder;
        Directory.CreateDirectory(folder);
        string day = row.Time.ToString("yyyyMMdd");

        lock (_csvLock)
        {
            if (_csvWriter is null || _csvDay != day)
            {
                _csvWriter?.Dispose();
                string path = Path.Combine(folder, $"stats_{day}.csv");
                bool isNew = !File.Exists(path);
                _csvWriter = new StreamWriter(path, append: true, new UTF8Encoding(true));
                if (isNew)
                {
                    _csvWriter.WriteLine("Time,Sensor,Axis,N,Min,Max,Mean,Median,StdDev,RMS,Peak,P-P,Crest");
                }
                _csvDay = day;
            }
            var inv = CultureInfo.InvariantCulture;
            _csvWriter.WriteLine(
                $"{row.Time:yyyy/MM/dd HH:mm:ss.fff},{Csv(row.SensorName)},{row.Axis},{row.N}," +
                $"{row.Min.ToString("F5", inv)},{row.Max.ToString("F5", inv)},{row.Mean.ToString("F5", inv)}," +
                $"{row.Median.ToString("F5", inv)},{row.StdDev.ToString("F5", inv)},{row.Rms.ToString("F5", inv)}," +
                $"{row.Peak.ToString("F5", inv)},{row.Pp.ToString("F5", inv)},{row.Crest.ToString("F2", inv)}");
            _csvWriter.Flush();
        }
    }

    private static string Csv(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private void OnExportNowClick(object sender, RoutedEventArgs e)
    {
        // 強制立即計算一次（不論是否到時間）
        ComputeAllSensors(DateTime.Now);
        RescheduleNextTick();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        foreach (var s in _seriesMap.Values) s.Points.Clear();
        _chartModel.InvalidatePlot(true);
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = LocalizationService.Instance["StatsWin.OutputFolder"],
            InitialDirectory = AppSettingsService.Instance.StatsCsvFolder
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.FolderName))
        {
            AppSettingsService.Instance.StatsCsvFolder = dlg.FolderName;
            UpdateFolderLabel();
            // 強制換新 csv
            lock (_csvLock)
            {
                _csvWriter?.Dispose();
                _csvWriter = null;
                _csvDay = "";
            }
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var folder = AppSettingsService.Instance.StatsCsvFolder;
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void UpdateFolderLabel()
    {
        var folder = AppSettingsService.Instance.StatsCsvFolder;
        lblFolder.Text = folder;
        lblFolder.ToolTip = folder;
    }

    private void SetStatusText(string text)
    {
        lblStatus.Text = text;
    }

    /// <summary>DataGrid row data</summary>
    public sealed class StatRow
    {
        public DateTime Time { get; init; }
        public string SensorName { get; init; } = "";
        public string Axis { get; init; } = "";
        public int N { get; init; }
        public double Min { get; init; }
        public double Max { get; init; }
        public double Mean { get; init; }
        public double Median { get; init; }
        public double StdDev { get; init; }
        public double Rms { get; init; }
        public double Peak { get; init; }
        public double Pp { get; init; }
        public double Crest { get; init; }

        public string TimeText => Time.ToString("MM/dd HH:mm:ss.fff");
        public string MinText    => Min.ToString("F4", CultureInfo.InvariantCulture);
        public string MaxText    => Max.ToString("F4", CultureInfo.InvariantCulture);
        public string MeanText   => Mean.ToString("F4", CultureInfo.InvariantCulture);
        public string MedianText => Median.ToString("F4", CultureInfo.InvariantCulture);
        public string StdDevText => StdDev.ToString("F4", CultureInfo.InvariantCulture);
        public string RmsText    => Rms.ToString("F4", CultureInfo.InvariantCulture);
        public string PeakText   => Peak.ToString("F4", CultureInfo.InvariantCulture);
        public string PpText     => Pp.ToString("F4", CultureInfo.InvariantCulture);
        public string CrestText  => Crest.ToString("F2", CultureInfo.InvariantCulture);
    }
}
