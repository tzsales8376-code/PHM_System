// ============================================================================
// Tranzx.iVS4.Analysis / FftAnalyzer.cs
// 自包含 FFT 實作 (Cooley-Tukey radix-2)
// 5 種窗函數：Rectangular / Hanning / Hamming / Blackman / FlatTop
// ============================================================================

namespace Tranzx.iVS4.Analysis;

public enum WindowFunction
{
    Rectangular,
    Hanning,
    Hamming,
    Blackman,
    FlatTop
}

public static class WindowFunctions
{
    public static double[] Generate(WindowFunction win, int n)
    {
        var w = new double[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / (n - 1);
            w[i] = win switch
            {
                WindowFunction.Rectangular => 1.0,
                WindowFunction.Hanning => 0.5 * (1 - Math.Cos(2 * Math.PI * t)),
                WindowFunction.Hamming => 0.54 - 0.46 * Math.Cos(2 * Math.PI * t),
                WindowFunction.Blackman => 0.42 - 0.5 * Math.Cos(2 * Math.PI * t)
                                                + 0.08 * Math.Cos(4 * Math.PI * t),
                WindowFunction.FlatTop => 0.21557895
                                              - 0.41663158 * Math.Cos(2 * Math.PI * t)
                                              + 0.277263158 * Math.Cos(4 * Math.PI * t)
                                              - 0.083578947 * Math.Cos(6 * Math.PI * t)
                                              + 0.006947368 * Math.Cos(8 * Math.PI * t),
                _ => 1.0
            };
        }
        return w;
    }

    /// <summary>窗函數的相干增益 (CG) - 用於振幅校正</summary>
    public static double CoherentGain(WindowFunction win) => win switch
    {
        WindowFunction.Rectangular => 1.0,
        WindowFunction.Hanning => 0.5,
        WindowFunction.Hamming => 0.54,
        WindowFunction.Blackman => 0.42,
        WindowFunction.FlatTop => 0.21557895,
        _ => 1.0
    };
}

public static class FftAnalyzer
{
    /// <summary>
    /// 計算單軸 FFT 振幅譜 (G)
    ///   - 自動選擇 N 為 2 的次方（取 ≤ samples.Length 的最大 2 次方）
    ///   - 振幅已套用窗函數 CG 校正與 2/N 因子
    /// </summary>
    public static (double[] freq, double[] amp) ComputeAmplitudeSpectrum(
        double[] samples, double sampleRate, WindowFunction win = WindowFunction.Hanning)
    {
        if (samples.Length < 2) return (Array.Empty<double>(), Array.Empty<double>());

        int n = NextPow2(samples.Length, downward: true);
        var window = WindowFunctions.Generate(win, n);
        double cg = WindowFunctions.CoherentGain(win);

        // 取最後 N 點 + 套窗
        var re = new double[n];
        var im = new double[n];
        int start = samples.Length - n;
        for (int i = 0; i < n; i++)
        {
            re[i] = samples[start + i] * window[i];
            im[i] = 0;
        }

        FftInPlace(re, im);

        // 單邊譜 N/2 點
        int half = n / 2;
        var freq = new double[half];
        var amp = new double[half];
        double df = sampleRate / n;
        for (int k = 0; k < half; k++)
        {
            freq[k] = k * df;
            double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
            amp[k] = mag * 2.0 / n / cg;
        }
        return (freq, amp);
    }

    /// <summary>振幅譜轉 dB (ref = 1G)</summary>
    public static double[] ToDb(double[] amp, double reference = 1.0)
    {
        var db = new double[amp.Length];
        for (int i = 0; i < amp.Length; i++)
            db[i] = 20.0 * Math.Log10(Math.Max(amp[i], 1e-12) / reference);
        return db;
    }

    /// <summary>找出主峰（最高 K 個）</summary>
    public static List<(double freq, double amp)> FindPeaks(
        double[] freq, double[] amp, int topK = 5, double minFreqHz = 5)
    {
        var list = new List<(double, double)>();
        for (int i = 1; i < amp.Length - 1; i++)
        {
            if (freq[i] < minFreqHz) continue;
            if (amp[i] > amp[i - 1] && amp[i] > amp[i + 1])
                list.Add((freq[i], amp[i]));
        }
        return list.OrderByDescending(p => p.Item2).Take(topK).ToList();
    }

    private static int NextPow2(int n, bool downward)
    {
        if (n < 2) return 2;
        int p = 1;
        while (p * 2 <= n) p *= 2;
        return downward ? p : p * 2;
    }

    private static void FftInPlace(double[] re, double[] im)
    {
        int n = re.Length;
        // bit-reversal
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wlenR = Math.Cos(ang), wlenI = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double wR = 1, wI = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    double uR = re[i + k], uI = im[i + k];
                    double vR = re[i + k + len / 2] * wR - im[i + k + len / 2] * wI;
                    double vI = re[i + k + len / 2] * wI + im[i + k + len / 2] * wR;
                    re[i + k] = uR + vR; im[i + k] = uI + vI;
                    re[i + k + len / 2] = uR - vR; im[i + k + len / 2] = uI - vI;
                    double nR = wR * wlenR - wI * wlenI;
                    double nI = wR * wlenI + wI * wlenR;
                    wR = nR; wI = nI;
                }
            }
        }
    }
}
