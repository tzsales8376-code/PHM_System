// ============================================================================
// Tranzx.iVS4.Core / Models / ChannelConfig.cs
// 通道設定（採樣率、量程、傳輸方式、Sensor ID 等）
// ============================================================================

namespace Tranzx.iVS4.Core.Models;

/// <summary>傳輸通道類型</summary>
public enum TransportType
{
    UsbCdc,
    Ble
}

/// <summary>感測器量程</summary>
public enum FullScale : byte
{
    G2 = 2,    // ±2G,  0.061 mG/LSB
    G4 = 4,    // ±4G,  0.122 mG/LSB
    G8 = 8,    // ±8G,  0.244 mG/LSB
    G16 = 16   // ±16G, 0.488 mG/LSB
}

/// <summary>輸出資料率 (Hz)</summary>
public enum OutputDataRate : ushort
{
    Hz12 = 12,
    Hz26 = 26,
    Hz52 = 52,
    Hz104 = 104,
    Hz208 = 208,
    Hz416 = 416,
    Hz833 = 833,
    Hz1666 = 1666,
    Hz3332 = 3332      // 預設最高
}

public static class FullScaleExtensions
{
    /// <summary>每量程對應的靈敏度 (mG/LSB)</summary>
    public static double ToScaleFactor(this FullScale fs) => fs switch
    {
        FullScale.G2 => 0.061,
        FullScale.G4 => 0.122,
        FullScale.G8 => 0.244,
        FullScale.G16 => 0.488,
        _ => 0.488
    };
}

/// <summary>單通道設定（執行期可修改）</summary>
public sealed class ChannelConfig
{
    /// <summary>通道索引 (0~3)</summary>
    public int Index { get; init; }

    /// <summary>顯示名稱（如「主軸 X」）</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>連線方式</summary>
    public TransportType Transport { get; set; } = TransportType.UsbCdc;

    /// <summary>USB COM Port (Transport=UsbCdc 時使用)</summary>
    public string? PortName { get; set; }

    /// <summary>BLE 裝置位址 (Transport=Ble 時使用)</summary>
    public ulong BluetoothAddress { get; set; }

    /// <summary>Sensor ID (8 碼)，用於校正檔配對</summary>
    public string SensorId { get; set; } = "";

    /// <summary>量程</summary>
    public FullScale FullScale { get; set; } = FullScale.G16;

    /// <summary>輸出資料率</summary>
    public OutputDataRate Odr { get; set; } = OutputDataRate.Hz3332;

    /// <summary>啟用此通道</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>校正檔路徑 (null = 自動配對)</summary>
    public string? CalibrationFilePath { get; set; }
}
