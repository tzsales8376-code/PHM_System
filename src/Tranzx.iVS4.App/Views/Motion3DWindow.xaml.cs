// ============================================================================
// Tranzx.iVS4.App / Views / Motion3DWindow.xaml.cs
//
// Phase 5-9 (A)：3D 動態回放視窗
//   - 載入 Tilt CSV（角度）+ 同時段的 Trend Vibration CSV（震動）
//   - 中央 Sensor Box 依 AngleX/Y/Z 旋轉、依振動 jitter 位移
//   - 加速度向量箭頭從盒子中心射出（顏色依 |G| 警報等級）
//   - 軌跡尾巴：最近 N 筆位置點連成漸淡的線
//   - Timeline scrubber + 播放/暫停/速度（0.1x ~ 10x）
//   - 視角：左鍵旋轉 / 右鍵平移 / 滾輪縮放
//
// 採用 WPF 內建 System.Windows.Media.Media3D，零外部依賴
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Tranzx.iVS4.App.Services;

namespace Tranzx.iVS4.App.Views;

public partial class Motion3DWindow : Window
{
    /// <summary>單一時間點的姿態 + 加速度</summary>
    private sealed class Frame
    {
        public DateTime Time;
        public double AngleX, AngleY, AngleZ;     // 度
        public double AccX, AccY, AccZ;            // G
        public double AccMag;                       // |G|
    }

    private readonly List<Frame> _frames = new();
    private int _frameIdx;
    private bool _playing;
    private double _speed = 1.0;
    private DispatcherTimer? _playTimer;
    private bool _initialized;  // 5-9：阻擋 InitializeComponent 期間 Slider/ComboBox 早觸發 event 造成 NRE

    // 3D 場景元素
    private readonly Model3DGroup _dynamicGroup = new(); // 隨時間變化
    private Model3DGroup? _boxGroup;                     // sensor box (6 faces with textures)
    private RotateTransform3D? _boxRot;
    private TranslateTransform3D? _boxTrans;
    private GeometryModel3D? _vectorArrow;               // 加速度向量
    private GeometryModel3D? _trail;                     // 軌跡尾巴（line）
    private GeometryModel3D? _grid;                      // 地面 grid
    private GeometryModel3D? _axisX;                     // 三軸參考線
    private GeometryModel3D? _axisY;
    private GeometryModel3D? _axisZ;

    // 視角控制
    private Point _lastMousePos;
    private bool _rotating, _panning;
    private double _camYaw = -45 * Math.PI / 180.0;   // 水平方位角（弧度）
    private double _camPitch = 30 * Math.PI / 180.0;  // 仰角，+ = 從上看下
    private double _camDist = 12.0;
    private Point3D _camTarget = new(0, 0, 0);

    public Motion3DWindow()
    {
        try
        {
            InitializeComponent();
            BuildStaticScene();
            UpdateCamera();

            cmbSpeed.ItemsSource = new[] { "0.1×", "0.25×", "0.5×", "1×", "2×", "5×", "10×" };
            cmbSpeed.SelectedItem = "1×";

            _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };  // ~30fps
            _playTimer.Tick += OnPlayTick;

            Closed += (_, _) => _playTimer?.Stop();
            _initialized = true;  // 所有 named element 都 ready 後才放行 handler
        }
        catch (Exception ex)
        {
            try { ErrorLogService.Instance.Error("Motion3D", $"init failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); } catch { }
            MessageBox.Show($"3D 視窗初始化失敗：\n\n{ex.GetType().Name}: {ex.Message}\n\n詳細請看 Event Log。",
                "3D Motion Replay", MessageBoxButton.OK, MessageBoxImage.Error);
            // 不 rethrow，讓 window 至少能關閉
        }
    }

    // ─────────────────────────────────────────────────
    //  3D 靜態場景：Box / Grid / Axes
    // ─────────────────────────────────────────────────

    private void BuildStaticScene()
    {
        // Lights — 從 code-behind 加（避免 XAML parse 時序問題）
        var lights = new ModelVisual3D
        {
            Content = new Model3DGroup
            {
                Children =
                {
                    new AmbientLight(Color.FromRgb(0x40, 0x40, 0x48)),
                    new DirectionalLight(Color.FromRgb(0xFF, 0xFF, 0xFF), new Vector3D(-0.5, -1, -0.7)),
                    new DirectionalLight(Color.FromRgb(0x20, 0x20, 0x50), new Vector3D(0.6, 0.3, 0.6)),
                }
            }
        };
        viewport.Children.Add(lights);

        // 加 dynamic group 到場景
        var dynVis = new ModelVisual3D { Content = _dynamicGroup };
        viewport.Children.Add(dynVis);

        // Sensor Box（仿 iVS 風格：黑色金屬殼 + 頂面 XYZ logo）
        // 5-9：尺寸 25×23×4mm 等比例縮放（4 × 3.7 × 0.65）
        _boxGroup = MakeIvsStyleBox(4.0, 3.7, 0.65);
        var boxTransGroup = new Transform3DGroup();
        _boxRot = new RotateTransform3D(new QuaternionRotation3D(Quaternion.Identity));
        _boxTrans = new TranslateTransform3D(0, 0, 0);
        boxTransGroup.Children.Add(_boxRot);
        boxTransGroup.Children.Add(_boxTrans);
        _boxGroup.Transform = boxTransGroup;
        _dynamicGroup.Children.Add(_boxGroup);

        // Trail（動態 line，先放空）
        _trail = new GeometryModel3D
        {
            Geometry = new MeshGeometry3D(),
            Material = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(180, 0xF3, 0x9C, 0x12))),
        };
        _dynamicGroup.Children.Add(_trail);

