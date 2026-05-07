// ============================================================================
// Tranzx.iVS4.Core / Protocol / ResponseParser.cs
// 解析設備命令回應，落實 L02 教訓（連線後讀回設備實際設定）
//
// 韌體實際回應格式由韌體決定，本解析器同時支援：
//   1. 二進位格式：[0xCC, CMD, LEN, PARAM..., CRC]
//   2. ASCII 格式："FS=16\r\n"、"ODR=3332\r\n"、"FS:16"
//
// 設計原則：盡力解析，無法解析時回傳 null（讓上層繼續流程而不阻塞）
// ============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Tranzx.iVS4.Core.Protocol;

/// <summary>設備設定回讀快照</summary>
public sealed record DeviceConfigSnapshot(byte? FsValue, ushort? OdrValue, string RawText)
{
    public bool HasFs => FsValue.HasValue;
    public bool HasOdr => OdrValue.HasValue;

    public override string ToString()
    {
        var fs = HasFs ? $"FS=±{FsValue}G" : "FS=?";
        var odr = HasOdr ? $"ODR={OdrValue}Hz" : "ODR=?";
        return $"{fs}, {odr}";
    }
}

public static class ResponseParser
{
    /// <summary>嘗試從累積位元組中找出 FS 值（2/4/8/16）</summary>
    public static byte? TryParseFs(byte[] data)
    {
        if (data is null || data.Length == 0) return null;

        // 1) 嘗試 ASCII 格式 "FS=16" / "FS:16" / "fs 16"
        var text = SafeAscii(data);
        var m = Regex.Match(text, @"FS\s*[=:]\s*(\d+)", RegexOptions.IgnoreCase);
        if (m.Success && byte.TryParse(m.Groups[1].Value, out var fs))
            return ValidFs(fs) ? fs : null;

        // 2) 嘗試二進位格式 [0xCC, 0xA7, 0x01, fs_byte, CRC]
        for (int i = 0; i + 4 < data.Length; i++)
        {
            if (data[i] == 0xCC && data[i + 1] == CommandBuilder.CmdReadFs && data[i + 2] == 0x01)
            {
                byte v = data[i + 3];
                if (ValidFs(v)) return v;
            }
        }

        // 3) 嘗試二進位格式（無前導碼）：CMD + LEN + value + CRC
        for (int i = 0; i + 3 < data.Length; i++)
        {
            if (data[i] == CommandBuilder.CmdReadFs && data[i + 1] == 0x01)
            {
                byte v = data[i + 2];
                if (ValidFs(v)) return v;
            }
        }

        return null;
    }

    /// <summary>嘗試從累積位元組中找出 ODR 值（12/26/52/104/208/416/833/1666/3332）</summary>
    public static ushort? TryParseOdr(byte[] data)
    {
        if (data is null || data.Length == 0) return null;

        // 1) ASCII 格式
        var text = SafeAscii(data);
        var m = Regex.Match(text, @"ODR\s*[=:]\s*(\d+)", RegexOptions.IgnoreCase);
        if (m.Success && ushort.TryParse(m.Groups[1].Value, out var odr))
            return ValidOdr(odr) ? odr : null;

        // 2) 二進位格式 [0xCC, 0xA5, 0x02, lo, hi, CRC]
        for (int i = 0; i + 5 < data.Length; i++)
        {
            if (data[i] == 0xCC && data[i + 1] == CommandBuilder.CmdReadOdr && data[i + 2] == 0x02)
            {
                ushort v = (ushort)(data[i + 3] | (data[i + 4] << 8));
                if (ValidOdr(v)) return v;
                // 大端序也試試
                v = (ushort)((data[i + 3] << 8) | data[i + 4]);
                if (ValidOdr(v)) return v;
            }
        }

        // 3) 無前導碼版本
        for (int i = 0; i + 4 < data.Length; i++)
        {
            if (data[i] == CommandBuilder.CmdReadOdr && data[i + 1] == 0x02)
            {
                ushort v = (ushort)(data[i + 2] | (data[i + 3] << 8));
                if (ValidOdr(v)) return v;
            }
        }

        return null;
    }

    private static bool ValidFs(byte v) => v == 2 || v == 4 || v == 8 || v == 16;

    private static bool ValidOdr(ushort v) => v switch
    {
        12 or 26 or 52 or 104 or 208 or 416 or 833 or 1666 or 3332 => true,
        _ => false
    };

    private static string SafeAscii(byte[] data)
    {
        // 過濾掉非可列印字元，避免 binary 干擾 regex
        var sb = new StringBuilder(data.Length);
        foreach (var b in data)
        {
            if (b >= 0x20 && b < 0x7F) sb.Append((char)b);
            else if (b == 0x0A || b == 0x0D) sb.Append((char)b);
            else sb.Append(' ');
        }
        return sb.ToString();
    }
}
