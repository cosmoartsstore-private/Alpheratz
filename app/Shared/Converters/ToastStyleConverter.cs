using System;
using Alpheratz.Shared.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Shared.Converters;

/// <summary>
/// ToastType を見出し色（左端ストライプ / アイコン色）に変換する。
/// success → APrimary、error → ADangerSolid、info → ATextDim。
/// テーマ辞書から取り出すため、ライト/ダーク両方で意図した色が使われる。
/// </summary>
public sealed class ToastAccentBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is ToastType type ? type switch
        {
            ToastType.success => "APrimary",
            ToastType.error => "ADangerSolid",
            _ => "ATextDim",
        } : "ATextDim";
        return ResolveThemeBrush(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();

    private static Brush? ResolveThemeBrush(string key)
    {
        try
        {
            var themeKey = Application.Current.RequestedTheme == ApplicationTheme.Dark ? "Dark" : "Light";
            if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var raw)
                && raw is ResourceDictionary dict
                && dict.TryGetValue(key, out var value)
                && value is Brush brush)
                return brush;
        }
        catch { }
        return null;
    }
}

/// <summary>
/// ToastType を絵文字アイコンに変換する。
/// 視覚記号でメッセージのトーンを即座に伝える。
/// </summary>
public sealed class ToastIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is ToastType type ? type switch
        {
            ToastType.success => "✔",  // ✔
            ToastType.error => "⚠",    // ⚠
            _ => "ℹ",                  // ℹ
        } : "ℹ";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
