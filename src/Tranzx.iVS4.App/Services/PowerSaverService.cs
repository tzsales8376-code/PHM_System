// ============================================================================
// Tranzx.iVS4.App / Services / PowerSaverService.cs
//
// Phase 5-8c8：節能模式（VMS 2.0 風格）
//   - 主視窗監聽全域 mouse / keyboard 事件 → 重置 last activity 時間
//   - 每分鐘檢查一次：若 (Now - LastActivity) > IdleMin → ChartsPaused = true
//   - 圖表訂閱者收到 ChartsPaused = true 時暫停渲染（但資料仍持續累積）
//   - 任何輸入立刻復原 ChartsPaused = false
//   - 設定關閉時 ChartsPaused 永遠為 false
// ============================================================================

using System;
using System.Windows;
using System.Windows.Threading;

namespace Tranzx.iVS4.App.Services;

public sealed class PowerSaverService
{
    public static PowerSaverService Instance { get; } = new();
    private PowerSaverService() { }

    private DateTime _lastActivity = DateTime.Now;
    private DispatcherTimer? _checkTimer;
    private bool _paused;

    /// <summary>true = 圖表暫停渲染中（節能）</summary>
    public bool ChartsPaused
    {
        get => _paused;
        private set
        {
            if (_paused == value) return;
            _paused = value;
            ChartsPausedChanged?.Invoke(value);
            // 推進即時狀態監控（有事件可追蹤就不會莫名其妙）
            try
            {
                var s = AppSettingsService.Instance;
                if (value)
                {
                    LiveStatusFeed.Instance.Push(FeedKind.Info, "PowerSaver",
                        $"💤 閒置 {s.PowerSaverIdleMin} 分鐘，圖表已暫停以節省效能");
                }
                else
                {
                    LiveStatusFeed.Instance.Push(FeedKind.Success, "PowerSaver",
                        "▶ 偵測到操作，圖表已恢復");
                }
            }
            catch { }
        }
    }

    /// <summary>圖表訂閱：true=暫停 / false=恢復</summary>
    public event Action<bool>? ChartsPausedChanged;

    /// <summary>App 啟動時呼叫一次，註冊主視窗事件</summary>
    public void AttachTo(Window mainWindow)
    {
        // 多種輸入事件全部視為「有人在操作」
        // PreviewMouseMove → 滑鼠移動
        // PreviewMouseDown → 按下鍵
        // PreviewKeyDown → 鍵盤
        // 用 PreviewXxx 確保即使子控件 handle 掉也接收得到
        mainWindow.PreviewMouseMove += (_, _) => OnUserActivity();
        mainWindow.PreviewMouseDown += (_, _) => OnUserActivity();
        mainWindow.PreviewKeyDown   += (_, _) => OnUserActivity();
        mainWindow.PreviewMouseWheel += (_, _) => OnUserActivity();

        // 切到/切回視窗時也算「有活動」
        mainWindow.Activated += (_, _) => OnUserActivity();

        // 每 30 秒檢查一次（足夠及時且不耗 CPU）
        _checkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _checkTimer.Tick += (_, _) => Check();
        _checkTimer.Start();
    }

    private void OnUserActivity()
    {
        _lastActivity = DateTime.Now;
        if (_paused) ChartsPaused = false;
    }

    private void Check()
    {
        var s = AppSettingsService.Instance;
        if (!s.PowerSaverEnabled)
        {
            // 設定關閉 → 確保 ChartsPaused 一定是 false
            if (_paused) ChartsPaused = false;
            return;
        }

        var idle = DateTime.Now - _lastActivity;
        if (idle.TotalMinutes >= s.PowerSaverIdleMin)
        {
            ChartsPaused = true;
        }
    }
}
