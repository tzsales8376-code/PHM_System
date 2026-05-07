// ============================================================================
// Tranzx.iVS4.Core / Protocol / CommandBuilder.cs
// 指令建構（從 TZ_ACC_Tester v1.5 移植）
// 格式：CMD(2B) + LEN(1B) + PARAM(nB) + CRC8(1B)  USB CDC 模式需加 \r\n
// ============================================================================

namespace Tranzx.iVS4.Core.Protocol;

public static class CommandBuilder
{
    // ── 指令字典（與 v1.4 通訊協議一致） ──
    public const byte CmdSetTime    = 0xA2;  // 設定時間
    public const byte CmdReadTime   = 0xA3;  // 讀取時間
    public const byte CmdSetOdr     = 0xA4;  // 設定 ODR
    public const byte CmdReadOdr    = 0xA5;  // 讀取 ODR
    public const byte CmdSetFs      = 0xA6;  // 設定量程
    public const byte CmdReadFs     = 0xA7;  // 讀取量程
    public const byte CmdShutdown   = 0xB1;  // 關機
    public const byte CmdSwitchCh   = 0xC3;  // 切換通道 0=BLE 1=USB

    /// <summary>建立完整指令（含 CRC，USB CDC 模式需自行附加 \r\n）</summary>
    private static byte[] Build(byte cmdHi, params byte[] paramBytes)
    {
        // 指令格式：[CMD_HI=0xCC] [CMD_LO=cmdHi] [LEN=paramLen] [PARAM...] [CRC8]
        // 註：實際 v1.4 規格用 2 bytes CMD，這裡簡化為 [0xCC, cmdHi]，可依韌體實際前導碼調整
        int len = paramBytes.Length;
        var bytes = new byte[3 + len + 1];
        bytes[0] = 0xCC;          // 前導碼
        bytes[1] = cmdHi;
        bytes[2] = (byte)len;
        Array.Copy(paramBytes, 0, bytes, 3, len);
        bytes[^1] = Crc8.Compute(bytes.AsSpan(0, bytes.Length - 1));
        return bytes;
    }

    /// <summary>設定 ODR (12/26/52/104/208/416/833/1666/3332)</summary>
    public static byte[] SetOdr(ushort odr)
    {
        return Build(CmdSetOdr, (byte)(odr & 0xFF), (byte)(odr >> 8));
    }

    public static byte[] ReadOdr() => Build(CmdReadOdr);

    /// <summary>設定量程 (2/4/8/16)</summary>
    public static byte[] SetFs(byte fs) => Build(CmdSetFs, fs);

    public static byte[] ReadFs() => Build(CmdReadFs);

    /// <summary>同步時間 (year+mon+day+hour+min+sec)</summary>
    public static byte[] SetTime(DateTime t)
    {
        return Build(CmdSetTime,
            (byte)(t.Year & 0xFF), (byte)(t.Year >> 8),
            (byte)t.Month, (byte)t.Day,
            (byte)t.Hour, (byte)t.Minute, (byte)t.Second);
    }

    public static byte[] ReadTime() => Build(CmdReadTime);

    public static byte[] Shutdown() => Build(CmdShutdown);

    /// <summary>切換通道：0=BLE 主、1=USB 主（設備重啟生效）</summary>
    public static byte[] SwitchChannel(byte ch) => Build(CmdSwitchCh, ch);

    /// <summary>USB CDC 串流控制（純文字，不走指令格式）</summary>
    public static byte[] StreamStop() => System.Text.Encoding.ASCII.GetBytes("stop\r\n");
    public static byte[] StreamStart() => System.Text.Encoding.ASCII.GetBytes("start\r\n");
}
