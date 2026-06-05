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
    /// <summary>正の int を Visible、それ以外を Collapsed に変換する。parameter が "invert" なら結果を反転する。</summary>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var positive = value is int n && n > 0;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            positive = !positive;
        return positive ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>表示制御専用の変換なので逆変換は提供しない。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
