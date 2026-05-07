// ============================================================================
// Tranzx.iVS4.App / Views / SensorDashboardWindow.xaml.cs
// 多 Sensor Dashboard：所有 sensor 的即時數值 + alarm 狀態 一頁顯示
// ============================================================================

using System.Collections.ObjectModel;
using System.Windows;
using Tranzx.iVS4.App.ViewModels;

namespace Tranzx.iVS4.App.Views;

public partial class SensorDashboardWindow : Window
{
    public SensorDashboardWindow(ObservableCollection<SensorTabViewModel> tabs)
    {
        InitializeComponent();
        icSensors.ItemsSource = tabs;
        UpdateSummary(tabs);
        tabs.CollectionChanged += (_, _) => UpdateSummary(tabs);
    }

    private void UpdateSummary(ObservableCollection<SensorTabViewModel> tabs)
    {
        int total = tabs.Count;
        int connected = 0;
        foreach (var t in tabs) if (t.IsConnected) connected++;
        var loc = Services.LocalizationService.Instance;
        lblSummary.Text = string.Format(loc["Dashboard.SummaryFmt"], connected, total);
    }
}
