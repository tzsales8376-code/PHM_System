using System.Windows;
using Tranzx.iVS4.App.Services;

namespace Tranzx.iVS4.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // ❗ Phase 5-8c3：即時狀態監控面板（連線事件）
        lstFeed.ItemsSource = LiveStatusFeed.Instance.Items;
        // ❗ Phase 5-8c10：警告監控面板（振動/環境）
        lstWarnFeed.ItemsSource = WarningFeed.Instance.Items;
        // ❗ Phase 5-8c8：節能模式 — 監聽全域輸入事件
        PowerSaverService.Instance.AttachTo(this);
    }

    private void OnClearFeedClick(object sender, RoutedEventArgs e)
    {
        LiveStatusFeed.Instance.Clear();
    }

    private void OnClearWarnFeedClick(object sender, RoutedEventArgs e)
    {
        WarningFeed.Instance.Clear();
    }
}
