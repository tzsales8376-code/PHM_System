// ============================================================================
// Tranzx.iVS4.Core / Models / RecipeFile.cs
// Recipe 檔案：將多通道設定打包儲存（.tzrcp / JSON 格式）
//   - 跨機器移轉時 PortName 可能失效，載入時讓使用者重新指派
//   - SensorId 與校正檔配對保留（自動配對機制可重用）
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tranzx.iVS4.Core.Models;

public sealed class RecipeFile
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string CreatedBy { get; set; } = Environment.UserName;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastModified { get; set; } = DateTime.Now;
    public string AppVersion { get; set; } = "Tranzx.iVS4 1.0";
    public string FileVersion { get; set; } = "1.0";

    /// <summary>每個槽位的設定（最多 4 個）</summary>
    public List<ChannelConfig> Channels { get; set; } = new();

    /// <summary>時鐘同步間隔（秒）</summary>
    public int TimeSyncIntervalSeconds { get; set; } = 60;

    /// <summary>預設錄製資料夾（可空，空則使用 App 預設）</summary>
    public string? DefaultRecordFolder { get; set; }

    public void Save(string path)
    {
        LastModified = DateTime.Now;
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(path, JsonSerializer.Serialize(this, opts));
    }

    public static RecipeFile Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RecipeFile>(json)
            ?? throw new InvalidDataException($"Cannot parse recipe: {path}");
    }

    /// <summary>產生簡短摘要（用於 UI 確認對話框）</summary>
    public string Summary()
    {
        var lines = new List<string>
        {
            $"Recipe: {Name}",
            $"建立者: {CreatedBy}",
            $"建立時間: {CreatedAt:yyyy-MM-dd HH:mm}",
            $"通道數: {Channels.Count}",
            ""
        };
        foreach (var ch in Channels.OrderBy(c => c.Index))
        {
            lines.Add($"  Ch{ch.Index + 1}  {ch.DisplayName}  " +
                      $"Port={ch.PortName ?? "(未指定)"}  " +
                      $"SensorID={ch.SensorId}  " +
                      $"FS=±{(byte)ch.FullScale}G  ODR={(ushort)ch.Odr}Hz");
        }
        return string.Join('\n', lines);
    }
}
