// ============================================================================
// Tranzx.iVS4.App / Views / AlarmThresholdDialog.xaml.cs
// 點擊圓環開啟，設定該量值的黃 / 紅 閾值，提供「複製到全部」批次套用
// ============================================================================

using System.Globalization;
using System.Windows;
using Tranzx.iVS4.App.Models;
using Tranzx.iVS4.App.Services;
using Tranzx.iVS4.App.ViewModels;

namespace Tranzx.iVS4.App.Views;

public partial class AlarmThresholdDialog : Window
{
    private readonly ChannelViewModel _channel;
    private readonly string _key;
    private readonly AlarmThreshold _target;
    private readonly string _unit;
    private static LocalizationService Loc => LocalizationService.Instance;

    public AlarmThresholdDialog(ChannelViewModel channel, string key)
    {
        InitializeComponent();
        _channel = channel;
        _key = key;
        (_target, _unit, string label) = ResolveTarget(channel.Thresholds, key);

        lblTarget.Text = $"{Loc["Alarm.Target"]}：{channel.DisplayName} — {label}";
        lblYellowUnit.Text = _unit;
        lblRedUnit.Text = _unit;
        txtYellow.Text = _target.Yellow.ToString("F3", CultureInfo.InvariantCulture);
        txtRed.Text = _target.Red.ToString("F3", CultureInfo.InvariantCulture);
        UpdatePreview();

        // 「複製到全部」按鈕文字依群組決定
        btnCopyToGroup.Content = key switch
        {
            "XPeak" or "YPeak" or "ZPeak" => Loc["Alarm.CopyToVibPeak"],
            "XRms"  or "YRms"  or "ZRms"  => Loc["Alarm.CopyToVibRms"],
            "AngleX" or "AngleY" or "AngleZ" => Loc["Alarm.CopyToTilt"],
            _ => Loc["Alarm.CopyToGroup"]
        };
        // 溫溼度沒有「群組」概念，隱藏按鈕
        if (key is "Temp" or "Hum")
            btnCopyToGroup.Visibility = Visibility.Collapsed;
    }

    private static (AlarmThreshold target, string unit, string label) ResolveTarget(
        ChannelAlarmThresholds t, string key) => key switch
    {
        "XPeak"  => (t.XPeak, "G",  "X · 0-P"),
        "YPeak"  => (t.YPeak, "G",  "Y · 0-P"),
        "ZPeak"  => (t.ZPeak, "G",  "Z · 0-P"),
        "XRms"   => (t.XRms,  "G",  "X · RMS"),
        "YRms"   => (t.YRms,  "G",  "Y · RMS"),
        "ZRms"   => (t.ZRms,  "G",  "Z · RMS"),
        "AngleX" => (t.AngleX, "°", "X · Angle"),
        "AngleY" => (t.AngleY, "°", "Y · Angle"),
        "AngleZ" => (t.AngleZ, "°", "Z · Angle"),
        "Temp"   => (t.Temp, "°C",  Loc["Series.Temp"]),
        "Hum"    => (t.Hum,  "%",   Loc["Series.Hum"]),
        _ => (t.XPeak, "G", key),
    };

    private void OnThresholdChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (lblYellowVal is null) return;
        if (!TryParse(txtYellow.Text, out double y) || !TryParse(txtRed.Text, out double r)) return;
        lblYellowVal.Text = y.ToString("F3");
        lblPreview.Text = string.Format(Loc["Alarm.PreviewFmt"],
            $"0 ~ {y:F2}", $"{y:F2} ~ {r:F2}", $"> {r:F2}", _unit);
    }

    private static bool TryParse(string s, out double v)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private void OnCopyToGroup(object sender, RoutedEventArgs e)
    {
        if (!ValidateThresholds(out double y, out double r)) return;
        var temp = new AlarmThreshold(y, r);
        switch (_key)
        {
            case "XPeak": case "YPeak": case "ZPeak":
                _channel.Thresholds.CopyToVibrationPeak(temp); break;
            case "XRms": case "YRms": case "ZRms":
                _channel.Thresholds.CopyToVibrationRms(temp); break;
            case "AngleX": case "AngleY": case "AngleZ":
                _channel.Thresholds.CopyToTilt(temp); break;
        }
        // 也套到當前 target（visual 立即更新）
        _target.Yellow = y;
        _target.Red = r;
        DialogResult = true;
        Close();
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (!ValidateThresholds(out double y, out double r)) return;
        _target.Yellow = y;
        _target.Red = r;
        DialogResult = true;
        Close();
    }

    private bool ValidateThresholds(out double yellow, out double red)
    {
        yellow = 0; red = 0;
        if (!TryParse(txtYellow.Text, out yellow) || !TryParse(txtRed.Text, out red))
        {
            MessageBox.Show(Loc["Alarm.InvalidNumber"], Loc["Error.Title"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (yellow < 0 || red <= yellow)
        {
            MessageBox.Show(Loc["Alarm.InvalidRange"], Loc["Error.Title"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
