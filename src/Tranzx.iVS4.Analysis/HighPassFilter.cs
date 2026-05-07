// ============================================================================
// Tranzx.iVS4.Analysis / HighPassFilter.cs
// 一階 IIR 高通濾波器（去除重力 DC 分量）
//   - 從 TZ_ACC_Tester v1.5 移植
//   - 拐點頻率預設 0.5 Hz
// ============================================================================

namespace Tranzx.iVS4.Analysis;

public sealed class HighPassFilter
{
    private double _alpha;
    private double _prevInput;
    private double _prevOutput;
    private bool _initialized;

    public double CutoffHz { get; private set; }

    public HighPassFilter(double sampleRate, double cutoffHz = 0.5)
    {
        SetCutoff(sampleRate, cutoffHz);
    }

    public void SetCutoff(double sampleRate, double cutoffHz)
    {
        CutoffHz = cutoffHz;
        double dt = 1.0 / sampleRate;
        double rc = 1.0 / (2 * Math.PI * cutoffHz);
        _alpha = rc / (rc + dt);
        Reset();
    }

    public void Reset()
    {
        _prevInput = 0;
        _prevOutput = 0;
        _initialized = false;
    }

    public double Process(double input)
    {
        if (!_initialized) { _prevInput = input; _prevOutput = 0; _initialized = true; return 0; }
        double output = _alpha * (_prevOutput + input - _prevInput);
        _prevInput = input;
        _prevOutput = output;
        return output;
    }
}
