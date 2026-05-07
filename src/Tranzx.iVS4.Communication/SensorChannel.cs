// ============================================================================
// Tranzx.iVS4.Communication / SensorChannel.cs
// 單一通道資料管線：
//   Transport  →  PacketParser  →  CalibrationEngine  →  RingBuffer
//
// Phase 3 變更：
//   - RawMode 切換：暫停 Parser.Feed，讓上層讀取命令回應
//   - ApplyConfigAsync 整合 SetFs/SetOdr/ReadFs/ReadOdr，在同一個 stream-stop
//     視窗內完成（避免多次 stop/start 增加風險）
//   - DeviceFs / DeviceOdr / ConfigMismatch 屬性紀錄驗證結果（落實 L02）
// ============================================================================

using Tranzx.iVS4.Analysis;
using Tranzx.iVS4.Calibration;
using Tranzx.iVS4.Core.Models;
using Tranzx.iVS4.Core.Protocol;
using Tranzx.iVS4.Communication.Transport;

namespace Tranzx.iVS4.Communication;

public sealed class SensorChannel : IDisposable
{
    public ChannelConfig Config { get; }
    public ITransport Transport { get; private set; }
    public PacketParser Parser { get; }
    public CalibrationEngine Calibration { get; }
    public RingBuffer Buffer { get; }
    public SpsSmoother Sps { get; }

    public bool Streaming { get; private set; }
    public DateTime ConnectedAt { get; private set; }

    /// <summary>設備回讀的實際 FS（null = 未驗證或解析失敗）</summary>
    public byte? DeviceFs { get; private set; }

    /// <summary>設備回讀的實際 ODR（null = 未驗證或解析失敗）</summary>
    public ushort? DeviceOdr { get; private set; }

    /// <summary>設定值與設備值的不一致警告（驗證成功且不一致為 true）</summary>
    public bool ConfigMismatch { get; private set; }

    // ── Phase 4 診斷：累計位元組數 + 最後 32 byte hex ──
    private long _rawBytesReceived;
    public long RawBytesReceived => Interlocked.Read(ref _rawBytesReceived);

    private readonly byte[] _lastBytesBuf = new byte[32];
    private int _lastBytesLen;
    private readonly object _lastBytesLock = new();

    /// <summary>取最近 N byte 的 hex 字串（ASCII 可印字元同時顯示）</summary>
    public string GetLastBytesHex(int n = 16)
    {
        lock (_lastBytesLock)
        {
            int take = Math.Min(n, _lastBytesLen);
            if (take == 0) return "";
            int start = _lastBytesLen - take;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < take; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(_lastBytesBuf[start + i].ToString("X2"));
            }
            return sb.ToString();
        }
    }

    public event Action<SensorChannel, SensorPacket>? OnPacketReceived;
    public event Action<SensorChannel, TransportState>? OnStateChanged;
#pragma warning disable CS0067 // 保留供 Phase 5 verify 功能用，目前 ApplyConfigAsync 已 No-op 故不會觸發
    public event Action<SensorChannel>? OnConfigVerified;
#pragma warning restore CS0067

    // ── Raw mode（保留欄位供 Phase 5 動態 FS/ODR 設定用，目前未使用）──
#pragma warning disable CS0649
    private volatile bool _rawMode;
    private Action<byte[]>? _rawSink;
#pragma warning restore CS0649

    public SensorChannel(ChannelConfig config, ITransport transport, CalibrationEngine cal)
    {
        Config = config;
        Transport = transport;
        Parser = new PacketParser { ScaleFactor = config.FullScale.ToScaleFactor() };
        Calibration = cal;
        Buffer = new RingBuffer(204_800);  // ~60s @ 3332Hz，三軸共 ~4.7MB（Phase 5-8b：支援長波形顯示）
        Sps = new SpsSmoother();

        Parser.OnPacket += OnPacketParsed;
        Transport.OnDataReceived += OnTransportData;
        Transport.OnStateChanged += s => OnStateChanged?.Invoke(this, s);
    }

    private void OnTransportData(byte[] data)
    {
        // 累計位元組計數 + 紀錄最後 32 byte 供 UI 診斷
        Interlocked.Add(ref _rawBytesReceived, data.Length);
        lock (_lastBytesLock)
        {
            int copy = Math.Min(data.Length, _lastBytesBuf.Length);
            // 把舊的後段往前推，新的填到尾端
            int keep = _lastBytesBuf.Length - copy;
            if (keep > 0 && _lastBytesLen >= copy)
                Array.Copy(_lastBytesBuf, _lastBytesBuf.Length - keep, _lastBytesBuf, 0, keep);
            Array.Copy(data, data.Length - copy, _lastBytesBuf, _lastBytesBuf.Length - copy, copy);
            _lastBytesLen = Math.Min(_lastBytesBuf.Length, _lastBytesLen + copy);
        }

        if (_rawMode) _rawSink?.Invoke(data);
        else Parser.Feed(data);
    }

    private void OnPacketParsed(SensorPacket pkt)
    {
        Buffer.AppendRange(pkt.AccSamples);
        Sps.OnSamples(pkt.AccSamples.Length);
        OnPacketReceived?.Invoke(this, pkt);
    }

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        var ok = await Transport.ConnectAsync(ct);
        if (ok) ConnectedAt = DateTime.Now;
        return ok;
    }

    public Task DisconnectAsync() => Transport.DisconnectAsync();

    /// <summary>由 TimeSyncService 統一呼叫，下發相同時間戳到所有通道</summary>
    public Task<bool> SetTimeAsync(DateTime t, CancellationToken ct = default)
        => Transport.SendAsync(CommandBuilder.SetTime(t), ct);

    /// <summary>
    /// Phase 4 修正：v1.5 校正工具能連同款韌體，代表韌體預設行為就是
    /// **插上 USB 即自動串流 241B/0x45 封包**。我們之前送的 stop\r\n / SetFs / SetOdr / start\r\n
    /// 反而把設備推進「raw 6-byte BE」奇怪狀態（L17）。
    ///
    /// 因此這個方法現在是 **No-op** — 連線後立即相信設備就在串流，PacketParser 會自動對齊 0x45。
    /// 若使用者真的需要動態變更 FS/ODR（罕見），請於 Phase 5 加上獨立「Apply Settings」按鈕。
    ///
    /// 本方法保留簽章與回傳型別以維持向上相容（MainViewModel 仍呼叫它）。
    /// </summary>
    public Task<DeviceConfigSnapshot?> ApplyConfigAsync(bool verify = false, CancellationToken ct = default)
    {
        // ScaleFactor 假設與 UI 設定一致（韌體預設 ±16G）；若不一致會導致數值放大/縮小，
        // 但這是離線校正可修正的線性誤差，比起把設備搞壞是兩害取其輕。
        Parser.ScaleFactor = Config.FullScale.ToScaleFactor();
        Streaming = true;
        ConnectedAt = DateTime.Now;
        return Task.FromResult<DeviceConfigSnapshot?>(null);
    }

    public void Dispose()
    {
        try { Transport.Dispose(); } catch { }
    }
}
