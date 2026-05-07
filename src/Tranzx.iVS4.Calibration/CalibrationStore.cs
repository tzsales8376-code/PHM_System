// ============================================================================
// Tranzx.iVS4.Calibration / CalibrationStore.cs
// 校正檔倉儲：啟動時掃描資料夾，依 Sensor ID 自動配對
// 預設位置：應用程式目錄下的 Calibration\
// 命名規則：TZ_CAL_<SensorID>.tzcal 或 .sr
// ============================================================================

namespace Tranzx.iVS4.Calibration;

public sealed class CalibrationStore
{
    /// <summary>掃描的根目錄</summary>
    public string RootFolder { get; }

    private readonly Dictionary<string, string> _sensorIdToPath = new(StringComparer.OrdinalIgnoreCase);

    public CalibrationStore(string? rootFolder = null)
    {
        RootFolder = rootFolder
            ?? Path.Combine(AppContext.BaseDirectory, "Calibration");

        if (!Directory.Exists(RootFolder))
            Directory.CreateDirectory(RootFolder);

        Rescan();
    }

    /// <summary>重新掃描資料夾</summary>
    public void Rescan()
    {
        _sensorIdToPath.Clear();

        var files = Directory.EnumerateFiles(RootFolder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext == ".tzcal" || ext == ".sr";
            });

        foreach (var f in files)
        {
            try
            {
                var cal = CalibrationFile.Load(f);
                if (!string.IsNullOrWhiteSpace(cal.SensorId))
                    _sensorIdToPath[cal.SensorId] = f;
            }
            catch
            {
                // 損壞的檔案跳過
            }
        }
    }

    /// <summary>依 Sensor ID 配對校正檔</summary>
    public CalibrationFile? FindBySensorId(string sensorId)
    {
        if (string.IsNullOrWhiteSpace(sensorId)) return null;
        if (_sensorIdToPath.TryGetValue(sensorId, out var path))
        {
            try { return CalibrationFile.Load(path); }
            catch { return null; }
        }
        return null;
    }

    /// <summary>以路徑直接載入</summary>
    public CalibrationFile? LoadFromPath(string path)
    {
        try { return CalibrationFile.Load(path); }
        catch { return null; }
    }

    /// <summary>所有已知的 Sensor ID</summary>
    public IReadOnlyCollection<string> KnownSensorIds => _sensorIdToPath.Keys;
}
