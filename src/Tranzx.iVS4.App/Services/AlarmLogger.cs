// ============================================================================
// Tranzx.iVS4.App / Services / AlarmLogger.cs
//
// 警報紀錄服務（singleton）：
//   - 偵測 alarm level 轉變（綠/黃/紅互相切換）
//   - 寫入 CSV：每 Sensor 每天一個檔 alarm_{SensorName}_{yyyy-MM-dd}.csv
//   - 包含綠燈解除（從黃/紅回到綠）以便算 Alarm 持續時間
//   - 維護今日計數（綠/黃/紅）供 UI 顯示
//   - 跨日自動 reset 計數
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Tranzx.iVS4.App.Models;

namespace Tranzx.iVS4.App.Services;

public sealed partial class AlarmLogger : ObservableObject
{
    public static AlarmLogger Instance { get; } = new();

    private readonly object _lock = new();
    /// <summary>
    /// 每個 (sensorIdx, key) 的當前等級。例 (0, "XPeak") -> Green
    /// 用來偵測等級轉變
    /// </summary>
    private readonly Dictionary<(int, string), AlarmLevel> _currentLevels = new();
    private DateTime _todayDate = DateTime.Today;

    // 今日計數（給左側面板綁定）
    [ObservableProperty] private int todayGreenCount;
    [ObservableProperty] private int todayYellowCount;
    [ObservableProperty] private int todayRedCount;

    /// <summary>檢查單一 Sensor 內所有量值是否有等級轉變</summary>
    /// <param name="sensorIdx">Sensor index（0-based）</param>
    /// <param name="sensorName">Sensor 名稱（用於檔名）</param>
    /// <param name="t">該 Sensor 的閾值集合</param>
    /// <param name="values">當前各量值的數值</param>
    public void CheckChannel(int sensorIdx, string sensorName,
                              ChannelAlarmThresholds t,
                              IReadOnlyDictionary<string, double> values)
    {
        if (t is null || values is null || string.IsNullOrEmpty(sensorName)) return;
        foreach (var kv in values)
            CheckOne(sensorIdx, sensorName, kv.Key, kv.Value, t);
    }

    private void CheckOne(int sensorIdx, string sensorName, string key, double value,
                           ChannelAlarmThresholds t)
    {
        AlarmThreshold? thr = key switch
        {
            "XPeak"  => t.XPeak,  "YPeak"  => t.YPeak,  "ZPeak"  => t.ZPeak,
            "XRms"   => t.XRms,   "YRms"   => t.YRms,   "ZRms"   => t.ZRms,
            "AngleX" => t.AngleX, "AngleY" => t.AngleY, "AngleZ" => t.AngleZ,
            "Temp"   => t.Temp,   "Hum"    => t.Hum,
            _ => null,
        };
        if (thr is null) return;

        var newLevel = thr.Level(value);
        AlarmLevel prev;
        var k = (sensorIdx, key);
        lock (_lock)
        {
            prev = _currentLevels.TryGetValue(k, out var l) ? l : AlarmLevel.Green;
            // 第一次：直接設定（不寫 CSV，避免啟動時對所有量值都當作轉變）
            if (!_currentLevels.ContainsKey(k))
            {
                _currentLevels[k] = newLevel;
                return;
            }
            if (newLevel == prev) return;
            _currentLevels[k] = newLevel;
        }

        // 寫 CSV（背景 thread 寫，I/O 不擋 UI）
        WriteCsvSafely(sensorName, key, prev, newLevel, value, thr.Yellow, thr.Red);

        // ❗ Phase 5-7：警報音效（依等級播不同系統音）
        PlayAlarmSound(newLevel);

        // ❗ Phase 5-8c3：警報改用即時狀態監控面板（不再用右下角彈窗）
        // ❗ Phase 5-8c8：同一 Sensor 短時間內多軸警報合併成一條訊息
        // ❗ Phase 5-8c10：振動警報改寫到 WarningFeed（連線狀態用 LiveStatusFeed）
        if (newLevel == AlarmLevel.Yellow || newLevel == AlarmLevel.Red)
        {
            var s = AppSettingsService.Instance;
            bool show = s.AlarmToastEnabled
                && ((newLevel == AlarmLevel.Yellow && s.AlarmToastOnYellow)
                 || (newLevel == AlarmLevel.Red    && s.AlarmToastOnRed));
            if (show)
            {
                WarningFeed.Instance.PushMerged(FeedKind.Alarm, sensorName,
                    $"{key}={value:F4}→{newLevel}");
            }
        }

        // 更新今日計數（dispatcher 切換到 UI thread 觸發 OnPropertyChanged）
        IncCounter(newLevel);
    }

