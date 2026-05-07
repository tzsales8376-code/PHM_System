// ============================================================================
// Tranzx.iVS4.Communication / Transport / UsbCdcTransport.cs
// USB Virtual COM Port 傳輸實作（從 TZ_ACC_Tester v1.5 SerialPort 邏輯移植）
//   - DTR/RTS 自動拉高
//   - DataReceived 事件搬到背景執行緒避免 UI 卡住
//   - 自動重連在上層 SensorChannel 層處理
// ============================================================================

using System.IO.Ports;
using System.Text;

namespace Tranzx.iVS4.Communication.Transport;

public sealed class UsbCdcTransport : ITransport
{
    public string PortName { get; }
    public int BaudRate { get; }

    public string Identifier => PortName;
    public TransportState State { get; private set; } = TransportState.Disconnected;

    public event Action<byte[]>? OnDataReceived;
    public event Action<TransportState>? OnStateChanged;

    private SerialPort? _port;
    private readonly object _portLock = new();
    private CancellationTokenSource? _cts;

    /// <summary>Phase 5-8c2：watchdog 定期檢查 port 是否仍在</summary>
    private System.Threading.Timer? _watchdogTimer;

    public UsbCdcTransport(string portName, int baudRate = 921600)
    {
        PortName = portName;
        BaudRate = baudRate;
    }

    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                ChangeState(TransportState.Connecting);

                // ❗ Phase 5-8c：確保前一次的 port handle 已釋放（避免重連失敗）
                Cleanup();

                lock (_portLock)
                {
                    _port = new SerialPort(PortName, BaudRate, Parity.None, 8, StopBits.One)
                    {
                        ReadBufferSize = 65536,
                        WriteBufferSize = 8192,
                        DtrEnable = true,
                        RtsEnable = true,
                        Encoding = Encoding.GetEncoding(28591) // ISO-8859-1 for binary
                    };
                    _port.DataReceived += OnPortDataReceived;
                    _port.Open();
                }

                _cts = new CancellationTokenSource();
                ChangeState(TransportState.Connected);

                // ❗ Phase 5-8c2：啟動 watchdog（每 2 秒檢查 port 是否還存在）
                StartWatchdog();
                return true;
            }
            catch
            {
                ChangeState(TransportState.Faulted);
                Cleanup();
                return false;
            }
        }, ct);
    }

    public Task DisconnectAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                _cts?.Cancel();
                Cleanup();
                ChangeState(TransportState.Disconnected);
            }
            catch { /* swallow on dispose */ }
        });
    }

    public Task<bool> SendAsync(byte[] data, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                lock (_portLock)
                {
                    if (_port is null || !_port.IsOpen) return false;
                    _port.Write(data, 0, data.Length);
                    return true;
                }
            }
            catch { return false; }
        }, ct);
    }

    public async Task<bool> SendStreamControlAsync(string text, CancellationToken ct = default)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        return await SendAsync(bytes, ct);
    }

    private void OnPortDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            lock (_portLock)
            {
                if (_port is null || !_port.IsOpen) return;
                int n = _port.BytesToRead;
                if (n <= 0) return;
                var buf = new byte[n];
                int read = _port.Read(buf, 0, n);
                if (read != n) Array.Resize(ref buf, read);
                OnDataReceived?.Invoke(buf);
            }
        }
        catch
        {
            // ❗ Phase 5-8c bug fix：裝置突然拔除時必須完整 cleanup
            //   舊版只 ChangeState(Faulted) 而 _port handle 還在，
            //   下次重連 new SerialPort + Open() 會因 handle 殘留而失敗
            //   (Windows 對同 COM port 同時兩個 handle 不允許)
            Cleanup();
            ChangeState(TransportState.Faulted);
        }
    }

    private void Cleanup()
    {
        // ❗ Phase 5-8c2：先停 watchdog（避免 cleanup 中又被 watchdog 呼叫）
        StopWatchdog();
        lock (_portLock)
        {
            if (_port is null) return;
            try { _port.DataReceived -= OnPortDataReceived; } catch { }
            try { if (_port.IsOpen) _port.Close(); } catch { }
            try { _port.Dispose(); } catch { }
            _port = null;
        }
    }

    /// <summary>
    /// Phase 5-8c2：啟動 watchdog timer 主動偵測 USB 拔除
    /// 不能只靠 OnPortDataReceived 的 catch — 那只在「有 data 進來」時觸發
    /// 拔線後 callback 不一定會跑，必須主動檢查。
    /// </summary>
    private void StartWatchdog()
    {
        StopWatchdog();
        _watchdogTimer = new System.Threading.Timer(_ => CheckPortAlive(),
            null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void StopWatchdog()
    {
        try { _watchdogTimer?.Dispose(); } catch { }
        _watchdogTimer = null;
    }

    private void CheckPortAlive()
    {
        // 抓當前 port 的 reference (避免 lock 內做太多事)
        SerialPort? port;
        lock (_portLock)
        {
            port = _port;
        }
        if (port is null) return;

        bool ok = false;
        string? failReason = null;
        try
        {
            // 1. IsOpen 檢查
            if (!port.IsOpen)
            {
                failReason = "IsOpen=false";
            }
            else
            {
                // 2. 主動 ping driver — 拔 USB 後 BytesToRead 會立刻 throw
                //    比 SerialPort.GetPortNames() (registry-based) 可靠
                _ = port.BytesToRead;
                ok = true;
            }
        }
        catch (Exception ex)
        {
            failReason = ex.GetType().Name + ":" + ex.Message;
        }

        if (!ok)
        {
            // 雙重保險：再檢查 system port list
            try
            {
                var ports = SerialPort.GetPortNames();
                bool listed = false;
                foreach (var p in ports)
                {
                    if (string.Equals(p, PortName, StringComparison.OrdinalIgnoreCase))
                    { listed = true; break; }
                }
                if (!listed) failReason = (failReason ?? "?") + " + not in GetPortNames";
            }
            catch { }

            System.Diagnostics.Debug.WriteLine($"[Watchdog] {PortName} disconnected: {failReason}");
            Cleanup();
            ChangeState(TransportState.Faulted);
        }
    }

    private void ChangeState(TransportState s)
    {
        if (State == s) return;
        State = s;
        OnStateChanged?.Invoke(s);
    }

    public void Dispose()
    {
        try { DisconnectAsync().Wait(500); } catch { }
        _cts?.Dispose();
    }
}
