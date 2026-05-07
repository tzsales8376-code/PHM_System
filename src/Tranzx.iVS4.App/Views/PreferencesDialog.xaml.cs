// ============================================================================
// Tranzx.iVS4.App / Views / PreferencesDialog.xaml.cs
// 集中式偏好設定面板（從工具列「⚙ 設定」按鈕開啟）
// ============================================================================

using System.Linq;
using System.Windows;
using Tranzx.iVS4.App.Services;

namespace Tranzx.iVS4.App.Views;

public partial class PreferencesDialog : Window
{
    public PreferencesDialog()
    {
        InitializeComponent();

        var s = AppSettingsService.Instance;
        switch (s.GravityMode)
        {
            case GravityMode.RemoveGravity: rbRemoveG.IsChecked = true; break;
            case GravityMode.KeepGravity:   rbKeepG.IsChecked = true; break;
        }
        switch (s.TiltAngleMode)
        {
            case TiltAngleMode.Inclinometer:  rbModeIncl.IsChecked = true; break;
            case TiltAngleMode.GravityVector: rbModeGrav.IsChecked = true; break;
        }

        cbLpfEnabled.IsChecked = s.TiltLpfEnabled;
        cmbLpfTau.ItemsSource = AppSettingsService.TiltLpfSecOptions;
        cmbLpfTau.SelectedItem = s.TiltLpfSec;

        cmbFont.SelectedItem = s.FontScale;

        cmbRefreshHz.ItemsSource = AppSettingsService.ChartRefreshHzOptions;
        cmbRefreshHz.SelectedItem = s.ChartRefreshHz;
        cmbMaxPoints.ItemsSource = AppSettingsService.ChartMaxPointsOptions;
        cmbMaxPoints.SelectedItem = s.ChartMaxPoints;
        cbShowDiag.IsChecked = s.ShowDiagnostics;
        cbAlarmSound.IsChecked = s.AlarmSoundEnabled;
        // ❗ Phase 5-8c2：toast 設定
        cbAlarmToastEnabled.IsChecked = s.AlarmToastEnabled;
        cbToastYellow.IsChecked = s.AlarmToastOnYellow;
        cbToastRed.IsChecked = s.AlarmToastOnRed;

        // ❗ Phase 5-8c：自動重連
        var loc = LocalizationService.Instance;
        string sec = loc["Unit.Sec"];
        string times = loc["Unit.Times"];
        string disabled = loc["Unit.Disabled"];

        var attemptsOpts = AppSettingsService.ReconnectAttemptsOptions
            .Select(n => new IntLabelItem(n, n == 0 ? disabled : $"{n} {times}")).ToArray();
        cmbReconnectAttempts.ItemsSource = attemptsOpts;
        cmbReconnectAttempts.SelectedItem =
            attemptsOpts.FirstOrDefault(x => x.Value == s.ReconnectAttempts) ?? attemptsOpts[0];

        var intervalOpts = AppSettingsService.ReconnectIntervalSecOptions
            .Select(n => new IntLabelItem(n, $"{n} {sec}")).ToArray();
        cmbReconnectInterval.ItemsSource = intervalOpts;
        cmbReconnectInterval.SelectedItem =
            intervalOpts.FirstOrDefault(x => x.Value == s.ReconnectIntervalSec) ?? intervalOpts[0];

        // ❗ 5-8c8：節能模式
        cbPowerSaver.IsChecked = s.PowerSaverEnabled;
        var minTxt = LocalizationService.Instance["Unit.Min"];
        var psOpts = AppSettingsService.PowerSaverIdleMinOptions
            .Select(n => new IntLabelItem(n, $"{n} {minTxt}")).ToArray();
        cmbPowerSaverMin.ItemsSource = psOpts;
        cmbPowerSaverMin.SelectedItem =
            psOpts.FirstOrDefault(x => x.Value == s.PowerSaverIdleMin) ?? psOpts[0];

        // 5-8c10：環境警告
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        cbEnvWarn.IsChecked = s.EnvWarnEnabled;
        txtTempY.Text = s.TempYellow.ToString("F1", inv);
        txtTempR.Text = s.TempRed.ToString("F1", inv);
        txtHumY.Text = s.HumidYellow.ToString("F1", inv);
        txtHumR.Text = s.HumidRed.ToString("F1", inv);
    }

    /// <summary>整數值 + 顯示文字包裝（給 ComboBox.ToString 用）</summary>
    private sealed record IntLabelItem(int Value, string Display)
    {
        public override string ToString() => Display;
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var s = AppSettingsService.Instance;

        if (rbRemoveG.IsChecked == true) s.GravityMode = GravityMode.RemoveGravity;
        else if (rbKeepG.IsChecked == true) s.GravityMode = GravityMode.KeepGravity;

        if (rbModeIncl.IsChecked == true) s.TiltAngleMode = TiltAngleMode.Inclinometer;
        else if (rbModeGrav.IsChecked == true) s.TiltAngleMode = TiltAngleMode.GravityVector;

        s.TiltLpfEnabled = cbLpfEnabled.IsChecked == true;
        if (cmbLpfTau.SelectedItem is double tau) s.TiltLpfSec = tau;

        if (cmbFont.SelectedItem is FontScale fs) s.FontScale = fs;

        if (cmbRefreshHz.SelectedItem is double hz) s.ChartRefreshHz = hz;
        if (cmbMaxPoints.SelectedItem is int mp) s.ChartMaxPoints = mp;
        s.ShowDiagnostics = cbShowDiag.IsChecked == true;
        s.AlarmSoundEnabled = cbAlarmSound.IsChecked == true;
        s.AlarmToastEnabled = cbAlarmToastEnabled.IsChecked == true;
        s.AlarmToastOnYellow = cbToastYellow.IsChecked == true;
        s.AlarmToastOnRed = cbToastRed.IsChecked == true;
        if (cmbReconnectAttempts.SelectedItem is IntLabelItem ra) s.ReconnectAttempts = ra.Value;
        if (cmbReconnectInterval.SelectedItem is IntLabelItem ri) s.ReconnectIntervalSec = ri.Value;
        // ❗ 5-8c8：節能模式
        s.PowerSaverEnabled = cbPowerSaver.IsChecked == true;
        if (cmbPowerSaverMin.SelectedItem is IntLabelItem pm) s.PowerSaverIdleMin = pm.Value;
        // 5-8c10：環境警告
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        s.EnvWarnEnabled = cbEnvWarn.IsChecked == true;
        if (double.TryParse(txtTempY.Text, System.Globalization.NumberStyles.Float, invc, out double ty)) s.TempYellow = ty;
        if (double.TryParse(txtTempR.Text, System.Globalization.NumberStyles.Float, invc, out double tr)) s.TempRed = tr;
        if (double.TryParse(txtHumY.Text, System.Globalization.NumberStyles.Float, invc, out double hy)) s.HumidYellow = hy;
        if (double.TryParse(txtHumR.Text, System.Globalization.NumberStyles.Float, invc, out double hr)) s.HumidRed = hr;

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
