using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Alpheratz.Shared.Converters;

/// <summary>
/// int → Visibility 変換。0 以下=Collapsed / 1 以上=Visible。
/// カウントバッジで「0」を非表示にする用途。
/// ConverterParameter="invert" で反転可能 (0=Visible / >0=Collapsed)。
/// </summary>
public sealed class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var positive = value is int n && n > 0;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            positive = !positive;
        return positive ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
