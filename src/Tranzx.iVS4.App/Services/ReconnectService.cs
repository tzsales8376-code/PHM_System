// ============================================================================
// Tranzx.iVS4.App / Services / ReconnectService.cs
//
// Phase 5-8c：Sensor 自動重連
//
//   觸發條件：
//     1. 使用者點過「連線」（或同步啟動）→ Sensor 有「user-intended connected」狀態
//     2. SensorChannel.OnStateChanged 收到 Disconnected / Faulted（非使用者主動斷線）
//     ⇒ 啟動重連協程，每隔 ReconnectIntervalSec 嘗試一次，最多 ReconnectAttempts 次
//
//   特殊情況：
//     - 使用者手動斷線（呼叫 NotifyUserDisconnect）→ 清掉 user-intended，不再重連
//     - ReconnectAttempts == 0 → 完全停用自動重連
//     - 重連途中收到 Connected → 重置計數，視為成功
//
//   Thread-safety：
//     每個 Sensor 一個 _CancellationTokenSource，以 lock 保護啟停
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Tranzx.iVS4.App.ViewModels;
using Tranzx.iVS4.Communication;
using Tranzx.iVS4.Communication.Transport;

namespace Tranzx.iVS4.App.Services;

public sealed class ReconnectService
{
    public static ReconnectService Instance { get; } = new();
    private ReconnectService() { }

    /// <summary>每個 Sensor 的重連狀態</summary>
    private sealed class State
    {
        public required SensorTabViewModel Tab { get; init; }
        public bool UserIntended;
        public CancellationTokenSource? Cts;
        public int AttemptCount;
        public readonly object Lock = new();
    }

    private readonly ConcurrentDictionary<int, State> _states = new();

    /// <summary>App 啟動時呼叫一次：對每個 SensorTab 訂閱 OnStateChanged</summary>
    public void Register(SensorTabViewModel tab)
    {
        var st = new State { Tab = tab };
        _states[tab.Channel.Index] = st;
        tab.Channel.Channel.OnStateChanged += (ch, ts) =>
        {
            // 診斷追蹤（背景，不上 Feed）
            try
            {
                ErrorLogService.Instance.Info(tab.Channel.DisplayName,
                    $"Transport state → {ts} (UserIntended={st.UserIntended})");
            }
            catch { }
            OnStateChanged(st, ts);
        };
    }

    /// <summary>使用者按下「連線」（或同步啟動成功）後呼叫，標記為 user-intended</summary>
    public void NotifyUserConnect(int sensorIdx)
    {
        if (!_states.TryGetValue(sensorIdx, out var st)) return;
        lock (st.Lock)
        {
            st.UserIntended = true;
            st.AttemptCount = 0;
            CancelLocked(st);
        }
        LiveStatusFeed.Instance.Push(FeedKind.Success, st.Tab.Channel.DisplayName, "已連線");
    }

    /// <summary>使用者按下「斷線」後呼叫，停止任何進行中的重試</summary>
    public void NotifyUserDisconnect(int sensorIdx)
    {
        if (!_states.TryGetValue(sensorIdx, out var st)) return;
        lock (st.Lock)
        {
            st.UserIntended = false;
            st.AttemptCount = 0;
            CancelLocked(st);
        }
        LiveStatusFeed.Instance.Push(FeedKind.Info, st.Tab.Channel.DisplayName, "已主動斷線");
    }

    /// <summary>停止所有 Sensor 的重連（App 退出時）</summary>
    public void StopAll()
    {
        foreach (var st in _states.Values)
        {
            lock (st.Lock)
            {
                st.UserIntended = false;
                CancelLocked(st);
            }
        }
    }

    private void OnStateChanged(State st, TransportState ts)
    {
        // 整個 state-change 處理移到 ThreadPool — 避免阻塞 SerialPort callback thread
        // 也避免在持有 transport 內部 lock 時嘗試取得 ReconnectService lock 造成連鎖等待
        _ = Task.Run(() => HandleStateChange(st, ts));
    }

