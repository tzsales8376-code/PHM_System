// ============================================================================
// Tranzx.iVS4.App / Converters / CommonConverters.cs
// Phase 5 簡化的 Converter 集合
// ============================================================================

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Tranzx.iVS4.App.Converters;

/// <summary>bool → Visibility (true=Visible, false=Collapsed)</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility (false=Visible)</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>非空字串 → Visible，空字串/null → Collapsed</summary>
public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s)
            ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>0-based index → 1-based 顯示</summary>
public sealed class ChannelIndexPlus1Converter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i ? (i + 1).ToString() : "?";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>數值比較：value == ConverterParameter (string) → true</summary>
public sealed class EqualsToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return value.ToString() == parameter.ToString();
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Enum.Parse(targetType, parameter?.ToString() ?? "") : Binding.DoNothing;
}

/// <summary>
/// 三段警報顏色：value, yellow, red → Brush
///   < yellow → 綠（LimeGreen）
///   < red    → 黃（Gold）
///   ≥ red    → 紅（OrangeRed）
/// 取絕對值處理（負數的角度也算）
/// </summary>
public sealed class AlarmLevelToBrushConverter : IMultiValueConverter
{
    public static readonly System.Windows.Media.SolidColorBrush GreenBrush
        = new(System.Windows.Media.Color.FromRgb(0x1A, 0xBC, 0x9C));
    public static readonly System.Windows.Media.SolidColorBrush YellowBrush
        = new(System.Windows.Media.Color.FromRgb(0xF3, 0x9C, 0x12));
    public static readonly System.Windows.Media.SolidColorBrush RedBrush
        = new(System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 3) return GreenBrush;
        if (values[0] is not double v) return GreenBrush;
        if (values[1] is not double yellow) return GreenBrush;
        if (values[2] is not double red) return GreenBrush;
        double abs = Math.Abs(v);
        if (abs >= red) return RedBrush;
        if (abs >= yellow) return YellowBrush;
        return GreenBrush;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Phase 5-8c：Dashboard 連線狀態 chip 用 brush
/// true → 綠 (#1ABC9C)；false → 灰 (#7F8C8D)
/// </summary>
public sealed class BoolToConnBrushConverter : IValueConverter
{
    private static readonly Brush ConnectedBrush =
        new SolidColorBrush(Color.FromRgb(0x1A, 0xBC, 0x9C));
    private static readonly Brush DisconnectedBrush =
        new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? ConnectedBrush : DisconnectedBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