    private static void PlayAlarmSound(AlarmLevel level)
    {
        if (!AppSettingsService.Instance.AlarmSoundEnabled) return;
        try
        {
            switch (level)
            {
                case AlarmLevel.Yellow:
                    System.Media.SystemSounds.Asterisk.Play();
                    break;
                case AlarmLevel.Red:
                    System.Media.SystemSounds.Hand.Play();
                    break;
                // 綠燈解除不播（避免吵）
            }
        }
        catch { /* SystemSounds 失敗不影響主流程 */ }
    }

    private void WriteCsvSafely(string sensorName, string key,
                                 AlarmLevel prev, AlarmLevel next,
                                 double value, double yellow, double red)
    {
        try
        {
            CheckRolloverDay();
            var folder = AppSettingsService.Instance.AlarmLogFolder;
            if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder);

            // 檔名安全化
            string safe = string.Join("_", sensorName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrEmpty(safe)) safe = "Sensor";
            string file = Path.Combine(folder,
                $"alarm_{safe}_{DateTime.Today:yyyy-MM-dd}.csv");
            bool isNew = !File.Exists(file);

            // 寫一行（append + UTF-8 BOM 給 Excel 讀繁體中文）
            using var sw = new StreamWriter(file, append: true,
                isNew ? new UTF8Encoding(true) : new UTF8Encoding(false));
            if (isNew)
            {
                // Metadata header 區（仿 VMS2.0 trend csv 格式）
                sw.WriteLine($"Device ID:,{Csv(sensorName)}");
                sw.WriteLine($"Date:,{DateTime.Today:yyyy/MM/dd}");
                sw.WriteLine($"Start time:,{DateTime.Now:HH:mm:ss.fff}");
                sw.WriteLine($"Log Type:,Alarm Log");
                sw.WriteLine($"Application:,Tranzx iVS 4.0");
                sw.WriteLine();
                sw.WriteLine("Timestamp,Sensor,Key,FromLevel,ToLevel,Value,Yellow,Red");
            }
            sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}," +
                         $"{Csv(sensorName)},{key},{prev},{next}," +
                         $"{value:F4},{yellow:F4},{red:F4}");
        }
        catch (Exception ex)
        {
            // 寫檔失敗不能讓上層 crash
            System.Diagnostics.Debug.WriteLine($"[AlarmLogger] Write fail: {ex.Message}");
        }
    }

    private static string Csv(string s)
        => s.Contains(',') || s.Contains('"')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;

    private void IncCounter(AlarmLevel level)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(() =>
        {
            CheckRolloverDay();
            switch (level)
            {
                case AlarmLevel.Green:  TodayGreenCount++;  break;
                case AlarmLevel.Yellow: TodayYellowCount++; break;
                case AlarmLevel.Red:    TodayRedCount++;    break;
            }
        });
    }

    private void CheckRolloverDay()
    {
        var today = DateTime.Today;
        if (today == _todayDate) return;
        _todayDate = today;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(() =>
        {
            TodayGreenCount = 0;
            TodayYellowCount = 0;
            TodayRedCount = 0;
        });
    }

    /// <summary>Reset 計數（給「清除今日計數」按鈕用，未必使用）</summary>
    public void ResetTodayCounters()
    {
        TodayGreenCount = 0;
        TodayYellowCount = 0;
        TodayRedCount = 0;
    }
}
