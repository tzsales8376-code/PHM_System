// ============================================================================
// Tranzx.iVS4.Calibration / CalibrationEngine.cs
// 即時補償引擎：對 AccSample 串流套用校正
//   - 振動：Vnode 補償（時域 P-P 換算用）
//   - 振動：FFT 補償（頻域峰值換算用）
//   - 水平：零點扣除 + 角度增益
// ============================================================================

using Tranzx.iVS4.Core.Models;

namespace Tranzx.iVS4.Calibration;

public sealed class CalibrationEngine
{
    private CalibrationFile? _cal;
    private readonly object _lock = new();

    /// <summary>當前載入的校正檔（null 表示無校正）</summary>
    public CalibrationFile? Loaded
    {
        get { lock (_lock) return _cal; }
    }

    public bool IsLoaded => Loaded is not null;

    public string LoadedSensorId => Loaded?.SensorId ?? "";

    public event Action<CalibrationFile?>? OnCalibrationChanged;

    public void Load(CalibrationFile? cal)
    {
        lock (_lock) _cal = cal;
        OnCalibrationChanged?.Invoke(cal);
    }

    public void Clear()
    {
        lock (_lock) _cal = null;
        OnCalibrationChanged?.Invoke(null);
    }

    // ─── FFT 頻譜補償（線性內插） ───

    public double CompensateFft(double freqHz, char axis, double rawValue)
    {
        var cal = Loaded;
        if (cal is null) return rawValue;
        return rawValue * cal.VibCal.InterpolateFft(freqHz, axis);
    }

    // ─── 水平校正：扣零點 + 角度增益後計算 Pitch/Roll ───

    public (double pitch, double roll, double total) ComputeTilt(double avgX_G, double avgY_G, double avgZ_G)
    {
        var cal = Loaded;
        if (cal is not null)
        {
            avgX_G -= cal.LevelCal.ZeroX;
            avgY_G -= cal.LevelCal.ZeroY;
            avgZ_G -= cal.LevelCal.ZeroZ;
            avgZ_G += 1.0; // 零點扣除後重力分量加回
        }

        // Pitch: 以 X 軸傾角；Roll: 以 Y 軸傾角
        double pitch = Math.Atan2(avgX_G, Math.Sqrt(avgY_G * avgY_G + avgZ_G * avgZ_G)) * 180.0 / Math.PI;
        double roll = Math.Atan2(avgY_G, Math.Sqrt(avgX_G * avgX_G + avgZ_G * avgZ_G)) * 180.0 / Math.PI;

        if (cal is not null)
        {
            pitch *= cal.LevelCal.PitchGain;
            roll *= cal.LevelCal.RollGain;
        }

        double total = Math.Sqrt(pitch * pitch + roll * roll);
        return (pitch, roll, total);
    }
}
