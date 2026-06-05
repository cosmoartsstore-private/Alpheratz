using Alpheratz.Features.Gallery;
using Alpheratz.Shared.Models;

namespace Alpheratz.Tests;

/// <summary>
/// GalleryDisplayState の既存 StateViewModelBehaviorTests で触れていない表示状態分岐を補うテスト。
///
/// GalleryDisplayState は XAML コントロールではなく、ギャラリー右ペインの寸法、表示モード、
/// 日付プリセット、グルーピング変更前の準備状態をまとめる状態クラスである。
/// ここでは UI を起動せず、getter と no-op 分岐、残りの日付プリセットを直接検証する。
/// </summary>
public sealed class GalleryDisplayStateAdditionalTests
{
    /// <summary>
    /// gallery 表示では masonry 向け制約が有効になり、表示写真が PhotoGridItem に包まれることを確認する。
    ///
    /// groupedPhotoLabel は UI の列見出しとして固定文言を返す。
    /// buildDisplayPhotoItems は標準グリッド用の薄い変換で、元の PhotoThumbnailItem 参照を失わないことが重要である。
    /// </summary>
    [Fact]
    public void DisplayHelpers_ReturnGroupingLabelMasonryAvailabilityAndGridItems()
    {
        var state = new GalleryDisplayState { ViewMode = ViewMode.gallery };
        var first = new PhotoThumbnailItem { PhotoPath = "/photos/a.jpg" };
        var second = new PhotoThumbnailItem { PhotoPath = "/photos/b.jpg" };

        var items = state.buildDisplayPhotoItems([first, second]);

        Assert.Equal("ワールド", state.groupedPhotoLabel);
        Assert.True(state.isGroupingUnavailableInMasonry);
        Assert.Equal(2, items.Count);
        Assert.Same(first, items[0].Photo);
        Assert.Same(second, items[1].Photo);
    }

    /// <summary>
    /// グルーピングモードと表示フォルダモードが同じ値へ変更された場合、setter を呼ばず準備表示も保持することを確認する。
    ///
    /// 同値変更で clearPendingViewPreparations や setter を動かすと、ユーザーが見ている準備表示を不要に消す。
    /// そのため no-op 分岐では callback を呼ばず、ViewPreparationLabel も維持する。
    /// </summary>
    [Fact]
    public void PrepareModeChange_DoesNothingWhenRequestedModeIsAlreadyCurrent()
    {
        var state = new GalleryDisplayState();
        state.beginViewPreparation("準備中");
        var groupingCalls = 0;
        var folderCalls = 0;

        state.prepareGroupingModeChange(GroupingMode.world, GroupingMode.world, _ => groupingCalls++);
        state.prepareDisplayFolderModeChange(DisplayFolderMode.secondary, DisplayFolderMode.secondary, _ => folderCalls++);

        Assert.Equal(0, groupingCalls);
        Assert.Equal(0, folderCalls);
        Assert.Equal("準備中", state.ViewPreparationLabel);
    }

    /// <summary>
    /// モード変更 callback が例外を投げても、状態クラスが例外を外へ漏らさないことを確認する。
    ///
    /// これらのメソッドは UI イベントから呼ばれるため、外部 setter の失敗で画面スレッドを落とさない。
    /// catch 後の状態は「変更に失敗したので準備ラベルを維持する」現在仕様として固定する。
    /// </summary>
    [Fact]
    public void PrepareModeChange_CatchesCallbackFailures()
    {
        var state = new GalleryDisplayState();
        state.beginViewPreparation("準備中");

        state.prepareGroupingModeChange(GroupingMode.none, GroupingMode.world, _ => throw new InvalidOperationException("group"));
        state.prepareDisplayFolderModeChange(DisplayFolderMode.all, DisplayFolderMode.primary, _ => throw new InvalidOperationException("folder"));

        Assert.Equal("準備中", state.ViewPreparationLabel);
    }

    /// <summary>
    /// thisMonth、halfYear、oneYear プリセットがローカル日付基準の範囲へ展開されることを確認する。
    ///
    /// 既存テストは today / last7days / lastMonth を対象にしている。
    /// このテストでは月初月末、半年前、一年前の代表分岐を補い、すべて yyyy-MM-dd 形式で返ることを固定する。
    /// </summary>
    [Fact]
    public void DatePresets_IncludeMonthHalfYearAndOneYearRanges()
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var monthEnd = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var halfYearFrom = today.AddMonths(-6).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var oneYearFrom = today.AddYears(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var todayText = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(new DatePresetRange(monthStart, monthEnd), GalleryDisplayState.getDateRangeFromPreset(DatePreset.thisMonth));
        Assert.Equal(new DatePresetRange(halfYearFrom, todayText), GalleryDisplayState.getDateRangeFromPreset(DatePreset.halfYear));
        Assert.Equal(new DatePresetRange(oneYearFrom, todayText), GalleryDisplayState.getDateRangeFromPreset(DatePreset.oneYear));
    }
}
