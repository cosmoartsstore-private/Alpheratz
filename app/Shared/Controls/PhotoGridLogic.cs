using System;

namespace Alpheratz.Shared.Controls;

/// <summary>
/// PhotoGrid のカスタムスクロールバーに使う数値計算を UI 要素なしで扱う補助ロジック。
/// ScrollViewer には触れず、track 位置、thumb 位置、thumb 高さだけを計算する。
/// </summary>
internal static class PhotoGridLogic
{
    public const double ScrollbarVerticalPadding = 48;
    public const double MinimumTrackHeight = 1;
    public const double MinimumThumbHeight = 18;

    /// <summary>コントロール高さからスクロールバー track の有効高さを返す。</summary>
    public static double TrackHeight(double actualHeight)
        => Math.Max(MinimumTrackHeight, actualHeight - ScrollbarVerticalPadding);

    /// <summary>track 上の Y 座標を ScrollViewer の縦 offset に変換する。スクロール不能なら null。</summary>
    public static double? TrackPositionToOffset(double trackY, double actualHeight, double extentHeight, double viewportHeight)
    {
        if (extentHeight <= viewportHeight) return null;
        var ratio = Math.Clamp(trackY / TrackHeight(actualHeight), 0, 1);
        return ratio * (extentHeight - viewportHeight);
    }

    /// <summary>ScrollViewer の寸法と現在 offset からカスタム thumb の表示位置と高さを返す。</summary>
    public static PhotoGridScrollbarThumb ScrollbarThumb(double actualHeight, double extentHeight, double viewportHeight, double verticalOffset)
    {
        if (extentHeight <= 0 || viewportHeight <= 0 || extentHeight <= viewportHeight)
            return new PhotoGridScrollbarThumb(Top: 0, Height: viewportHeight);

        var trackHeight = TrackHeight(actualHeight);
        var ratio = viewportHeight / extentHeight;
        var thumbHeight = Math.Max(MinimumThumbHeight, trackHeight * ratio);
        var top = (verticalOffset / (extentHeight - viewportHeight)) * Math.Max(0, trackHeight - thumbHeight);
        return new PhotoGridScrollbarThumb(top, thumbHeight);
    }
}

/// <summary>カスタムスクロールバー thumb の上端位置と高さ。</summary>
internal sealed record PhotoGridScrollbarThumb(double Top, double Height);
