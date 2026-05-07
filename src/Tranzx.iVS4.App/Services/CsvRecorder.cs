// ============================================================================
// Tranzx.iVS4.App / Services / CsvRecorder.cs
// 多通道同步 CSV 錄製：
//   - 每通道一個 CSV，全部在同一 Session 資料夾下
//   - Metadata header（# 開頭，pandas/Excel 可解析）含 Sensor ID/FS/ODR/校正資訊
//   - 每筆 38 樣本展開為 38 行寫入
//   - BufferedStream + 鎖避免高頻寫入競爭
//   - 同步開始/停止，Session 資料夾以時間戳命名
// ============================================================================

using System.Globalization;
using System.IO;
using Tranzx.iVS4.Communication;
using Tranzx.iVS4.Core.Models;

namespace Tranzx.iVS4.App.Services;

public sealed class CsvRecorder : IDisposable
{
    private sealed class ChannelState : IDisposable
    {
        public SensorChannel Channel { get; }
        public StreamWriter Writer { get; }
        public string FilePath { get; }
        public long Samples;
        public readonly object Lock = new();

        public ChannelState(SensorChannel ch, StreamWriter w, string path)
        { Channel = ch; Writer = w; FilePath = path; }

        public void Dispose()
        {
            try { Writer.Flush(); Writer.Dispose(); } catch { }
        }
    }

    private readonly List<ChannelState> _states = new();
    private readonly object _stateLock = new();
    public string RootFolder { get; }
    public string? SessionFolder { get; private set; }
    public bool IsRecording { get; private set; }
    public DateTime? StartedAt { get; private set; }

    /// <summary>累計樣本數（所有通道總和）</summary>
    public long TotalSamples
    {
        get
        {
            lock (_stateLock)
                return _states.Sum(s => s.Samples);
        }
    }

    public event Action<long>? OnSamplesWritten;

    public CsvRecorder(string? rootFolder = null)
    {
        RootFolder = rootFolder ?? Path.Combine(AppContext.BaseDirectory, "Records");
        if (!Directory.Exists(RootFolder)) Directory.CreateDirectory(RootFolder);
    }

    public void Start(IEnumerable<SensorChannel> channels)
    {
        if (IsRecording) throw new InvalidOperationException("Already recording");

        StartedAt = DateTime.Now;
        var stamp = StartedAt.Value.ToString("yyyyMMdd_HHmmss");
        SessionFolder = Path.Combine(RootFolder, $"Run_{stamp}");
        Directory.CreateDirectory(SessionFolder);

        lock (_stateLock)
        {
            _states.Clear();
            foreach (var ch in channels)
            {
                if (ch is null) continue;
                var sid = string.IsNullOrEmpty(ch.Config.SensorId) ? "Unknown" : ch.Config.SensorId;
                var safeName = $"Ch{ch.Config.Index + 1}_{sid}.csv";
                var path = Path.Combine(SessionFolder, safeName);

                var stream = new BufferedStream(File.Create(path), 65536);
                var w = new StreamWriter(stream, System.Text.Encoding.UTF8);

                WriteMetadata(w, ch);
                w.WriteLine("DeviceTime,PcTime,RawX,RawY,RawZ,X_G,Y_G,Z_G,Temperature_C,Humidity_RH,SeqNo");

                _states.Add(new ChannelState(ch, w, path));
                ch.OnPacketReceived += OnPacket;
            }
        }

        // metadata.json：Session 級資訊
        WriteSessionMetadata();

        IsRecording = true;
    }

    public void Stop()
    {
        if (!IsRecording) return;
        IsRecording = false;

        lock (_stateLock)
        {
            foreach (var st in _states)
            {
                try { st.Channel.OnPacketReceived -= OnPacket; } catch { }
                st.Dispose();
            }
            _states.Clear();
        }
    }

