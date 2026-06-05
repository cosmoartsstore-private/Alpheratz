using Alpheratz.Shared.Models;

namespace Alpheratz.Shared.Converters;

/// <summary>
/// ToastType から表示に使うアクセントキーと記号を決める補助ロジック。
/// 実際の Brush 解決は ThemeHelper に任せ、ここでは入力種別から名前だけを返す。
/// </summary>
internal static class ToastStyleLogic
{
    /// <summary>ToastType に対応するテーマブラシキーを返す。</summary>
    public static string AccentBrushKey(object value)
        => value is ToastType type ? type switch
        {
            ToastType.success => "APrimary",
            ToastType.error => "ADangerSolid",
            _ => "ATextDim",
        } : "ATextDim";

    /// <summary>ToastType に対応する短い記号文字を返す。</summary>
    public static string IconText(object value)
        => value is ToastType type ? type switch
        {
            ToastType.success => "✔",
            ToastType.error => "⚠",
            _ => "ℹ",
        } : "ℹ";
}
