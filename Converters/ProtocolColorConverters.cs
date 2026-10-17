using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using JxTerminal.Models;

namespace JxTerminal.Converters;

public class ProtocolToBrushConverter : IValueConverter
{
    public static readonly ProtocolToBrushConverter Instance = new();

    private static readonly SolidColorBrush SshBrush = new(Color.Parse("#3B82F6"));
    private static readonly SolidColorBrush TelnetBrush = new(Color.Parse("#F59E0B"));
    private static readonly SolidColorBrush SerialBrush = new(Color.Parse("#A855F7"));
    private static readonly SolidColorBrush LocalBrush = new(Color.Parse("#10B981"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var type = value is ConnectionType ct ? ct : ConnectionType.Local;
        return type switch
        {
            ConnectionType.Ssh => SshBrush,
            ConnectionType.Telnet => TelnetBrush,
            ConnectionType.Serial => SerialBrush,
            _ => LocalBrush
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ProtocolToBgBrushConverter : IValueConverter
{
    public static readonly ProtocolToBgBrushConverter Instance = new();

    private static readonly SolidColorBrush SshBgBrush = new(Color.FromArgb(0x28, 0x3B, 0x82, 0xF6));
    private static readonly SolidColorBrush TelnetBgBrush = new(Color.FromArgb(0x28, 0xF5, 0x9E, 0x0B));
    private static readonly SolidColorBrush SerialBgBrush = new(Color.FromArgb(0x28, 0xA8, 0x55, 0xF7));
    private static readonly SolidColorBrush LocalBgBrush = new(Color.FromArgb(0x28, 0x10, 0xB9, 0x81));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var type = value is ConnectionType ct ? ct : ConnectionType.Local;
        return type switch
        {
            ConnectionType.Ssh => SshBgBrush,
            ConnectionType.Telnet => TelnetBgBrush,
            ConnectionType.Serial => SerialBgBrush,
            _ => LocalBgBrush
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
