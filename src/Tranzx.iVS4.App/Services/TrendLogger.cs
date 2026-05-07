// ============================================================================
// Tranzx.iVS4.App / Services / TrendLogger.cs
//
// Trend CSV 紀錄服務（Phase 5-7b 重構）
//   每個 Sensor 一個子資料夾，啟動錄製時並行開 3 個 trend csv：
//     Sensor1/
//       trend_Sensor1_20260503_152310_Vib.csv   ← 振動（每 stats）
//       trend_Sensor1_20260503_152310_Tilt.csv  ← 水平角度
//       trend_Sensor1_20260503_152310_Env.csv   ← 溫溼度
//       trend_Sensor1_20260503_152310_Raw.csv   ← (可選) 每筆 sample
//
//   Header（仿 VMS2.0 格式，已拿掉 Freq Range）：
//     Device ID:,SensorName
//     Date:,2026/05/03
//     Start time:,15:23:10.245
//     Log Type:,Vibration Trend
//     Range:,16G
//     Interval time(ms):,50
//     Unit:,G
//     Application:,Tranzx iVS 4.0
//     <空行>
//     Time,X-peak,...
//
//   自動 segment 切割：trend / raw 各自獨立切割時間
//     寫入前檢查 (Now - SegmentStart) 是否 > segmentMinutes，是 → 關舊檔開新檔
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Tranzx.iVS4.App.Services;

public sealed class TrendLogger
{
    public static TrendLogger Instance { get; } = new();
    private TrendLogger() { }

    private enum TrendType { Vib, Tilt, Env, Raw }

    /// <summary>單一 csv 的當前 segment 狀態</summary>
    private sealed class SegmentState
    {
        public StreamWriter? Writer;
        public string? FilePath;
        public DateTime SegmentStart;
        public readonly object Lock = new();
    }

    /// <summary>單一 Sensor 的 4 個 segment 狀態</summary>
    private sealed class SensorRecorders
    {
        public required int SensorIdx { get; init; }
        public required string SensorName { get; init; }
        public required int RangeG { get; init; }
        public required int FreqRange { get; init; }
        public required bool RawEnabled { get; init; }
        public bool IsSmartLog { get; init; } = false;
        public DateTime SessionStart { get; init; } = DateTime.Now;

        public SegmentState Vib  { get; } = new();
        public SegmentState Tilt { get; } = new();
        public SegmentState Env  { get; } = new();
        public SegmentState Raw  { get; } = new();

        // Raw 的 sample 起始時間（用於 Time 欄）
        public DateTime RawSampleStart;
    }

    private readonly ConcurrentDictionary<int, SensorRecorders> _recorders = new();

