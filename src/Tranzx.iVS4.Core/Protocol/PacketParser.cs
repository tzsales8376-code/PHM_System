// ============================================================================
// Tranzx.iVS4.Core / Protocol / PacketParser.cs
// 241 bytes 封包解析（從 TZ_ACC_Tester v1.5 SensorProtocol.cs 移植）
// 封包結構：
//   [0]     Header 0x45
//   [1-4]   Timestamp_s   uint32 LE
//   [5-6]   Timestamp_ms  int16 LE
//   [7-8]   Humidity      int16 LE  (HDC1080 raw)
//   [9-10]  Temperature   int16 LE  (HDC1080 raw)
//   [11]    Overflow      0x00 / 0x01
//   [12]    SeqNo         0~255 循環
//   [13-240] ACC Data     38 × (X,Y,Z) int16 LE = 228 bytes
// ============================================================================

using Tranzx.iVS4.Core.Models;

namespace Tranzx.iVS4.Core.Protocol;

public sealed class PacketParser
{
    public const byte HeaderByte = 0x45;
    public const int PacketSize = 241;
    public const int SamplesPerPacket = 38;

    /// <summary>當前量程靈敏度 (mG/LSB)</summary>
    public double ScaleFactor { get; set; } = FullScale.G16.ToScaleFactor();

    /// <summary>序列流式解析緩衝</summary>
    private readonly List<byte> _buffer = new(PacketSize * 4);

    /// <summary>上一次序號（用於丟包偵測）</summary>
    public byte LastSeqNo { get; private set; } = 0xFF;

    /// <summary>累計丟包數</summary>
    public long LostPackets { get; private set; }

    /// <summary>累計成功封包數</summary>
    public long ValidPackets { get; private set; }

    /// <summary>解析事件</summary>
    public event Action<SensorPacket>? OnPacket;

    /// <summary>由原始位元組流持續餵入，遇到完整封包就觸發 OnPacket</summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        _buffer.AddRange(data.ToArray());

        while (_buffer.Count >= PacketSize)
        {
            // 對齊 Header
            if (_buffer[0] != HeaderByte)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            // 取出一包並嘗試解析
            var raw = new byte[PacketSize];
            _buffer.CopyTo(0, raw, 0, PacketSize);
            _buffer.RemoveRange(0, PacketSize);

            var pkt = ParseSingle(raw);
            if (pkt is not null)
            {
                ValidPackets++;
                TrackSeq(pkt.SeqNo);
                OnPacket?.Invoke(pkt);
            }
        }
    }

    /// <summary>解析單包（不做 buffer 操作）</summary>
    public SensorPacket? ParseSingle(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != PacketSize || raw[0] != HeaderByte) return null;

        var pcNow = DateTime.Now;

        // 設備時間戳 (sec + ms)
        uint sec = BitConverter.ToUInt32(raw[1..5]);
        short ms = BitConverter.ToInt16(raw[5..7]);
        DateTime devTime;
        try
        {
            devTime = DateTimeOffset.FromUnixTimeSeconds(sec)
                                    .AddMilliseconds(ms)
                                    .LocalDateTime;
        }
        catch
        {
            devTime = pcNow; // 設備時鐘異常時 fallback PC 時間
        }

        // 環境
        short rawHum = BitConverter.ToInt16(raw[7..9]);
        short rawTmp = BitConverter.ToInt16(raw[9..11]);
        var env = new EnvSample(rawTmp, rawHum, devTime, pcNow);

        byte ovf = raw[11];
        byte seq = raw[12];

        // 38 × (X,Y,Z) int16 LE，每筆 6 bytes
        var samples = new AccSample[SamplesPerPacket];
        int o = 13;
        // 假設 38 筆樣本在封包時間區間內均勻分布
        // ODR 越高間隔越小：間隔 = 1 / ODR 秒
        // 此處不做 per-sample 內插時間，由上層用 ODR 推算（或留 devTime 給整包）
        for (int i = 0; i < SamplesPerPacket; i++, o += 6)
        {
            short x = BitConverter.ToInt16(raw[o..(o + 2)]);
            short y = BitConverter.ToInt16(raw[(o + 2)..(o + 4)]);
            short z = BitConverter.ToInt16(raw[(o + 4)..(o + 6)]);
            samples[i] = new AccSample(x, y, z, ScaleFactor, devTime, pcNow);
        }

        return new SensorPacket
        {
            Header = HeaderByte,
            DeviceTime = devTime,
            PcReceiveTime = pcNow,
            Env = env,
            OverflowFlag = ovf,
            SeqNo = seq,
            AccSamples = samples,
            CrcValid = true
        };
    }

    private void TrackSeq(byte seq)
    {
        if (LastSeqNo != 0xFF)
        {
            byte expected = (byte)((LastSeqNo + 1) & 0xFF);
            if (seq != expected)
            {
                int lost = (seq - expected + 256) & 0xFF;
                LostPackets += lost;
            }
        }
        LastSeqNo = seq;
    }

    public void ResetStats()
    {
        LastSeqNo = 0xFF;
        LostPackets = 0;
        ValidPackets = 0;
        _buffer.Clear();
    }
}
