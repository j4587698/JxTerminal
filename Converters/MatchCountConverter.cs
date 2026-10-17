using Avalonia.Data.Converters;
using JxTerminal.Services;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace JxTerminal.Converters;

/// <summary>
/// Formats the search match indicator: empty while no query is active, a localized
/// "no matches" hint when the query has zero hits, otherwise "ordinal / count".
/// Inputs: [0] match ordinal (1-based), [1] match count, [2] current search text.
/// </summary>
public class MatchCountConverter : IMultiValueConverter
{
    public static readonly MatchCountConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3) return "";

        var query = values[2] as string;
        if (string.IsNullOrEmpty(query)) return "";

        int count = values[1] is int c ? c : 0;
        if (count == 0) return LocalizationService.Instance["NoMatches"];

        int ordinal = values[0] is int o ? o : 0;
        return string.Format(LocalizationService.Instance["MatchCountFormat"], ordinal, count);
    }
}
