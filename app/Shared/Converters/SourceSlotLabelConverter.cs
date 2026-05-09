using System;
using Microsoft.UI.Xaml.Data;

namespace Alpheratz.Shared.Converters;

public sealed class SourceSlotLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var slot = value switch
        {
            long l => l,
            int i => i,
            string s when long.TryParse(s, out var parsed) => parsed,
            _ => 1,
        };

        return slot == 2 ? "2nd" : "1st";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
