// ============================================================================
// Tranzx.iVS4.App / Services / AlarmToastService.cs
//
// Phase 5-8c：簡易視覺通知（取代 Windows Toast 避免額外 NuGet 依賴）
//   - 在主視窗右下角顯示一個無框 Window（非 modal），背景色依 alarm level
//   - 4 秒後自動淡出關閉；多個通知會垂直堆疊（最新在最下）
//   - 點擊 popup 立即關閉
//
// 用途：黃 / 紅燈觸發時呼叫 Show()，使用者不必盯著畫面也看得到視覺提醒
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Tranzx.iVS4.App.Models;

namespace Tranzx.iVS4.App.Services;

public static class AlarmToastService
{
    private const int PopupWidth  = 320;
    private const int PopupHeight = 84;
    private const int Spacing     = 8;
    private const double DisplaySeconds = 4.0;

    /// <summary>當前活躍的 popups（從上到下顯示）</summary>
    private static readonly List<Window> _active = new();

    /// <summary>持續顯示的 reconnect 進度 popup（每個 sensor 一個，會更新內文）</summary>
    /// <param name="sensorName">Sensor 名稱（用於識別）</param>
    /// <param name="message">當前進度訊息</param>
    /// <param name="status">"reconnecting" / "success" / "giveup" — 決定顏色與是否自動消失</param>
    public static void ShowReconnect(string sensorName, string message, string status)
    {
        if (Application.Current is null) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // 找到該 sensor 既有的 reconnect popup（用 Tag 標記 sensor 名）
                Window? existing = null;
                foreach (var w in _active)
                {
                    if (w.Tag is string t && t == "reconnect:" + sensorName)
                    {
                        existing = w; break;
                    }
                }

                if (existing != null)
                {
                    // 更新既有 popup 內容
                    UpdateReconnectPopup(existing, sensorName, message, status);
                    if (status == "success" || status == "giveup")
                    {
                        // 30 秒後自動關閉
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                        timer.Tick += (_, _) => { timer.Stop(); FadeOutAndClose(existing); };
                        timer.Start();
                    }
                    return;
                }

                // 新建
                var win = BuildReconnectPopup(sensorName, message, status);
                _active.Add(win);
                Reflow();
                win.Show();
                win.Loaded += (_, _) =>
                {
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                    win.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                };

                if (status == "success" || status == "giveup")
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                    timer.Tick += (_, _) => { timer.Stop(); FadeOutAndClose(win); };
                    timer.Start();
                }
                // status == "reconnecting" 時不自動關閉，等下次 ShowReconnect 更新或 success/giveup
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Toast.Reconnect] {ex.Message}");
            }
        });
    }

    private static Window BuildReconnectPopup(string sensorName, string message, string status)
    {
        var (bg, icon) = status switch
        {
            "success" => (Color.FromRgb(0x1A, 0xBC, 0x9C), "✓"),
            "giveup"  => (Color.FromRgb(0xE7, 0x4C, 0x3C), "⛔"),
            _         => (Color.FromRgb(0xF3, 0x9C, 0x12), "⟳"),
        };
        var iconText = new TextBlock
        {
            Text = icon, FontSize = 28, Margin = new Thickness(0, 0, 10, 0),
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "icon",
        };
        var titleText = new TextBlock
        {
            Text = sensorName, Foreground = Brushes.White,
            FontWeight = FontWeights.Bold, FontSize = 13,
            Tag = "title",
        };
        var msgText = new TextBlock
        {
            Text = message, Foreground = Brushes.White,
            FontSize = 12, Opacity = 0.95,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 240,
            Tag = "msg",
        };
        var textCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textCol.Children.Add(titleText);
        textCol.Children.Add(msgText);
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(iconText);
        stack.Children.Add(textCol);

        var border = new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16, ShadowDepth = 2, Opacity = 0.6, Color = Colors.Black,
            },
            Child = stack,
        };
        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Width = PopupWidth, Height = 90,
            Content = border,
            ShowActivated = false, Focusable = false,
            Opacity = 0,
            Tag = "reconnect:" + sensorName,
        };
        win.MouseLeftButtonDown += (_, _) => FadeOutAndClose(win);
        win.Closed += (_, _) => { _active.Remove(win); Reflow(); };
        return win;
    }

    private static void UpdateReconnectPopup(Window win, string sensorName, string message, string status)
    {
        var (bg, icon) = status switch
        {
            "success" => (Color.FromRgb(0x1A, 0xBC, 0x9C), "✓"),
            "giveup"  => (Color.FromRgb(0xE7, 0x4C, 0x3C), "⛔"),
            _         => (Color.FromRgb(0xF3, 0x9C, 0x12), "⟳"),
        };
        if (win.Content is Border b)
        {
            b.Background = new SolidColorBrush(bg);
            if (b.Child is StackPanel s && s.Children.Count >= 2)
            {
                if (s.Children[0] is TextBlock tIcon) tIcon.Text = icon;
                if (s.Children[1] is StackPanel col && col.Children.Count >= 2)
                {
                    if (col.Children[0] is TextBlock tTitle) tTitle.Text = sensorName;
                    if (col.Children[1] is TextBlock tMsg)   tMsg.Text   = message;
                }
            }
        }
    }

    public static void Show(string sensorName, string keyText, double value,
                             AlarmLevel level)
    {
        if (Application.Current is null) return;

        // ❗ Phase 5-8c2：依使用者偏好過濾
        var s = Tranzx.iVS4.App.Services.AppSettingsService.Instance;
        if (!s.AlarmToastEnabled) return;
        if (level == AlarmLevel.Yellow && !s.AlarmToastOnYellow) return;
        if (level == AlarmLevel.Red    && !s.AlarmToastOnRed)    return;
        if (level != AlarmLevel.Yellow && level != AlarmLevel.Red) return;

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var popup = BuildPopup(sensorName, keyText, value, level);
                _active.Add(popup);
                Reflow();
                popup.Show();

                // 自動關閉
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(DisplaySeconds) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    FadeOutAndClose(popup);
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlarmToast] {ex.Message}");
            }
        });
    }

    /// <summary>簡易訊息 popup（重連成功 / 失敗等）</summary>
    public static void ShowSimple(string message, bool isError = false)
    {
        if (Application.Current is null) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var bg = isError ? Color.FromRgb(0xE7, 0x4C, 0x3C)
                                 : Color.FromRgb(0x1A, 0xBC, 0x9C);
                var border = new Border
                {
                    Background = new SolidColorBrush(bg),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 8, 12, 8),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 16, ShadowDepth = 2, Opacity = 0.6, Color = Colors.Black,
                    },
                    Child = new TextBlock
                    {
                        Text = message,
                        Foreground = Brushes.White,
                        FontSize = 12, FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                    },
                };
                var win = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    Topmost = true,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Width = PopupWidth, Height = 56,
                    Content = border,
                    ShowActivated = false, Focusable = false,
                    Opacity = 0,
                };
                win.MouseLeftButtonDown += (_, _) => FadeOutAndClose(win);
                win.Closed += (_, _) => { _active.Remove(win); Reflow(); };
                _active.Add(win);
                Reflow();
                win.Show();
                win.Loaded += (_, _) =>
                {
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                    win.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                };
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(DisplaySeconds) };
                timer.Tick += (_, _) => { timer.Stop(); FadeOutAndClose(win); };
                timer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlarmToast.Simple] {ex.Message}");
            }
        });
    }

    private static Window BuildPopup(string sensorName, string keyText, double value, AlarmLevel level)
    {
        var (bg, fg) = level switch
        {
            AlarmLevel.Red    => (Color.FromRgb(0xE7, 0x4C, 0x3C), Colors.White),
            AlarmLevel.Yellow => (Color.FromRgb(0xF3, 0x9C, 0x12), Colors.White),
            _                 => (Color.FromRgb(0x36, 0x36, 0x50), Colors.White),
        };
        string icon = level == AlarmLevel.Red ? "⛔" : level == AlarmLevel.Yellow ? "⚠" : "ℹ";

        var border = new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16, ShadowDepth = 2, Opacity = 0.6,
                Color = Colors.Black,
            },
        };
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = icon, FontSize = 28, Margin = new Thickness(0, 0, 10, 0),
            Foreground = new SolidColorBrush(fg),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var textCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textCol.Children.Add(new TextBlock
        {
            Text = $"{sensorName} — {keyText}",
            Foreground = new SolidColorBrush(fg),
            FontWeight = FontWeights.Bold, FontSize = 13,
        });
        textCol.Children.Add(new TextBlock
        {
            Text = $"{value:F4}  →  {LevelText(level)}",
            Foreground = new SolidColorBrush(fg),
            FontSize = 12, Opacity = 0.95,
        });
        stack.Children.Add(textCol);
        border.Child = stack;

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Width = PopupWidth, Height = PopupHeight,
            Content = border,
            ShowActivated = false,
            Focusable = false,
        };
        win.MouseLeftButtonDown += (_, _) => FadeOutAndClose(win);
        win.Closed += (_, _) =>
        {
            _active.Remove(win);
            Reflow();
        };

        // 淡入
        win.Opacity = 0;
        win.Loaded += (_, _) =>
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            win.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };
        return win;
    }

    private static void FadeOutAndClose(Window win)
    {
        if (!win.IsVisible) return;
        var fadeOut = new DoubleAnimation(win.Opacity, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) => { try { win.Close(); } catch { } };
        win.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    /// <summary>重新排列所有活躍的 popup（最新在最上方堆疊向上）</summary>
    private static void Reflow()
    {
        var work = SystemParameters.WorkArea;
        for (int i = 0; i < _active.Count; i++)
        {
            var w = _active[_active.Count - 1 - i];  // 最新（index 大）排最下
            w.Left = work.Right - PopupWidth - 16;
            w.Top  = work.Bottom - (PopupHeight + Spacing) * (i + 1);
        }
    }

    private static string LevelText(AlarmLevel l) => l switch
    {
        AlarmLevel.Red    => "Red",
        AlarmLevel.Yellow => "Yellow",
        AlarmLevel.Green  => "Green",
        _ => l.ToString()
    };
}
