// ============================================================================
// Tranzx.iVS4.App / Views / GaugeArc.xaml.cs
//
// SCADA 風格半圓弧儀表
//   弧範圍 240°（從 8 點鐘 經 12 點鐘 到 4 點鐘）
//   三段背景顏色（綠/黃/紅）依 Yellow / Red 閾值動態分段
//   中央指針 + 大字數值 + 子標籤
// ============================================================================

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tranzx.iVS4.App.Views;

public partial class GaugeArc : UserControl
{
    // ──────── 幾何參數（與 XAML Canvas 對應）────────
    private const double Cx = 120, Cy = 100, R = 70;
    private const double StartDeg = 150;   // 起點：8 點鐘方向（左下）
    private const double SweepDeg = 240;   // 跨度：240° 弧

    // ──────── 顏色 brushes ────────
    private static readonly SolidColorBrush GreenBrush  = new(Color.FromRgb(0x1A, 0xBC, 0x9C));
    private static readonly SolidColorBrush YellowBrush = new(Color.FromRgb(0xF3, 0x9C, 0x12));
    private static readonly SolidColorBrush RedBrush    = new(Color.FromRgb(0xE7, 0x4C, 0x3C));

    static GaugeArc()
    {
        GreenBrush.Freeze();
        YellowBrush.Freeze();
        RedBrush.Freeze();
    }

    public GaugeArc()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    // ──────── DependencyProperties ────────
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeArc),
            new PropertyMetadata(0.0, OnAnyChanged));

    public double Yellow
    {
        get => (double)GetValue(YellowProperty);
        set => SetValue(YellowProperty, value);
    }
    public static readonly DependencyProperty YellowProperty =
        DependencyProperty.Register(nameof(Yellow), typeof(double), typeof(GaugeArc),
            new PropertyMetadata(0.3, OnAnyChanged));

    public double Red
    {
        get => (double)GetValue(RedProperty);
        set => SetValue(RedProperty, value);
    }
    public static readonly DependencyProperty RedProperty =
        DependencyProperty.Register(nameof(Red), typeof(double), typeof(GaugeArc),
            new PropertyMetadata(0.5, OnAnyChanged));

    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }
    public static readonly DependencyProperty ValueFormatProperty =
        DependencyProperty.Register(nameof(ValueFormat), typeof(string), typeof(GaugeArc),
            new PropertyMetadata("F3", OnAnyChanged));

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }
    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(GaugeArc),
            new PropertyMetadata("G", OnAnyChanged));

    public string SubLabel
    {
        get => (string)GetValue(SubLabelProperty);
        set => SetValue(SubLabelProperty, value);
    }
    public static readonly DependencyProperty SubLabelProperty =
        DependencyProperty.Register(nameof(SubLabel), typeof(string), typeof(GaugeArc),
            new PropertyMetadata("0-P", OnAnyChanged));

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GaugeArc g) g.Refresh();
    }

    // ──────── 重新繪製 ────────
    private void Refresh()
    {
        if (arcGray == null) return;  // 還沒 loaded

        // 防呆
        double yel = Yellow > 0 ? Yellow : 0.3;
        double red = Red > yel ? Red : yel * 1.5;
        double max = red * 1.2;  // 給 20% headroom 留一點紅段空間

        // 三段角度
        double yellowEnd = StartDeg + (yel / max) * SweepDeg;
        double redEnd    = StartDeg + (red / max) * SweepDeg;
        double endDeg    = StartDeg + SweepDeg;

        arcGray.Data   = MakeArc(StartDeg,   endDeg);
        arcGreen.Data  = MakeArc(StartDeg,   yellowEnd);
        arcYellow.Data = MakeArc(yellowEnd,  redEnd);
        arcRed.Data    = MakeArc(redEnd,     endDeg);

        // 指針角度
        double v = System.Math.Abs(Value);
        double ratio = System.Math.Min(1.0, v / max);
        double pointerDeg = StartDeg + ratio * SweepDeg;
        double pa = pointerDeg * System.Math.PI / 180;
        double pl = R - 8;  // 指針長度比弧短一點
        needle.X1 = Cx;
        needle.Y1 = Cy;
        needle.X2 = Cx + pl * System.Math.Cos(pa);
        needle.Y2 = Cy + pl * System.Math.Sin(pa);

        // 中央數值
        lblValue.Text = v.ToString(ValueFormat) + " " + Unit;
        lblValue.Foreground = LevelBrush(v, yel, red);

        // 子標籤
        lblSub.Text = SubLabel ?? "";
    }

    private static PathGeometry MakeArc(double startDeg, double endDeg)
    {
        double sweep = endDeg - startDeg;
        if (sweep < 0.5) return new PathGeometry();

        double sa = startDeg * System.Math.PI / 180;
        double ea = endDeg   * System.Math.PI / 180;

        var fig = new PathFigure
        {
            StartPoint = new Point(Cx + R * System.Math.Cos(sa), Cy + R * System.Math.Sin(sa)),
            IsClosed = false,
        };
        fig.Segments.Add(new ArcSegment
        {
            Point = new Point(Cx + R * System.Math.Cos(ea), Cy + R * System.Math.Sin(ea)),
            Size  = new Size(R, R),
            IsLargeArc = sweep > 180,
            SweepDirection = SweepDirection.Clockwise   // y 軸向下，clockwise 經過上方
        });
        var pg = new PathGeometry();
        pg.Figures.Add(fig);
        return pg;
    }

    private static Brush LevelBrush(double v, double yellow, double red)
    {
        if (v >= red) return RedBrush;
        if (v >= yellow) return YellowBrush;
        return GreenBrush;
    }
}
