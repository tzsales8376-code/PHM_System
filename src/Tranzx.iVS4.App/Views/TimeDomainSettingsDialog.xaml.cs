// ============================================================================
// Tranzx.iVS4.App / Views / TimeDomainSettingsDialog.xaml.cs
// 時域統計設定（Statistics Rate + Overlap）
// 從圖表上方獨立 ⚙ icon 開啟
// ============================================================================

using System.Windows;
using Tranzx.iVS4.App.Services;

namespace Tranzx.iVS4.App.Views;

public partial class TimeDomainSettingsDialog : Window
{
    public TimeDomainSettingsDialog()
    {
        InitializeComponent();

        cmbStatsHz.ItemsSource = AppSettingsService.StatsHzOptions;
        cmbOverlap.ItemsSource = AppSettingsService.OverlapPctOptions;

        var s = AppSettingsService.Instance;
        cmbStatsHz.SelectedItem = s.StatisticsHz;
        cmbOverlap.SelectedItem = s.StatisticsOverlapPct;
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var s = AppSettingsService.Instance;
        if (cmbStatsHz.SelectedItem is double hz) s.StatisticsHz = hz;
        if (cmbOverlap.SelectedItem is double op) s.StatisticsOverlapPct = op;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
