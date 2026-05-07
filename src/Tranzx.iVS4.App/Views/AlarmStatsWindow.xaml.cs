// ============================================================================
// Tranzx.iVS4.App / Views / AlarmStatsWindow.xaml.cs
// 歷史警報統計：掃 alarm CSV 資料夾 → 解析每行 → 按 sensor/key/day 統計
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Tranzx.iVS4.App.Services;

namespace Tranzx.iVS4.App.Views;

public partial class AlarmStatsWindow : Window
{
    private sealed record StatsRow
    {
        public string Sensor { get; init; } = "";
        public string Key { get; init; } = "";
        public int Yellow { get; set; }
        public int Red { get; set; }
    }

    /// <summary>解析後的單筆 alarm 紀錄</summary>
    private sealed record AlarmEntry(DateTime Time, string Sensor, string Key,
                                      string FromLevel, string ToLevel);

    private readonly DateRangeOption[] _ranges;

    private sealed record DateRangeOption(int Days, string Display)
    {
        public override string ToString() => Display;
    }

    public AlarmStatsWindow()
    {
        InitializeComponent();
        var loc = LocalizationService.Instance;
        _ranges = new[]
        {
            new DateRangeOption(1,   loc["AlarmStats.RangeToday"]),
            new DateRangeOption(7,   loc["AlarmStats.Range7d"]),
            new DateRangeOption(30,  loc["AlarmStats.Range30d"]),
            new DateRangeOption(90,  loc["AlarmStats.Range90d"]),
            new DateRangeOption(0,   loc["AlarmStats.RangeAll"]),
        };
        cmbRange.ItemsSource = _ranges;
        cmbRange.SelectedIndex = 1;  // default 7 天
        lblFolder.Text = AppSettingsService.Instance.AlarmLogFolder;
        lblFolder.ToolTip = AppSettingsService.Instance.AlarmLogFolder;
        Loaded += (_, _) => Refresh();
    }

