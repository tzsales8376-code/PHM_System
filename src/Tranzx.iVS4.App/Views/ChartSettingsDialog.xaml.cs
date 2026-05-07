// ============================================================================
// Tranzx.iVS4.App / Views / ChartSettingsDialog.xaml.cs
// 圖表 X/Y 軸設定（從圖表上方 ⚙ icon 開啟）
// ============================================================================

using System.Globalization;
using System.Windows;
using Tranzx.iVS4.App.Services;

namespace Tranzx.iVS4.App.Views;

public partial class ChartSettingsDialog : Window
{
    public ChartSettingsDialog()
    {
        InitializeComponent();

        // 載入選項與當前值
        cmbVibX.ItemsSource  = AppSettingsService.VibXSecOptions;
        cmbTiltX.ItemsSource = AppSettingsService.TiltXSecOptions;
        cmbEnvX.ItemsSource  = AppSettingsService.EnvXSecOptions;

        // VibYMaxOptions 含 0 = Auto，做格式化顯示
        cmbVibY.ItemsSource = AppSettingsService.VibYMaxOptions;
        cmbVibY.ItemTemplate = (DataTemplate)Application.Current.Resources["VibYItemTpl"];

        cmbTiltY.ItemsSource = AppSettingsService.TiltYRangeOptions;
        cmbTiltY.ItemTemplate = (DataTemplate)Application.Current.Resources["TiltYItemTpl"];

        var s = AppSettingsService.Instance;
        cmbVibX.SelectedItem  = s.VibXSec;
        cmbVibY.SelectedItem  = s.VibYMaxG;
        cmbTiltX.SelectedItem = s.TiltXSec;
        cmbTiltY.SelectedItem = s.TiltYRangeDeg;
        cmbEnvX.SelectedItem  = s.EnvXSec;
        txtEnvMin.Text = s.EnvYMin.ToString(CultureInfo.InvariantCulture);
        txtEnvMax.Text = s.EnvYMax.ToString(CultureInfo.InvariantCulture);
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var s = AppSettingsService.Instance;

        if (cmbVibX.SelectedItem is double vx) s.VibXSec = vx;
        if (cmbVibY.SelectedItem is double vy) s.VibYMaxG = vy;
        if (cmbTiltX.SelectedItem is double tx) s.TiltXSec = tx;
        if (cmbTiltY.SelectedItem is double ty) s.TiltYRangeDeg = ty;
        if (cmbEnvX.SelectedItem is double ex) s.EnvXSec = ex;

        if (double.TryParse(txtEnvMin.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double emin) &&
            double.TryParse(txtEnvMax.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double emax) &&
            emax > emin)
        {
            s.EnvYMin = emin;
            s.EnvYMax = emax;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
