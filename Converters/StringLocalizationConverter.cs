using Avalonia.Data.Converters;
using System;
using System.Globalization;
using JxTerminal.Services;

namespace JxTerminal.Converters
{
    public class StringLocalizationConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string key)
            {
                return LocalizationService.Instance[key];
            }
            if (value != null)
            {
                return LocalizationService.Instance[value.ToString() ?? ""];
            }
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
