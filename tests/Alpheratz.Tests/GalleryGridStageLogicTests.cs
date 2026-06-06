using Alpheratz.Features.Gallery.Controls;

namespace Alpheratz.Tests;

/// <summary>
/// GalleryGridStage から分離した表示状態判定を検証するテスト。
///
/// GalleryGridStage 本体は WinUI の Visibility、GridLength、x:Load=False の MasonryView 実体化を扱う。
/// ここでは UI 要素を生成せず、表示モードと loading/empty の入力から、標準グリッド、
/// Masonry、MonthNav、EmptyState をどう出し分けるかを固定する。
/// </summary>
public sealed class GalleryGridStageLogicTests
{
    /// <summary>
    /// 表示モード切替時に、標準グリッドと Masonry の表示が排他的になることを確認する。
    ///
    /// 標準表示では PhotoGrid を表示して Masonry を隠す。
    /// Masonry 表示では PhotoGrid を隠し、遅延実体化済みの MasonryView を表示する。
    /// MonthNav は日付順で使うため、表示幅 56px を維持する。
    /// </summary>
    [Fact]
    public void ViewModeDisplay_TogglesPhotoGridAndMasonry()
    {
        Assert.Equal(
            new GridStageDisplayState(
                PhotoGridVisible: true,
                MasonryVisible: false,
                MonthNavVisible: true,
                MonthNavWidth: GalleryGridStageLogic.MonthNavVisibleWidth),
            GalleryGridStageLogic.ViewModeDisplay(masonryActive: false));

        Assert.Equal(
            new GridStageDisplayState(
                PhotoGridVisible: false,
                MasonryVisible: true,
                MonthNavVisible: true,
                MonthNavWidth: GalleryGridStageLogic.MonthNavVisibleWidth),
            GalleryGridStageLogic.ViewModeDisplay(masonryActive: true));
    }

    /// <summary>
    /// ワールド順など日付順ではない表示では、MonthNav の日付ジャンプを隠すことを確認する。
    /// </summary>
    [Fact]
    public void MonthNavCanBeHiddenWhenDateOrderIsUnavailable()
    {
        Assert.Equal(
            new GridStageDisplayState(
                PhotoGridVisible: true,
                MasonryVisible: false,
                MonthNavVisible: false,
                MonthNavWidth: 0),
            GalleryGridStageLogic.ViewModeDisplay(masonryActive: false, monthNavAvailable: false));

        Assert.Equal(
            new GridStageLoadingDisplay(
                LoadingVisible: false,
                EmptyVisible: false,
                MonthNavVisible: false,
                MonthNavWidth: 0),
            GalleryGridStageLogic.LoadingDisplay(isLoading: false, totalCount: 3, monthNavAvailable: false));
    }

    /// <summary>
    /// ロード中、空状態、写真あり状態で loading veil、EmptyState、MonthNav の表示が切り替わることを確認する。
    ///
    /// ロード中は既存グリッドを残したまま veil を重ねるため EmptyState は出さない。
    /// ロード完了後に totalCount=0 なら EmptyState を出し、MonthNav の列幅を 0 にして余白を消す。
    /// 写真が 1 件以上ある場合は EmptyState を隠し、MonthNav を通常幅で表示する。
    /// </summary>
    [Fact]
    public void LoadingDisplay_ReturnsLoadingEmptyAndMonthNavStates()
    {
        Assert.Equal(
            new GridStageLoadingDisplay(
                LoadingVisible: true,
                EmptyVisible: false,
                MonthNavVisible: true,
                MonthNavWidth: GalleryGridStageLogic.MonthNavVisibleWidth),
            GalleryGridStageLogic.LoadingDisplay(isLoading: true, totalCount: 0));

        Assert.Equal(
            new GridStageLoadingDisplay(
                LoadingVisible: false,
                EmptyVisible: true,
                MonthNavVisible: false,
                MonthNavWidth: 0),
            GalleryGridStageLogic.LoadingDisplay(isLoading: false, totalCount: 0));

        Assert.Equal(
            new GridStageLoadingDisplay(
                LoadingVisible: false,
                EmptyVisible: false,
                MonthNavVisible: true,
                MonthNavWidth: GalleryGridStageLogic.MonthNavVisibleWidth),
            GalleryGridStageLogic.LoadingDisplay(isLoading: false, totalCount: 3));
    }

    /// <summary>
    /// EmptyState はロード完了後かつ totalCount が 0 の場合だけ表示対象になることを確認する。
    ///
    /// isLoading=true の間は totalCount がまだ古い値または 0 の可能性があるため、
    /// 空表示へ切り替えず loading veil を優先する。
    /// </summary>
    [Fact]
    public void ShouldShowEmpty_ReturnsTrueOnlyAfterLoadingWithZeroTotal()
    {
        Assert.False(GalleryGridStageLogic.ShouldShowEmpty(isLoading: true, totalCount: 0));
        Assert.True(GalleryGridStageLogic.ShouldShowEmpty(isLoading: false, totalCount: 0));
        Assert.False(GalleryGridStageLogic.ShouldShowEmpty(isLoading: false, totalCount: 1));
    }
}
