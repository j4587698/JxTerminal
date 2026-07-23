using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace JxTerminal.Converters;

public class ColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            return new SolidColorBrush(color);
        }
        else if (value is HsvColor hsv)
        {
             return new SolidColorBrush(hsv.ToRgb());
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    public static IValueConverter BooleanToGreenRed { get; } = new FuncValueConverter<bool, IBrush>(b => b ? Brushes.Green : Brushes.Red);
    public static IValueConverter BooleanToPrimary { get; } = new FuncValueConverter<bool, IBrush>(b => b ? new SolidColorBrush(Color.Parse("#2563EB")) : Brushes.Gray);
}
