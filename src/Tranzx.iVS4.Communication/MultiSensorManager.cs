// ============================================================================
// Tranzx.iVS4.Communication / MultiSensorManager.cs
// 多通道管理器：管理 1~4 組 SensorChannel
//   - 通道生命週期（建立、連線、斷線、Dispose）
//   - 統一指令派送（同步時間、套用設定）
//   - 通道事件聚合
// ============================================================================

using Tranzx.iVS4.Calibration;
using Tranzx.iVS4.Communication.Transport;
using Tranzx.iVS4.Core.Models;
using Tranzx.iVS4.Core.Protocol;

namespace Tranzx.iVS4.Communication;

public sealed class MultiSensorManager : IDisposable
{
    public const int MaxChannels = 4;

    private readonly SensorChannel?[] _channels = new SensorChannel?[MaxChannels];
    private readonly CalibrationStore _calStore;

    public CalibrationStore CalibrationStore => _calStore;
    public IReadOnlyList<SensorChannel?> Channels => _channels;

    public event Action<int, SensorChannel>? OnChannelAttached;
    public event Action<int>? OnChannelDetached;

    public MultiSensorManager(CalibrationStore? calStore = null)
    {
        _calStore = calStore ?? new CalibrationStore();
    }

    /// <summary>建立並掛載通道（呼叫前先確認 index 槽位空著）</summary>
    public SensorChannel Attach(int index, ChannelConfig config)
    {
        if (index < 0 || index >= MaxChannels) throw new ArgumentOutOfRangeException(nameof(index));
        Detach(index);

        ITransport transport = config.Transport switch
        {
            TransportType.UsbCdc => new UsbCdcTransport(config.PortName ?? throw new InvalidOperationException("PortName required")),
            // BLE 已於 Phase 4 暫停開發，留 Phase 5+ 再恢復
            _ => throw new NotSupportedException($"Transport {config.Transport} 暫不支援，請使用 USB CDC")
        };

        var calEngine = new CalibrationEngine();
        // 自動配對校正檔
        if (string.IsNullOrEmpty(config.CalibrationFilePath) && !string.IsNullOrEmpty(config.SensorId))
        {
            var cal = _calStore.FindBySensorId(config.SensorId);
            if (cal is not null) calEngine.Load(cal);
        }
        else if (!string.IsNullOrEmpty(config.CalibrationFilePath))
        {
            var cal = _calStore.LoadFromPath(config.CalibrationFilePath);
            if (cal is not null) calEngine.Load(cal);
        }

        var ch = new SensorChannel(config, transport, calEngine);
        _channels[index] = ch;
        OnChannelAttached?.Invoke(index, ch);
        return ch;
    }

    public void Detach(int index)
    {
        if (index < 0 || index >= MaxChannels) return;
        var ch = _channels[index];
        if (ch is null) return;
        ch.Dispose();
        _channels[index] = null;
        OnChannelDetached?.Invoke(index);
    }

    public SensorChannel? Get(int index)
        => index >= 0 && index < MaxChannels ? _channels[index] : null;

    /// <summary>連線所有已掛載的通道（並行）</summary>
    public async Task<bool[]> ConnectAllAsync(CancellationToken ct = default)
    {
        var tasks = _channels
            .Select((c, i) => c?.ConnectAsync(ct) ?? Task.FromResult(false))
            .ToArray();
        return await Task.WhenAll(tasks);
    }

    public async Task DisconnectAllAsync()
    {
        var tasks = _channels.Select(c => c?.DisconnectAsync() ?? Task.CompletedTask).ToArray();
        await Task.WhenAll(tasks);
    }

    public async Task<DeviceConfigSnapshot?[]> ApplyConfigAllAsync(bool verify = true, CancellationToken ct = default)
    {
        var tasks = _channels
            .Select(c => c?.ApplyConfigAsync(verify, ct) ?? Task.FromResult<DeviceConfigSnapshot?>(null))
            .ToArray();
        return await Task.WhenAll(tasks);
    }

    public IEnumerable<SensorChannel> Active => _channels.OfType<SensorChannel>();

    public void Dispose()
    {
        for (int i = 0; i < MaxChannels; i++) Detach(i);
    }
}
