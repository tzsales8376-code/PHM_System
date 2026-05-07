// ============================================================================
// Tranzx.iVS4.App / Services / LocalizationService.cs
// 多語系核心服務（單例）
//   - 透過交換 ResourceDictionary 實作執行期語系切換
//   - DynamicResource 綁定會自動跟著更新
//   - 動態字串（含 {0} {1} 格式）由 ViewModel 透過 Get/Format 呼叫
//   - 訂閱 LanguageChanged 事件可重算動態字串
// ============================================================================

using System.ComponentModel;
using System.Globalization;
using System.Windows;

namespace Tranzx.iVS4.App.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    public sealed record LanguageOption(string Code, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public List<LanguageOption> Languages { get; } = new()
    {
        new("zh-TW", "🇹🇼 繁體中文"),
        new("en",    "🇺🇸 English"),
        new("ja",    "🇯🇵 日本語"),
    };

    private string _currentLanguage = "zh-TW";
    public string CurrentLanguage
    {
        get => _currentLanguage;
        set { if (_currentLanguage != value) SetLanguage(value); }
    }

    public LanguageOption CurrentLanguageOption
    {
        get => Languages.FirstOrDefault(l => l.Code == _currentLanguage) ?? Languages[0];
        set { if (value is not null) CurrentLanguage = value.Code; }
    }

    /// <summary>取得字串資源（找不到時回傳 ⟨key⟩ 方便 debug）</summary>
    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (Application.Current?.TryFindResource(key) is string s) return s;
        return $"⟨{key}⟩";
    }

    /// <summary>format 字串資源（例如 "Connected {0}/{1}"）</summary>
    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>每次語系變更觸發，所有 ViewModel 應訂閱重算動態字串</summary>
    public event Action<string>? LanguageChanged;

    public void SetLanguage(string lang)
    {
        if (Application.Current is null)
        {
            // App 還沒啟動，先存下來
            _currentLanguage = lang;
            return;
        }

        var dicts = Application.Current.Resources.MergedDictionaries;

        // 移除舊的語系字典
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            var src = dicts[i].Source?.OriginalString;
            if (!string.IsNullOrEmpty(src) && src.Contains("Strings.", StringComparison.Ordinal))
                dicts.RemoveAt(i);
        }

        // 載入新的
        var uri = new Uri($"pack://application:,,,/Resources/Strings.{lang}.xaml", UriKind.Absolute);
        dicts.Add(new ResourceDictionary { Source = uri });

        _currentLanguage = lang;
        PropertyChanged?.Invoke(this, new(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new(nameof(CurrentLanguageOption)));
        LanguageChanged?.Invoke(lang);
    }

    /// <summary>App 啟動時自動偵測作業系統語系</summary>
    public string DetectSystemLanguage()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        return name switch
        {
            var n when n.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) => "zh-TW",
            var n when n.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) => "zh-TW",
            "zh" or "zh-Hans" or "zh-CN" => "zh-TW", // 簡中暫時 fallback 到繁中
            var n when n.StartsWith("ja", StringComparison.OrdinalIgnoreCase) => "ja",
            _ => "en"
        };
    }
}
