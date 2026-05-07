using System.Windows;
using Tranzx.iVS4.App.Services;

namespace Tranzx.iVS4.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ❗ Phase 5-7：啟動時載入持久化設定（alarm 資料夾、音效偏好等）
        AppSettingsService.Instance.Load();

        // ❗ Phase 5-7b：啟動 CSV 自動清理服務（依保留天數刪除舊 trend / raw csv）
        CsvRetentionService.Initialize();

        // 套用初始字型大小（從 AppSettingsService 預設值）
        AppSettingsService.Instance.ApplyFontScale();

        // 啟動時依系統語系自動切換
        var detected = LocalizationService.Instance.DetectSystemLanguage();
        if (detected != "zh-TW")
            LocalizationService.Instance.SetLanguage(detected);

        // ❗ Phase 5-8c2：啟動事件 log
        ErrorLogService.Instance.Info("App", $"Tranzx iVS 4.0 started.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // ❗ Phase 5-8c2：關閉事件 log
        try { ErrorLogService.Instance.Info("App", "Tranzx iVS 4.0 exiting."); } catch { }
        // ❗ Phase 5-7：關閉所有 trend recorder，確保 buffer flush 到磁碟
        try { TrendLogger.Instance.StopAll(); } catch { }
        // ❗ Phase 5-8c：停止所有 sensor 重連協程
        try { ReconnectService.Instance.StopAll(); } catch { }
        base.OnExit(e);
    }
}
