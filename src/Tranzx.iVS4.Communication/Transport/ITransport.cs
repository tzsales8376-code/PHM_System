// ============================================================================
// Tranzx.iVS4.Communication / Transport / ITransport.cs
// 抽象傳輸介面：USB CDC 與 BLE 共用，方便上層程式無感切換
// ============================================================================

namespace Tranzx.iVS4.Communication.Transport;

public enum TransportState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Faulted
}

public interface ITransport : IDisposable
{
    /// <summary>傳輸層識別字串（COM4 / BleAddr-XXXX）</summary>
    string Identifier { get; }

    /// <summary>當前狀態</summary>
    TransportState State { get; }

    /// <summary>收到原始位元組</summary>
    event Action<byte[]>? OnDataReceived;

    /// <summary>狀態變更</summary>
    event Action<TransportState>? OnStateChanged;

    /// <summary>非同步建立連線</summary>
    Task<bool> ConnectAsync(CancellationToken ct = default);

    /// <summary>非同步斷線</summary>
    Task DisconnectAsync();

    /// <summary>送出指令位元組</summary>
    Task<bool> SendAsync(byte[] data, CancellationToken ct = default);

    /// <summary>送出 USB CDC 串流控制（純文字 stop\r\n / start\r\n），BLE 實作可空操作</summary>
    Task<bool> SendStreamControlAsync(string text, CancellationToken ct = default);
}
