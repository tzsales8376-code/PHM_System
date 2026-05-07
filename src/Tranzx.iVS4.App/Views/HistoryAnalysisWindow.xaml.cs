// ============================================================================
// Tranzx.iVS4.App / Views / HistoryAnalysisWindow.xaml.cs
//
// Phase 5-8c11：歷史分析（強化版）
//   - 載入 1~4 個 CSV 疊加比較
//   - 滑鼠左鍵框選圈選縮放（OxyPlot RectangleZoom）
//   - 「恢復原狀」按鈕重置縮放
//   - 右側統計面板（依 metric 類型動態切換）：
//       * 振動 trend / Raw：X/Y/Z Max / Mean / Median / StdDev
//       * 頻率（FFT 譜）：X/Y/Z 最大振幅的頻率（dominant frequency）
//       * 角度：X/Y/Z Max / Min / Mean / StdDev
//       * 溫濕度：Max / Min / Mean
//   - 圈選範圍會即時重算統計（限縮在可見 X 範圍）
//   - 多檔評比：自動標出最好/最壞數字
//   - 匯出 Word / PDF 報告
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using Tranzx.iVS4.App.Services;

namespace Tranzx.iVS4.App.Views;

public partial class HistoryAnalysisWindow : Window
{
    private readonly PlotModel _chartModel = new();
    private LinearAxis? _xAxis;       // 5-8c12：改成相對秒數
    private LinearAxis? _yAxis;
    private const int MaxDatasets = 4;

    /// <summary>每個 dataset 用不同色系（4 組）</summary>
    private static readonly OxyColor[] DatasetColors =
    {
        OxyColor.FromRgb(0x1A, 0xBC, 0x9C), // Teal
        OxyColor.FromRgb(0xF3, 0x9C, 0x12), // Amber
        OxyColor.FromRgb(0x9B, 0x59, 0xB6), // Purple
        OxyColor.FromRgb(0xE7, 0x4C, 0x3C), // Red
    };

    /// <summary>單個載入的 CSV（internal 讓 AlignDialog 可直接 reference）</summary>
    internal sealed class Dataset
    {
        public required string FileName { get; init; }
        public required string FullPath { get; init; }
        public required DataKind Kind { get; init; }
        public required List<string[]> Rows { get; init; }
        /// <summary>(metric, axis) → column index</summary>
        public required Dictionary<(string metric, string axis), int> MetricCols { get; init; }
        /// <summary>raw csv：軸 → column index</summary>
        public required Dictionary<string, int> AxisCols { get; init; }
        public required int TimeCol { get; init; }
        /// <summary>給 chart 用的 series（key = axis "X"/"Y"/"Z"）</summary>
        public Dictionary<string, LineSeries> Series { get; } = new();

        // 5-8c12：時間對齊
        /// <summary>檔案內第一筆有效時間（用來計算 elapsed seconds）</summary>
        public DateTime FirstTime { get; set; }
        /// <summary>使用者選的「對齊錨點」時間（預設 = FirstTime）</summary>
        public DateTime AnchorTime { get; set; }
        /// <summary>所有解析過的時間 cache（避免反覆 parse）</summary>
        public List<(DateTime t, int rowIdx)> ParsedTimes { get; } = new();
    }

    public enum DataKind { Trend, Raw, Stats, Fft, Tilt, Env, Unknown }

    private readonly List<Dataset> _datasets = new();

    public HistoryAnalysisWindow()
    {
        InitializeComponent();
        InitChart();
        // 圖表互動：
        //   左鍵 click   → SnapTrack（黑底白字 tracker，含十字線）
        //   左鍵 drag    → ZoomRectangle（框選縮放）
        //   右鍵 drag    → Pan
        //   滾輪         → Zoom
        var pc = new PlotController();
        pc.UnbindAll();
        pc.BindMouseDown(OxyMouseButton.Left, PlotCommands.SnapTrack);
        pc.BindMouseDown(OxyMouseButton.Left, OxyModifierKeys.Control, PlotCommands.ZoomRectangle);
        pc.BindMouseDown(OxyMouseButton.Middle, PlotCommands.ZoomRectangle);
        pc.BindMouseDown(OxyMouseButton.Right, PlotCommands.PanAt);
        pc.BindMouseWheel(PlotCommands.ZoomWheel);
        histChart.Controller = pc;
    }

