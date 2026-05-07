// ============================================================================
// Tranzx.iVS4.App / Services / WarningFeed.cs
//
// Phase 5-8c10：警告監控（與 LiveStatusFeed 分離）
//   LiveStatusFeed 只放連線狀態（連線/斷線/重連）
//   WarningFeed 放振動 / 水平 / 溫濕度的警告訊息
// ============================================================================

using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace Tranzx.iVS4.App.Services;

public sealed class WarningFeed
{
    public static WarningFeed Instance { get; } = new();
    private WarningFeed() { }

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
        // 同步寫到 ErrorLog
        var lvl = kind switch
        {
            FeedKind.Error => LogLevel.Error,
            FeedKind.Warn  => LogLevel.Warn,
            FeedKind.Alarm => LogLevel.Warn,
            _ => LogLevel.Info,
        };
        try { ErrorLogService.Instance.Log(lvl, source, message); } catch { }
    }

    /// <summary>合併推送（同 LiveStatusFeed 邏輯）— 給多軸振動警報用</summary>
    public void PushMerged(FeedKind kind, string source, string segment, int mergeWithinMs = 800)
    {
        var now = DateTime.Now;
        var dp = Application.Current?.Dispatcher;
        if (dp is null) return;

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
            if (Items.Count > 0)
            {
                var top = Items[0];
                if (top.Kind == kind && top.Source == source
                    && (now - top.Time).TotalMilliseconds <= mergeWithinMs)
                {
                    if (!top.Message.Contains(segment))
                    {
                        Items[0] = top with
                        {
                            Time = now,
                            Message = top.Message + "; " + segment
                        };
                    }
                    else
                    {
                        Items[0] = top with { Time = now };
                    }
                    return;
                }
            }
            var item = new FeedItem(now, kind, source, segment);
            Items.Insert(0, item);
            while (Items.Count > MaxItems) Items.RemoveAt(Items.Count - 1);
        });
    }

    public void Clear()
    {
        if (Application.Current?.Dispatcher is { } dp)
            dp.BeginInvoke(() => Items.Clear());
    }
}
