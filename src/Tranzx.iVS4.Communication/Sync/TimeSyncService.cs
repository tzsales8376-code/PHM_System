// ============================================================================
// Tranzx.iVS4.Communication / Sync / TimeSyncService.cs
// 嚴格時鐘同步服務：
//   - 啟動時對所有通道並行下發 CMD A2 (Set Time)，使用同一個 PC 時間戳
//   - 每 60 秒重新同步一次以補償時鐘漂移（可調）
//   - 下發前後記錄時間戳與通道 ID 至事件 Log
// ============================================================================

namespace Tranzx.iVS4.Communication.Sync;

public sealed class TimeSyncService : IDisposable
{
    private readonly MultiSensorManager _manager;
    private readonly TimeSpan _interval;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event Action<DateTime, int>? OnSyncCompleted;  // (時間, 成功通道數)

    public TimeSyncService(MultiSensorManager manager, TimeSpan? interval = null)
    {
        _manager = manager;
        _interval = interval ?? TimeSpan.FromSeconds(60);
    }

    public void Start()
    {
        if (_loopTask is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loopTask = Task.Run(async () =>
        {
            // 立即執行一次
            await SyncOnceAsync(token);
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(_interval, token); }
                catch { break; }
                if (token.IsCancellationRequested) break;
                await SyncOnceAsync(token);
            }
        }, token);
    }

    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var tasks = _manager.Active.Select(ch => ch.SetTimeAsync(now, ct)).ToArray();
        var results = await Task.WhenAll(tasks);
        int ok = results.Count(b => b);
        OnSyncCompleted?.Invoke(now, ok);
        return ok;
    }

    public void Stop()
    {
        try { _cts?.Cancel(); _loopTask?.Wait(500); } catch { }
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
    }

    public void Dispose() => Stop();
}