    private void OnRangeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => Refresh();

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = AppSettingsService.Instance.AlarmLogFolder;
            if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder, UseShellExecute = true, Verb = "open"
            });
        }
        catch { }
    }

    private void Refresh()
    {
        var loc = LocalizationService.Instance;
        var folder = AppSettingsService.Instance.AlarmLogFolder;
        if (!Directory.Exists(folder))
        {
            lblStatus.Text = string.Format(loc["AlarmStats.FolderMissing"], folder);
            ClearAll();
            return;
        }

        var range = cmbRange.SelectedItem as DateRangeOption ?? _ranges[1];
        DateTime cutoff = range.Days <= 0 ? DateTime.MinValue : DateTime.Now.Date.AddDays(-(range.Days - 1));

        // 掃所有 alarm_*.csv
        var entries = new List<AlarmEntry>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "alarm_*.csv", SearchOption.TopDirectoryOnly))
            {
                ParseFile(file, cutoff, entries);
            }
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
            ClearAll();
            return;
        }

        // 統計：只計「進入」黃/紅事件（ToLevel 為 Yellow / Red 且 FromLevel 為其他）
        int gCount = 0, yCount = 0, rCount = 0;
        var perKey = new Dictionary<string, StatsRow>();
        var perDay = new SortedDictionary<DateTime, (int Y, int R)>();

        foreach (var ent in entries)
        {
            if (ent.ToLevel == ent.FromLevel) continue;
            string compositeKey = ent.Sensor + "|" + ent.Key;
            if (!perKey.TryGetValue(compositeKey, out var row))
            {
                row = new StatsRow { Sensor = ent.Sensor, Key = ent.Key };
                perKey[compositeKey] = row;
            }
            var d = ent.Time.Date;
            if (!perDay.ContainsKey(d)) perDay[d] = (0, 0);

            switch (ent.ToLevel)
            {
                case "Yellow":
                    yCount++; row.Yellow++;
                    perDay[d] = (perDay[d].Y + 1, perDay[d].R);
                    break;
                case "Red":
                    rCount++; row.Red++;
                    perDay[d] = (perDay[d].Y, perDay[d].R + 1);
                    break;
                case "Green":
                    gCount++;
                    break;
            }
        }

        lblGreen.Text  = gCount.ToString();
        lblYellow.Text = yCount.ToString();
        lblRed.Text    = rCount.ToString();

        gridStats.ItemsSource = perKey.Values
            .OrderByDescending(r => r.Red).ThenByDescending(r => r.Yellow)
            .ToList();

        BuildChart(perDay);

        lblStatus.Text = string.Format(loc["AlarmStats.SummaryFmt"],
            entries.Count, perKey.Count);
    }

    private void ParseFile(string path, DateTime cutoff, List<AlarmEntry> sink)
    {
        try
        {
            using var sr = new StreamReader(path);
            string? line;
            bool inHeader = true;
            while ((line = sr.ReadLine()) != null)
            {
                if (inHeader)
                {
                    // metadata 區結束於空行 + Timestamp,Sensor,... header 行
                    if (line.StartsWith("Timestamp,")) { inHeader = false; }
                    continue;
                }
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Timestamp,Sensor,Key,FromLevel,ToLevel,Value,Yellow,Red
                var parts = SplitCsv(line);
                if (parts.Length < 5) continue;
                if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss.fff",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                {
                    if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                        continue;
                }
                if (cutoff > DateTime.MinValue && t < cutoff) continue;
                sink.Add(new AlarmEntry(t, parts[1], parts[2], parts[3], parts[4]));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AlarmStats.Parse {path}] {ex.Message}");
        }
    }

    /// <summary>簡易 CSV split（支援雙引號 escape）</summary>
    private static string[] SplitCsv(string line)
    {
        var list = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQ = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQ)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQ = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == ',') { list.Add(sb.ToString()); sb.Clear(); }
                else if (c == '"') inQ = true;
                else sb.Append(c);
            }
        }
        list.Add(sb.ToString());
        return list.ToArray();
    }

    private void BuildChart(SortedDictionary<DateTime, (int Y, int R)> perDay)
    {
        var loc = LocalizationService.Instance;
        var pm = new PlotModel
        {
            PlotAreaBorderColor = OxyColor.FromArgb(60, 200, 200, 200),
            TextColor = OxyColors.LightGray,
        };
        pm.Legends.Add(new OxyPlot.Legends.Legend
        {
            LegendPosition = OxyPlot.Legends.LegendPosition.TopRight,
            LegendBackground = OxyColor.FromArgb(120, 30, 30, 50),
            LegendTextColor = OxyColors.LightGray,
        });

        var cat = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            Title = loc["AlarmStats.Date"],
            FontSize = 10, TitleFontSize = 11,
            TextColor = OxyColors.LightGray, AxislineColor = OxyColors.Gray,
            MajorGridlineColor = OxyColor.FromArgb(20, 200, 200, 200),
            MajorGridlineStyle = LineStyle.Dot,
        };
        var val = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = loc["AlarmStats.Count"],
            Minimum = 0, MinimumPadding = 0, MaximumPadding = 0.1,
            FontSize = 10, TitleFontSize = 11,
            TextColor = OxyColors.LightGray, AxislineColor = OxyColors.Gray,
            MajorGridlineColor = OxyColor.FromArgb(20, 200, 200, 200),
            MajorGridlineStyle = LineStyle.Dot,
        };
        pm.Axes.Add(cat); pm.Axes.Add(val);

        var sY = new BarSeries
        {
            Title = loc["AlarmStats.Yellow"],
            FillColor = OxyColor.FromRgb(0xF3, 0x9C, 0x12),
            StrokeThickness = 0, StackGroup = "1", IsStacked = true,
        };
        var sR = new BarSeries
        {
            Title = loc["AlarmStats.Red"],
            FillColor = OxyColor.FromRgb(0xE7, 0x4C, 0x3C),
            StrokeThickness = 0, StackGroup = "1", IsStacked = true,
        };

        foreach (var kv in perDay)
        {
            cat.Labels.Add(kv.Key.ToString("MM/dd"));
            sY.Items.Add(new BarItem { Value = kv.Value.Y });
            sR.Items.Add(new BarItem { Value = kv.Value.R });
        }
        pm.Series.Add(sY); pm.Series.Add(sR);
        chart.Model = pm;
    }

    private void ClearAll()
    {
        lblGreen.Text = "0"; lblYellow.Text = "0"; lblRed.Text = "0";
        gridStats.ItemsSource = null;
        chart.Model = null;
    }
}
