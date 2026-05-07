// ============================================================================
// Tranzx.iVS4.App / Services / SmartLogMonitor.cs
//
// Phase 5-8c6：Smart Log 智慧錄製
//   參考 VMS V2.0 / V3.0 的 Smart Log 設計：
//
//   - 啟用後，每個 Sensor 自動監聽其 max(|PeakX|,|PeakY|,|PeakZ|)
//   - 觸發條件：max ≥ StartG 持續 StartHoldSec 秒 → 啟動該 Sensor 錄製
//   - 停止條件：max <  StopG  持續 StopHoldSec  秒 → 停止該 Sensor 錄製
//   - 若實際錄製時長 < MinRecordSec → 視為雜訊，可保留事件 log 但 CSV 已寫入
//
//   每個 Sensor 獨立 state machine：
//     Idle ──(超閾值持續 StartHoldSec)──▶ Recording ──(低閾值持續 StopHoldSec)──▶ Idle
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using Tranzx.iVS4.App.ViewModels;
using Tranzx.iVS4.Communication.Transport;

namespace Tranzx.iVS4.App.Services;

public sealed class SmartLogMonitor
{
    public static SmartLogMonitor Instance { get; } = new();
    private SmartLogMonitor() { }

    private enum SensorState { Idle, AboveHolding, Recording, BelowHolding }

    private sealed class State
    {
        public required ChannelViewModel Vm { get; init; }
        public SensorState Stage = SensorState.Idle;
        public DateTime AboveSince;        // 超閾值起始時間
        public DateTime BelowSince;        // 低閾值起始時間
        public DateTime RecordStartedAt;   // 實際錄製開始時間
    }

    private readonly ConcurrentDictionary<int, State> _states = new();

    /// <summary>App 啟動時對每個 SensorTab 註冊</summary>
    public void Register(ChannelViewModel vm)
    {
        var st = new State { Vm = vm };
        _states[vm.Index] = st;
        vm.PropertyChanged += (_, e) => OnVmPropertyChanged(st, e);
    }

    private void OnVmPropertyChanged(State st, PropertyChangedEventArgs e)
    {
        // 每次 PeakX/Y/Z 任一更新 → 重新評估
        // (PeakZ 通常更新最晚，等它觸發再算可避免一次 stats 觸發 3 次)
        if (e.PropertyName != nameof(ChannelViewModel.PeakZ)) return;
        if (!AppSettingsService.Instance.SmartLogEnabled) return;

        // 必須先連線（沒連線時不啟動）
        if (st.Vm.State != TransportState.Connected) return;

        Evaluate(st);
    }

    private static double Max3(double x, double y, double z)
    {
        double a = Math.Abs(x);
        double b = Math.Abs(y);
        double c = Math.Abs(z);
        if (b > a) a = b;
        if (c > a) a = c;
        return a;
    }

    private void Evaluate(State st)
    {
        var s = AppSettingsService.Instance;
        double level = Max3(st.Vm.PeakX, st.Vm.PeakY, st.Vm.PeakZ);
        var now = DateTime.Now;

        switch (st.Stage)
        {
            case SensorState.Idle:
                if (level >= s.SmartStartG)
                {
                    st.AboveSince = now;
                    st.Stage = SensorState.AboveHolding;
                }
                break;

            case SensorState.AboveHolding:
                if (level < s.SmartStartG)
                {
                    // 跌回去 → 復位
                    st.Stage = SensorState.Idle;
                }
                else if ((now - st.AboveSince).TotalSeconds >= s.SmartStartHoldSec)
                {
                    // 觸發！啟動錄製
                    StartRecording(st);
                }
                break;

            case SensorState.Recording:
                if (level < s.SmartStopG)
                {
                    st.BelowSince = now;
                    st.Stage = SensorState.BelowHolding;
                }
                break;

            case SensorState.BelowHolding:
                if (level >= s.SmartStopG)
                {
                    // 又升回去 → 維持錄製
                    st.Stage = SensorState.Recording;
                }
                else if ((now - st.BelowSince).TotalSeconds >= s.SmartStopHoldSec)
                {
                    // 確定停止
                    StopRecording(st);
                }
                break;
        }
    }

    private void StartRecording(State st)
    {
        st.Stage = SensorState.Recording;
        st.RecordStartedAt = DateTime.Now;

        // 透過 UI thread 設定 IsRecording = true（會觸發 TrendLogger 開檔）
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // 已經在錄就不重啟（避免人手動 + Smart Log 雙重觸發）
            if (!st.Vm.IsRecording)
            {
                st.Vm.IsSmartLogRecording = true;  // 5-8c10：標記為 Smart Log 觸發
                st.Vm.IsRecording = true;
            }
        });

        var s = AppSettingsService.Instance;
        LiveStatusFeed.Instance.Push(FeedKind.Info, st.Vm.DisplayName,
            $"🎯 Smart Log 觸發（≥{s.SmartStartG:F3}G 持續 {s.SmartStartHoldSec:F1}s）→ 開始錄製");
    }

    private void StopRecording(State st)
    {
        var dur = DateTime.Now - st.RecordStartedAt;
        st.Stage = SensorState.Idle;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (st.Vm.IsRecording)
            {
                st.Vm.IsRecording = false;
            }
        });

        var s = AppSettingsService.Instance;
        bool tooShort = dur.TotalSeconds < s.SmartMinRecordSec;
        var kind = tooShort ? FeedKind.Warn : FeedKind.Success;
        string note = tooShort ? "（短於最小事件時長，視為雜訊）" : "";
        LiveStatusFeed.Instance.Push(kind, st.Vm.DisplayName,
            $"⏹ Smart Log 停止錄製，事件時長 {dur.TotalSeconds:F1}s {note}");
    }

    /// <summary>停用 Smart Log 時呼叫，把所有錄製中的 Sensor 都停下</summary>
    public void StopAllIfActive()
    {
        foreach (var st in _states.Values)
        {
            if (st.Stage == SensorState.Recording || st.Stage == SensorState.BelowHolding)
            {
                StopRecording(st);
            }
            else
            {
                st.Stage = SensorState.Idle;
            }
        }
    }
}
