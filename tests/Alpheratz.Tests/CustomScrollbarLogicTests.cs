using Alpheratz.Shared.Controls;

namespace Alpheratz.Tests;

/// <summary>
/// CustomScrollbar から分離したスクロールバー表示値とドラッグ通知条件を検証するテスト。
///
/// CustomScrollbar 本体は WinUI の Border、TranslateTransform、PointerRoutedEventArgs を直接扱う。
/// ここでは UI 要素を生成せず、依存プロパティ値やポインタ状態から code-behind が適用する
/// Thumb 寸法、hover 表示、ドラッグ通知の条件だけを固定する。
/// </summary>
public sealed class CustomScrollbarLogicTests
{
    /// <summary>
    /// Thumb の表示高さが最小値 18px を下回らず、Y 位置は入力された ThumbTop を保つことを確認する。
    ///
    /// ThumbHeight はスクロール可能領域と表示領域の比率から外側で計算される。
    /// コンテンツが極端に長い場合でも操作できる幅を残すため、CustomScrollbar 側で最小高さへ丸める。
    /// </summary>
    [Fact]
    public void ThumbLayout_KeepsMinimumHeightAndCurrentTop()
    {
        Assert.Equal(
            new ScrollbarThumbLayout(CustomScrollbarLogic.MinimumThumbHeight, 32),
            CustomScrollbarLogic.ThumbLayout(thumbTop: 32, thumbHeight: 4));
        Assert.Equal(
            new ScrollbarThumbLayout(72, 128),
            CustomScrollbarLogic.ThumbLayout(thumbTop: 128, thumbHeight: 72));
    }

    /// <summary>
    /// トラック押下とドラッグ移動で親へ通知する Y 位置を確認する。
    ///
    /// 押下時はその位置へ即座にスクロールさせるため Y 位置をそのまま通知する。
    /// 移動中の通知は CapturePointer 後のドラッグ中に限定し、hover だけのポインタ移動では
    /// PhotoGrid 側のスクロール位置を変えない。
    /// </summary>
    [Fact]
    public void PointerNotifications_ReturnTrackYOnlyWhenInteractionRequiresScroll()
    {
        Assert.Equal(240, CustomScrollbarLogic.TrackClickPosition(240));
        Assert.Equal(300, CustomScrollbarLogic.DragPosition(isDragging: true, leftButtonPressed: true, trackY: 300));
        Assert.Null(CustomScrollbarLogic.DragPosition(isDragging: true, leftButtonPressed: false, trackY: 300));
        Assert.Null(CustomScrollbarLogic.DragPosition(isDragging: false, leftButtonPressed: true, trackY: 300));
        Assert.False(CustomScrollbarLogic.DraggingAfterRelease());
        Assert.True(CustomScrollbarLogic.ShouldContinueDragging(isDragging: true, leftButtonPressed: true));
        Assert.False(CustomScrollbarLogic.ShouldContinueDragging(isDragging: true, leftButtonPressed: false));
    }

    /// <summary>
    /// ポインタが入った時は rail と Thumb を強調し、通常 exit では元の細い表示へ戻すことを確認する。
    ///
    /// hover 中は track rail を表示し、Thumb を 8px に広げて操作対象を見やすくする。
    /// ポインタが外れたら rail を非表示に戻し、Thumb 幅とブラシを通常状態へ戻す。
    /// </summary>
    [Fact]
    public void HoverVisual_ReturnsEnteredAndRestVisualValues()
    {
        Assert.Equal(
            new ScrollbarHoverVisual(
                CustomScrollbarLogic.VisibleRailOpacity,
                CustomScrollbarLogic.HoverThumbWidth,
                CustomScrollbarLogic.HoverThumbBrushKey,
                CustomScrollbarLogic.HoverAnimationDurationMilliseconds),
            CustomScrollbarLogic.HoverVisual(ScrollbarPointerState.Entered, isDragging: false));

        Assert.Equal(
            new ScrollbarHoverVisual(
                CustomScrollbarLogic.HiddenRailOpacity,
                CustomScrollbarLogic.RestThumbWidth,
                CustomScrollbarLogic.RestThumbBrushKey,
                CustomScrollbarLogic.HoverAnimationDurationMilliseconds),
            CustomScrollbarLogic.HoverVisual(ScrollbarPointerState.Exited, isDragging: false));
    }

    /// <summary>
    /// ドラッグ中にポインタが track 外へ出た場合は hover 表示を維持することを確認する。
    ///
    /// CapturePointer 中は押下したまま track 外へ出てもスクロール操作が続く。
    /// その間に Thumb を細く戻すとドラッグ中の操作対象が消えたように見えるため、exit 表示更新を行わない。
    /// </summary>
    [Fact]
    public void HoverVisual_DoesNotReturnExitVisualWhileDragging()
    {
        Assert.Null(CustomScrollbarLogic.HoverVisual(ScrollbarPointerState.Exited, isDragging: true));
    }
}
