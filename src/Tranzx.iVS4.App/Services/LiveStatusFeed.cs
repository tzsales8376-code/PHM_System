// ============================================================================
// Tranzx.iVS4.App / Services / LiveStatusFeed.cs
//
// Phase 5-8c3：即時狀態監控（取代右下角 toast）
//   - 集中所有 sensor 連線、斷線、重連進度、警報事件
//   - UI 用 ListBox 顯示在主視窗左側面板（不擋繪圖）
//   - 每筆有時間戳、等級色（Info/Warn/Error/Success）、來源、訊息
//   - 自動同步寫到 ErrorLogService（持久化 CSV）
// ============================================================================

using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace Tranzx.iVS4.App.Services;

public enum FeedKind { Info, Warn, Error, Success, Alarm }

public sealed record FeedItem(DateTime Time, FeedKind Kind, string Source, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss");
    public string Icon => Kind switch
    {
        FeedKind.Success => "✓",
        FeedKind.Warn    => "⚠",
        FeedKind.Error   => "⛔",
        FeedKind.Alarm   => "🔔",
        _ => "ℹ",
    };
    public Brush AccentBrush => Kind switch
    {
        FeedKind.Success => new SolidColorBrush(Color.FromRgb(0x1A, 0xBC, 0x9C)),
        FeedKind.Warn    => new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
        FeedKind.Error   => new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
        FeedKind.Alarm   => new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
        _ => new SolidColorBrush(Color.FromRgb(0x52, 0x94, 0xE2)),
    };
}

public sealed class LiveStatusFeed
{
    public static LiveStatusFeed Instance { get; } = new();
    private LiveStatusFeed() { }

    /// <summary>最新事件在頂端（Insert(0)）；最多保留 30 筆（5-8c8 從 200 → 30）</summary>
    public ObservableCollection<FeedItem> Items { get; } = new();
    private const int MaxItems = 30;

    public void Push(FeedKind kind, string source, string message)
    {
        var item = new FeedItem(DateTime.Now, kind, source, message);

        if (Application.Current?.Dispatcher is { } dp)
        {
            dp.BeginInvoke(() =>
            {
                Items.Insert(0, item);
                while (Items.Count > MaxItems) Items.RemoveAt(Items.Count - 1);
            });
        }

        // 自動同步寫到 ErrorLog（持久化 CSV）
        var lvl = kind switch
        {
            FeedKind.Error => LogLevel.Error,
            FeedKind.Warn  => LogLevel.Warn,
            FeedKind.Alarm => LogLevel.Warn,
            _ => LogLevel.Info,
        };
        try { ErrorLogService.Instance.Log(lvl, source, message); } catch { }
    }

    /// <summary>清除所有 feed 紀錄（不影響已寫入 CSV 的）</summary>
    public void Clear()
    {
        if (Application.Current?.Dispatcher is { } dp)
            dp.BeginInvoke(() => Items.Clear());
    }

    /// <summary>
    /// 5-8c8：警報合併推送
    ///   若最近 mergeWithinMs 毫秒內已有同 source + 同 kind 的條目（且仍是頂端第一筆）
    ///   → 把新 segment 接到那條訊息後面（如「X=0.50→Red; Y=0.60→Red」）
    ///   否則新增一筆
    ///   ErrorLog 仍逐條完整紀錄（無合併）
    /// </summary>
    public void PushMerged(FeedKind kind, string source, string segment, int mergeWithinMs = 800)
    {
        var now = DateTime.Now;
        var dp = Application.Current?.Dispatcher;
        if (dp is null) return;

        // 同步寫 ErrorLog（每筆都記，不合併）
        var lvl = kind switch
        {
            FeedKind.Error => LogLevel.Error,
            FeedKind.Warn  => LogLevel.Warn,
            FeedKind.Alarm => LogLevel.Warn,
            _ => LogLevel.Info,
        };
        try { ErrorLogService.Instance.Log(lvl, source, segment); } catch { }

        dp.BeginInvoke(() =>
        {
            // 嘗試與第 0 筆合併
            if (Items.Count > 0)
            {
                var top = Items[0];
                if (top.Kind == kind && top.Source == source
                    && (now - top.Time).TotalMilliseconds <= mergeWithinMs)
                {
                    // 不重複 segment（同 key 升級時 message 開頭可能完全相同）
                    if (!top.Message.Contains(segment))
                    {
                        var merged = top with
                        {
                            Time = now,
                            Message = top.Message + "; " + segment
                        };
                        Items[0] = merged;
                    }
                    else
                    {
                        // 只更新時間，避免畫面看起來「卡住」
                        Items[0] = top with { Time = now };
                    }
                    return;
                }
            }

            // 不能合併 → 新增
            var item = new FeedItem(now, kind, source, segment);
            Items.Insert(0, item);
            while (Items.Count > MaxItems) Items.RemoveAt(Items.Count - 1);
        });
    }
}
