// ============================================================================
// Tranzx.iVS4.App / Views / SensorTabView.xaml.cs
//
// View 變得很薄（沒有 PlotModel、沒有 sweep 邏輯）
//   - PlotModel 在 ViewModel，所以切 Tab 時資料不會丟失
//   - 此 View 只負責：
//     1. RadioButton 點擊 → 設定全域 ViewMode
//     2. 監聽全域 ViewMode 變更 → 同步 RadioButton 狀態 + 切換 CheckBox 群顯示
//     3. 振動 sub-mode dropdown：點 ▾ 開 ContextMenu 三選項（Phase 5-8b）
// ============================================================================

using System.Windows;
using System.Windows.Controls;
using Tranzx.iVS4.App.Services;

namespace Tranzx.iVS4.App.Views;

public partial class SensorTabView : UserControl
{
    private bool _suppressRadioEvent;

    public SensorTabView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppSettingsService.Instance.ViewModeChanged += OnGlobalViewModeChanged;
        AppSettingsService.Instance.VibrationSubModeChanged += OnGlobalSubModeChanged;
        ApplyExternalViewMode(AppSettingsService.Instance.ViewMode);
        ApplyExternalSubMode(AppSettingsService.Instance.VibrationSubMode);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppSettingsService.Instance.ViewModeChanged -= OnGlobalViewModeChanged;
        AppSettingsService.Instance.VibrationSubModeChanged -= OnGlobalSubModeChanged;
    }

    private void OnGlobalViewModeChanged(AppViewMode m)
    {
        Dispatcher.BeginInvoke(() => ApplyExternalViewMode(m));
    }

    private void OnGlobalSubModeChanged(VibrationSubMode m)
    {
        Dispatcher.BeginInvoke(() => ApplyExternalSubMode(m));
    }

    private void ApplyExternalViewMode(AppViewMode m)
    {
        _suppressRadioEvent = true;
        try
        {
            switch (m)
            {
                case AppViewMode.Vibration: rbModeVib.IsChecked = true; break;
                case AppViewMode.Tilt:      rbModeTilt.IsChecked = true; break;
                case AppViewMode.Env:       rbModeEnv.IsChecked = true; break;
            }
        }
        finally { _suppressRadioEvent = false; }

        // 切換 CheckBox 群顯示
        pnlVibToggles.Visibility  = m == AppViewMode.Vibration ? Visibility.Visible : Visibility.Collapsed;
        pnlTiltToggles.Visibility = m == AppViewMode.Tilt      ? Visibility.Visible : Visibility.Collapsed;
        pnlEnvToggles.Visibility  = m == AppViewMode.Env       ? Visibility.Visible : Visibility.Collapsed;

        // 水平歸零按鈕只在水平模式顯示
        btnTiltZero.Visibility = m == AppViewMode.Tilt ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyExternalSubMode(VibrationSubMode m)
    {
        if (lblVibSubChip == null) return;
        string key = m switch
        {
            VibrationSubMode.Trend    => "VibSub.Trend",
            VibrationSubMode.Waveform => "VibSub.Waveform",
            VibrationSubMode.Fft      => "VibSub.Fft",
            _ => "VibSub.Trend"
        };
        lblVibSubChip.Text = LocalizationService.Instance[key];
    }

    private void OnModeVibrationChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressRadioEvent) return;
        AppSettingsService.Instance.ViewMode = AppViewMode.Vibration;
    }
    private void OnModeTiltChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressRadioEvent) return;
        AppSettingsService.Instance.ViewMode = AppViewMode.Tilt;
    }
    private void OnModeEnvChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressRadioEvent) return;
        AppSettingsService.Instance.ViewMode = AppViewMode.Env;
    }

    // ❗ Phase 5-8b：sub-mode 改用 dropdown popup
    private void OnVibSubMenuClick(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        var current = AppSettingsService.Instance.VibrationSubMode;
        var menu = new ContextMenu
        {
            Background = (System.Windows.Media.Brush)FindResource("BgSecondary"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
        };
        AddSubModeItem(menu, "📈  " + loc["VibSub.Trend"],    VibrationSubMode.Trend,    current);
        AddSubModeItem(menu, "〰  " + loc["VibSub.Waveform"], VibrationSubMode.Waveform, current);
        AddSubModeItem(menu, "🔊  " + loc["VibSub.Fft"],      VibrationSubMode.Fft,      current);
        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static void AddSubModeItem(ContextMenu menu, string header, VibrationSubMode m, VibrationSubMode current)
    {
        var mi = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = m == current,
            FontSize = 13,
        };
        mi.Click += (_, _) => AppSettingsService.Instance.VibrationSubMode = m;
        menu.Items.Add(mi);
    }
}
