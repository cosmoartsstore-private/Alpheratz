using Alpheratz.Shared.Controls;

namespace Alpheratz.Tests;

/// <summary>
/// PhotoGrid のカスタムスクロールバー計算を検証するテスト。
///
/// PhotoGrid 本体は ScrollViewer と CustomScrollbar を直接操作するが、
/// track 上のクリック位置から縦 offset を求める計算と、ScrollViewer の表示範囲から
/// thumb の上端・高さを求める計算は UI 要素なしで決まる。
/// </summary>
public sealed class PhotoGridLogicTests
{
    /// <summary>
    /// コントロール高さから、上下余白 48px を除いた track 高さが計算されることを確認する。
    ///
    /// 高さが極端に小さい場合でも 0 で割らないよう、track 高さは最低 1px に丸める。
    /// </summary>
    [Fact]
    public void TrackHeight_SubtractsPaddingAndKeepsMinimum()
    {
        Assert.Equal(752, PhotoGridLogic.TrackHeight(800));
        Assert.Equal(PhotoGridLogic.MinimumTrackHeight, PhotoGridLogic.TrackHeight(20));
    }

    /// <summary>
    /// track 上の Y 座標が、スクロール可能範囲内の縦 offset に変換されることを確認する。
    ///
    /// trackY は 0〜trackHeight に clamp されるため、負値は先頭、track を超えた値は末尾へ移動する。
    /// extentHeight が viewportHeight 以下ならスクロール不能なので null を返す。
    /// </summary>
    [Fact]
    public void TrackPositionToOffset_ClampsTrackPositionAndSkipsUnscrollableContent()
    {
        Assert.Null(PhotoGridLogic.TrackPositionToOffset(trackY: 100, actualHeight: 800, extentHeight: 700, viewportHeight: 700));
        Assert.Equal(0, PhotoGridLogic.TrackPositionToOffset(trackY: -10, actualHeight: 848, extentHeight: 2000, viewportHeight: 500));
        Assert.Equal(750, PhotoGridLogic.TrackPositionToOffset(trackY: 400, actualHeight: 848, extentHeight: 2000, viewportHeight: 500));
        Assert.Equal(1500, PhotoGridLogic.TrackPositionToOffset(trackY: 999, actualHeight: 848, extentHeight: 2000, viewportHeight: 500));
    }

    /// <summary>
    /// ScrollViewer の寸法から、カスタム thumb の高さと上端位置が計算されることを確認する。
    ///
    /// thumb 高さは viewport/extent の比率で求め、最低 18px を下回らない。
    /// top は現在 offset の比率を、trackHeight から thumbHeight を引いた可動範囲へ掛ける。
    /// </summary>
    [Fact]
    public void ScrollbarThumb_ReturnsThumbHeightAndTopFromScrollMetrics()
    {
        Assert.Equal(
            new PhotoGridScrollbarThumb(Top: 0, Height: 500),
            PhotoGridLogic.ScrollbarThumb(actualHeight: 800, extentHeight: 500, viewportHeight: 500, verticalOffset: 0));

        Assert.Equal(
            new PhotoGridScrollbarThumb(Top: 200, Height: 200),
            PhotoGridLogic.ScrollbarThumb(actualHeight: 848, extentHeight: 2000, viewportHeight: 500, verticalOffset: 500));

        Assert.Equal(
            new PhotoGridScrollbarThumb(Top: 782, Height: PhotoGridLogic.MinimumThumbHeight),
            PhotoGridLogic.ScrollbarThumb(actualHeight: 848, extentHeight: 80000, viewportHeight: 500, verticalOffset: 79500));
    }
}
