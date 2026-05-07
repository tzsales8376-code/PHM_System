// ============================================================================
// Tranzx.iVS4.App / Converters / TransportStateToColorConverter.cs
// 把 TransportState enum 轉成顏色 / 文字（連線狀態指示燈）
// 共用的 BoolToVisibility / StringNotEmpty 等 converter 在 CommonConverters.cs
// ============================================================================

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Tranzx.iVS4.Communication.Transport;

namespace Tranzx.iVS4.App.Converters;

public sealed class TransportStateToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Disconnected = new(Color.FromRgb(0x60, 0x60, 0x70));
    private static readonly SolidColorBrush Connecting = new(Color.FromRgb(0xF3, 0x9C, 0x12));
    private static readonly SolidColorBrush Connected = new(Color.FromRgb(0x1A, 0xBC, 0x9C));
    private static readonly SolidColorBrush Reconnecting = new(Color.FromRgb(0xF3, 0x9C, 0x12));
    private static readonly SolidColorBrush Faulted = new(Color.FromRgb(0xE7, 0x4C, 0x3C));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TransportState s) return Disconnected;
        return s switch
        {
            TransportState.Connected => Connected,
            TransportState.Connecting => Connecting,
            TransportState.Reconnecting => Reconnecting,
            TransportState.Faulted => Faulted,
            _ => Disconnected
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TransportStateToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is TransportState s ? s switch
        {
            TransportState.Connected => "State.Connected",
            TransportState.Connecting => "State.Connecting",
            TransportState.Reconnecting => "State.Reconnecting",
            TransportState.Faulted => "State.Faulted",
            _ => "State.Disconnected"
        } : "State.Disconnected";

        return System.Windows.Application.Current?.TryFindResource(key) as string ?? key;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
