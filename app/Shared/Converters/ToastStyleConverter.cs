using System;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml.Data;

namespace Alpheratz.Shared.Converters;

/// <summary>
/// ToastType を見出し色（左端ストライプ / アイコン色）に変換する。
/// success → APrimary、error → ADangerSolid、info → ATextDim。
///
/// IValueConverter は element context を受け取れないため、
/// ThemeHelper.SelectedTheme (ShellPage.ApplyTheme から最後に通知された値) を使って解決する。
/// 既に表示中のトーストはテーマ切替に追従しないが、トースト自体が短命なため許容範囲。
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
        return ThemeHelper.BrushForSelectedTheme(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
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