    private void InitChart()
    {
        _chartModel.Background = OxyColors.Transparent;
        _chartModel.TextColor = OxyColor.FromRgb(0xC0, 0xC0, 0xCF);
        _chartModel.PlotAreaBorderColor = OxyColor.FromArgb(50, 200, 200, 220);
        _chartModel.PlotMargins = new OxyThickness(60, 12, 12, 36);

        // 5-8c12：X 軸改用「相對秒數」便於多 CSV 對齊比較
        _xAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Elapsed (s)",
            TitleColor = OxyColor.FromRgb(0xA0, 0xA0, 0xB8),
            TitleFontSize = 11,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(50, 200, 200, 220),
            TextColor = OxyColor.FromRgb(0xA0, 0xA0, 0xB8),
            IsZoomEnabled = true,
            IsPanEnabled = true,
        };
        _yAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(50, 200, 200, 220),
            TextColor = OxyColor.FromRgb(0xA0, 0xA0, 0xB8),
            Title = "Value",
            TitleColor = OxyColor.FromRgb(0xA0, 0xA0, 0xB8),
            TitleFontSize = 11,
            IsZoomEnabled = true,
            IsPanEnabled = true,
        };
        _chartModel.Axes.Add(_xAxis);
        _chartModel.Axes.Add(_yAxis);
        _chartModel.Legends.Add(new Legend
        {
            LegendPosition = LegendPosition.TopRight,
            LegendBackground = OxyColor.FromArgb(180, 30, 30, 46),
            LegendBorder = OxyColor.FromArgb(80, 200, 200, 220),
            LegendTextColor = OxyColor.FromRgb(0xC0, 0xC0, 0xCF),
            LegendFontSize = 10,
        });

        // 5-8c12：Tracker 由 LineSeries.TrackerFormatString 提供（黑底白字是 OxyPlot.Wpf 預設樣式）
        // X 軸範圍變化 → 重算統計
        _xAxis.AxisChanged += (_, _) => UpdateStatsPanel();
        histChart.Model = _chartModel;
    }

    // ─────────────────────────────────────────────────
    //  載入 CSV
    // ─────────────────────────────────────────────────

    private void OnLoadCsvClick(object sender, RoutedEventArgs e)
    {
        // 第一個檔 → clear 後載入
        _datasets.Clear();
        if (LoadDialog() is { } ds)
        {
            _datasets.Add(ds);
            BuildMetricList();
            RebuildChart();
            UpdateFooter();
        }
    }

    private void OnAddOverlayClick(object sender, RoutedEventArgs e)
    {
        if (_datasets.Count >= MaxDatasets)
        {
            MessageBox.Show(string.Format(LocalizationService.Instance["HistoryWin.MaxOverlayFmt"], MaxDatasets),
                LocalizationService.Instance["HistoryWin.Title"]);
            return;
        }
        if (LoadDialog() is { } ds)
        {
            _datasets.Add(ds);
            RebuildChart();
            UpdateFooter();
        }
    }

    private Dataset? LoadDialog()
    {
        var phmRoot = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tranzx PHM");
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationService.Instance["HistoryWin.LoadCsv"],
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Multiselect = false,
            InitialDirectory = Directory.Exists(phmRoot) ? phmRoot : null,
        };
        if (dlg.ShowDialog() != true) return null;
        try
        {
            return ParseCsv(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message,
                LocalizationService.Instance["HistoryWin.Title"],
                MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    private static Dataset ParseCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) throw new Exception(LocalizationService.Instance["HistoryWin.EmptyFile"]);

        // 找 header
        int headerIdx = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("Time,", StringComparison.OrdinalIgnoreCase)
                || lines[i].StartsWith("\"Time\",", StringComparison.OrdinalIgnoreCase)
                || lines[i].StartsWith("Frequency,", StringComparison.OrdinalIgnoreCase))
            {
                headerIdx = i;
                break;
            }
        }

        var headers = SplitCsv(lines[headerIdx]);
        var rows = new List<string[]>(lines.Length - headerIdx - 1);
        for (int i = headerIdx + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            rows.Add(SplitCsv(lines[i]));
        }

        // 解析 header → metric/axis 對應
        int timeCol = -1;
        var metricCols = new Dictionary<(string, string), int>();
        var axisCols = new Dictionary<string, int>();

        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Trim();
            if (h.Equals("Time", StringComparison.OrdinalIgnoreCase) || h.Equals("Frequency", StringComparison.OrdinalIgnoreCase))
            {
                timeCol = i;
                continue;
            }

            string axis = "";
            string metric = "";
            var parts = h.Split(new[] { '-', '_', ' ', '(', ')', '.' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var pu = p.ToUpperInvariant();
                if (pu == "X" || pu == "Y" || pu == "Z") axis = pu;
                else if (pu == "ANGLEX" || pu == "TILTX") { axis = "X"; metric = "ANGLE"; }
                else if (pu == "ANGLEY" || pu == "TILTY") { axis = "Y"; metric = "ANGLE"; }
                else if (pu == "ANGLEZ" || pu == "TILTZ") { axis = "Z"; metric = "ANGLE"; }
                else if (pu == "RMS" || pu == "PEAK" || pu == "PP"
                         || pu == "MIN" || pu == "MAX" || pu == "MEAN"
                         || pu == "MEDIAN" || pu == "STDDEV" || pu == "CREST"
                         || pu == "AMPLITUDE" || pu == "FFT")
                {
                    metric = pu == "PP" ? "P-P" : pu;
                }
                else if (pu == "TEMPERATURE" || pu == "TEMP" || pu == "TEMPC")
                {
                    axis = "TEMP"; metric = "ENV";
                }
                else if (pu == "HUMIDITY" || pu == "HUM")
                {
                    axis = "HUMID"; metric = "ENV";
                }
            }

            // P-P 特殊：header 直接是 "P-P (G)" 或 "P-P"
            if (h.IndexOf("P-P", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                metric = "P-P";
                if (string.IsNullOrEmpty(axis))
                {
                    foreach (var c in h.ToUpper()) if (c == 'X' || c == 'Y' || c == 'Z') axis = c.ToString();
                }
            }

            if (!string.IsNullOrEmpty(axis) && string.IsNullOrEmpty(metric))
            {
                axisCols[axis] = i;
            }
            else if (!string.IsNullOrEmpty(axis) && !string.IsNullOrEmpty(metric))
            {
                metricCols[(metric, axis)] = i;
                // 5-8c12：環境檔的 TEMP/HUMID 也加進 axisCols 以便繪圖
                if (metric == "ENV") axisCols[axis] = i;
            }
        }

        // 推測 DataKind（影響統計面板呈現）
        DataKind kind = DataKind.Unknown;
        var fname = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (fname.Contains("tilt"))      kind = DataKind.Tilt;
        else if (fname.Contains("env"))  kind = DataKind.Env;
        else if (fname.Contains("fft"))  kind = DataKind.Fft;
        else if (fname.Contains("raw"))  kind = DataKind.Raw;
        else if (fname.Contains("stats")) kind = DataKind.Stats;
        else if (fname.Contains("vib") || fname.Contains("trend")) kind = DataKind.Trend;
        else if (axisCols.ContainsKey("TEMP") || axisCols.ContainsKey("HUMID")
                 || metricCols.Keys.Any(k => k.Item2 == "TEMP" || k.Item2 == "HUMID")) kind = DataKind.Env;
        else if (metricCols.Keys.Any(k => k.Item1 == "ANGLE")) kind = DataKind.Tilt;
        else if (axisCols.Count > 0) kind = DataKind.Raw;
        else if (metricCols.Count > 0) kind = DataKind.Trend;

        var ds = new Dataset
        {
            FileName = System.IO.Path.GetFileName(path),
            FullPath = path,
            Kind = kind,
            Rows = rows,
            TimeCol = timeCol,
            MetricCols = metricCols,
            AxisCols = axisCols,
        };

        // 5-8c12：parse 所有時間並 cache，計算 FirstTime
        if (timeCol >= 0)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Length <= timeCol) continue;
                if (TryParseTime(rows[i][timeCol], out var t))
                {
                    ds.ParsedTimes.Add((t, i));
                }
            }
            if (ds.ParsedTimes.Count > 0)
            {
                ds.FirstTime = ds.ParsedTimes[0].t;
                ds.AnchorTime = ds.FirstTime;
            }
        }
        return ds;
    }

    private static string[] SplitCsv(string line)
    {
        var list = new List<string>();
        bool inQ = false;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQ && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQ = !inQ;
            }
            else if (c == ',' && !inQ) { list.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        list.Add(sb.ToString());
        return list.ToArray();
    }

    private static bool TryParseTime(string s, out DateTime t)
    {
        s = s.Trim();
        var formats = new[]
        {
            "yyyy/MM/dd HH:mm:ss.fff", "yyyy/M/d HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss.fff", "HH:mm:ss.fff", "HH:mm:ss",
        };
        foreach (var f in formats)
        {
            if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out t)) return true;
        }
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out t);
    }

    // ─────────────────────────────────────────────────
    //  Metric list & chart
    // ─────────────────────────────────────────────────

    private void BuildMetricList()
    {
        if (_datasets.Count == 0) { cmbMetric.ItemsSource = null; return; }
        var first = _datasets[0];
        // 5-8c12：保留標準排序（PEAK/RMS/CREST/...），不要字母順序
        var raw = first.MetricCols.Keys.Select(k => k.Item1).Distinct().ToList();
        var preferred = new[] { "PEAK", "RMS", "P-P", "MEAN", "MEDIAN", "STDDEV", "CREST", "MIN", "MAX", "ANGLE", "ENV" };
        var metrics = preferred.Where(p => raw.Contains(p)).ToList();
        // 加上未被預設順序涵蓋的
        metrics.AddRange(raw.Where(r => !metrics.Contains(r)));
        if (metrics.Count == 0 && first.AxisCols.Count > 0) metrics.Add("Raw");
        cmbMetric.ItemsSource = metrics;
        cmbMetric.SelectedIndex = metrics.Count > 0 ? 0 : -1;
    }

    private void RebuildChart()
    {
        _chartModel.Series.Clear();
        foreach (var ds in _datasets) ds.Series.Clear();

        if (_datasets.Count == 0 || cmbMetric.SelectedItem is not string metric)
        {
            _chartModel.InvalidatePlot(true);
            UpdateStatsPanel();
            return;
        }

        bool showX = cbColX.IsChecked == true;
        bool showY = cbColY.IsChecked == true;
        bool showZ = cbColZ.IsChecked == true;

        for (int dsIdx = 0; dsIdx < _datasets.Count; dsIdx++)
        {
            var ds = _datasets[dsIdx];
            var baseColor = DatasetColors[dsIdx % DatasetColors.Length];

            // X / Y / Z 用不同 dash 樣式（同 dataset 同色）
            void TryAddSeries(string axis, LineStyle style)
            {
                int col;
                if (metric == "Raw")
                {
                    if (!ds.AxisCols.TryGetValue(axis, out col)) return;
                }
                else
                {
                    if (!ds.MetricCols.TryGetValue((metric, axis), out col)) return;
                }
                var series = new LineSeries
                {
                    Title = $"[{dsIdx + 1}] {System.IO.Path.GetFileNameWithoutExtension(ds.FileName)} {axis}",
                    Color = baseColor,
                    StrokeThickness = 1.4,
                    LineStyle = style,
                    MarkerType = MarkerType.None,
                    // 5-8c12：黑底白字 tracker + 顯示完整資訊
                    TrackerFormatString = "{0}\nTime: {2:F3} s\nValue: {4:F4}",
                };
                ds.Series[axis] = series;
                _chartModel.Series.Add(series);

                // 5-8c12：用 cached ParsedTimes，X = (time - AnchorTime).TotalSeconds
                foreach (var (t, rowIdx) in ds.ParsedTimes)
                {
                    var row = ds.Rows[rowIdx];
                    if (row.Length <= col) continue;
                    if (!double.TryParse(row[col], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) continue;
                    double elapsedSec = (t - ds.AnchorTime).TotalSeconds;
                    series.Points.Add(new DataPoint(elapsedSec, y));
                }
            }

            if (showX) TryAddSeries("X", LineStyle.Solid);
            if (showY) TryAddSeries("Y", LineStyle.Dash);
            if (showZ) TryAddSeries("Z", LineStyle.Dot);
            // 環境 csv 特殊欄位
            if (ds.AxisCols.ContainsKey("TEMP")) TryAddSeries("TEMP", LineStyle.Solid);
            if (ds.AxisCols.ContainsKey("HUMID")) TryAddSeries("HUMID", LineStyle.Dash);
        }

        UpdateYAxisTitle(metric);
        _yAxis?.Reset();
        _xAxis?.Reset();
        _chartModel.InvalidatePlot(true);
        UpdateStatsPanel();
    }

    private void UpdateYAxisTitle(string metric)
    {
        if (_yAxis is null) return;
        // 5-8c12：依資料類型 Y 軸 title
        bool anyTilt = _datasets.Any(d => d.Kind == DataKind.Tilt);
        bool anyEnv = _datasets.Any(d => d.Kind == DataKind.Env);
        if (anyTilt) _yAxis.Title = "Angle (°)";
        else if (anyEnv) _yAxis.Title = "Temperature (°C) / Humidity (%)";
        else _yAxis.Title = metric == "Raw" ? "Acceleration (G)" : $"{metric} (G)";
    }

    private void OnMetricChanged(object sender, SelectionChangedEventArgs e) => RebuildChart();
    private void OnColumnToggle(object sender, RoutedEventArgs e) => RebuildChart();

    private void OnResetZoomClick(object sender, RoutedEventArgs e)
    {
        _xAxis?.Reset();
        _yAxis?.Reset();
        _chartModel.InvalidatePlot(false);
        UpdateStatsPanel();
    }

    /// <summary>5-8c12：手動對齊 — 對每個 dataset 輸入「希望對齊到的目標秒數」（其餘自動偏移）</summary>
    private void OnAlignClick(object sender, RoutedEventArgs e)
    {
        if (_datasets.Count < 2)
        {
            MessageBox.Show(LocalizationService.Instance["HistoryWin.AlignNeedMulti"]);
            return;
        }
        // 簡單對齊 dialog
        var dlg = new AlignDialog(_datasets) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        // 套用每個 dataset 的新 anchor
        for (int i = 0; i < _datasets.Count; i++)
        {
            // 使用者輸入「該 dataset 希望被視為 t=0 的時間點（從檔案起算的秒數）」
            double offsetFromFirst = dlg.Offsets[i];
            _datasets[i].AnchorTime = _datasets[i].FirstTime.AddSeconds(offsetFromFirst);
        }
        RebuildChart();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _datasets.Clear();
        cmbMetric.ItemsSource = null;
        lblFile.Text = "";
        lblRange.Text = "";
        pnlStats.Children.Clear();
        _chartModel.Series.Clear();
        _chartModel.InvalidatePlot(true);
    }

    // ─────────────────────────────────────────────────
    //  統計面板（依 metric / DataKind 動態切換）
    // ─────────────────────────────────────────────────

    /// <summary>單個 dataset 在「目前可見 X 範圍」內的統計</summary>
    private sealed class StatsRow
    {
        public required string DatasetName { get; init; }
        public required int DatasetIdx { get; init; }
        public required string Axis { get; init; }
        public int N;
        public double Min, Max, Mean, Median, StdDev;
        /// <summary>給 FFT 用 — 主頻 (Hz)</summary>
        public double DominantFreq;
        public double DominantAmp;
    }

    private void UpdateStatsPanel()
    {
        pnlStats.Children.Clear();
        if (_datasets.Count == 0 || cmbMetric.SelectedItem is not string metric) return;

        // 5-8c12：可見 X 範圍是 elapsed seconds
        double xMin = _xAxis?.ActualMinimum ?? double.NaN;
        double xMax = _xAxis?.ActualMaximum ?? double.NaN;

        if (double.IsFinite(xMin) && double.IsFinite(xMax))
            lblRange.Text = $"{xMin:F1}s  →  {xMax:F1}s  (Δ={xMax - xMin:F1}s)";
        else
            lblRange.Text = "(全部資料)";

        // 對每個 dataset 計算
        var allStats = new List<StatsRow>();
        foreach (var ds in _datasets.Select((d, i) => (d, i)))
        {
            CollectStats(ds.d, ds.i, metric, xMin, xMax, allStats);
        }

        for (int i = 0; i < _datasets.Count; i++)
        {
            var ds = _datasets[i];
            var color = DatasetColors[i % DatasetColors.Length];
            var dsBlock = BuildDatasetBlock(ds, i, color, allStats.Where(s => s.DatasetIdx == i).ToList());
            pnlStats.Children.Add(dsBlock);
        }

        if (_datasets.Count >= 2)
            pnlStats.Children.Add(BuildComparisonBlock(allStats));
    }

    private void CollectStats(Dataset ds, int dsIdx, string metric,
                              double xMinSec, double xMaxSec,
                              List<StatsRow> output)
    {
        var axes = new List<string>();
        switch (ds.Kind)
        {
            case DataKind.Env:  axes.AddRange(new[] { "TEMP", "HUMID" }); break;
            default:            axes.AddRange(new[] { "X", "Y", "Z" }); break;
        }

        bool hasXLimits = double.IsFinite(xMinSec) && double.IsFinite(xMaxSec);

        foreach (var axis in axes)
        {
            int col;
            if (metric == "Raw" || ds.Kind == DataKind.Env)
            {
                if (!ds.AxisCols.TryGetValue(axis, out col)) continue;
            }
            else if (!ds.MetricCols.TryGetValue((metric, axis), out col)) continue;

            // 5-8c12：使用 cached ParsedTimes
            var values = new List<double>(ds.ParsedTimes.Count);
            int dominantIdx = -1; double dominantMax = double.MinValue;
            for (int i = 0; i < ds.ParsedTimes.Count; i++)
            {
                var (t, rowIdx) = ds.ParsedTimes[i];
                double elapsedSec = (t - ds.AnchorTime).TotalSeconds;
                if (hasXLimits && (elapsedSec < xMinSec || elapsedSec > xMaxSec)) continue;
                var row = ds.Rows[rowIdx];
                if (row.Length <= col) continue;
                if (!double.TryParse(row[col], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) continue;
                values.Add(y);
                if (y > dominantMax) { dominantMax = y; dominantIdx = rowIdx; }
            }

            if (values.Count == 0) continue;

            var sr = new StatsRow
            {
                DatasetIdx = dsIdx,
                DatasetName = System.IO.Path.GetFileNameWithoutExtension(ds.FileName),
                Axis = axis,
                N = values.Count,
            };
            ComputeBasicStats(values, sr);
            // FFT 檔：「Time」欄實際是頻率值（Hz）
            if (ds.Kind == DataKind.Fft && dominantIdx >= 0)
            {
                if (double.TryParse(ds.Rows[dominantIdx][ds.TimeCol],
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double freq))
                {
                    sr.DominantFreq = freq;
                    sr.DominantAmp = dominantMax;
                }
            }
            output.Add(sr);
        }
    }

    private static void ComputeBasicStats(List<double> values, StatsRow sr)
    {
        if (values.Count == 0) return;
        double sum = 0, sumSq = 0;
        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in values)
        {
            sum += v; sumSq += v * v;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        double mean = sum / values.Count;
        double std = Math.Sqrt(Math.Max(0, sumSq / values.Count - mean * mean));
        var sorted = values.ToArray();
        Array.Sort(sorted);
        double median = sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) * 0.5;
        sr.Min = min; sr.Max = max; sr.Mean = mean; sr.StdDev = std; sr.Median = median;
    }

    private FrameworkElement BuildDatasetBlock(Dataset ds, int dsIdx, OxyColor color, List<StatsRow> stats)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(4),
            Background = (SolidColorBrush)Application.Current.FindResource("BgPrimary"),
            BorderBrush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
            BorderThickness = new Thickness(3, 0, 0, 0),
        };
        var sp = new StackPanel();
        // 標題
        sp.Children.Add(new TextBlock
        {
            Text = $"[{dsIdx + 1}] {ds.FileName}",
            FontWeight = System.Windows.FontWeights.Bold,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        sp.Children.Add(new TextBlock
        {
            Text = ds.Kind.ToString(),
            FontSize = 9,
            Foreground = (SolidColorBrush)Application.Current.FindResource("TextSecondary"),
            Margin = new Thickness(0, 0, 0, 4),
        });

        if (stats.Count == 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "(無資料)",
                FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.FindResource("TextSecondary"),
            });
            border.Child = sp;
            return border;
        }

        // Grid 列：軸 / N / Max / Min / Mean / Median / StdDev / 主頻
        bool hasFreq = stats.Any(s => s.DominantFreq > 0);
        bool isEnv = ds.Kind == DataKind.Env;
        bool isTilt = ds.Kind == DataKind.Tilt;

        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        // headers
        var headers = new List<string> { "" };
        if (isEnv)         headers.AddRange(new[] { "Max", "Min", "Mean" });
        else if (isTilt)   headers.AddRange(new[] { "Max", "Min", "Mean", "StdDev" });
        else if (hasFreq)  headers.AddRange(new[] { "Max", "MaxF(Hz)" });
        else               headers.AddRange(new[] { "Max", "Mean", "Median", "StdDev" });

        for (int c = 0; c < headers.Count; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(48) : new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition());
        for (int c = 0; c < headers.Count; c++)
        {
            var h = new TextBlock
            {
                Text = headers[c],
                FontSize = 10, FontWeight = System.Windows.FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (SolidColorBrush)Application.Current.FindResource("TextSecondary"),
                Margin = new Thickness(2, 1, 2, 1),
            };
            Grid.SetRow(h, 0); Grid.SetColumn(h, c); grid.Children.Add(h);
        }

        for (int i = 0; i < stats.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            var s = stats[i];
            int row = i + 1;

            var axisLabel = AxisDisplay(s.Axis);
            AddCell(grid, row, 0, axisLabel, AxisColor(s.Axis), bold: true);
            int colIdx = 1;
            if (isEnv)
            {
                AddCell(grid, row, colIdx++, F2(s.Max), null);
                AddCell(grid, row, colIdx++, F2(s.Min), null);
                AddCell(grid, row, colIdx++, F2(s.Mean), null);
            }
            else if (isTilt)
            {
                AddCell(grid, row, colIdx++, F2(s.Max), null);
                AddCell(grid, row, colIdx++, F2(s.Min), null);
                AddCell(grid, row, colIdx++, F2(s.Mean), null);
                AddCell(grid, row, colIdx++, F3(s.StdDev), null);
            }
            else if (hasFreq)
            {
                AddCell(grid, row, colIdx++, F4(s.DominantAmp), null);
                AddCell(grid, row, colIdx++, F1(s.DominantFreq), null);
            }
            else
            {
                AddCell(grid, row, colIdx++, F4(s.Max), null);
                AddCell(grid, row, colIdx++, F4(s.Mean), null);
                AddCell(grid, row, colIdx++, F4(s.Median), null);
                AddCell(grid, row, colIdx++, F4(s.StdDev), null);
            }
        }
        sp.Children.Add(grid);
        border.Child = sp;
        return border;
    }

    private static string AxisDisplay(string a) => a switch
    {
        "TEMP" => "🌡 °C", "HUMID" => "💧 %", _ => a
    };
    private static SolidColorBrush AxisColor(string a) => a switch
    {
        "X" => new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
        "Y" => new SolidColorBrush(Color.FromRgb(0x1A, 0xBC, 0x9C)),
        "Z" => new SolidColorBrush(Color.FromRgb(0x52, 0x94, 0xE2)),
        "TEMP" => new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
        "HUMID" => new SolidColorBrush(Color.FromRgb(0x52, 0x94, 0xE2)),
        _ => new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xCF)),
    };
    private static void AddCell(Grid g, int r, int c, string text, Brush? color, bool bold = false)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 10, FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(2, 1, 2, 1),
            FontWeight = bold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
        };
        if (color is not null) tb.Foreground = color;
        else tb.Foreground = (SolidColorBrush)Application.Current.FindResource("TextPrimary");
        Grid.SetRow(tb, r); Grid.SetColumn(tb, c); g.Children.Add(tb);
    }
    private static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
    private static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
    private static string F3(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    private static string F4(double v) => v.ToString("F4", CultureInfo.InvariantCulture);

    /// <summary>多 CSV 比較區塊 — 找出 Max/Min/StdDev 各組哪個最好/最壞</summary>
    private FrameworkElement BuildComparisonBlock(List<StatsRow> all)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 8, 0, 8),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(4),
            Background = (SolidColorBrush)Application.Current.FindResource("BgPrimary"),
            BorderBrush = (SolidColorBrush)Application.Current.FindResource("AccentAmber"),
            BorderThickness = new Thickness(3, 0, 0, 0),
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = "🏆 " + LocalizationService.Instance["HistoryWin.Comparison"],
            FontWeight = System.Windows.FontWeights.Bold, FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.FindResource("AccentAmber"),
            Margin = new Thickness(0, 0, 0, 6),
        });

        // 對每個軸：找最大 Max（最差） + 最小 Max（最好）
        // 振動越小越好 → 最小 Max 為「最好」
        foreach (var axis in all.Select(s => s.Axis).Distinct())
        {
            var rows = all.Where(s => s.Axis == axis).ToList();
            if (rows.Count == 0) continue;
            var bestByMax = rows.OrderBy(s => s.Max).First();
            var worstByMax = rows.OrderByDescending(s => s.Max).First();
            var bestByStd = rows.OrderBy(s => s.StdDev).First();

            sp.Children.Add(new TextBlock
            {
                Text = $"━ {AxisDisplay(axis)} ━",
                FontSize = 10, FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = AxisColor(axis), Margin = new Thickness(0, 4, 0, 2),
            });
            sp.Children.Add(MakeLine($"  ✓ 最佳 (Max最低)：[{bestByMax.DatasetIdx + 1}] {F4(bestByMax.Max)}",
                (SolidColorBrush)Application.Current.FindResource("AccentTeal")));
            sp.Children.Add(MakeLine($"  ✗ 最差 (Max最高)：[{worstByMax.DatasetIdx + 1}] {F4(worstByMax.Max)}",
                (SolidColorBrush)Application.Current.FindResource("AccentRed")));
            sp.Children.Add(MakeLine($"  ⚖ 最穩定 (StdDev最小)：[{bestByStd.DatasetIdx + 1}] {F4(bestByStd.StdDev)}",
                (SolidColorBrush)Application.Current.FindResource("AccentBlue")));
        }
        border.Child = sp;
        return border;
    }

    private static TextBlock MakeLine(string text, Brush color) => new TextBlock
    {
        Text = text, FontSize = 10, FontFamily = new FontFamily("Consolas"),
        Foreground = color, Margin = new Thickness(0, 1, 0, 1),
    };

    // ─────────────────────────────────────────────────
    //  匯出報告
    // ─────────────────────────────────────────────────

    private void OnExportReportClick(object sender, RoutedEventArgs e)
    {
        if (_datasets.Count == 0)
        {
            MessageBox.Show(LocalizationService.Instance["HistoryWin.NoDataToExport"]);
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = LocalizationService.Instance["HistoryWin.ExportReport"],
            Filter = "PDF report (*.pdf)|*.pdf|HTML report (*.html)|*.html|CSV summary (*.csv)|*.csv",
            FileName = $"PHM_Report_{DateTime.Now:yyyyMMdd_HHmmss}",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
            if (ext == ".csv")
            {
                ExportCsvSummary(dlg.FileName);
            }
            else if (ext == ".pdf")
            {
                // 5-8c12：先產 HTML 到 temp，再用 Edge headless 轉 PDF
                var tmpHtml = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    $"phm_report_{DateTime.Now:yyyyMMddHHmmss}.html");
                ExportHtmlReport(tmpHtml);
                bool ok = ConvertHtmlToPdf(tmpHtml, dlg.FileName);
                try { File.Delete(tmpHtml); } catch { }
                if (!ok)
                {
                    MessageBox.Show(
                        LocalizationService.Instance["HistoryWin.PdfFallback"],
                        LocalizationService.Instance["HistoryWin.Title"],
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    // fallback：改存 HTML
                    var fallbackHtml = System.IO.Path.ChangeExtension(dlg.FileName, ".html");
                    ExportHtmlReport(fallbackHtml);
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                          { FileName = fallbackHtml, UseShellExecute = true }); } catch { }
                    return;
                }
            }
            else
            {
                ExportHtmlReport(dlg.FileName);
            }

            // 開啟檔案
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                  { FileName = dlg.FileName, UseShellExecute = true }); }
            catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message,
                LocalizationService.Instance["HistoryWin.Title"],
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>5-8c12：用 Microsoft Edge headless 把 HTML 轉成 PDF</summary>
    private static bool ConvertHtmlToPdf(string htmlPath, string pdfPath)
    {
        // 嘗試找 Edge
        string[] edgeCandidates =
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        };
        string? exe = edgeCandidates.FirstOrDefault(File.Exists);
        if (exe is null) return false;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("--headless=new");
            psi.ArgumentList.Add("--disable-gpu");
            psi.ArgumentList.Add("--no-sandbox");
            psi.ArgumentList.Add($"--print-to-pdf={pdfPath}");
            psi.ArgumentList.Add("--print-to-pdf-no-header");
            psi.ArgumentList.Add(new Uri(htmlPath).AbsoluteUri);

            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return false;
            // 等最多 30 秒
            if (!p.WaitForExit(30_000))
            {
                try { p.Kill(); } catch { }
                return false;
            }
            return File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private void ExportHtmlReport(string path)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>Tranzx PHM History Analysis Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:'Segoe UI',sans-serif;margin:24px;background:#fafafa;color:#222;}");
        sb.AppendLine("h1{color:#1ABC9C;border-bottom:3px solid #1ABC9C;padding-bottom:8px;}");
        sb.AppendLine("h2{color:#5294E2;margin-top:24px;}");
        sb.AppendLine("h3{color:#444;margin-top:16px;}");
        sb.AppendLine("table{border-collapse:collapse;margin:8px 0;}");
        sb.AppendLine("th,td{padding:6px 12px;border:1px solid #ddd;text-align:right;font-family:Consolas,monospace;font-size:12px;}");
        sb.AppendLine("th{background:#5294E2;color:#fff;}");
        sb.AppendLine("td:first-child,th:first-child{text-align:left;}");
        sb.AppendLine("tr:nth-child(even){background:#f6f6f8;}");
        sb.AppendLine(".best{color:#1ABC9C;font-weight:bold;}");
        sb.AppendLine(".worst{color:#E74C3C;font-weight:bold;}");
        sb.AppendLine(".meta{color:#666;font-size:11px;}");
        sb.AppendLine(".x{color:#E74C3C;} .y{color:#1ABC9C;} .z{color:#5294E2;}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>Tranzx Vibration PHM System v1.0</h1>");
        sb.AppendLine($"<p class=\"meta\">Generated: {DateTime.Now:yyyy/MM/dd HH:mm:ss}<br>");
        sb.AppendLine($"Datasets: {_datasets.Count}<br>");

        double xMin = _xAxis?.ActualMinimum ?? double.NaN;
        double xMax = _xAxis?.ActualMaximum ?? double.NaN;
        if (double.IsFinite(xMin) && double.IsFinite(xMax))
        {
            sb.AppendLine($"Visible range: {xMin:F1}s ~ {xMax:F1}s (Δ={xMax - xMin:F1}s)<br>");
        }
        sb.AppendLine($"Metric: {cmbMetric.SelectedItem ?? "(none)"}</p>");

        var allStats = new List<StatsRow>();
        var metric = cmbMetric.SelectedItem as string ?? "";
        for (int i = 0; i < _datasets.Count; i++)
            CollectStats(_datasets[i], i, metric, xMin, xMax, allStats);

        // Per-dataset 統計
        sb.AppendLine("<h2>Per-Dataset Statistics</h2>");
        for (int i = 0; i < _datasets.Count; i++)
        {
            var ds = _datasets[i];
            sb.AppendLine($"<h3>[{i + 1}] {System.Net.WebUtility.HtmlEncode(ds.FileName)} <span class=\"meta\">({ds.Kind})</span></h3>");
            var rows = allStats.Where(s => s.DatasetIdx == i).ToList();
            if (rows.Count == 0) { sb.AppendLine("<p class=\"meta\">(無資料)</p>"); continue; }
            bool hasFreq = rows.Any(r => r.DominantFreq > 0);
            sb.AppendLine("<table>");
            if (hasFreq)
                sb.AppendLine("<tr><th>Axis</th><th>N</th><th>Max</th><th>Dominant Freq (Hz)</th></tr>");
            else
                sb.AppendLine("<tr><th>Axis</th><th>N</th><th>Min</th><th>Max</th><th>Mean</th><th>Median</th><th>StdDev</th></tr>");
            foreach (var r in rows)
            {
                string axCls = r.Axis.ToLower();
                if (hasFreq)
                    sb.AppendLine($"<tr><td class=\"{axCls}\">{r.Axis}</td><td>{r.N}</td><td>{F4(r.DominantAmp)}</td><td>{F1(r.DominantFreq)}</td></tr>");
                else
                    sb.AppendLine($"<tr><td class=\"{axCls}\">{r.Axis}</td><td>{r.N}</td><td>{F4(r.Min)}</td><td>{F4(r.Max)}</td><td>{F4(r.Mean)}</td><td>{F4(r.Median)}</td><td>{F4(r.StdDev)}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        // 比較
        if (_datasets.Count >= 2)
        {
            sb.AppendLine("<h2>🏆 Comparison &amp; Ranking</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Axis</th><th>Best (Max lowest)</th><th>Worst (Max highest)</th><th>Most Stable (StdDev lowest)</th></tr>");
            foreach (var axis in allStats.Select(s => s.Axis).Distinct())
            {
                var rows = allStats.Where(s => s.Axis == axis).ToList();
                if (rows.Count == 0) continue;
                var bestByMax = rows.OrderBy(s => s.Max).First();
                var worstByMax = rows.OrderByDescending(s => s.Max).First();
                var bestByStd = rows.OrderBy(s => s.StdDev).First();
                sb.AppendLine($"<tr><td class=\"{axis.ToLower()}\">{axis}</td>" +
                    $"<td class=\"best\">[{bestByMax.DatasetIdx + 1}] {F4(bestByMax.Max)}</td>" +
                    $"<td class=\"worst\">[{worstByMax.DatasetIdx + 1}] {F4(worstByMax.Max)}</td>" +
                    $"<td class=\"best\">[{bestByStd.DatasetIdx + 1}] {F4(bestByStd.StdDev)}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<h2>File List</h2><ul>");
        for (int i = 0; i < _datasets.Count; i++)
            sb.AppendLine($"<li>[{i + 1}] <code>{System.Net.WebUtility.HtmlEncode(_datasets[i].FullPath)}</code></li>");
        sb.AppendLine("</ul>");

        sb.AppendLine("<p class=\"meta\">— End of report — Tranzx Vibration PHM System v1.0</p>");
        sb.AppendLine("</body></html>");
        File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
    }

    private void ExportCsvSummary(string path)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Tranzx PHM History Analysis Report");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy/MM/dd HH:mm:ss}");
        sb.AppendLine($"# Datasets: {_datasets.Count}");
        sb.AppendLine($"# Metric: {cmbMetric.SelectedItem ?? "(none)"}");
        sb.AppendLine();
        sb.AppendLine("DatasetIdx,FileName,Kind,Axis,N,Min,Max,Mean,Median,StdDev,DominantFreqHz");

        var allStats = new List<StatsRow>();
        var metric = cmbMetric.SelectedItem as string ?? "";
        double xMin = _xAxis?.ActualMinimum ?? double.NaN;
        double xMax = _xAxis?.ActualMaximum ?? double.NaN;
        for (int i = 0; i < _datasets.Count; i++)
            CollectStats(_datasets[i], i, metric, xMin, xMax, allStats);

        foreach (var r in allStats)
        {
            sb.AppendLine($"{r.DatasetIdx + 1},{r.DatasetName},{_datasets[r.DatasetIdx].Kind},{r.Axis},{r.N}," +
                $"{F4(r.Min)},{F4(r.Max)},{F4(r.Mean)},{F4(r.Median)},{F4(r.StdDev)},{F1(r.DominantFreq)}");
        }
        File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
    }

    private void UpdateFooter()
    {
        if (_datasets.Count == 0) { lblFile.Text = ""; return; }
        lblFile.Text = string.Join("  |  ",
            _datasets.Select((d, i) => $"[{i + 1}] {d.FileName}"));
    }
}
