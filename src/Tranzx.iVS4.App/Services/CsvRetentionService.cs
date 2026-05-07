// ============================================================================
// Tranzx.iVS4.App / Services / CsvRetentionService.cs
//
// 自動清理舊 CSV，避免硬碟塞爆。
//   啟動時呼叫 CleanupAll() 一次
//   每天 0 點再呼叫一次（DispatcherTimer 24h 觸發）
//
// 邏輯：
//   1. 掃描 TrendLogFolder 內所有 *.csv（包含 Sensor 子資料夾）
//   2. 依檔名標籤分流：含 _Raw.csv → 用 RawRetentionDays，否則 TrendRetentionDays
//   3. 找出 LastWriteTime 早於 (now - retentionDays) 的檔案
//   4. 排序最舊優先，**一次只刪 7 天份**（避免 IO 衝擊）— 從最舊那一天起算 7 天內的檔
//
// 也提供 ManualCleanup(folder, retentionDays) 給設定對話框「立即清理」按鈕用。
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace Tranzx.iVS4.App.Services;

public static class CsvRetentionService
{
    private static DispatcherTimer? _dailyTimer;

    /// <summary>App 啟動時呼叫一次，並啟動每日 timer</summary>
    public static void Initialize()
    {
        try
        {
            CleanupAll();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Retention.Init] {ex.Message}");
        }

        // 每 24 小時清理一次
        _dailyTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        _dailyTimer.Tick += (_, _) => CleanupAll();
        _dailyTimer.Start();
    }

    /// <summary>清理 trend folder 內所有檔案（依 trend / raw 各自的保留天數）</summary>
    public static void CleanupAll()
    {
        var s = AppSettingsService.Instance;
        try
        {
            CleanupFolder(s.TrendLogFolder, s.TrendRetentionDays, isRaw: false);
            CleanupFolder(s.TrendLogFolder, s.RawRetentionDays, isRaw: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Retention.All] {ex.Message}");
        }
    }

    /// <summary>立即清理：給設定對話框「立即清理」按鈕用</summary>
    public static int ManualCleanup(string folder, int trendRetention, int rawRetention)
    {
        int total = 0;
        total += CleanupFolder(folder, trendRetention, isRaw: false);
        total += CleanupFolder(folder, rawRetention,   isRaw: true);
        return total;
    }

    /// <summary>實際清理邏輯：刪除超過保留期、最舊那 7 天份的檔</summary>
    /// <returns>已刪除的檔案數</returns>
    private static int CleanupFolder(string folder, int retentionDays, bool isRaw)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return 0;
        if (retentionDays <= 0) return 0;

        var cutoff = DateTime.Now.Date.AddDays(-retentionDays);
        // 過期的 csv，依檔名是否含 _Raw 區分
        var files = Directory.EnumerateFiles(folder, "*.csv", SearchOption.AllDirectories)
            .Where(f =>
            {
                bool fileIsRaw = Path.GetFileNameWithoutExtension(f)
                    .EndsWith("_Raw", StringComparison.OrdinalIgnoreCase);
                return fileIsRaw == isRaw;
            })
            .Select(f => new { Path = f, Time = File.GetLastWriteTime(f) })
            .Where(x => x.Time < cutoff)
            .OrderBy(x => x.Time)
            .ToList();

        if (files.Count == 0) return 0;

        // 一次只刪「最舊那一天起 7 天內」的檔（避免一次 IO 太重）
        var oldestDate = files[0].Time.Date;
        var batchEnd   = oldestDate.AddDays(7);

        int deleted = 0;
        foreach (var f in files)
        {
            if (f.Time.Date >= batchEnd) break;
            try
            {
                File.Delete(f.Path);
                deleted++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Retention.Delete] {f.Path}: {ex.Message}");
            }
        }
        return deleted;
    }
}