    private void OnPacket(SensorChannel ch, SensorPacket pkt)
    {
        ChannelState? st;
        lock (_stateLock)
        {
            st = _states.FirstOrDefault(s => ReferenceEquals(s.Channel, ch));
        }
        if (st is null) return;

        // 估算每筆 sample 的時間：均勻分布在封包時間區間內
        // 假設 38 樣本對應 1/SPS × 38 ≈ 11 ms 區間
        double sps = ch.Sps.Current > 100 ? ch.Sps.Current : (double)(ushort)ch.Config.Odr;
        double dtMs = 1000.0 / sps;

        lock (st.Lock)
        {
            for (int i = 0; i < pkt.AccSamples.Length; i++)
            {
                var s = pkt.AccSamples[i];
                var t = pkt.DeviceTime.AddMilliseconds(i * dtMs);

                st.Writer.Write(t.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                st.Writer.Write(',');
                st.Writer.Write(pkt.PcReceiveTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                st.Writer.Write(',');
                st.Writer.Write(s.RawX); st.Writer.Write(',');
                st.Writer.Write(s.RawY); st.Writer.Write(',');
                st.Writer.Write(s.RawZ); st.Writer.Write(',');
                st.Writer.Write(s.X_G.ToString("F6", CultureInfo.InvariantCulture)); st.Writer.Write(',');
                st.Writer.Write(s.Y_G.ToString("F6", CultureInfo.InvariantCulture)); st.Writer.Write(',');
                st.Writer.Write(s.Z_G.ToString("F6", CultureInfo.InvariantCulture)); st.Writer.Write(',');
                // 環境只在每包第一筆寫入完整值，其餘留空（avoiding 38 倍冗餘）
                if (i == 0)
                {
                    st.Writer.Write(pkt.Env.TemperatureC.ToString("F2", CultureInfo.InvariantCulture));
                    st.Writer.Write(',');
                    st.Writer.Write(pkt.Env.HumidityPercent.ToString("F2", CultureInfo.InvariantCulture));
                }
                else st.Writer.Write(",");
                st.Writer.Write(',');
                st.Writer.Write(pkt.SeqNo);
                st.Writer.Write('\n');
            }
            st.Samples += pkt.AccSamples.Length;
        }

        OnSamplesWritten?.Invoke(TotalSamples);
    }

    private static void WriteMetadata(StreamWriter w, SensorChannel ch)
    {
        var cal = ch.Calibration.Loaded;
        w.WriteLine($"# Tranzx iVS 4.0 CSV Export");
        w.WriteLine($"# RecordStarted={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        w.WriteLine($"# ChannelIndex={ch.Config.Index + 1}");
        w.WriteLine($"# DisplayName={ch.Config.DisplayName}");
        w.WriteLine($"# SensorId={ch.Config.SensorId}");
        w.WriteLine($"# Transport={ch.Config.Transport}");
        w.WriteLine($"# PortName={ch.Config.PortName}");
        w.WriteLine($"# FullScale={ch.Config.FullScale}");
        w.WriteLine($"# ScaleFactor_mGperLSB={ch.Parser.ScaleFactor:F4}");
        w.WriteLine($"# OdrConfigured_Hz={(ushort)ch.Config.Odr}");
        w.WriteLine($"# Calibrated={ch.Calibration.IsLoaded}");
        if (cal is not null)
        {
            w.WriteLine($"# CalibrationSensorId={cal.SensorId}");
            w.WriteLine($"# CalibrationDate={cal.CalibratedDate}");
            w.WriteLine($"# CalibrationBy={cal.CalibratedBy}");
        }
        w.WriteLine("#");
    }

    private void WriteSessionMetadata()
    {
        if (SessionFolder is null) return;
        var path = Path.Combine(SessionFolder, "metadata.txt");
        using var sw = new StreamWriter(path);
        sw.WriteLine($"Tranzx iVS 4.0 Recording Session");
        sw.WriteLine($"Started: {StartedAt:yyyy-MM-dd HH:mm:ss}");
        sw.WriteLine($"Channels:");
        lock (_stateLock)
        {
            foreach (var st in _states)
            {
                sw.WriteLine($"  - Ch{st.Channel.Config.Index + 1}: " +
                            $"{st.Channel.Config.PortName} " +
                            $"SensorID={st.Channel.Config.SensorId} " +
                            $"FS={st.Channel.Config.FullScale} " +
                            $"ODR={(ushort)st.Channel.Config.Odr}Hz " +
                            $"Calibrated={st.Channel.Calibration.IsLoaded}");
            }
        }
    }

    public void Dispose() => Stop();
}
