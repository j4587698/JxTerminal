using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using JxTerminal.Models;

namespace JxTerminal.Converters;

public class PingToBrushConverter : IValueConverter
{
    public static readonly PingToBrushConverter Instance = new();

    private static readonly SolidColorBrush FastBrush = new(Color.Parse("#10B981"));
    private static readonly SolidColorBrush MediumBrush = new(Color.Parse("#F59E0B"));
    private static readonly SolidColorBrush SlowBrush = new(Color.Parse("#EF4444"));
    private static readonly SolidColorBrush TimeoutBrush = new(Color.Parse("#EF4444"));
    private static readonly SolidColorBrush PingingBrush = new(Color.Parse("#94A3B8"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value is PingLevel pl ? pl : PingLevel.Pinging;
        return level switch
        {
            PingLevel.Fast => FastBrush,
            PingLevel.Medium => MediumBrush,
            PingLevel.Slow => SlowBrush,
            PingLevel.Timeout => TimeoutBrush,
            _ => PingingBrush
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class PingToBgBrushConverter : IValueConverter
{
    public static readonly PingToBgBrushConverter Instance = new();

    private static readonly SolidColorBrush FastBgBrush = new(Color.FromArgb(0x22, 0x10, 0xB9, 0x81));
    private static readonly SolidColorBrush MediumBgBrush = new(Color.FromArgb(0x22, 0xF5, 0x9E, 0x0B));
    private static readonly SolidColorBrush SlowBgBrush = new(Color.FromArgb(0x22, 0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush TimeoutBgBrush = new(Color.FromArgb(0x18, 0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush PingingBgBrush = new(Color.FromArgb(0x18, 0x94, 0xA3, 0xB8));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value is PingLevel pl ? pl : PingLevel.Pinging;
        return level switch
        {
            PingLevel.Fast => FastBgBrush,
            PingLevel.Medium => MediumBgBrush,
            PingLevel.Slow => SlowBgBrush,
            PingLevel.Timeout => TimeoutBgBrush,
            _ => PingingBgBrush
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
