// ============================================================================
// Tranzx.iVS4.Core / Protocol / Crc8.cs
// CRC8 計算 (多項式 x^8+x^5+x^4+1 = 0x31, 初始值 0x00)
// ============================================================================

namespace Tranzx.iVS4.Core.Protocol;

public static class Crc8
{
    private static readonly byte[] Table = BuildTable();

    private static byte[] BuildTable()
    {
        var t = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            byte crc = (byte)i;
            for (int b = 0; b < 8; b++)
                crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0x31) : (byte)(crc << 1);
            t[i] = crc;
        }
        return t;
    }

    public static byte Compute(ReadOnlySpan<byte> data)
    {
        byte crc = 0x00;
        foreach (var b in data) crc = Table[crc ^ b];
        return crc;
    }
}
