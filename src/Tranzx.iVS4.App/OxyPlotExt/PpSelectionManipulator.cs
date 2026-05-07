// ============================================================================
// Tranzx.iVS4.App / OxyPlotExt / PpSelectionManipulator.cs
//
// Phase 5-8c：滑鼠圈選計算 Peak-to-Peak（每軸獨立）
//   觸發：Shift + 左鍵拖曳
//   行為：
//     1. Started：在當前 cursor 位置建立 RectangleAnnotation（半透明橘色）
//     2. Delta：拖動時更新 rect 邊界
//     3. Completed：取出範圍內所有可見 LineSeries，計算 max - min = P-P，
//        把結果寫到 rect.Text 顯示在框內
//   每次新拖曳會自動清除上一次的 rect（用 Tag=="PP" 標記）
// ============================================================================

using System.Globalization;
using System.Linq;
using System.Text;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;

namespace Tranzx.iVS4.App.OxyPlotExt;

public sealed class PpSelectionManipulator : MouseManipulator
{
    private const string AnnotationTag = "PP";
    private RectangleAnnotation? _rect;
    private DataPoint _start;

    public PpSelectionManipulator(IPlotView plotView) : base(plotView) { }

    public override void Started(OxyMouseEventArgs e)
    {
        base.Started(e);
        var model = PlotView.ActualModel;
        if (model is null) return;

        // 移除上一次的 P-P annotation
        var stale = model.Annotations.Where(a => Equals(a.Tag, AnnotationTag)).ToList();
        foreach (var a in stale) model.Annotations.Remove(a);

        _start = InverseTransform(e.Position.X, e.Position.Y);
        _rect = new RectangleAnnotation
        {
            Tag = AnnotationTag,
            MinimumX = _start.X, MaximumX = _start.X,
            MinimumY = _start.Y, MaximumY = _start.Y,
            Fill = OxyColor.FromArgb(40, 0xF3, 0x9C, 0x12),
            Stroke = OxyColor.FromRgb(0xF3, 0x9C, 0x12),
            StrokeThickness = 1,
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
            TextVerticalAlignment = OxyPlot.VerticalAlignment.Top,
            TextColor = OxyColor.FromRgb(0xF3, 0x9C, 0x12),
            FontWeight = FontWeights.Bold,
            FontSize = 11,
        };
        model.Annotations.Add(_rect);
        PlotView.InvalidatePlot(false);
    }

    public override void Delta(OxyMouseEventArgs e)
    {
        base.Delta(e);
        if (_rect is null) return;

        var current = InverseTransform(e.Position.X, e.Position.Y);
        _rect.MinimumX = System.Math.Min(_start.X, current.X);
        _rect.MaximumX = System.Math.Max(_start.X, current.X);
        _rect.MinimumY = System.Math.Min(_start.Y, current.Y);
        _rect.MaximumY = System.Math.Max(_start.Y, current.Y);
        PlotView.InvalidatePlot(false);
    }

    public override void Completed(OxyMouseEventArgs e)
    {
        base.Completed(e);
        if (_rect is null) return;
        var model = PlotView.ActualModel;
        if (model is null) return;

        double xMin = _rect.MinimumX, xMax = _rect.MaximumX;
        // 太小視為點擊取消
        if (xMax - xMin < 1e-9)
        {
            model.Annotations.Remove(_rect);
            _rect = null;
            PlotView.InvalidatePlot(false);
            return;
        }

        // 計算範圍內每條可見 LineSeries 的 P-P
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.Append("Range: ").Append(xMin.ToString("F2", inv)).Append(" ~ ")
          .Append(xMax.ToString("F2", inv)).Append('\n');
        bool any = false;
        foreach (var s in model.Series)
        {
            if (s is LineSeries ls && ls.IsVisible && ls.Points.Count > 0)
            {
                double maxY = double.NegativeInfinity;
                double minY = double.PositiveInfinity;
                int count = 0;
                foreach (var p in ls.Points)
                {
                    if (p.X >= xMin && p.X <= xMax && !double.IsNaN(p.Y))
                    {
                        if (p.Y > maxY) maxY = p.Y;
                        if (p.Y < minY) minY = p.Y;
                        count++;
                    }
                }
                if (count > 1)
                {
                    double pp = maxY - minY;
                    sb.Append(ls.Title).Append(": ").Append(pp.ToString("F4", inv))
                      .Append(" (P-P)\n");
                    any = true;
                }
            }
        }

        _rect.Text = any ? sb.ToString().TrimEnd() : "(no data in range)";
        PlotView.InvalidatePlot(false);
    }
}

/// <summary>自訂 PlotCommand：把 P-P selection manipulator 套到滑鼠按下事件</summary>
public static class CustomPlotCommands
{
    public static readonly IViewCommand<OxyMouseDownEventArgs> SelectPp =
        new DelegatePlotCommand<OxyMouseDownEventArgs>(
            (view, controller, args) =>
            {
                var m = new PpSelectionManipulator(view);
                controller.AddMouseManipulator(view, m, args);
            });
}
