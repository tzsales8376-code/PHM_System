// ============================================================================
// Tranzx.iVS4.Analysis / RingBuffer.cs
// 執行緒安全環形緩衝（樣本/通道隔離 UI 與資料）
//   - 容量：預設 8192 點 × 3 軸 ≈ 2.5 秒 @ 3332 Hz
//   - 用途：時域顯示、FFT 視窗、RMS 視窗統計
// ============================================================================

using Tranzx.iVS4.Core.Models;

namespace Tranzx.iVS4.Analysis;

public sealed class RingBuffer
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _z;
    private readonly DateTime[] _t;
    private readonly int _capacity;
    private readonly object _lock = new();

    private int _head;            // 下一筆寫入位置
    private int _count;           // 目前已存樣本數

    public int Capacity => _capacity;
    public int Count { get { lock (_lock) return _count; } }

    public RingBuffer(int capacity = 8192)
    {
        _capacity = capacity;
        _x = new double[capacity];
        _y = new double[capacity];
        _z = new double[capacity];
        _t = new DateTime[capacity];
    }

    public void Append(AccSample s)
    {
        lock (_lock)
        {
            _x[_head] = s.X_G;
            _y[_head] = s.Y_G;
            _z[_head] = s.Z_G;
            _t[_head] = s.DeviceTime;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity) _count++;
        }
    }

    public void AppendRange(IReadOnlyList<AccSample> samples)
    {
        lock (_lock)
        {
            foreach (var s in samples)
            {
                _x[_head] = s.X_G;
                _y[_head] = s.Y_G;
                _z[_head] = s.Z_G;
                _t[_head] = s.DeviceTime;
                _head = (_head + 1) % _capacity;
                if (_count < _capacity) _count++;
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _count = 0;
        }
    }

    /// <summary>取出最近 N 筆（線性順序，最新在尾端）</summary>
    public (double[] x, double[] y, double[] z, DateTime[] t) Snapshot(int n)
    {
        lock (_lock)
        {
            int take = Math.Min(n, _count);
            var rx = new double[take];
            var ry = new double[take];
            var rz = new double[take];
            var rt = new DateTime[take];

            int start = (_head - take + _capacity) % _capacity;
            for (int i = 0; i < take; i++)
            {
                int src = (start + i) % _capacity;
                rx[i] = _x[src];
                ry[i] = _y[src];
                rz[i] = _z[src];
                rt[i] = _t[src];
            }
            return (rx, ry, rz, rt);
        }
    }
}
