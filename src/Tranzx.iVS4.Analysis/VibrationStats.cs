// ============================================================================
// Tranzx.iVS4.Analysis / VibrationStats.cs
// 時域統計量：RMS / P-P / Peak / Crest Factor / Mean
// ============================================================================

using System;

namespace Tranzx.iVS4.Analysis;

public readonly record struct VibrationStats(
    double Rms,
    double Peak,
    double PeakToPeak,
    double CrestFactor,
    double Mean
)
{
    public static VibrationStats Compute(double[] samples)
    {
        if (samples.Length == 0)
            return new VibrationStats(0, 0, 0, 0, 0);

        double sum = 0, sumSq = 0;
        double max = double.MinValue, min = double.MaxValue;
        for (int i = 0; i < samples.Length; i++)
        {
            double v = samples[i];
            sum += v;
            sumSq += v * v;
            if (v > max) max = v;
            if (v < min) min = v;
        }
        double mean = sum / samples.Length;

        // ❗ Phase 5-8c4 bug fix：Peak / RMS 都基於 AC 分量（去 DC）
        //   原本 peakAbs = max(|v|) 包含重力分量 → 例如 Z 軸靜置 ~1G 會被當成 1G peak
        //   修正後 peakAbs = max(|v - mean|) 只反映振動真正的振幅，與 RMS 的 AC 概念一致
        double peakAbs = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double a = Math.Abs(samples[i] - mean);
            if (a > peakAbs) peakAbs = a;
        }

        // RMS 通常去除 DC 分量再計算（避免重力影響）
        double rmsAc = Math.Sqrt(Math.Max(0, sumSq / samples.Length - mean * mean));
        double pp = max - min;
        double crest = rmsAc > 1e-9 ? peakAbs / rmsAc : 0;

        return new VibrationStats(rmsAc, peakAbs, pp, crest, mean);
    }
}

/// <summary>
/// Phase 5-8c7：擴充統計（給「振動統計數據」分頁用）
/// 比 VibrationStats 多 Min / Max / Median / StdDev
/// </summary>
public readonly record struct ExtendedVibrationStats(
    double Min,
    double Max,
    double Mean,
    double Median,
    double StdDev,
    double Rms,
    double Peak,
    double PeakToPeak,
    double CrestFactor)
{
    public static ExtendedVibrationStats Compute(double[] samples)
    {
        if (samples is null || samples.Length == 0)
            return new ExtendedVibrationStats(0, 0, 0, 0, 0, 0, 0, 0, 0);

        // 一次掃過：min / max / sum / sumSq
        double sum = 0, sumSq = 0;
        double min = double.MaxValue, max = double.MinValue;
        for (int i = 0; i < samples.Length; i++)
        {
            double v = samples[i];
            sum += v;
            sumSq += v * v;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        double mean = sum / samples.Length;

        // AC 分量（去 DC）
        double rmsAc = Math.Sqrt(Math.Max(0, sumSq / samples.Length - mean * mean));
        // 母體標準差 = AC RMS（同義）— 但給統計學上的命名
        double std = rmsAc;

        // Peak (AC) = max(|v - mean|)
        double peakAbs = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double a = Math.Abs(samples[i] - mean);
            if (a > peakAbs) peakAbs = a;
        }

        // Median：拷貝後 sort（O(n log n)，window 通常 ≤ 數萬點）
        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        double median = sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) * 0.5;

        double pp = max - min;
        double crest = rmsAc > 1e-9 ? peakAbs / rmsAc : 0;

        return new ExtendedVibrationStats(min, max, mean, median, std, rmsAc, peakAbs, pp, crest);
    }
}