    private void HandleStateChange(State st, TransportState ts)
    {
        switch (ts)
        {
            case TransportState.Connected:
                lock (st.Lock)
                {
                    st.AttemptCount = 0;
                    CancelLocked(st);
                }
                break;

            case TransportState.Disconnected:
            case TransportState.Faulted:
                CancellationToken token;
                lock (st.Lock)
                {
                    if (!st.UserIntended) return;
                    if (st.Cts is not null) return;
                    int max = AppSettingsService.Instance.ReconnectAttempts;
                    if (max <= 0)
                    {
                        LiveStatusFeed.Instance.Push(FeedKind.Warn, st.Tab.Channel.DisplayName,
                            "連線中斷（自動重連已停用，請至設定開啟）");
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        { st.Tab.IsConnected = false; });
                        return;
                    }
                    st.Cts = new CancellationTokenSource();
                    token = st.Cts.Token;
                }
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    st.Tab.IsConnected = false;
                });
                _ = Task.Run(() => RunReconnectLoopAsync(st, token));
                break;
        }
    }

    private static void CancelLocked(State st)
    {
        try { st.Cts?.Cancel(); } catch { }
        st.Cts?.Dispose();
        st.Cts = null;
    }

    private async Task RunReconnectLoopAsync(State st, CancellationToken token)
    {
        try
        {
            string sensor = st.Tab.Channel.DisplayName;
            int max = AppSettingsService.Instance.ReconnectAttempts;
            int intervalSec = AppSettingsService.Instance.ReconnectIntervalSec;
            if (intervalSec < 1) intervalSec = 10;

            int attempt = 0;
            while (!token.IsCancellationRequested && attempt < max)
            {
                attempt++;
                lock (st.Lock) { st.AttemptCount = attempt; }
                NotifyStatus(sensor, attempt, max, intervalSec, "wait");

                // 等候間隔（可被中斷）
                try { await Task.Delay(TimeSpan.FromSeconds(intervalSec), token); }
                catch (OperationCanceledException) { return; }

                if (token.IsCancellationRequested) return;
                if (!st.UserIntended) return;

                // 檢查是否已連回（user 可能手動連線了）
                if (st.Tab.Channel.Channel.Transport.State == TransportState.Connected)
                {
                    lock (st.Lock) { st.AttemptCount = 0; CancelLocked(st); }
                    return;
                }

                NotifyStatus(sensor, attempt, max, intervalSec, "trying");
                bool ok = false;
                try
                {
                    ok = await st.Tab.Channel.Channel.ConnectAsync(token);
                    if (ok)
                    {
                        await st.Tab.Channel.Channel.ApplyConfigAsync(verify: false);
                        // 通知 UI thread 同步 IsConnected 狀態
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            st.Tab.IsConnected = true;
                            st.Tab.IsPausedPlot = false;
                        });
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Reconnect] {sensor}: {ex.Message}");
                }

                if (ok)
                {
                    NotifyStatus(sensor, attempt, max, intervalSec, "success");
                    lock (st.Lock) { st.AttemptCount = 0; CancelLocked(st); }
                    return;
                }
            }

            // 用完所有次數
            if (!token.IsCancellationRequested)
            {
                NotifyStatus(st.Tab.Channel.DisplayName, attempt, max, intervalSec, "giveup");
                lock (st.Lock) { CancelLocked(st); }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reconnect.Loop] {ex.Message}");
        }
    }

    private static void NotifyStatus(string sensor, int attempt, int max, int intervalSec, string phase)
    {
        // ❗ Phase 5-8c3：改用即時狀態監控面板（不再用右下角彈窗）
        switch (phase)
        {
            case "wait":
                Instance.StatusChanged?.Invoke($"⟳ {sensor}: reconnecting in {intervalSec}s ({attempt}/{max})", phase);
                if (attempt == 1)
                {
                    LiveStatusFeed.Instance.Push(FeedKind.Warn, sensor,
                        $"連線中斷，啟動自動重連（{max} 次 × {intervalSec} 秒）");
                }
                else
                {
                    LiveStatusFeed.Instance.Push(FeedKind.Info, sensor,
                        $"等待 {intervalSec} 秒後第 {attempt}/{max} 次重試…");
                }
                break;

            case "trying":
                Instance.StatusChanged?.Invoke($"⟳ {sensor}: attempting ({attempt}/{max})…", phase);
                LiveStatusFeed.Instance.Push(FeedKind.Info, sensor,
                    $"第 {attempt}/{max} 次嘗試重新連線…");
                break;

            case "success":
                Instance.StatusChanged?.Invoke($"✓ {sensor}: reconnected (attempt {attempt})", phase);
                LiveStatusFeed.Instance.Push(FeedKind.Success, sensor,
                    $"重新連線成功（第 {attempt} 次嘗試）");
                break;

            case "giveup":
                Instance.StatusChanged?.Invoke($"✗ {sensor}: failed after {max} attempts", phase);
                LiveStatusFeed.Instance.Push(FeedKind.Error, sensor,
                    $"重新連線失敗，已停止（共試 {max} 次）");
                break;
        }
    }

    /// <summary>UI 訂閱此事件以顯示重連狀態（status bar / toast）</summary>
    public event Action<string, string>? StatusChanged;
}
