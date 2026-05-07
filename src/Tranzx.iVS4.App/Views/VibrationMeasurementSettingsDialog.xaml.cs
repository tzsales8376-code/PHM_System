// ============================================================================
// Tranzx.iVS4.App / Views / VibrationMeasurementSettingsDialog.xaml.cs
// 振動量測設定：趨勢 X/Y、波形時間、FFT 頻率/Y/窗/N
// ============================================================================

using System.Linq;
using System.Windows;
using Tranzx.iVS4.App.Services;
using Tranzx.iVS4.Analysis;

namespace Tranzx.iVS4.App.Views;

public partial class VibrationMeasurementSettingsDialog : Window
{
    /// <summary>數值 + 單位字串包裝（過 ToString 顯示給 ComboBox）</summary>
    private sealed record DurationItem(double Value, string Display)
    {
        public override string ToString() => Display;
    }
    private sealed record WindowItem(WindowFunction Win, string Display)
    {
        public override string ToString() => Display;
    }

    public VibrationMeasurementSettingsDialog()
    {
        InitializeComponent();
        var s = AppSettingsService.Instance;
        var loc = LocalizationService.Instance;
        string sec  = loc["Unit.Sec"];
        string g    = "G";
        string hz   = "Hz";
        string auto = loc["Unit.Auto"];

        // 時域統計
        cmbStatsHz.ItemsSource = AppSettingsService.StatsHzOptions;
        cmbStatsHz.SelectedItem = s.StatisticsHz;
        cmbOverlap.ItemsSource = AppSettingsService.OverlapPctOptions;
        cmbOverlap.SelectedItem = s.StatisticsOverlapPct;

        // Trend X 軸（秒）
        var trendXOpts = AppSettingsService.VibXSecOptions
            .Select(v => new DurationItem(v, $"{v:F0} {sec}")).ToArray();
        cmbTrendXSec.ItemsSource = trendXOpts;
        cmbTrendXSec.SelectedItem = trendXOpts.FirstOrDefault(x => x.Value == s.VibXSec)
                                  ?? trendXOpts.FirstOrDefault();

        // Trend Y 軸（G）
        var trendYOpts = AppSettingsService.VibYMaxOptions
            .Select(v => new DurationItem(v, v == 0 ? auto : $"{v:F2} {g}")).ToArray();
        cmbTrendYMax.ItemsSource = trendYOpts;
        cmbTrendYMax.SelectedItem = trendYOpts.FirstOrDefault(x => x.Value == s.VibYMaxG)
                                  ?? trendYOpts.FirstOrDefault();

        // 波形時間（秒）
        var wavSecOpts = AppSettingsService.WaveformSecOptions
            .Select(v => new DurationItem(v, $"{v} {sec}")).ToArray();
        cmbWaveformSec.ItemsSource = wavSecOpts;
        cmbWaveformSec.SelectedItem = wavSecOpts.FirstOrDefault(x => (int)x.Value == s.WaveformSec)
                                    ?? wavSecOpts.FirstOrDefault();

        // 波形 Y 軸 (±N G，0 = Auto)
        var wavYOpts = AppSettingsService.WaveformYMaxOptions
            .Select(v => new DurationItem(v, v == 0 ? auto : $"±{v:F1} {g}")).ToArray();
        cmbWaveformY.ItemsSource = wavYOpts;
        cmbWaveformY.SelectedItem = wavYOpts.FirstOrDefault(x => System.Math.Abs(x.Value - s.WaveformYMaxG) < 1e-9)
                                  ?? wavYOpts.FirstOrDefault();

        // FFT 頻率範圍
        var fftFreqOpts = AppSettingsService.FftFreqMaxOptions
            .Select(v => new DurationItem(v, $"{v} {hz}")).ToArray();
        cmbFftFreqMax.ItemsSource = fftFreqOpts;
        cmbFftFreqMax.SelectedItem = fftFreqOpts.FirstOrDefault(x => (int)x.Value == s.FftFreqMax)
                                   ?? fftFreqOpts.FirstOrDefault();

        // FFT Y 軸最大
        var fftYOpts = AppSettingsService.FftYMaxOptions
            .Select(v => new DurationItem(v, v == 0 ? auto : $"{v:G3} {g}")).ToArray();
        cmbFftYMax.ItemsSource = fftYOpts;
        cmbFftYMax.SelectedItem = fftYOpts.FirstOrDefault(x => System.Math.Abs(x.Value - s.FftYMax) < 1e-9)
                                ?? fftYOpts.FirstOrDefault();

        // FFT 窗函數
        var winOpts = AppSettingsService.FftWindowOptions
            .Select(w => new WindowItem(w, w.ToString())).ToArray();
        cmbFftWindow.ItemsSource = winOpts;
        cmbFftWindow.SelectedItem = winOpts.FirstOrDefault(x => x.Win == s.FftWindow)
                                  ?? winOpts.FirstOrDefault();

        // FFT Lines of Scan
        var fftNOpts = AppSettingsService.FftNOptions
            .Select(v => new DurationItem(v, v.ToString())).ToArray();
        cmbFftN.ItemsSource = fftNOpts;
        cmbFftN.SelectedItem = fftNOpts.FirstOrDefault(x => (int)x.Value == s.FftN)
                             ?? fftNOpts.FirstOrDefault();
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var s = AppSettingsService.Instance;
        if (cmbStatsHz.SelectedItem  is double hz)         s.StatisticsHz = hz;
        if (cmbOverlap.SelectedItem  is double op)         s.StatisticsOverlapPct = op;
        if (cmbTrendXSec.SelectedItem   is DurationItem ts) s.VibXSec = ts.Value;
        if (cmbTrendYMax.SelectedItem   is DurationItem tym) s.VibYMaxG = tym.Value;
        if (cmbWaveformSec.SelectedItem is DurationItem ws) s.WaveformSec = (int)ws.Value;
        if (cmbWaveformY.SelectedItem   is DurationItem wy) s.WaveformYMaxG = wy.Value;
        if (cmbFftFreqMax.SelectedItem  is DurationItem fm) s.FftFreqMax = (int)fm.Value;
        if (cmbFftYMax.SelectedItem     is DurationItem fy) s.FftYMax = fy.Value;
        if (cmbFftWindow.SelectedItem   is WindowItem   fw) s.FftWindow = fw.Win;
        if (cmbFftN.SelectedItem        is DurationItem fn) s.FftN = (int)fn.Value;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
