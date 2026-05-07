// ============================================================================
// Tranzx.iVS4.App / Services / ErrorLogService.cs
//
// Phase 5-8c2：軟體狀況監控（Error / Warn / Info 事件 log）
//   - 記錄到 %LocalAppData%\Tranzx.iVS4\Logs\app_yyyyMMdd.csv
//   - In-memory 最近 N 筆給 UI 顯示
//   - 自動依日期切割
// ============================================================================

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;

namespace Tranzx.iVS4.App.Services;

public enum LogLevel { Info, Warn, Error }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Source, string Message)
{
    public string LevelText => Level switch
    {
        LogLevel.Error => "ERROR",
        LogLevel.Warn  => "WARN",
        _ => "INFO"
    };
}

public sealed class ErrorLogService
{
    public static ErrorLogService Instance { get; } = new();
    private ErrorLogService() { }

    /// <summary>記憶體保留最近 500 筆給 UI 顯示</summary>
    public ObservableCollection<LogEntry> Recent { get; } = new();
    private const int MaxRecent = 500;

    private static readonly string LogFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tranzx PHM", "Logs");

    private readonly object _lock = new();

    public void Log(LogLevel level, string source, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, source, message);

        // UI thread：加到 ObservableCollection
        if (Application.Current?.Dispatcher is { } dp)
        {
            dp.BeginInvoke(() =>
            {
                Recent.Insert(0, entry);
                while (Recent.Count > MaxRecent) Recent.RemoveAt(Recent.Count - 1);
            });
        }

        // 寫檔（背景，不擋呼叫端）
        System.Threading.Tasks.Task.Run(() => WriteToFile(entry));
    }

    public void Info(string source, string message) => Log(LogLevel.Info, source, message);
    public void Warn(string source, string message) => Log(LogLevel.Warn, source, message);
    public void Error(string source, string message) => Log(LogLevel.Error, source, message);

    private void WriteToFile(LogEntry e)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            string file = Path.Combine(LogFolder, $"app_{e.Time:yyyyMMdd}.csv");
            bool isNew = !File.Exists(file);
            lock (_lock)
            {
                using var sw = new StreamWriter(file, append: true,
                    isNew ? new UTF8Encoding(true) : new UTF8Encoding(false));
                if (isNew)
                {
                    sw.WriteLine($"Date:,{e.Time:yyyy/MM/dd}");
                    sw.WriteLine($"Application:,Tranzx iVS 4.0");
                    sw.WriteLine();
                    sw.WriteLine("Timestamp,Level,Source,Message");
                }
                sw.WriteLine($"{e.Time:yyyy-MM-dd HH:mm:ss.fff},{e.LevelText},{Csv(e.Source)},{Csv(e.Message)}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ErrorLog.Write] {ex.Message}");
        }
    }

    public string LogFolderPath => LogFolder;

    private static string Csv(string s)
        => s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}