    /// <summary>啟動錄製（依 AppSettings 決定要開哪幾個 trend：振動/水平/溫溼度/Raw）</summary>
    /// <param name="isSmartLog">5-8c10：true → 寫到 Smart Log 子資料夾，並強制每小時切檔</param>
    /// <returns>所建立的 sensor 子資料夾路徑（給 UI 顯示）；失敗回 null</returns>
    public string? StartRecording(int sensorIdx, string sensorName,
                                   int rangeG, int freqRange, bool rawEnabled,
                                   bool isSmartLog = false)
    {
        StopRecording(sensorIdx);  // 確保只有一個

        try
        {
            var s = AppSettingsService.Instance;
            var rec = new SensorRecorders
            {
                SensorIdx = sensorIdx,
                SensorName = sensorName,
                RangeG = rangeG,
                FreqRange = freqRange,
                RawEnabled = rawEnabled,
                IsSmartLog = isSmartLog,
            };

            string folder = GetSensorFolder(sensorName, isSmartLog);
            Directory.CreateDirectory(folder);

            // 依設定決定要開哪幾個 writer
            if (s.LogVibration) OpenSegment(rec, TrendType.Vib);
            if (s.LogTilt)      OpenSegment(rec, TrendType.Tilt);
            if (s.LogEnv)       OpenSegment(rec, TrendType.Env);
            if (rawEnabled)     OpenSegment(rec, TrendType.Raw);

            // 若沒任何一個開啟（4 個都沒勾），視為失敗
            if (rec.Vib.Writer is null && rec.Tilt.Writer is null
                && rec.Env.Writer is null && rec.Raw.Writer is null)
            {
                return null;
            }

            _recorders[sensorIdx] = rec;
            return folder;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrendLogger.Start] {ex.Message}");
            return null;
        }
    }

    public void StopRecording(int sensorIdx)
    {
        if (!_recorders.TryRemove(sensorIdx, out var rec)) return;
        CloseSegment(rec.Vib);
        CloseSegment(rec.Tilt);
        CloseSegment(rec.Env);
        CloseSegment(rec.Raw);
    }

    public bool IsRecording(int sensorIdx) => _recorders.ContainsKey(sensorIdx);

    public void StopAll()
    {
        foreach (var k in _recorders.Keys.ToArray())
            StopRecording(k);
    }

    // ─────────── Write APIs ───────────
    public void WriteVibration(int sensorIdx,
        double xPeak, double yPeak, double zPeak,
        double xRms,  double yRms,  double zRms)
    {
        if (!_recorders.TryGetValue(sensorIdx, out var rec)) return;
        RotateIfNeeded(rec, TrendType.Vib);
        WriteLine(rec.Vib, $"{TimeStamp()}," +
            F5(xPeak)+","+F5(yPeak)+","+F5(zPeak)+","+F5(xRms)+","+F5(yRms)+","+F5(zRms));
    }

    public void WriteTilt(int sensorIdx, double xAng, double yAng, double zAng)
    {
        if (!_recorders.TryGetValue(sensorIdx, out var rec)) return;
        RotateIfNeeded(rec, TrendType.Tilt);
        WriteLine(rec.Tilt, $"{TimeStamp()}," + F3(xAng)+","+F3(yAng)+","+F3(zAng));
    }

    public void WriteEnv(int sensorIdx, double tempC, double humPct)
    {
        if (!_recorders.TryGetValue(sensorIdx, out var rec)) return;
        RotateIfNeeded(rec, TrendType.Env);
        WriteLine(rec.Env, $"{TimeStamp()}," + F2(tempC)+","+F1(humPct));
    }

    /// <summary>Raw sample 寫入（每個 sample 一行）</summary>
    public void WriteRawSample(int sensorIdx, DateTime sampleTime, double x, double y, double z)
    {
        if (!_recorders.TryGetValue(sensorIdx, out var rec)) return;
        if (!rec.RawEnabled) return;
        RotateIfNeeded(rec, TrendType.Raw);
        WriteLine(rec.Raw, $"{sampleTime:yyyy/MM/dd HH:mm:ss.fff}," + F5(x)+","+F5(y)+","+F5(z));
    }

    // ─────────── 內部：開檔 / 切割 / 關檔 ───────────

    private static string GetSensorFolder(string sensorName, bool isSmartLog = false)
    {
        string root = AppSettingsService.Instance.TrendLogFolder;
        if (string.IsNullOrEmpty(root))
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tranzx PHM", "Trends");
        // 5-8c10：Smart Log 寫入專屬資料夾（與一般 Trends 分開）
        if (isSmartLog)
        {
            // 把 root 從 ...\Trends 換成 ...\Smart Log
            string parent = Path.GetDirectoryName(root) ?? root;
            root = Path.Combine(parent, "Smart Log");
        }
        string safe = string.Join("_", sensorName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrEmpty(safe)) safe = "Sensor";
        return Path.Combine(root, safe);
    }

    private static void OpenSegment(SensorRecorders rec, TrendType type)
    {
        var seg = SegOf(rec, type);
        var now = DateTime.Now;

        // ❗ Phase 5-8c5：三類獨立子資料夾，便於分開檢視
        //   <root>/Sensor1/Vibration/trend_Sensor1_yyyyMMdd_HHmmss_Vib.csv
        //   <root>/Sensor1/Tilt/...
        //   <root>/Sensor1/Env/...
        //   <root>/Sensor1/Raw/...
        // ❗ Phase 5-8c10：若是 Smart Log，root 會自動改成 Smart Log 資料夾
        string sensorFolder = GetSensorFolder(rec.SensorName, rec.IsSmartLog);
        string folder = Path.Combine(sensorFolder, TypeFolderName(type));
        Directory.CreateDirectory(folder);
        string safe = string.Join("_", rec.SensorName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrEmpty(safe)) safe = "Sensor";
        string fname = $"trend_{safe}_{now:yyyyMMdd_HHmmss}_{TypeTag(type)}.csv";
        string path = Path.Combine(folder, fname);

        var sw = new StreamWriter(path, append: false, new UTF8Encoding(true));
        // ── Metadata header（仿 VMS2.0；已拿掉 Freq Range）──
        sw.WriteLine($"Device ID:,{Csv(rec.SensorName)}");
        sw.WriteLine($"Date:,{now:yyyy/MM/dd}");
        sw.WriteLine($"Start time:,{now:HH:mm:ss.fff}");
        sw.WriteLine($"Log Type:,{LogTypeText(type)}");
        sw.WriteLine($"Range:,{rec.RangeG}G");
        double intervalMs = type == TrendType.Raw
            ? 1000.0 / Math.Max(1, rec.FreqRange)
            : 1000.0 / Math.Max(1, AppSettingsService.Instance.StatisticsHz);
        sw.WriteLine($"Interval time(ms):,{intervalMs:F1}");
        sw.WriteLine($"Unit:,{UnitText(type)}");
        sw.WriteLine($"Application:,Tranzx iVS 4.0");
        sw.WriteLine();
        sw.WriteLine(ColumnHeader(type));
        sw.Flush();

        lock (seg.Lock)
        {
            seg.Writer = sw;
            seg.FilePath = path;
            seg.SegmentStart = now;
        }

        if (type == TrendType.Raw)
            rec.RawSampleStart = now;
    }

    private static void CloseSegment(SegmentState seg)
    {
        try
        {
            lock (seg.Lock)
            {
                seg.Writer?.Flush();
                seg.Writer?.Dispose();
                seg.Writer = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrendLogger.Close] {ex.Message}");
        }
    }

    /// <summary>檢查是否需要切割到下一個 segment</summary>
    private static void RotateIfNeeded(SensorRecorders rec, TrendType type)
    {
        var seg = SegOf(rec, type);
        if (seg.Writer is null) return;

        // 5-8c10：Smart Log 強制每 60 分鐘切檔（覆蓋 ContinuousRecording 設定）
        if (rec.IsSmartLog)
        {
            var elapsed = DateTime.Now - seg.SegmentStart;
            if (elapsed.TotalMinutes < 60) return;
            CloseSegment(seg);
            OpenSegment(rec, type);
            return;
        }

        // ❗ Phase 5-8c5：持續不間斷錄製 → 跳過切檔（整段錄到停止才切）
        if (AppSettingsService.Instance.ContinuousRecording) return;

        int segMin = type == TrendType.Raw
            ? AppSettingsService.Instance.RawSegmentMinutes
            : AppSettingsService.Instance.TrendSegmentMinutes;
        if (segMin <= 0) return;

        var elapsed2 = DateTime.Now - seg.SegmentStart;
        if (elapsed2.TotalMinutes < segMin) return;

        // 切割：關閉舊檔，開新檔
        CloseSegment(seg);
        OpenSegment(rec, type);
    }

    private static SegmentState SegOf(SensorRecorders rec, TrendType type) => type switch
    {
        TrendType.Vib  => rec.Vib,
        TrendType.Tilt => rec.Tilt,
        TrendType.Env  => rec.Env,
        TrendType.Raw  => rec.Raw,
        _ => rec.Vib
    };

    private static void WriteLine(SegmentState seg, string line)
    {
        if (seg.Writer is null) return;  // mode 未啟用 → 跳過
        try
        {
            lock (seg.Lock)
            {
                seg.Writer?.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrendLogger.Write] {ex.Message}");
        }
    }

    // ─────────── Helpers ───────────
    // 5-8c5：用完整 yyyy/MM/dd HH:mm:ss.fff 才能讓 Excel 完整保留時分秒
    // （只給 HH:mm:ss.fff 時 Excel 會把它當成 elapsed time 並 truncate）
    private static string TimeStamp() => DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.fff");
    private static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
    private static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
    private static string F3(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    private static string F5(double v) => v.ToString("F5", CultureInfo.InvariantCulture);

    private static string TypeTag(TrendType t) => t switch
    {
        TrendType.Vib => "Vib", TrendType.Tilt => "Tilt", TrendType.Env => "Env",
        TrendType.Raw => "Raw", _ => "Trend"
    };

    /// <summary>5-8c5：每類紀錄獨立子資料夾名</summary>
    private static string TypeFolderName(TrendType t) => t switch
    {
        TrendType.Vib  => "Vibration",
        TrendType.Tilt => "Tilt",
        TrendType.Env  => "Env",
        TrendType.Raw  => "Raw",
        _ => "Trend"
    };

    private static string LogTypeText(TrendType t) => t switch
    {
        TrendType.Vib => "Vibration Trend",
        TrendType.Tilt => "Tilt Trend",
        TrendType.Env => "Env Trend",
        TrendType.Raw => "Vibration Raw",
        _ => "Trend"
    };

    private static string UnitText(TrendType t) => t switch
    {
        TrendType.Vib  => "G",
        TrendType.Tilt => "deg",
        TrendType.Env  => "C/%",
        TrendType.Raw  => "G",
        _ => "-"
    };

    private static string ColumnHeader(TrendType t) => t switch
    {
        TrendType.Vib  => "Time,X-peak,Y-peak,Z-peak,X-RMS,Y-RMS,Z-RMS",
        TrendType.Tilt => "Time,X-angle,Y-angle,Z-angle",
        TrendType.Env  => "Time,Temperature,Humidity",
        TrendType.Raw  => "Time,X,Y,Z",
        _ => "Time"
    };

    private static string Csv(string s)
        => s.Contains(',') || s.Contains('"')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}
