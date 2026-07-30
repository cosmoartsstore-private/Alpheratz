using Alpheratz.Features.Gallery;

namespace Alpheratz.Tests;

/// <summary>
/// GalleryPage から分離した表示同期ロジックを検証するテスト。
///
/// GalleryPage 本体は BulkOpBar、MonthNav、MasonryView、GridStage を直接操作する。
/// その中でもバルク操作バーの遷移と月ナビのアクティブ位置は入力状態だけで決まるため、
/// UI を起動せずに GalleryPageLogic の純粋関数として固定する。
/// </summary>
public sealed class GalleryPageLogicTests
{
    /// <summary>
    /// マルチセレクト状態と直前表示状態から、バルク操作バーの表示遷移とラベルが決まることを確認する。
    ///
    /// マルチセレクトへ入った瞬間だけ SlideIn を行い、抜けた瞬間だけ SlideOut を行う。
    /// 同じ状態が続く場合はアニメーションを繰り返さず、選択枚数ラベルだけを現在件数へ更新する。
    /// </summary>
    [Fact]
    public void ComputeBulkOperationBarState_EmitsTransitionsOnlyWhenVisibilityChanges()
    {
        Assert.Equal(
            new BulkOperationBarState(BulkOperationBarTransition.Show, true, "3枚を選択中"),
            GalleryPageLogic.ComputeBulkOperationBarState(true, false, 3));
        Assert.Equal(
            new BulkOperationBarState(BulkOperationBarTransition.None, true, "5枚を選択中"),
            GalleryPageLogic.ComputeBulkOperationBarState(true, true, 5));
        Assert.Equal(
            new BulkOperationBarState(BulkOperationBarTransition.Hide, false, "0枚を選択中"),
            GalleryPageLogic.ComputeBulkOperationBarState(false, true, 0));
        Assert.Equal(
            new BulkOperationBarState(BulkOperationBarTransition.None, false, "0枚を選択中"),
            GalleryPageLogic.ComputeBulkOperationBarState(false, false, 0));
    }

    /// <summary>
    /// 表示中の先頭写真インデックスから、対応する月グループの位置が選ばれることを確認する。
    ///
    /// 月グループは FirstIndex 昇順で保持される。
    /// firstVisibleIndex 以下で最も後ろにあるグループを active にすることで、
    /// スクロールが月境界を越えた直後にサイドバーの選択月が切り替わる。
    /// </summary>
    [Fact]
    public void FindActiveMonthGroupIndex_UsesLastGroupStartingBeforeVisibleIndex()
    {
        var groups = new[]
        {
            new GalleryMonthGroup("2026-06", 2026, 6, "6月", 0, 10),
            new GalleryMonthGroup("2026-05", 2026, 5, "5月", 10, 8),
            new GalleryMonthGroup("2026-04", 2026, 4, "4月", 18, 12),
        };

        Assert.Equal(0, GalleryPageLogic.FindActiveMonthGroupIndex(groups, -1));
        Assert.Equal(0, GalleryPageLogic.FindActiveMonthGroupIndex(groups, 0));
        Assert.Equal(1, GalleryPageLogic.FindActiveMonthGroupIndex(groups, 10));
        Assert.Equal(1, GalleryPageLogic.FindActiveMonthGroupIndex(groups, 17));
        Assert.Equal(2, GalleryPageLogic.FindActiveMonthGroupIndex(groups, 18));
        Assert.Equal(2, GalleryPageLogic.FindActiveMonthGroupIndex(groups, 999));
        Assert.Null(GalleryPageLogic.FindActiveMonthGroupIndex([], 0));
    }
}
