namespace Alpheratz.Features.Gallery.Controls;

/// <summary>
/// GalleryGridStage の表示モード、loading、empty 状態を UI 要素なしで決める補助ロジック。
/// Visibility や GridLength は扱わず、code-behind が変換する bool と幅だけを返す。
/// </summary>
internal static class GalleryGridStageLogic
{
    public const double MonthNavVisibleWidth = 56;

    /// <summary>表示モード切替時の標準グリッド、Masonry、MonthNav の表示状態を返す。</summary>
    public static GridStageDisplayState ViewModeDisplay(bool masonryActive)
        => new GridStageDisplayState(
            PhotoGridVisible: !masonryActive,
            MasonryVisible: masonryActive,
            MonthNavVisible: true,
            MonthNavWidth: MonthNavVisibleWidth);

    /// <summary>ロード中・空状態から loading veil、empty、MonthNav の表示状態を返す。</summary>
    public static GridStageLoadingDisplay LoadingDisplay(bool isLoading, int totalCount)
    {
        var showEmpty = ShouldShowEmpty(isLoading, totalCount);
        return new GridStageLoadingDisplay(
            LoadingVisible: isLoading,
            EmptyVisible: showEmpty,
            MonthNavVisible: !showEmpty,
            MonthNavWidth: showEmpty ? 0 : MonthNavVisibleWidth);
    }

    /// <summary>ロード完了後に写真が 0 件なら empty state を表示する。</summary>
    public static bool ShouldShowEmpty(bool isLoading, int totalCount)
        => !isLoading && totalCount == 0;
}

/// <summary>表示モード切替で適用する表示状態。</summary>
internal sealed record GridStageDisplayState(
    bool PhotoGridVisible,
    bool MasonryVisible,
    bool MonthNavVisible,
    double MonthNavWidth);

/// <summary>ロード状態切替で適用する表示状態。</summary>
internal sealed record GridStageLoadingDisplay(
    bool LoadingVisible,
    bool EmptyVisible,
    bool MonthNavVisible,
    double MonthNavWidth);
