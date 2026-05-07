// ============================================================================
// Tranzx.iVS4.Core / Protocol / SpsSmoother.cs
// 實測 SPS 平滑器（避免 ODR 設定不生效時 FFT 頻率軸偏移）
// 從 TZ_ACC_Tester v1.5 經驗：ODR 與 SPS 可能不同步，FFT 軸用 SPS
// ============================================================================

namespace Tranzx.iVS4.Core.Protocol;

public sealed class SpsSmoother
{
    private long _samples;
    private DateTime _start;
    private double _smoothed;
    private const double Alpha = 0.05;

    public double Current => _smoothed;

    public void OnSamples(int n)
    {
        if (_samples == 0) _start = DateTime.UtcNow;
        _samples += n;

        var elapsed = (DateTime.UtcNow - _start).TotalSeconds;
        if (elapsed < 0.5) return;

        double instant = _samples / elapsed;
        _smoothed = _smoothed == 0 ? instant : _smoothed * (1 - Alpha) + instant * Alpha;

        // 每 10 秒重置一次累計，避免長時間漂移
        if (elapsed > 10)
        {
            _samples = 0;
            _start = DateTime.UtcNow;
        }
    }

    public void Reset()
    {
        _samples = 0;
        _smoothed = 0;
    }
}
