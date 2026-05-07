// ============================================================================
// Tranzx.iVS4.Core / Models / AccSample.cs
// 三軸加速度單筆採樣（PC 對齊時間 + 設備時間戳）
// ============================================================================

namespace Tranzx.iVS4.Core.Models;

/// <summary>三軸加速度單次採樣 (XYZ)</summary>
public readonly record struct AccSample(
    short RawX,
    short RawY,
    short RawZ,
    double ScaleFactor,            // mG/LSB (依量程而定)
    DateTime DeviceTime,           // 設備時間戳 (sec + ms)
    DateTime PcTime                // PC 接收時間戳
)
{
    /// <summary>X 軸 (G)</summary>
    public double X_G => RawX * ScaleFactor / 1000.0;
    /// <summary>Y 軸 (G)</summary>
    public double Y_G => RawY * ScaleFactor / 1000.0;
    /// <summary>Z 軸 (G)</summary>
    public double Z_G => RawZ * ScaleFactor / 1000.0;

    /// <summary>X 軸 (m/s²)</summary>
    public double X_Mps2 => X_G * 9.80665;
    /// <summary>Y 軸 (m/s²)</summary>
    public double Y_Mps2 => Y_G * 9.80665;
    /// <summary>Z 軸 (m/s²)</summary>
    public double Z_Mps2 => Z_G * 9.80665;

    /// <summary>合向量 (G)</summary>
    public double Magnitude_G => Math.Sqrt(X_G * X_G + Y_G * Y_G + Z_G * Z_G);
}

/// <summary>溫濕度單筆採樣 (HDC1080)</summary>
public readonly record struct EnvSample(
    short RawTemperature,
    short RawHumidity,
    DateTime DeviceTime,
    DateTime PcTime
)
{
    /// <summary>溫度 (°C)：raw / 65536 × 165 − 40</summary>
    public double TemperatureC => (ushort)RawTemperature / 65536.0 * 165.0 - 40.0;

    /// <summary>濕度 (%RH)：raw / 65536 × 100</summary>
    public double HumidityPercent => (ushort)RawHumidity / 65536.0 * 100.0;
}

/// <summary>單封包解析結果（38 筆 ACC + 1 筆 ENV + meta）</summary>
public sealed class SensorPacket
{
    public byte Header { get; init; }                          // 0x45
    public DateTime DeviceTime { get; init; }                  // 設備時間戳
    public DateTime PcReceiveTime { get; init; }               // PC 接收時間戳
    public EnvSample Env { get; init; }
    public byte OverflowFlag { get; init; }
    public byte SeqNo { get; init; }
    public AccSample[] AccSamples { get; init; } = Array.Empty<AccSample>();
    public bool CrcValid { get; init; } = true;
}
