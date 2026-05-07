// ============================================================================
// Tranzx.iVS4.Calibration / CalibrationFile.cs
// 從 TZ_ACC_Tester v1.5 移植：6 頻率 × 3 軸 Vnode/FFT 補償 + 水平校正
// 雙格式：.sr (舊版相容、純字串) + .tzcal (JSON 完整版)
// ============================================================================

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tranzx.iVS4.Calibration;

/// <summary>振動校正：6 頻率 × 3 軸（Vnode P-P + FFT 峰值）</summary>
public sealed class VibrationCalibration
{
    /// <summary>校正頻率（Hz）— 與校正器設定一致</summary>
    public double[] Frequencies { get; set; } = { 15.92, 40.0, 80.0, 159.2, 320.0, 640.0 };

    // ── Vnode (P-P/2 × 9.81 m/s²) 補償 ──
    public double[] VnodeCompX { get; set; } = { 1, 1, 1, 1, 1, 1 };
    public double[] VnodeCompY { get; set; } = { 1, 1, 1, 1, 1, 1 };
    public double[] VnodeCompZ { get; set; } = { 1, 1, 1, 1, 1, 1 };

    // ── FFT 峰值補償 ──
    public double[] FftCompX { get; set; } = { 1, 1, 1, 1, 1, 1 };
    public double[] FftCompY { get; set; } = { 1, 1, 1, 1, 1, 1 };
    public double[] FftCompZ { get; set; } = { 1, 1, 1, 1, 1, 1 };

    /// <summary>窗函數（Hanning / Hamming / Blackman / Rectangular / FlatTop）</summary>
    public string Window { get; set; } = "Hanning";

    /// <summary>線性內插：依頻率取得補償係數</summary>
    public double InterpolateFft(double freq, char axis)
    {
        var arr = axis switch
        {
            'X' or 'x' => FftCompX,
            'Y' or 'y' => FftCompY,
            'Z' or 'z' => FftCompZ,
            _ => FftCompZ
        };
        return Interpolate(freq, Frequencies, arr);
    }

    public double InterpolateVnode(double freq, char axis)
    {
        var arr = axis switch
        {
            'X' or 'x' => VnodeCompX,
            'Y' or 'y' => VnodeCompY,
            'Z' or 'z' => VnodeCompZ,
            _ => VnodeCompZ
        };
        return Interpolate(freq, Frequencies, arr);
    }

    private static double Interpolate(double freq, double[] xs, double[] ys)
    {
        if (xs.Length == 0) return 1.0;
        if (freq <= xs[0]) return ys[0];
        if (freq >= xs[^1]) return ys[^1];
        for (int i = 0; i < xs.Length - 1; i++)
        {
            if (freq >= xs[i] && freq <= xs[i + 1])
            {
                double t = (freq - xs[i]) / (xs[i + 1] - xs[i]);
                return ys[i] + t * (ys[i + 1] - ys[i]);
            }
        }
        return 1.0;
    }
}

/// <summary>水平校正：零點 + 兩軸增益</summary>
public sealed class LevelCalibration
{
    /// <summary>零點偏移 (G)</summary>
    public double ZeroX { get; set; } = 0;
    public double ZeroY { get; set; } = 0;
    public double ZeroZ { get; set; } = 1.0;

    /// <summary>Pitch / Roll 角度增益（已知參考角校正）</summary>
    public double PitchGain { get; set; } = 1.0;
    public double RollGain { get; set; } = 1.0;
}

