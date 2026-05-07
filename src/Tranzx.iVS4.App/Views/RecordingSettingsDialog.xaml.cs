// ============================================================================
// Tranzx.iVS4.App / Views / RecordingSettingsDialog.xaml.cs
// 錄製設定：紀錄資料夾、Trend / Raw 各自的切割時間 + 保留天數
// ============================================================================

using System.Linq;
using System.Windows;
using Tranzx.iVS4.App.Services;

namespace Tranzx.iVS4.App.Views;

public partial class RecordingSettingsDialog : Window
{
    /// <summary>包裝 int 加上本地化單位字串（用 ToString 顯示在 ComboBox）</summary>
    private sealed record DurationItem(int Value, string Suffix)
    {
        public override string ToString() => $"{Value} {Suffix}";
    }

    public RecordingSettingsDialog()
    {
        InitializeComponent();
        var s = AppSettingsService.Instance;
        var loc = LocalizationService.Instance;
        string minText = loc["Recording.Min"];
        string dayText = loc["Recording.Day"];

        lblFolder.Text = s.TrendLogFolder;
        lblFolder.ToolTip = s.TrendLogFolder;

        // 紀錄範圍 + 三類 + raw
        cbScopeAll.IsChecked = s.LogScopeAll;
        cbLogVib.IsChecked   = s.LogVibration;
        cbLogTilt.IsChecked  = s.LogTilt;
        cbLogEnv.IsChecked   = s.LogEnv;
        cbLogRaw.IsChecked   = s.RawDataEnabled;
        cbLogStats.IsChecked = s.StatsToEventLog;

        // ❗ Phase 5-8c5：定時 / 持續錄製
        cbTimedRecording.IsChecked = s.TimedRecordingEnabled;
        txtDurationSec.Text = s.RecordingDurationSec.ToString();
        cbContinuousRecording.IsChecked = s.ContinuousRecording;

        // ❗ Phase 5-8c6：Smart Log
        cbSmartLog.IsChecked = s.SmartLogEnabled;
        txtStartG.Text    = s.SmartStartG.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        txtStartHold.Text = s.SmartStartHoldSec.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        txtStopG.Text     = s.SmartStopG.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        txtStopHold.Text  = s.SmartStopHoldSec.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        txtMinSec.Text    = s.SmartMinRecordSec.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

        // 切割時間（分鐘）
        var trendSegOpts = AppSettingsService.SegmentMinutesOptions
            .Select(m => new DurationItem(m, minText)).ToArray();
        cmbTrendSeg.ItemsSource = trendSegOpts;
        cmbTrendSeg.SelectedItem = trendSegOpts.FirstOrDefault(x => x.Value == s.TrendSegmentMinutes)
                                  ?? trendSegOpts.FirstOrDefault();

        var rawSegOpts = AppSettingsService.SegmentMinutesOptions
            .Select(m => new DurationItem(m, minText)).ToArray();
        cmbRawSeg.ItemsSource = rawSegOpts;
        cmbRawSeg.SelectedItem = rawSegOpts.FirstOrDefault(x => x.Value == s.RawSegmentMinutes)
                                ?? rawSegOpts.FirstOrDefault();

        // 保留天數
        var trendRetOpts = AppSettingsService.RetentionDaysOptions
            .Select(d => new DurationItem(d, dayText)).ToArray();
        cmbTrendRet.ItemsSource = trendRetOpts;
        cmbTrendRet.SelectedItem = trendRetOpts.FirstOrDefault(x => x.Value == s.TrendRetentionDays)
                                  ?? trendRetOpts.FirstOrDefault();

        var rawRetOpts = AppSettingsService.RetentionDaysOptions
            .Select(d => new DurationItem(d, dayText)).ToArray();
        cmbRawRet.ItemsSource = rawRetOpts;
        cmbRawRet.SelectedItem = rawRetOpts.FirstOrDefault(x => x.Value == s.RawRetentionDays)
                                ?? rawRetOpts.FirstOrDefault();
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var s = AppSettingsService.Instance;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = LocalizationService.Instance["Trend.LogFolder"],
            InitialDirectory = s.TrendLogFolder
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.FolderName))
        {
            s.TrendLogFolder = dlg.FolderName;
            lblFolder.Text = dlg.FolderName;
            lblFolder.ToolTip = dlg.FolderName;
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = AppSettingsService.Instance.TrendLogFolder;
            if (string.IsNullOrEmpty(folder)) return;
            System.IO.Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder, UseShellExecute = true, Verb = "open"
            });
        }
        catch { }
    }

    private void OnCleanupNow(object sender, RoutedEventArgs e)
    {
        var s = AppSettingsService.Instance;
        int trendRet = (cmbTrendRet.SelectedItem as DurationItem)?.Value ?? s.TrendRetentionDays;
        int rawRet   = (cmbRawRet.SelectedItem   as DurationItem)?.Value ?? s.RawRetentionDays;
        int deleted = CsvRetentionService.ManualCleanup(s.TrendLogFolder, trendRet, rawRet);
        MessageBox.Show(
            string.Format(LocalizationService.Instance["Recording.CleanedFmt"], deleted),
            LocalizationService.Instance["Recording.Title"],
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var s = AppSettingsService.Instance;
        s.LogScopeAll    = cbScopeAll.IsChecked == true;
        s.LogVibration   = cbLogVib.IsChecked   == true;
        s.LogTilt        = cbLogTilt.IsChecked  == true;
        s.LogEnv         = cbLogEnv.IsChecked   == true;
        s.RawDataEnabled = cbLogRaw.IsChecked   == true;
        s.StatsToEventLog = cbLogStats.IsChecked == true;
        if (cmbTrendSeg.SelectedItem is DurationItem ts) s.TrendSegmentMinutes = ts.Value;
        if (cmbTrendRet.SelectedItem is DurationItem tr) s.TrendRetentionDays = tr.Value;
        if (cmbRawSeg.SelectedItem   is DurationItem rs) s.RawSegmentMinutes = rs.Value;
        if (cmbRawRet.SelectedItem   is DurationItem rr) s.RawRetentionDays = rr.Value;
        // ❗ Phase 5-8c5：定時 / 持續錄製
        s.TimedRecordingEnabled = cbTimedRecording.IsChecked == true;
        s.ContinuousRecording = cbContinuousRecording.IsChecked == true;
        if (int.TryParse(txtDurationSec.Text, out int dur))
        {
            s.RecordingDurationSec = dur;  // setter 內已經 clamp 10..86400
        }

        // ❗ Phase 5-8c6：Smart Log（最後再設 Enabled，先讓參數套用）
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (double.TryParse(txtStartG.Text, System.Globalization.NumberStyles.Float, inv, out double sg)) s.SmartStartG = sg;
        if (double.TryParse(txtStartHold.Text, System.Globalization.NumberStyles.Float, inv, out double sh)) s.SmartStartHoldSec = sh;
        if (double.TryParse(txtStopG.Text, System.Globalization.NumberStyles.Float, inv, out double pg)) s.SmartStopG = pg;
        if (double.TryParse(txtStopHold.Text, System.Globalization.NumberStyles.Float, inv, out double ph)) s.SmartStopHoldSec = ph;
        if (double.TryParse(txtMinSec.Text, System.Globalization.NumberStyles.Float, inv, out double ms)) s.SmartMinRecordSec = ms;
        s.SmartLogEnabled = cbSmartLog.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