        // 加速度向量箭頭（先放空）
        _vectorArrow = new GeometryModel3D
        {
            Geometry = new MeshGeometry3D(),
            Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C))),
            BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C))),
        };
        _dynamicGroup.Children.Add(_vectorArrow);

        // 地面 grid（10x10 cells, size 10）
        _grid = MakeGrid(10, 10, Color.FromArgb(80, 0x90, 0x90, 0xA8));
        _dynamicGroup.Children.Add(_grid);

        // 5-9：三軸只畫正向（從 -L 到 +L 改成 0 ~ +L），末端加 X/Y/Z 標籤
        const double axisLen = 3.5;
        var colorX = Color.FromRgb(0xE7, 0x4C, 0x3C);  // 紅
        var colorY = Color.FromRgb(0x1A, 0xBC, 0x9C);  // 綠
        var colorZ = Color.FromRgb(0x52, 0x94, 0xE2);  // 藍

        _axisX = MakeLine(new Point3D(0, 0, 0), new Point3D(axisLen, 0, 0), 0.04, colorX);
        _axisY = MakeLine(new Point3D(0, 0, 0), new Point3D(0, axisLen, 0), 0.04, colorY);
        _axisZ = MakeLine(new Point3D(0, 0, 0), new Point3D(0, 0, axisLen), 0.04, colorZ);
        _dynamicGroup.Children.Add(_axisX);
        _dynamicGroup.Children.Add(_axisY);
        _dynamicGroup.Children.Add(_axisZ);

        // 軸標籤 sprite — 在每個軸末端放一個文字 quad（永遠面對相機需要 update，但簡化版固定方向）
        _dynamicGroup.Children.Add(MakeAxisLabel(new Point3D(axisLen + 0.4, 0, 0), "X", colorX));
        _dynamicGroup.Children.Add(MakeAxisLabel(new Point3D(0, axisLen + 0.4, 0), "Y", colorY));
        _dynamicGroup.Children.Add(MakeAxisLabel(new Point3D(0, 0, axisLen + 0.4), "Z", colorZ));
    }

    /// <summary>5-9：仿 iVS Sensor 黑色金屬盒，頂面（+Z）有 XYZ logo
    /// 維度：sx = X 寬, sy = Y 深, sz = Z 高（厚度）— Z 朝上世界座標</summary>
    private static Model3DGroup MakeIvsStyleBox(double sx, double sy, double sz)
    {
        var group = new Model3DGroup();
        Color caseColor = Color.FromRgb(0x1F, 0x1F, 0x22);
        var caseMat = new MaterialGroup();
        caseMat.Children.Add(new DiffuseMaterial(new SolidColorBrush(caseColor)));
        caseMat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 80));

        double hx = sx / 2, hy = sy / 2, hz = sz / 2;

        // 頂面 +Z（朝上）→ 貼 XYZ logo
        var topMat = new MaterialGroup();
        topMat.Children.Add(new DiffuseMaterial(MakeXyzLogoBrush()));
        topMat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 60));
        // 頂面 4 角（從 +Z 法線方向往下看，順時針）：
        //   (-hx,-hy, hz) (+hx,-hy, hz) (+hx,+hy, hz) (-hx,+hy, hz)
        // UV 配對：
        //   logo 內 X 向右、Y 向上 → 在 3D 上 X 向 +X、Y 向 +Y
        //   貼圖 (0,0) 對 (-hx,-hy,+hz)，(1,0) 對 (+hx,-hy,+hz)，(1,1) 對 (+hx,+hy,+hz)，(0,1) 對 (-hx,+hy,+hz)
        group.Children.Add(MakeFaceWithUV(
            new Point3D(-hx, -hy, hz), new Point3D(hx, -hy, hz),
            new Point3D( hx,  hy, hz), new Point3D(-hx,  hy, hz),
            new System.Windows.Point(0, 1), new System.Windows.Point(1, 1),
            new System.Windows.Point(1, 0), new System.Windows.Point(0, 0),
            topMat));

        // 底面 -Z
        group.Children.Add(MakeFaceWithUV(
            new Point3D(-hx,  hy, -hz), new Point3D(hx,  hy, -hz),
            new Point3D( hx, -hy, -hz), new Point3D(-hx, -hy, -hz),
            new System.Windows.Point(0, 1), new System.Windows.Point(1, 1),
            new System.Windows.Point(1, 0), new System.Windows.Point(0, 0),
            caseMat));

        // +X 面
        group.Children.Add(MakeFaceWithUV(
            new Point3D(hx, -hy, -hz), new Point3D(hx,  hy, -hz),
            new Point3D(hx,  hy,  hz), new Point3D(hx, -hy,  hz),
            new System.Windows.Point(0, 1), new System.Windows.Point(1, 1),
            new System.Windows.Point(1, 0), new System.Windows.Point(0, 0),
            caseMat));

        // -X 面
        group.Children.Add(MakeFaceWithUV(
            new Point3D(-hx,  hy, -hz), new Point3D(-hx, -hy, -hz),
            new Point3D(-hx, -hy,  hz), new Point3D(-hx,  hy,  hz),
            new System.Windows.Point(0, 1), new System.Windows.Point(1, 1),
            new System.Windows.Point(1, 0), new System.Windows.Point(0, 0),
            caseMat));

        // +Y 面
        group.Children.Add(MakeFaceWithUV(
            new Point3D( hx, hy, -hz), new Point3D(-hx, hy, -hz),
            new Point3D(-hx, hy,  hz), new Point3D( hx, hy,  hz),
            new System.Windows.Point(0, 1), new System.Windows.Point(1, 1),
            new System.Windows.Point(1, 0), new System.Windows.Point(0, 0),
            caseMat));

        // -Y 面
        group.Children.Add(MakeFaceWithUV(
            new Point3D(-hx, -hy, -hz), new Point3D( hx, -hy, -hz),
            new Point3D( hx, -hy,  hz), new Point3D(-hx, -hy,  hz),
            new System.Windows.Point(0, 1), new System.Windows.Point(1, 1),
            new System.Windows.Point(1, 0), new System.Windows.Point(0, 0),
            caseMat));

        return group;
    }

    private static GeometryModel3D MakeFaceWithUV(
        Point3D a, Point3D b, Point3D c, Point3D d,
        System.Windows.Point ua, System.Windows.Point ub,
        System.Windows.Point uc, System.Windows.Point ud,
        Material mat)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c); mesh.Positions.Add(d);
        mesh.TextureCoordinates.Add(ua);
        mesh.TextureCoordinates.Add(ub);
        mesh.TextureCoordinates.Add(uc);
        mesh.TextureCoordinates.Add(ud);
        mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(1); mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(2); mesh.TriangleIndices.Add(3);
        return new GeometryModel3D
        {
            Geometry = mesh,
            Material = mat,
            BackMaterial = mat,
        };
    }

    private static GeometryModel3D MakeFaceWithTexture(Point3D a, Point3D b, Point3D c, Point3D d, Material mat)
        => MakeFaceWithUV(a, b, c, d,
            new System.Windows.Point(0, 1), new System.Windows.Point(1, 1),
            new System.Windows.Point(1, 0), new System.Windows.Point(0, 0), mat);

    private static System.Windows.Media.Brush MakeXyzLogoBrush()
    {
        const int size = 256;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // 背景：黑色金屬感
            dc.DrawRectangle(System.Windows.Media.Brushes.Black, null,
                new System.Windows.Rect(0, 0, size, size));

            // 邊框圓角白框（仿 iVS 圖示）
            var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 4);
            dc.DrawRoundedRectangle(null, pen, new System.Windows.Rect(16, 16, size - 32, size - 32), 18, 18);

            // 軸原點偏左下
            double cx = size * 0.32;
            double cy = size * 0.68;

            // X 軸：→ 紅
            DrawAxisArrow(dc, cx, cy, size * 0.55, 0, "X", Color.FromRgb(0xE7, 0x4C, 0x3C));
            // Y 軸：↑ 綠
            DrawAxisArrow(dc, cx, cy, 0, -size * 0.42, "Y", Color.FromRgb(0x1A, 0xBC, 0x9C));
            // Z 軸：原點圈圈標 Z（藍）
            var zBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x94, 0xE2));
            dc.DrawEllipse(null, new System.Windows.Media.Pen(zBrush, 4), new System.Windows.Point(cx, cy), 14, 14);
            dc.DrawText(MakeText("Z", 18, zBrush, true), new System.Windows.Point(cx - 7, cy - 12));

            // 右上角小立方體圖示（裝飾，仿 iVS 圖）
            DrawMiniCube(dc, size * 0.65, size * 0.32, 38);
        }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        var brush = new System.Windows.Media.ImageBrush(rtb)
        {
            Stretch = System.Windows.Media.Stretch.Fill,
            TileMode = System.Windows.Media.TileMode.None,
        };
        brush.Freeze();
        return brush;
    }

    private static void DrawAxisArrow(System.Windows.Media.DrawingContext dc,
                                       double x0, double y0, double dx, double dy,
                                       string label, Color color)
    {
        var brush = new SolidColorBrush(color);
        var pen = new System.Windows.Media.Pen(brush, 5);
        var endX = x0 + dx;
        var endY = y0 + dy;
        dc.DrawLine(pen, new System.Windows.Point(x0, y0), new System.Windows.Point(endX, endY));
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len > 1e-6)
        {
            double ux = dx / len, uy = dy / len;
            double px = -uy, py = ux;
            const double headLen = 14, headHalf = 8;
            var p1 = new System.Windows.Point(endX - ux * headLen + px * headHalf, endY - uy * headLen + py * headHalf);
            var p2 = new System.Windows.Point(endX - ux * headLen - px * headHalf, endY - uy * headLen - py * headHalf);
            var fig = new System.Windows.Media.PathFigure(new System.Windows.Point(endX, endY), new[]
            {
                new System.Windows.Media.LineSegment(p1, false),
                new System.Windows.Media.LineSegment(p2, false),
            }, true);
            dc.DrawGeometry(brush, null, new System.Windows.Media.PathGeometry(new[] { fig }));
        }
        dc.DrawText(MakeText(label, 22, brush, true),
            new System.Windows.Point(endX + (dx >= 0 ? 6 : -22),
                                     endY + (dy >= 0 ? 4 : -28)));
    }

    private static void DrawMiniCube(System.Windows.Media.DrawingContext dc, double cx, double cy, double s)
    {
        var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 2.5);
        double dx = s * 0.5, dy = s * 0.28;
        var f1 = new System.Windows.Point(cx - dx, cy);
        var f2 = new System.Windows.Point(cx,      cy + dy);
        var f3 = new System.Windows.Point(cx + dx, cy);
        var f4 = new System.Windows.Point(cx,      cy - dy);
        var topL = new System.Windows.Point(cx - dx, cy - dy * 2);
        var topR = new System.Windows.Point(cx + dx, cy - dy * 2);
        var top  = new System.Windows.Point(cx,      cy - dy * 2);
        dc.DrawLine(pen, f1, f2); dc.DrawLine(pen, f2, f3);
        dc.DrawLine(pen, f3, f4); dc.DrawLine(pen, f4, f1);
        dc.DrawLine(pen, f1, topL); dc.DrawLine(pen, f3, topR);
        dc.DrawLine(pen, f4, top);
        dc.DrawLine(pen, top, topL); dc.DrawLine(pen, top, topR);
    }

    private static System.Windows.Media.FormattedText MakeText(string s, double size,
                                                                System.Windows.Media.Brush brush, bool bold)
    {
        return new System.Windows.Media.FormattedText(
            s, CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface(
                new System.Windows.Media.FontFamily("Segoe UI"),
                System.Windows.FontStyles.Normal,
                bold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
                System.Windows.FontStretches.Normal),
            size, brush, 96.0 / 96.0);
    }

    /// <summary>5-9：3D 軸末端的文字標籤（quad billboard，固定方向）</summary>
    private static GeometryModel3D MakeAxisLabel(Point3D pos, string text, Color color)
    {
        // 用 256x256 RTB 畫文字 → ImageBrush
        const int size = 128;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // 透明背景
            dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null,
                new System.Windows.Rect(0, 0, size, size));
            // 文字（白邊 + 該軸色填）
            var ft = new System.Windows.Media.FormattedText(
                text, CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface(
                    new System.Windows.Media.FontFamily("Segoe UI"),
                    System.Windows.FontStyles.Normal,
                    System.Windows.FontWeights.Bold,
                    System.Windows.FontStretches.Normal),
                90, new SolidColorBrush(color), 96.0 / 96.0);
            // 居中
            double tx = (size - ft.Width) / 2;
            double ty = (size - ft.Height) / 2;
            // 黑色描邊（畫四個偏移）
            var blackFt = new System.Windows.Media.FormattedText(
                text, CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface(
                    new System.Windows.Media.FontFamily("Segoe UI"),
                    System.Windows.FontStyles.Normal,
                    System.Windows.FontWeights.Bold,
                    System.Windows.FontStretches.Normal),
                90, System.Windows.Media.Brushes.Black, 96.0 / 96.0);
            for (int dx = -2; dx <= 2; dx += 2)
                for (int dy = -2; dy <= 2; dy += 2)
                    if (dx != 0 || dy != 0)
                        dc.DrawText(blackFt, new System.Windows.Point(tx + dx, ty + dy));
            dc.DrawText(ft, new System.Windows.Point(tx, ty));
        }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        var brush = new System.Windows.Media.ImageBrush(rtb)
        {
            Stretch = System.Windows.Media.Stretch.Uniform,
        };
        brush.Freeze();

        // 0.4 大小的 quad，在 Z up 世界下，sprite 平面垂直 (normal 是 -Y)，這樣從各方向都看得到
        const double s = 0.4;
        var mesh = new MeshGeometry3D();
        // 立面：在 XZ 平面上 (normal = -Y)
        mesh.Positions.Add(new Point3D(pos.X - s, pos.Y, pos.Z - s));
        mesh.Positions.Add(new Point3D(pos.X + s, pos.Y, pos.Z - s));
        mesh.Positions.Add(new Point3D(pos.X + s, pos.Y, pos.Z + s));
        mesh.Positions.Add(new Point3D(pos.X - s, pos.Y, pos.Z + s));
        mesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));
        mesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
        mesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
        mesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));
        mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(1); mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(2); mesh.TriangleIndices.Add(3);
        var mat = new EmissiveMaterial(brush);
        return new GeometryModel3D
        {
            Geometry = mesh,
            Material = mat,
            BackMaterial = mat,
        };
    }

    private static GeometryModel3D MakeBox(double sx, double sy, double sz, Color color)
    {
        var mesh = new MeshGeometry3D();
        double hx = sx / 2, hy = sy / 2, hz = sz / 2;
        // 8 個頂點
        var p = new[]
        {
            new Point3D(-hx, -hy, -hz), new Point3D( hx, -hy, -hz),
            new Point3D( hx,  hy, -hz), new Point3D(-hx,  hy, -hz),
            new Point3D(-hx, -hy,  hz), new Point3D( hx, -hy,  hz),
            new Point3D( hx,  hy,  hz), new Point3D(-hx,  hy,  hz),
        };
        foreach (var pt in p) mesh.Positions.Add(pt);
        // 12 個三角形 (6 面)
        int[] idx = {
            0,1,2, 0,2,3,   // 後 (-Z)
            5,4,7, 5,7,6,   // 前 (+Z)
            4,0,3, 4,3,7,   // 左 (-X)
            1,5,6, 1,6,2,   // 右 (+X)
            3,2,6, 3,6,7,   // 上 (+Y)
            4,5,1, 4,1,0,   // 下 (-Y)
        };
        foreach (var i in idx) mesh.TriangleIndices.Add(i);

        var mat = new MaterialGroup();
        mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        mat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 50));

        return new GeometryModel3D
        {
            Geometry = mesh,
            Material = mat,
            BackMaterial = new DiffuseMaterial(new SolidColorBrush(color)),
        };
    }

    /// <summary>用細長 box 模擬 line（WPF 沒原生 3D line）</summary>
    private static GeometryModel3D MakeLine(Point3D a, Point3D b, double thickness, Color color)
    {
        var mesh = new MeshGeometry3D();
        Vector3D dir = b - a;
        double len = dir.Length;
        if (len < 1e-9) return new GeometryModel3D();
        dir.Normalize();
        // 找一個垂直方向
        Vector3D up = Math.Abs(Vector3D.DotProduct(dir, new Vector3D(0, 1, 0))) > 0.95
            ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
        Vector3D side = Vector3D.CrossProduct(dir, up); side.Normalize();
        Vector3D up2 = Vector3D.CrossProduct(side, dir); up2.Normalize();
        double t = thickness * 0.5;

        Point3D[] verts =
        {
            a - side * t - up2 * t, a + side * t - up2 * t,
            a + side * t + up2 * t, a - side * t + up2 * t,
            b - side * t - up2 * t, b + side * t - up2 * t,
            b + side * t + up2 * t, b - side * t + up2 * t,
        };
        foreach (var v in verts) mesh.Positions.Add(v);
        int[] idx = {
            0,1,2, 0,2,3,
            5,4,7, 5,7,6,
            4,0,3, 4,3,7,
            1,5,6, 1,6,2,
            3,2,6, 3,6,7,
            4,5,1, 4,1,0,
        };
        foreach (var i in idx) mesh.TriangleIndices.Add(i);
        return new GeometryModel3D
        {
            Geometry = mesh,
            Material = new EmissiveMaterial(new SolidColorBrush(color)),
            BackMaterial = new EmissiveMaterial(new SolidColorBrush(color)),
        };
    }

    private static GeometryModel3D MakeGrid(int cells, double size, Color color)
    {
        // 5-9：地面在 XY 平面（Z = z0 為高度）
        var mesh = new MeshGeometry3D();
        double half = size / 2;
        double step = size / cells;
        double z0 = -1.5;       // 地面 Z 高度（box 中心下方）
        double t = 0.012;       // line 厚度

        // 沿 X 方向的線（每條 Y 不同，從 -half 到 half）
        for (int i = 0; i <= cells; i++)
        {
            double y = -half + i * step;
            int basei = mesh.Positions.Count;
            // 在 z = z0 平面上一條沿 X 的細長矩形
            mesh.Positions.Add(new Point3D(-half, y - t, z0));
            mesh.Positions.Add(new Point3D( half, y - t, z0));
            mesh.Positions.Add(new Point3D( half, y + t, z0));
            mesh.Positions.Add(new Point3D(-half, y + t, z0));
            mesh.TriangleIndices.Add(basei);     mesh.TriangleIndices.Add(basei + 1); mesh.TriangleIndices.Add(basei + 2);
            mesh.TriangleIndices.Add(basei);     mesh.TriangleIndices.Add(basei + 2); mesh.TriangleIndices.Add(basei + 3);
        }
        // 沿 Y 方向的線（每條 X 不同）
        for (int i = 0; i <= cells; i++)
        {
            double x = -half + i * step;
            int basei = mesh.Positions.Count;
            mesh.Positions.Add(new Point3D(x - t, -half, z0));
            mesh.Positions.Add(new Point3D(x + t, -half, z0));
            mesh.Positions.Add(new Point3D(x + t,  half, z0));
            mesh.Positions.Add(new Point3D(x - t,  half, z0));
            mesh.TriangleIndices.Add(basei);     mesh.TriangleIndices.Add(basei + 1); mesh.TriangleIndices.Add(basei + 2);
            mesh.TriangleIndices.Add(basei);     mesh.TriangleIndices.Add(basei + 2); mesh.TriangleIndices.Add(basei + 3);
        }
        return new GeometryModel3D
        {
            Geometry = mesh,
            Material = new EmissiveMaterial(new SolidColorBrush(color)),
            BackMaterial = new EmissiveMaterial(new SolidColorBrush(color)),
        };
    }

    // ─────────────────────────────────────────────────
    //  載入 CSV
    // ─────────────────────────────────────────────────

    private void OnLoadCsvClick(object sender, RoutedEventArgs e)
    {
        // 5-9：可以選 Tilt 或 Vib 任一檔，系統自動找配對
        var phmRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tranzx PHM");
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationService.Instance["Motion3D.SelectTiltCsv"],
            Filter = "Trend CSV (*Tilt*.csv;*Vib*.csv)|*Tilt*.csv;*Vib*.csv|All CSV (*.csv)|*.csv",
            InitialDirectory = Directory.Exists(phmRoot) ? phmRoot : null,
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            _frames.Clear();
            string selected = dlg.FileName;
            string? tiltPath = null, vibPath = null;

            // 看選的是哪個類型
            string fname = Path.GetFileNameWithoutExtension(selected).ToLowerInvariant();
            if (fname.Contains("tilt"))
            {
                tiltPath = selected;
                vibPath = TryFindCompanion(selected, "Tilt", "Vib", "Vibration");
            }
            else if (fname.Contains("vib"))
            {
                vibPath = selected;
                tiltPath = TryFindCompanion(selected, "Vib", "Tilt", "Tilt");
            }
            else
            {
                // 不確定 → 試解析後判斷
                tiltPath = selected;
            }

            // 載入 Tilt（提供 frame timeline）
            if (tiltPath is not null && File.Exists(tiltPath))
                LoadTiltCsv(tiltPath);

            // 若 Tilt 沒有有效資料，嘗試讓 Vib 自己當 timeline 來源
            if (_frames.Count == 0 && vibPath is not null && File.Exists(vibPath))
            {
                LoadVibAsTimeline(vibPath);
            }
            else if (vibPath is not null && File.Exists(vibPath))
            {
                LoadVibCsvAndMerge(vibPath);
            }

            // 顯示載入結果
            string tiltLabel = tiltPath is null ? "(none)" : Path.GetFileName(tiltPath);
            string vibLabel  = vibPath  is null ? "(none)" : Path.GetFileName(vibPath);
            lblFile.Text = $"Tilt: {tiltLabel}    |    Vib: {vibLabel}";

            if (_frames.Count == 0)
            {
                MessageBox.Show(LocalizationService.Instance["Motion3D.NoData"]);
                return;
            }

            sldTimeline.Maximum = _frames.Count - 1;
            sldTimeline.Value = 0;
            _frameIdx = 0;
            UpdateScene(0);
            UpdateTimelineLabel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message,
                LocalizationService.Instance["Motion3D.Title"],
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>5-9：給定任一 trend csv 路徑，找同 sensor 同 timestamp 的另一個類型 csv</summary>
    private static string? TryFindCompanion(string sourcePath, string sourceKind, string targetSuffix, string targetFolderName)
    {
        // 例：sourcePath = ...\Sensor1\Tilt\trend_Sensor1_yyyyMMdd_HHmmss_Tilt.csv
        // 期望找：    ...\Sensor1\Vibration\trend_Sensor1_yyyyMMdd_HHmmss_Vib.csv
        try
        {
            var dir = Path.GetDirectoryName(sourcePath);
            if (dir is null) return null;
            var parent = Path.GetDirectoryName(dir);
            if (parent is null) return null;
            var targetDir = Path.Combine(parent, targetFolderName);
            if (!Directory.Exists(targetDir)) return null;

            // 從檔名抽 timestamp
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            // 把 _Tilt 或 _Vib 拿掉得 base key
            var key = name.Replace("_" + sourceKind, "");
            // 找直接配對
            var direct = Directory.GetFiles(targetDir, key + "_" + targetSuffix + ".csv");
            if (direct.Length > 0) return direct[0];
            // 退而求其次：找該 timestamp 內最近的
            var allCandidates = Directory.GetFiles(targetDir, "*_" + targetSuffix + ".csv");
            return allCandidates.Length > 0 ? allCandidates[0] : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>5-9：當 Tilt 檔不存在時，用 Vib csv 自己建 timeline（角度=0）</summary>
    private void LoadVibAsTimeline(string path)
    {
        var lines = File.ReadAllLines(path);
        int headerIdx = FindHeader(lines);
        if (headerIdx < 0) return;
        var headers = SplitCsv(lines[headerIdx]);
        int timeCol = -1, peakX = -1, peakY = -1, peakZ = -1;
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Trim().ToUpperInvariant();
            if (h == "TIME") { timeCol = i; continue; }
            if (h.Contains("PEAK"))
            {
                if (h.Contains("X") && peakX < 0) peakX = i;
                else if (h.Contains("Y") && peakY < 0) peakY = i;
                else if (h.Contains("Z") && peakZ < 0) peakZ = i;
            }
        }
        if (timeCol < 0) return;
        for (int li = headerIdx + 1; li < lines.Length; li++)
        {
            if (string.IsNullOrWhiteSpace(lines[li])) continue;
            var row = SplitCsv(lines[li]);
            if (row.Length <= timeCol) continue;
            if (!TryParseTime(row[timeCol], out var t)) continue;
            double x = 0, y = 0, z = 0;
            if (peakX >= 0 && row.Length > peakX) double.TryParse(row[peakX], NumberStyles.Float, CultureInfo.InvariantCulture, out x);
            if (peakY >= 0 && row.Length > peakY) double.TryParse(row[peakY], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
            if (peakZ >= 0 && row.Length > peakZ) double.TryParse(row[peakZ], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
            _frames.Add(new Frame
            {
                Time = t, AngleX = 0, AngleY = 0, AngleZ = 0,
                AccX = x, AccY = y, AccZ = z,
                AccMag = Math.Sqrt(x * x + y * y + z * z),
            });
        }
    }

    private void LoadTiltCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        int headerIdx = FindHeader(lines);
        if (headerIdx < 0) return;

        var headers = SplitCsv(lines[headerIdx]);
        int timeCol = -1, ax = -1, ay = -1, az = -1;
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Trim().ToUpperInvariant();
            if (h == "TIME") { timeCol = i; continue; }
            // 5-9：兼容多種 header 寫法
            //   "X-angle" / "AngleX" / "TiltX" / "X" 都對應 X 軸
            bool hasAng = h.Contains("ANGLE") || h.Contains("TILT");
            if (hasAng || h == "X" || h == "Y" || h == "Z")
            {
                if (h.Contains("X") && ax < 0) ax = i;
                else if (h.Contains("Y") && ay < 0) ay = i;
                else if (h.Contains("Z") && az < 0) az = i;
            }
        }
        if (timeCol < 0 || ax < 0 || ay < 0 || az < 0) return;

        for (int li = headerIdx + 1; li < lines.Length; li++)
        {
            if (string.IsNullOrWhiteSpace(lines[li])) continue;
            var row = SplitCsv(lines[li]);
            if (row.Length <= Math.Max(Math.Max(timeCol, ax), Math.Max(ay, az))) continue;
            if (!TryParseTime(row[timeCol], out var t)) continue;
            if (!double.TryParse(row[ax], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) continue;
            if (!double.TryParse(row[ay], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) continue;
            if (!double.TryParse(row[az], NumberStyles.Float, CultureInfo.InvariantCulture, out double z)) continue;
            _frames.Add(new Frame { Time = t, AngleX = x, AngleY = y, AngleZ = z });
        }
    }

    private void LoadVibCsvAndMerge(string path)
    {
        var lines = File.ReadAllLines(path);
        int headerIdx = FindHeader(lines);
        if (headerIdx < 0) return;
        var headers = SplitCsv(lines[headerIdx]);
        int timeCol = -1, peakX = -1, peakY = -1, peakZ = -1;
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Trim().ToUpperInvariant();
            if (h == "TIME") { timeCol = i; continue; }
            // 找 X-peak / Y-peak / Z-peak（也接受 X-PEAK / PEAK X / X_PEAK）
            if (h.Contains("PEAK"))
            {
                if (h.Contains("X") && peakX < 0) peakX = i;
                else if (h.Contains("Y") && peakY < 0) peakY = i;
                else if (h.Contains("Z") && peakZ < 0) peakZ = i;
            }
        }
        if (timeCol < 0) return;
        // 至少有一軸 peak 才有意義
        if (peakX < 0 && peakY < 0 && peakZ < 0) return;

        // 讀進 list
        var vibFrames = new List<(DateTime t, double x, double y, double z)>();
        for (int li = headerIdx + 1; li < lines.Length; li++)
        {
            if (string.IsNullOrWhiteSpace(lines[li])) continue;
            var row = SplitCsv(lines[li]);
            int maxNeed = Math.Max(timeCol, Math.Max(peakX, Math.Max(peakY, peakZ)));
            if (row.Length <= maxNeed) continue;
            if (!TryParseTime(row[timeCol], out var t)) continue;
            double x = 0, y = 0, z = 0;
            if (peakX >= 0) double.TryParse(row[peakX], NumberStyles.Float, CultureInfo.InvariantCulture, out x);
            if (peakY >= 0) double.TryParse(row[peakY], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
            if (peakZ >= 0) double.TryParse(row[peakZ], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
            vibFrames.Add((t, x, y, z));
        }
        if (vibFrames.Count == 0) return;

        // 合併到 _frames（用最近鄰時間對齊）
        int j = 0;
        foreach (var f in _frames)
        {
            while (j + 1 < vibFrames.Count
                   && Math.Abs((vibFrames[j + 1].t - f.Time).TotalSeconds)
                    < Math.Abs((vibFrames[j].t - f.Time).TotalSeconds))
                j++;
            f.AccX = vibFrames[j].x;
            f.AccY = vibFrames[j].y;
            f.AccZ = vibFrames[j].z;
            f.AccMag = Math.Sqrt(f.AccX * f.AccX + f.AccY * f.AccY + f.AccZ * f.AccZ);
        }
    }



    private static int FindHeader(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("Time,", StringComparison.OrdinalIgnoreCase)
                || lines[i].StartsWith("\"Time\",", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string[] SplitCsv(string line)
    {
        var list = new List<string>();
        bool inQ = false;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQ && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQ = !inQ;
            }
            else if (c == ',' && !inQ) { list.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        list.Add(sb.ToString());
        return list.ToArray();
    }

    private static bool TryParseTime(string s, out DateTime t)
    {
        s = s.Trim();
        var formats = new[]
        {
            "yyyy/MM/dd HH:mm:ss.fff", "yyyy/M/d HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss.fff", "HH:mm:ss.fff", "HH:mm:ss",
        };
        foreach (var f in formats)
        {
            if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out t)) return true;
        }
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out t);
    }

    // ─────────────────────────────────────────────────
    //  動畫播放
    // ─────────────────────────────────────────────────

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (_frames.Count == 0) return;
        _playing = !_playing;
        btnPlay.Content = _playing ? "⏸" : "▶";
        if (_playing)
        {
            if (_frameIdx >= _frames.Count - 1) _frameIdx = 0;
            _playTimer?.Start();
        }
        else _playTimer?.Stop();
    }

    private void OnRewindClick(object sender, RoutedEventArgs e)
    {
        _frameIdx = 0;
        sldTimeline.Value = 0;
        UpdateScene(0);
        UpdateTimelineLabel();
    }

    /// <summary>5-9 (B)：把整段 timeline 渲染成 AVI 影片</summary>
    private async void OnExportAviClick(object sender, RoutedEventArgs e)
    {
        if (_frames.Count == 0)
        {
            MessageBox.Show(LocalizationService.Instance["Motion3D.NoData"]);
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = LocalizationService.Instance["Motion3D.ExportAvi"],
            Filter = "AVI video (*.avi)|*.avi",
            FileName = $"Motion3D_{DateTime.Now:yyyyMMdd_HHmmss}.avi",
        };
        if (dlg.ShowDialog() != true) return;

        bool ok = await RenderTimelineToAviAsync(dlg.FileName);
        if (ok)
        {
            MessageBox.Show($"已輸出：\n{dlg.FileName}",
                "Motion 3D", MessageBoxButton.OK, MessageBoxImage.Information);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = "explorer.exe", Arguments = $"/select,\"{dlg.FileName}\"" }); } catch { }
        }
    }

    /// <summary>5-9 (B)：渲染 timeline → 暫存 AVI → 用 ffmpeg 轉成 MP4 (H.264)</summary>
    private async void OnExportMp4Click(object sender, RoutedEventArgs e)
    {
        if (_frames.Count == 0)
        {
            MessageBox.Show(LocalizationService.Instance["Motion3D.NoData"]);
            return;
        }
        // 先檢查 ffmpeg 是否存在
        string? ffmpeg = Services.FFmpegService.FindFFmpeg();
        if (ffmpeg is null)
        {
            ShowFFmpegMissingDialog();
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = LocalizationService.Instance["Motion3D.ExportMp4"],
            Filter = "MP4 video (*.mp4)|*.mp4",
            FileName = $"Motion3D_{DateTime.Now:yyyyMMdd_HHmmss}.mp4",
        };
        if (dlg.ShowDialog() != true) return;
        string mp4Path = dlg.FileName;
        string tmpAvi = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"motion3d_tmp_{DateTime.Now:yyyyMMddHHmmss}.avi");

        bool aviOk = await RenderTimelineToAviAsync(tmpAvi);
        if (!aviOk) return;

        var (progWin, progLbl, progBar) = CreateProgressWindow(
            LocalizationService.Instance["Motion3D.EncodingMp4"]);
        progBar.IsIndeterminate = true;
        progWin.Show();
        try
        {
            bool ok = await Services.FFmpegService.ConvertAviToMp4Async(
                ffmpeg, tmpAvi, mp4Path,
                onProgress: (frame, _) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        progLbl.Text = $"H.264 轉碼中：{frame} 幀...";
                    });
                });
            progWin.Close();
            try { File.Delete(tmpAvi); } catch { }

            if (ok)
            {
                MessageBox.Show($"已輸出：\n{mp4Path}",
                    "Motion 3D", MessageBoxButton.OK, MessageBoxImage.Information);
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    { FileName = "explorer.exe", Arguments = $"/select,\"{mp4Path}\"" }); } catch { }
            }
            else
            {
                MessageBox.Show("MP4 轉碼失敗。建議改用 AVI 匯出 + VLC 播放。",
                    "Motion 3D", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            try { progWin.Close(); } catch { }
            try { File.Delete(tmpAvi); } catch { }
            MessageBox.Show($"轉碼失敗：{ex.Message}", "Motion 3D",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowFFmpegMissingDialog()
    {
        var msg = LocalizationService.Instance["Motion3D.FFmpegMissing"];
        var result = MessageBox.Show(
            msg,
            LocalizationService.Instance["Motion3D.ExportMp4"],
            MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (result == MessageBoxResult.Yes)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/BtbN/FFmpeg-Builds/releases",
                    UseShellExecute = true,
                });
            }
            catch { }
        }
    }

    /// <summary>渲染整個 timeline 成 AVI（給 AVI 匯出 / MP4 轉碼前置 共用）</summary>
    private async System.Threading.Tasks.Task<bool> RenderTimelineToAviAsync(string aviPath)
    {
        bool wasPlaying = _playing;
        if (_playing) { _playing = false; _playTimer?.Stop(); btnPlay.Content = "▶"; }

        const int videoFps = 30;
        int vw = 1280, vh = 720;
        double durSec = (_frames[_frames.Count - 1].Time - _frames[0].Time).TotalSeconds;
        if (durSec < 0.1) durSec = _frames.Count / 30.0;
        int totalVideoFrames = Math.Max(2, (int)Math.Round(durSec * videoFps));
        if (totalVideoFrames > 1800) totalVideoFrames = 1800;

        var (progWin, progLbl, progBar) = CreateProgressWindow(
            LocalizationService.Instance["Motion3D.RenderingAvi"]);
        progBar.Maximum = totalVideoFrames;
        progWin.Show();

        try
        {
            int dropped = 0;
            using (var avi = new Services.MjpegAviWriter(aviPath, vw, vh, videoFps, jpegQuality: 85))
            {
                int srcFrames = _frames.Count;
                for (int v = 0; v < totalVideoFrames; v++)
                {
                    int srcIdx = (int)((double)v / (totalVideoFrames - 1) * (srcFrames - 1));
                    srcIdx = Math.Clamp(srcIdx, 0, srcFrames - 1);
                    UpdateScene(srcIdx);
                    sldTimeline.Value = srcIdx;
                    _frameIdx = srcIdx;
                    UpdateTimelineLabel();

                    viewport.InvalidateVisual();
                    viewport.UpdateLayout();
                    await System.Windows.Threading.Dispatcher.Yield(
                        System.Windows.Threading.DispatcherPriority.Render);
                    await System.Threading.Tasks.Task.Delay(1);

                    var rtb = Services.FrameCapture.CaptureElement(viewport, vw, vh);
                    if (rtb is null)
                    {
                        // 第一次失敗 → 多等一點再試一次
                        await System.Threading.Tasks.Task.Delay(50);
                        rtb = Services.FrameCapture.CaptureElement(viewport, vw, vh);
                    }
                    if (rtb is null)
                    {
                        dropped++;
                        try { ErrorLogService.Instance.Warn("Motion3D",
                            $"frame {v} capture failed, dropped"); } catch { }
                    }
                    else
                    {
                        avi.AddFrame(rtb);
                    }

                    // 每 30 幀強制 GC 一次，避免 RenderTargetBitmap 累積爆 GPU memory
                    if (v > 0 && v % 30 == 0)
                    {
                        System.GC.Collect();
                        System.GC.WaitForPendingFinalizers();
                    }

                    if (v % 5 == 0)
                    {
                        progLbl.Text = dropped > 0
                            ? $"渲染 3D {v + 1} / {totalVideoFrames} 幀（{dropped} 幀失敗已跳過）..."
                            : $"渲染 3D {v + 1} / {totalVideoFrames} 幀...";
                        progBar.Value = v + 1;
                    }
                }
            }
            progWin.Close();
            if (dropped > totalVideoFrames / 4)
            {
                MessageBox.Show($"⚠ 有 {dropped} / {totalVideoFrames} 幀截圖失敗（GPU 暫時無法回應）。\n\n" +
                                "影片可能不完整。建議：\n" +
                                "  • 關閉其他佔用 GPU 的程式（瀏覽器、遊戲、Teams 等）\n" +
                                "  • 縮短 timeline（用滑鼠拖小範圍再匯出）\n" +
                                "  • 切到節能模式重試",
                    "Motion 3D", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return true;
        }
        catch (Exception ex)
        {
            try { progWin.Close(); } catch { }
            try { ErrorLogService.Instance.Error("Motion3D", $"Render AVI failed: {ex.Message}\n{ex.StackTrace}"); } catch { }
            MessageBox.Show($"輸出失敗：{ex.Message}", "Motion 3D",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            if (wasPlaying) { _playing = true; btnPlay.Content = "⏸"; _playTimer?.Start(); }
        }
    }

    private (Window win, TextBlock lbl, ProgressBar bar) CreateProgressWindow(string title)
    {
        var w = new Window
        {
            Title = title,
            Width = 420, Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("BgPrimary"),
            ResizeMode = ResizeMode.NoResize,
        };
        var sp = new StackPanel { Margin = new Thickness(16) };
        var lbl = new TextBlock
        {
            Text = title + "...",
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimary"),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        };
        var bar = new ProgressBar
        {
            Minimum = 0, Maximum = 100, Height = 22, Margin = new Thickness(0, 12, 0, 0),
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("AccentTeal"),
        };
        sp.Children.Add(lbl);
        sp.Children.Add(bar);
        w.Content = sp;
        return (w, lbl, bar);
    }

    private void OnPlayTick(object? sender, EventArgs e)
    {
        if (_frames.Count == 0) return;
        // 33ms tick；speed=1× 以資料原始 sampling rate 播放
        // 為簡化：用「frame index 增量 = speed」（speed=1 表示每 tick 跳 1 frame）
        int step = Math.Max(1, (int)Math.Round(_speed));
        if (_speed < 1.0)
        {
            // 慢速：用機率跳格
            if (Random.Shared.NextDouble() > _speed) return;
            step = 1;
        }
        _frameIdx += step;
        if (_frameIdx >= _frames.Count)
        {
            _frameIdx = _frames.Count - 1;
            _playing = false;
            btnPlay.Content = "▶";
            _playTimer?.Stop();
        }
        sldTimeline.Value = _frameIdx;
        UpdateScene(_frameIdx);
        UpdateTimelineLabel();
    }

    private void OnTimelineChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        if (_frames.Count == 0) return;
        int idx = (int)Math.Round(e.NewValue);
        if (idx < 0 || idx >= _frames.Count) return;
        _frameIdx = idx;
        UpdateScene(idx);
        UpdateTimelineLabel();
    }

    private void OnSpeedChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (cmbSpeed.SelectedItem is not string s) return;
        s = s.Replace("×", "").Trim();
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            _speed = v;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        UpdateScene(_frameIdx);
    }
    private void OnSettingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        if (lblMotionScale is not null) lblMotionScale.Text = $"{(int)sldMotionScale.Value}×";
        if (lblTrail is not null) lblTrail.Text = $"{(int)sldTrail.Value}";
        UpdateScene(_frameIdx);
    }

    private void OnGridToggle(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        if (_grid is null) return;
        if (cbShowGrid.IsChecked == true)
        {
            if (!_dynamicGroup.Children.Contains(_grid))
                _dynamicGroup.Children.Add(_grid);
        }
        else
        {
            _dynamicGroup.Children.Remove(_grid);
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _playing = false;
        _playTimer?.Stop();
        btnPlay.Content = "▶";
        _frames.Clear();
        _frameIdx = 0;
        sldTimeline.Value = 0;
        sldTimeline.Maximum = 100;
        UpdateTimelineLabel();
        lblFile.Text = "";
        ResetScene();
    }

    // ─────────────────────────────────────────────────
    //  更新 3D 場景
    // ─────────────────────────────────────────────────

    private void UpdateScene(int idx)
    {
        if (_frames.Count == 0) { ResetScene(); return; }
        if (idx < 0) idx = 0;
        if (idx >= _frames.Count) idx = _frames.Count - 1;
        var f = _frames[idx];

        // 1. 旋轉：直接套用 sensor 報告的絕對角度
        // - 場景已是 Z up：sensor 平放 → AngleX≈0 AngleY≈0 → 盒子保持平放（XYZ logo 朝上 +Z）
        // - sensor 側立 X 朝上 → AngleY 接近 ±90°（繞 Y 軸轉）→ 盒子立起來
        // - sensor 側立 Y 朝上 → AngleX 接近 ±90°（繞 X 軸轉）→ 盒子側翻
        if (_boxRot is not null)
        {
            // 組合（順序：Z → Y → X）
            Quaternion q = Quaternion.Identity;
            q *= new Quaternion(new Vector3D(0, 0, 1), f.AngleZ);
            q *= new Quaternion(new Vector3D(0, 1, 0), f.AngleY);
            q *= new Quaternion(new Vector3D(1, 0, 0), f.AngleX);
            _boxRot.Rotation = new QuaternionRotation3D(q);
        }

        // 2. 平移：振動 jitter（放大 sldMotionScale 倍）
        double scale = sldMotionScale.Value / 1000.0;  // slider 1~200 → 0.001~0.2
        if (_boxTrans is not null)
        {
            _boxTrans.OffsetX = f.AccX * scale;
            _boxTrans.OffsetY = f.AccY * scale;
            _boxTrans.OffsetZ = f.AccZ * scale;
        }

        // 3. 加速度向量箭頭
        if (_vectorArrow is not null && cbShowVector.IsChecked == true)
        {
            // 從 box 中心射出，方向 = (AccX, AccY, AccZ)，長度 = |G| * 4
            var origin = new Point3D(_boxTrans?.OffsetX ?? 0, _boxTrans?.OffsetY ?? 0, _boxTrans?.OffsetZ ?? 0);
            var dir = new Vector3D(f.AccX, f.AccY, f.AccZ);
            double len = Math.Min(5.0, dir.Length * 4);
            if (dir.LengthSquared > 1e-6 && len > 0.1)
            {
                dir.Normalize();
                var tip = origin + dir * len;
                // 顏色：依 |G| 警報等級
                Color col = f.AccMag > 0.5
                    ? Color.FromRgb(0xE7, 0x4C, 0x3C)  // 紅
                    : f.AccMag > 0.2
                    ? Color.FromRgb(0xF3, 0x9C, 0x12)  // 黃
                    : Color.FromRgb(0x1A, 0xBC, 0x9C); // 綠
                _vectorArrow.Geometry = MakeArrowMesh(origin, tip, 0.06, col);
                _vectorArrow.Material = new EmissiveMaterial(new SolidColorBrush(col));
                _vectorArrow.BackMaterial = new EmissiveMaterial(new SolidColorBrush(col));
            }
            else
            {
                _vectorArrow.Geometry = new MeshGeometry3D();
            }
        }
        else if (_vectorArrow is not null)
        {
            _vectorArrow.Geometry = new MeshGeometry3D();
        }

        // 4. 軌跡尾巴
        UpdateTrail(idx);
        UpdateLissajous(idx);

        // 5. 右側即時數值
        lblAngleX.Text = $"{f.AngleX,7:F2} °";
        lblAngleY.Text = $"{f.AngleY,7:F2} °";
        lblAngleZ.Text = $"{f.AngleZ,7:F2} °";
        lblAccX.Text   = $"{f.AccX,7:F4} G";
        lblAccY.Text   = $"{f.AccY,7:F4} G";
        lblAccZ.Text   = $"{f.AccZ,7:F4} G";
        lblAccMag.Text = $"{f.AccMag,7:F4} G";
        lblFrameTime.Text = f.Time.ToString("yyyy/MM/dd HH:mm:ss.fff");
    }

    /// <summary>5-9 (A)：Lissajous X-Y 投影 — 工業界判讀軸對心 / 不平衡的標準工具</summary>
    private void UpdateLissajous(int upToIdx)
    {
        lissajousCanvas.Children.Clear();
        if (_frames.Count == 0) { lblLissajousHint.Text = "—"; return; }

        // 取最近 N 點（用 trail 一樣的長度，但有上限）
        int trailLen = Math.Max(60, (int)sldTrail.Value);
        int start = Math.Max(0, upToIdx - trailLen);
        int n = upToIdx - start + 1;
        if (n < 2) { lblLissajousHint.Text = "—"; return; }

        double cw = lissajousCanvas.ActualWidth > 0 ? lissajousCanvas.ActualWidth : 246;
        double ch = lissajousCanvas.ActualHeight > 0 ? lissajousCanvas.ActualHeight : 246;
        double cx = cw / 2, cy = ch / 2;

        // 找 X / Y 振動的最大絕對值，scale 適當
        double absMax = 1e-6;
        for (int i = start; i <= upToIdx; i++)
        {
            absMax = Math.Max(absMax, Math.Abs(_frames[i].AccX));
            absMax = Math.Max(absMax, Math.Abs(_frames[i].AccY));
        }
        // 留 10% margin
        double r = Math.Min(cw, ch) / 2 * 0.85;
        double scale = r / absMax;

        // 背景十字 + 圓圈刻度
        var axisBrush = new SolidColorBrush(Color.FromArgb(80, 0xC0, 0xC0, 0xCF));
        var crossPen = new System.Windows.Media.Pen(axisBrush, 1);
        var l1 = new System.Windows.Shapes.Line { X1 = 0, X2 = cw, Y1 = cy, Y2 = cy, Stroke = axisBrush, StrokeThickness = 0.6 };
        var l2 = new System.Windows.Shapes.Line { X1 = cx, X2 = cx, Y1 = 0, Y2 = ch, Stroke = axisBrush, StrokeThickness = 0.6 };
        lissajousCanvas.Children.Add(l1);
        lissajousCanvas.Children.Add(l2);
        // 三圈刻度（25%, 50%, 100% absMax）
        for (int k = 1; k <= 4; k++)
        {
            double rk = r * k / 4;
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = rk * 2, Height = rk * 2,
                Stroke = new SolidColorBrush(Color.FromArgb(50, 0xC0, 0xC0, 0xCF)),
                StrokeThickness = 0.5, StrokeDashArray = new System.Windows.Media.DoubleCollection { 2, 3 },
            };
            Canvas.SetLeft(ring, cx - rk);
            Canvas.SetTop(ring, cy - rk);
            lissajousCanvas.Children.Add(ring);
        }
        // 軸標籤
        var lblX = new TextBlock { Text = "X", Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
                                    FontSize = 11, FontWeight = System.Windows.FontWeights.Bold };
        Canvas.SetLeft(lblX, cw - 14); Canvas.SetTop(lblX, cy + 2);
        lissajousCanvas.Children.Add(lblX);
        var lblY = new TextBlock { Text = "Y", Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0xBC, 0x9C)),
                                    FontSize = 11, FontWeight = System.Windows.FontWeights.Bold };
        Canvas.SetLeft(lblY, cx + 4); Canvas.SetTop(lblY, 2);
        lissajousCanvas.Children.Add(lblY);
        // 最大值標示
        var lblMax = new TextBlock { Text = $"±{absMax:F3}G",
            Foreground = new SolidColorBrush(Color.FromArgb(180, 0xC0, 0xC0, 0xCF)),
            FontSize = 9, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        Canvas.SetLeft(lblMax, 4); Canvas.SetTop(lblMax, ch - 14);
        lissajousCanvas.Children.Add(lblMax);

        // 漸淡的 polyline
        var pts = new List<System.Windows.Point>(n);
        for (int i = start; i <= upToIdx; i++)
        {
            var f = _frames[i];
            double x = cx + f.AccX * scale;
            double y = cy - f.AccY * scale;  // canvas Y 朝下
            pts.Add(new System.Windows.Point(x, y));
        }
        // 用多段不同 opacity 繪製
        for (int i = 1; i < pts.Count; i++)
        {
            double age = (double)i / pts.Count;
            byte alpha = (byte)(40 + age * 200);
            var pen = new SolidColorBrush(Color.FromArgb(alpha, 0x1A, 0xBC, 0x9C));
            var seg = new System.Windows.Shapes.Line
            {
                X1 = pts[i - 1].X, Y1 = pts[i - 1].Y,
                X2 = pts[i].X, Y2 = pts[i].Y,
                Stroke = pen, StrokeThickness = 1.2,
            };
            lissajousCanvas.Children.Add(seg);
        }
        // 當前點（橘色實心圓 + 外圈）
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8,
            Fill = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
            Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 1.2,
        };
        Canvas.SetLeft(dot, pts[pts.Count - 1].X - 4);
        Canvas.SetTop(dot, pts[pts.Count - 1].Y - 4);
        lissajousCanvas.Children.Add(dot);

        // 圖形辨識（自動診斷）
        lblLissajousHint.Text = ClassifyLissajous(start, upToIdx);
    }

    /// <summary>從散點分布判斷振動模式</summary>
    private string ClassifyLissajous(int from, int to)
    {
        int n = to - from + 1;
        if (n < 30) return "📊 採樣不足，無法判讀（需至少 30 點）";

        // 計算 X / Y 標準差 + 相關係數
        double sumX = 0, sumY = 0;
        for (int i = from; i <= to; i++) { sumX += _frames[i].AccX; sumY += _frames[i].AccY; }
        double mx = sumX / n, my = sumY / n;

        double sxx = 0, syy = 0, sxy = 0;
        for (int i = from; i <= to; i++)
        {
            double dx = _frames[i].AccX - mx;
            double dy = _frames[i].AccY - my;
            sxx += dx * dx;
            syy += dy * dy;
            sxy += dx * dy;
        }
        double stdX = Math.Sqrt(sxx / n);
        double stdY = Math.Sqrt(syy / n);
        if (stdX < 1e-6 && stdY < 1e-6) return "💤 靜止（無振動）";
        double corr = (stdX * stdY > 1e-9) ? (sxy / n) / (stdX * stdY) : 0;
        double absCorr = Math.Abs(corr);
        double ratio = stdY > 1e-9 ? stdX / stdY : 1e9;
        // 比例反過來看
        double majMin = ratio >= 1 ? ratio : 1.0 / ratio;

        // 判讀
        if (majMin < 1.4 && absCorr < 0.3)
            return "● 圓形分布 → 正常振動或不平衡（XY 振幅相當）";
        if (majMin > 3.5 && absCorr > 0.7)
            return "● 直線分布 → 嚴重單方向偏振（鬆動 / 共振）";
        if (majMin > 2.0 && absCorr > 0.5)
            return "● 橢圓分布 → 不對心（軸偏移或聯軸器問題）";
        if (absCorr < 0.4 && majMin > 2.0)
            return "● 不規則團塊 → 軸承故障或衝擊式振動";
        if (absCorr > 0.6 && majMin < 2.0)
            return "● 8 字 / 複合形狀 → 不平衡 + 不對心同時存在";
        return $"● 一般分布（X/Y 比 {majMin:F1}, 相關 {absCorr:F2}）";
    }

    private void UpdateTrail(int upToIdx)
    {
        if (_trail is null) return;
        if (cbShowTrail.IsChecked != true)
        {
            _trail.Geometry = new MeshGeometry3D();
            return;
        }
        int trailLen = (int)sldTrail.Value;
        if (trailLen <= 0)
        {
            _trail.Geometry = new MeshGeometry3D();
            return;
        }
        int start = Math.Max(0, upToIdx - trailLen);
        double scale = sldMotionScale.Value / 1000.0;

        var mesh = new MeshGeometry3D();
        Point3D? prev = null;
        for (int i = start; i <= upToIdx; i++)
        {
            var f = _frames[i];
            var p = new Point3D(f.AccX * scale, f.AccY * scale, f.AccZ * scale);
            if (prev is { } pv)
            {
                AppendLineSegmentToMesh(mesh, pv, p, 0.015);
            }
            prev = p;
        }
        _trail.Geometry = mesh;
    }

    private static void AppendLineSegmentToMesh(MeshGeometry3D mesh, Point3D a, Point3D b, double thickness)
    {
        Vector3D dir = b - a;
        if (dir.LengthSquared < 1e-12) return;
        dir.Normalize();
        Vector3D up = Math.Abs(Vector3D.DotProduct(dir, new Vector3D(0, 1, 0))) > 0.95
            ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
        Vector3D side = Vector3D.CrossProduct(dir, up); side.Normalize();
        Vector3D up2 = Vector3D.CrossProduct(side, dir); up2.Normalize();
        double t = thickness * 0.5;

        int basei = mesh.Positions.Count;
        mesh.Positions.Add(a - side * t - up2 * t);
        mesh.Positions.Add(a + side * t - up2 * t);
        mesh.Positions.Add(a + side * t + up2 * t);
        mesh.Positions.Add(a - side * t + up2 * t);
        mesh.Positions.Add(b - side * t - up2 * t);
        mesh.Positions.Add(b + side * t - up2 * t);
        mesh.Positions.Add(b + side * t + up2 * t);
        mesh.Positions.Add(b - side * t + up2 * t);
        int[] idx = {
            0,1,2, 0,2,3,
            5,4,7, 5,7,6,
            4,0,3, 4,3,7,
            1,5,6, 1,6,2,
            3,2,6, 3,6,7,
            4,5,1, 4,1,0,
        };
        foreach (var i in idx) mesh.TriangleIndices.Add(basei + i);
    }

    private static MeshGeometry3D MakeArrowMesh(Point3D from, Point3D to, double thickness, Color color)
    {
        var mesh = new MeshGeometry3D();
        // shaft = line（80%）+ tip = box (20%)
        Vector3D dir = to - from;
        if (dir.LengthSquared < 1e-12) return mesh;
        double len = dir.Length;
        dir.Normalize();
        var shaftEnd = from + dir * (len * 0.8);
        AppendLineSegmentToMesh(mesh, from, shaftEnd, thickness);
        AppendLineSegmentToMesh(mesh, shaftEnd, to, thickness * 2.5); // 箭頭粗一點
        return mesh;
    }

    private void ResetScene()
    {
        if (_boxRot is not null) _boxRot.Rotation = new QuaternionRotation3D(Quaternion.Identity);
        if (_boxTrans is not null) { _boxTrans.OffsetX = _boxTrans.OffsetY = _boxTrans.OffsetZ = 0; }
        if (_trail is not null) _trail.Geometry = new MeshGeometry3D();
        if (_vectorArrow is not null) _vectorArrow.Geometry = new MeshGeometry3D();
        lblAngleX.Text = lblAngleY.Text = lblAngleZ.Text = "—";
        lblAccX.Text = lblAccY.Text = lblAccZ.Text = lblAccMag.Text = "—";
        lblFrameTime.Text = "—";
        lissajousCanvas?.Children.Clear();
        if (lblLissajousHint is not null) lblLissajousHint.Text = "—";
    }

    private void UpdateTimelineLabel()
    {
        if (_frames.Count == 0) { lblTimeline.Text = "0.0s / 0.0s"; return; }
        double elapsed = (_frames[_frameIdx].Time - _frames[0].Time).TotalSeconds;
        double total = (_frames[_frames.Count - 1].Time - _frames[0].Time).TotalSeconds;
        lblTimeline.Text = $"{elapsed,6:F1}s / {total:F1}s   [{_frameIdx + 1}/{_frames.Count}]";
    }

    // ─────────────────────────────────────────────────
    //  視角控制
    // ─────────────────────────────────────────────────

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        _lastMousePos = e.GetPosition(viewport);
        if (e.ChangedButton == MouseButton.Left) _rotating = true;
        else if (e.ChangedButton == MouseButton.Right) _panning = true;
        viewport.CaptureMouse();
    }
    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        _rotating = _panning = false;
        viewport.ReleaseMouseCapture();
    }
    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_rotating && !_panning) return;
        var p = e.GetPosition(viewport);
        double dx = p.X - _lastMousePos.X;
        double dy = p.Y - _lastMousePos.Y;
        _lastMousePos = p;
        if (_rotating)
        {
            _camYaw -= dx * 0.005;
            _camPitch -= dy * 0.005;
            _camPitch = Math.Max(-Math.PI / 2 + 0.05, Math.Min(Math.PI / 2 - 0.05, _camPitch));
        }
        else if (_panning)
        {
            // 簡化平移：依目前 yaw 方向決定
            double speed = _camDist * 0.002;
            _camTarget.X -= (Math.Cos(_camYaw) * dx - Math.Sin(_camYaw) * dy) * speed;
            _camTarget.Z += (Math.Sin(_camYaw) * dx + Math.Cos(_camYaw) * dy) * speed;
        }
        UpdateCamera();
    }
    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _camDist *= e.Delta > 0 ? 0.9 : 1.1;
        _camDist = Math.Max(2, Math.Min(50, _camDist));
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        // 5-9：Z 軸朝上的球面坐標
        // pitch = 0 → 從赤道看（水平視線），pitch = +90° → 從天頂往下看
        double cx = _camTarget.X + _camDist * Math.Cos(_camPitch) * Math.Cos(_camYaw);
        double cy = _camTarget.Y + _camDist * Math.Cos(_camPitch) * Math.Sin(_camYaw);
        double cz = _camTarget.Z + _camDist * Math.Sin(_camPitch);
        camera.Position = new Point3D(cx, cy, cz);
        camera.LookDirection = new Vector3D(_camTarget.X - cx, _camTarget.Y - cy, _camTarget.Z - cz);
        camera.UpDirection = new Vector3D(0, 0, 1);
    }
}
