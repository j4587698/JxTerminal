using System;
using System.Globalization;
using System.IO.Ports;
using Avalonia.Data.Converters;
using JxTerminal.Services;

namespace JxTerminal.Converters;

/// <summary>
/// Localized display names for serial-port enums (Parity / StopBits / Handshake) so the
/// ComboBoxes in the connection editor never show raw English identifiers.
/// </summary>
public class SerialEnumDisplayConverter : IValueConverter
{
    public static readonly SerialEnumDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var l = LocalizationService.Instance;
        return value switch
        {
            Parity.None => l["ParityNone"],
            Parity.Even => l["ParityEven"],
            Parity.Odd => l["ParityOdd"],
            Parity.Mark => l["ParityMark"],
            Parity.Space => l["ParitySpace"],

            // Stop bits are numeric everywhere; no translation needed.
            StopBits.One => "1",
            StopBits.OnePointFive => "1.5",
            StopBits.Two => "2",

            Handshake.None => l["FlowNone"],
            Handshake.XOnXOff => l["FlowXOnXOff"],
            Handshake.RequestToSend => l["FlowRtsCts"],
            Handshake.RequestToSendXOnXOff => l["FlowBoth"],

            _ => value?.ToString() ?? ""
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
