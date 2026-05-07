// ============================================================================
// Tranzx.iVS4.App / Views / AlignDialog.xaml.cs
// Phase 5-8c12：歷史分析手動對齊對話框
// ============================================================================

using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tranzx.iVS4.App.Views;

public partial class AlignDialog : Window
{
    /// <summary>每個 dataset 的 anchor offset（從第一筆起算的秒數）</summary>
    public double[] Offsets { get; }
    private readonly TextBox[] _boxes;
    private readonly int _count;

    private static readonly Color[] AccentColors =
    {
        Color.FromRgb(0x1A, 0xBC, 0x9C),
        Color.FromRgb(0xF3, 0x9C, 0x12),
        Color.FromRgb(0x9B, 0x59, 0xB6),
        Color.FromRgb(0xE7, 0x4C, 0x3C),
    };

    internal AlignDialog(IList<HistoryAnalysisWindow.Dataset> datasets)
    {
        InitializeComponent();
        _count = datasets.Count;
        Offsets = new double[_count];
        _boxes = new TextBox[_count];

        for (int i = 0; i < _count; i++)
        {
            var ds = datasets[i];
            double duration = 0;
            if (ds.ParsedTimes.Count > 1)
                duration = (ds.ParsedTimes[ds.ParsedTimes.Count - 1].t - ds.ParsedTimes[0].t).TotalSeconds;

            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(4),
                Background = (SolidColorBrush)Application.Current.FindResource("BgSecondary"),
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = new SolidColorBrush(AccentColors[i % AccentColors.Length]),
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = $"[{i + 1}] {ds.FileName}",
                FontFamily = new FontFamily("Consolas"),
                FontWeight = System.Windows.FontWeights.Bold,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.FindResource("TextPrimary"),
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"   First: {ds.FirstTime:yyyy/MM/dd HH:mm:ss.fff}    Duration: {duration:F1}s",
                FontSize = 10, FontFamily = new FontFamily("Consolas"),
                Foreground = (SolidColorBrush)Application.Current.FindResource("TextSecondary"),
                Margin = new Thickness(0, 2, 0, 4),
            });

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lbl = new TextBlock
            {
                Text = (string)Application.Current.FindResource("HistoryWin.AlignOffset"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = (SolidColorBrush)Application.Current.FindResource("TextSecondary"),
            };
            Grid.SetColumn(lbl, 0); grid.Children.Add(lbl);

            var tb = new TextBox
            {
                Padding = new Thickness(6, 3, 6, 3),
                Background = (SolidColorBrush)Application.Current.FindResource("BgPrimary"),
                Foreground = (SolidColorBrush)Application.Current.FindResource("TextPrimary"),
                BorderBrush = (SolidColorBrush)Application.Current.FindResource("BgTertiary"),
                Text = (ds.AnchorTime - ds.FirstTime).TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
            };
            Grid.SetColumn(tb, 1); grid.Children.Add(tb);
            _boxes[i] = tb;

            var unit = new TextBlock
            {
                Text = "s", Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = (SolidColorBrush)Application.Current.FindResource("TextSecondary"),
            };
            Grid.SetColumn(unit, 2); grid.Children.Add(unit);
            sp.Children.Add(grid);
            border.Child = sp;
            pnlAnchors.Children.Add(border);
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var inv = CultureInfo.InvariantCulture;
        for (int i = 0; i < _count; i++)
        {
            if (double.TryParse(_boxes[i].Text, NumberStyles.Float, inv, out double v))
                Offsets[i] = v;
            else Offsets[i] = 0;
        }
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnAlignResetClick(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < _count; i++) _boxes[i].Text = "0.000";
    }
}
