// ============================================================================
// Tranzx.iVS4.App / Models / AlarmModels.cs
//
// 三段警報閾值模型：
//   Green  (正常)：0 ~ Yellow
//   Yellow (警告)：Yellow ~ Red
//   Red    (警報)：> Red
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;

namespace Tranzx.iVS4.App.Models;

public enum AlarmLevel { Green, Yellow, Red }

/// <summary>單一量值的兩段閾值</summary>
public partial class AlarmThreshold : ObservableObject
{
    [ObservableProperty] private double yellow;
    [ObservableProperty] private double red;

    public AlarmThreshold(double yellow = 0.3, double red = 0.5)
    {
        this.yellow = yellow;
        this.red = red;
    }

    public AlarmLevel Level(double value)
    {
        double v = Math.Abs(value);
        if (v >= Red) return AlarmLevel.Red;
        if (v >= Yellow) return AlarmLevel.Yellow;
        return AlarmLevel.Green;
    }

    public AlarmThreshold Clone() => new(Yellow, Red);
    public void CopyFrom(AlarmThreshold other) { Yellow = other.Yellow; Red = other.Red; }
}

/// <summary>單一 Sensor 內所有量值的閾值集合</summary>
public class ChannelAlarmThresholds
{
    // 振動：每軸的 0-P 與 RMS（單位 G）
    public AlarmThreshold XPeak { get; } = new(0.3, 0.5);
    public AlarmThreshold YPeak { get; } = new(0.3, 0.5);
    public AlarmThreshold ZPeak { get; } = new(0.3, 0.5);
    public AlarmThreshold XRms  { get; } = new(0.1, 0.2);
    public AlarmThreshold YRms  { get; } = new(0.1, 0.2);
    public AlarmThreshold ZRms  { get; } = new(0.1, 0.2);

    // 水平角度（單位度）
    public AlarmThreshold AngleX { get; } = new(5, 10);
    public AlarmThreshold AngleY { get; } = new(5, 10);
    public AlarmThreshold AngleZ { get; } = new(5, 10);

    // 溫溼度
    public AlarmThreshold Temp { get; } = new(40, 60);
    public AlarmThreshold Hum  { get; } = new(70, 85);

    /// <summary>「複製到全部」用：把 src 的閾值套到指定群組的所有量值</summary>
    public void CopyToVibrationPeak(AlarmThreshold src)
    {
        XPeak.CopyFrom(src); YPeak.CopyFrom(src); ZPeak.CopyFrom(src);
    }
    public void CopyToVibrationRms(AlarmThreshold src)
    {
        XRms.CopyFrom(src); YRms.CopyFrom(src); ZRms.CopyFrom(src);
    }
    public void CopyToTilt(AlarmThreshold src)
    {
        AngleX.CopyFrom(src); AngleY.CopyFrom(src); AngleZ.CopyFrom(src);
    }
}
