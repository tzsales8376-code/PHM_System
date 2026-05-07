// ============================================================================
// Tranzx.iVS4.App / Views / EventLogWindow.xaml.cs
// 軟體狀況監控：顯示 ErrorLogService 的最近事件
// ============================================================================

using System.Windows;
using Tranzx.iVS4.App.Services;

namespace Tranzx.iVS4.App.Views;

public partial class EventLogWindow : Window
{
    public EventLogWindow()
    {
        InitializeComponent();
        grid.ItemsSource = ErrorLogService.Instance.Recent;
        lblFolder.Text = ErrorLogService.Instance.LogFolderPath;
        lblFolder.ToolTip = ErrorLogService.Instance.LogFolderPath;
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = ErrorLogService.Instance.LogFolderPath;
            System.IO.Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder, UseShellExecute = true, Verb = "open"
            });
        }
        catch { }
    }
}
