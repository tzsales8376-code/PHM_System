// ============================================================================
// Tranzx.iVS4.Communication / Discovery / UsbScanner.cs
// 透過 WMI 掃描 USB CDC 裝置，依 VID/PID 篩選出 nRF52840 USB CDC
//   - Nordic VID = 0x1915（如有自訂 VID 需更新）
//   - 回傳 (PortName, FriendlyName, SerialNumber)
// ============================================================================

using System.Management;
using System.Text.RegularExpressions;

namespace Tranzx.iVS4.Communication.Discovery;

public sealed record UsbDeviceInfo(string PortName, string FriendlyName, string SerialNumber, string Vid, string Pid);

public static class UsbScanner
{
    /// <summary>Nordic Semiconductor 預設 VID（可加入廠商自訂 VID）</summary>
    public static readonly HashSet<string> KnownVids = new(StringComparer.OrdinalIgnoreCase)
    {
        "1915",   // Nordic Semiconductor
        "239A",   // Adafruit (nRF52840 開發板常見)
        "303A",   // Espressif (測試用)
    };

    /// <summary>掃描所有可用的 COM Port (含完整裝置資訊)</summary>
    public static List<UsbDeviceInfo> ScanAll()
    {
        var list = new List<UsbDeviceInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'");

            foreach (ManagementObject entity in searcher.Get())
            {
                var caption = entity["Caption"]?.ToString() ?? "";
                var deviceId = entity["DeviceID"]?.ToString() ?? "";

                // 解析 COMx
                var portMatch = Regex.Match(caption, @"\(COM(\d+)\)");
                if (!portMatch.Success) continue;
                var portName = "COM" + portMatch.Groups[1].Value;

                // 解析 VID/PID
                var vidMatch = Regex.Match(deviceId, @"VID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                var pidMatch = Regex.Match(deviceId, @"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                var serialMatch = Regex.Match(deviceId, @"\\([0-9A-Z]+)$", RegexOptions.IgnoreCase);

                var vid = vidMatch.Success ? vidMatch.Groups[1].Value.ToUpper() : "";
                var pid = pidMatch.Success ? pidMatch.Groups[1].Value.ToUpper() : "";
                var serial = serialMatch.Success ? serialMatch.Groups[1].Value : "";

                list.Add(new UsbDeviceInfo(portName, caption, serial, vid, pid));
            }
        }
        catch
        {
            // WMI 不可用時退回基本掃描
            foreach (var p in System.IO.Ports.SerialPort.GetPortNames())
                list.Add(new UsbDeviceInfo(p, p, "", "", ""));
        }

        return list;
    }

    /// <summary>僅回傳已知 VID 的 iVS 候選裝置</summary>
    public static List<UsbDeviceInfo> ScanIvsCandidates()
    {
        return ScanAll().Where(d => KnownVids.Contains(d.Vid)).ToList();
    }
}
