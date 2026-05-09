using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace Alpheratz.Shared.Converters;

// Bridges CalendarDatePicker.SelectedDate (DateTimeOffset?) to a string field
// holding an ISO yyyy-MM-dd date. Empty/whitespace strings round-trip to null
// so the picker shows its placeholder.
public sealed class IsoDateStringToDateTimeOffsetConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return null;
        if (DateTimeOffset.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
        {
            return dto;
        }
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var loose))
        {
            return loose;
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTimeOffset dto)
        {
            return dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        return string.Empty;
    }
}
