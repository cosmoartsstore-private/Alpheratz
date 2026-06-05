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
    /// <summary>ToastType に対応するアクセントブラシを現在テーマから取得する。</summary>
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var key = ToastStyleLogic.AccentBrushKey(value);
        return ThemeHelper.BrushForSelectedTheme(key);
    }

    /// <summary>Toast 表示専用の変換なので逆変換は提供しない。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// ToastType を絵文字アイコンに変換する。
/// 視覚記号でメッセージのトーンを即座に伝える。
/// </summary>
public sealed class ToastIconConverter : IValueConverter
{
    /// <summary>ToastType に対応する短い記号文字を返す。</summary>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return ToastStyleLogic.IconText(value);
    }

    /// <summary>Toast 表示専用の変換なので逆変換は提供しない。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
