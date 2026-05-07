// ============================================================================
// Tranzx.iVS4.App / Converters / InverseBoolConverter.cs
// 用於對話框中二選一 RadioButton 的雙向綁定
// ============================================================================

using System.Globalization;
using System.Windows.Data;

namespace Tranzx.iVS4.App.Converters;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