/// <summary>完整校正檔（與舊版 TZ_ACC_Tester .tzcal 格式相容）</summary>
public sealed class CalibrationFile
{
    public string SensorId { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public string CalibratedBy { get; set; } = "";
    public string CalibratedDate { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    public string FullScale { get; set; } = "±16G";
    public int ODR { get; set; } = 3332;
    public double ScaleFactor { get; set; } = 0.488;

    public VibrationCalibration VibCal { get; set; } = new();
    public LevelCalibration LevelCal { get; set; } = new();

    public string FileVersion { get; set; } = "1.0";
    public string Software { get; set; } = "Tranzx.iVS4 v1.0";

    // ─── 儲存／載入 .tzcal (JSON) ───

    public void SaveTzcal(string path)
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        File.WriteAllText(path, JsonSerializer.Serialize(this, opts));
    }

    public static CalibrationFile LoadTzcal(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CalibrationFile>(json)
            ?? throw new InvalidDataException($"Cannot parse {path}");
    }

    // ─── 儲存／載入 .sr (舊版相容字串格式) ───
    //   <sid>XVnode,1,1,c1,c2,c3,c4
    //   <sid>YVnode,1,1,c1,c2,c3,c4
    //   <sid>ZVnode,1,1,c1,c2,c3,c4
    //   <sid>HannXFFT,c1,c2,c3,c4,c5,c6
    //   <sid>HannYFFT,c1,c2,c3,c4,c5,c6
    //   <sid>HannZFFT,c1,c2,c3,c4,c5,c6

    public void SaveSr(string path)
    {
        using var w = new StreamWriter(path);
        // Vnode: 跳過前 2 個頻率，從第 3 個開始記 4 個值（與舊韌體一致）
        WriteVnode(w, $"{SensorId}XVnode", VibCal.VnodeCompX);
        WriteVnode(w, $"{SensorId}YVnode", VibCal.VnodeCompY);
        WriteVnode(w, $"{SensorId}ZVnode", VibCal.VnodeCompZ);
        // FFT: 6 個頻率
        WriteFft(w, $"{SensorId}{ShortWindow()}XFFT", VibCal.FftCompX);
        WriteFft(w, $"{SensorId}{ShortWindow()}YFFT", VibCal.FftCompY);
        WriteFft(w, $"{SensorId}{ShortWindow()}ZFFT", VibCal.FftCompZ);
    }

    private string ShortWindow() => VibCal.Window switch
    {
        "Hanning" => "Hann",
        "Hamming" => "Hamm",
        "Blackman" => "Blkm",
        "FlatTop" => "Flat",
        _ => "Rect"
    };

    private static void WriteVnode(StreamWriter w, string id, double[] coeffs)
    {
        var c = string.Join(",", coeffs.Select(d => d.ToString("F4", CultureInfo.InvariantCulture)));
        w.WriteLine($"{id},1,1,{c}");
    }

    private static void WriteFft(StreamWriter w, string id, double[] coeffs)
    {
        var c = string.Join(",", coeffs.Select(d => d.ToString("F4", CultureInfo.InvariantCulture)));
        w.WriteLine($"{id},{c}");
    }

    /// <summary>載入 .sr (舊版字串格式)</summary>
    public static CalibrationFile LoadSr(string path)
    {
        var cf = new CalibrationFile();
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) throw new InvalidDataException("Empty .sr file");

        // Sensor ID = 第一行前 8 碼
        cf.SensorId = lines[0].Length >= 8 ? lines[0][..8] : "";

        foreach (var line in lines)
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;

            var key = parts[0];
            // Vnode (跳過前 2 個 padding "1,1")
            if (key.EndsWith("Vnode") && parts.Length >= 7)
            {
                var vals = parts.Skip(3).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                if (key.Contains("XVnode")) Array.Copy(PadTo6(vals), cf.VibCal.VnodeCompX, 6);
                else if (key.Contains("YVnode")) Array.Copy(PadTo6(vals), cf.VibCal.VnodeCompY, 6);
                else if (key.Contains("ZVnode")) Array.Copy(PadTo6(vals), cf.VibCal.VnodeCompZ, 6);
            }
            // FFT (6 個值)
            else if (key.EndsWith("FFT") && parts.Length >= 7)
            {
                var vals = parts.Skip(1).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                if (key.Contains("XFFT")) Array.Copy(PadTo6(vals), cf.VibCal.FftCompX, 6);
                else if (key.Contains("YFFT")) Array.Copy(PadTo6(vals), cf.VibCal.FftCompY, 6);
                else if (key.Contains("ZFFT")) Array.Copy(PadTo6(vals), cf.VibCal.FftCompZ, 6);
            }
        }
        return cf;
    }

    private static double[] PadTo6(double[] src)
    {
        var dst = new double[] { 1, 1, 1, 1, 1, 1 };
        for (int i = 0; i < src.Length && i < 6; i++) dst[i] = src[i];
        return dst;
    }

    /// <summary>自動偵測格式並載入</summary>
    public static CalibrationFile Load(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".tzcal" => LoadTzcal(path),
            ".sr" => LoadSr(path),
            _ => throw new NotSupportedException($"Unknown extension: {ext}")
        };
    }
}
