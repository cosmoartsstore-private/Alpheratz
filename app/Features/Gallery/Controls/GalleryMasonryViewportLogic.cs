using Alpheratz.Features.Gallery;
using System.Collections.Specialized;

namespace Alpheratz.Features.Gallery.Controls;

/// <summary>
/// GalleryMasonryView の仮想化判定を UI 要素から切り離して扱う補助ロジック。
/// カラム数、表示対象、解放対象、先頭表示インデックスの計算だけを持つ。
/// </summary>
internal static class GalleryMasonryViewportLogic
{
    public const double ShimmerWidthRatio = 0.4;
    public const int ShimmerDurationMilliseconds = 1500;

    /// <summary>コントロール幅と指定カラム数から、内部幅と実効カラム数を算出する。</summary>
    public static (double InnerWidth, int EffectiveColumns) ComputeColumns(
        double actualWidth,
        double scrollHostWidth,
        int? requestedColumnCount)
    {
        var availableWidth = actualWidth;
        if (availableWidth <= 0) availableWidth = scrollHostWidth;
        var inner = Math.Max(0, availableWidth - 28);
        var autoCols = inner > 0
            ? Math.Max(1, (int)Math.Floor((inner + GalleryMasonryLayout.Gap) / (GalleryMasonryLayout.MinColumnWidth + GalleryMasonryLayout.Gap)))
            : 1;
        var effectiveCols = requestedColumnCount.HasValue
            ? Math.Max(1, Math.Min(requestedColumnCount.Value, autoCols))
            : autoCols;
        return (inner, effectiveCols);
    }

    /// <summary>外部指定カラム数を、0 以下なら自動計算指定へ正規化する。</summary>
    public static int? ResolveRequestedColumnCount(int count) => count > 0 ? count : null;

    /// <summary>コレクション変更時に、全再構築ではなく座標再計算だけで済ませるかを返す。</summary>
    public static bool ShouldReuseCardsForCollectionChange(NotifyCollectionChangedAction action)
        => action == NotifyCollectionChangedAction.Add;

    /// <summary>MasonryLayoutResult から Canvas の幅と高さを算出する。</summary>
    public static MasonryCanvasSize CanvasSize(MasonryLayoutResult layout)
        => new(
            layout.ColumnCount * layout.ColumnWidth + (layout.ColumnCount - 1) * layout.Gap,
            layout.TotalHeight);

    /// <summary>レイアウトアイテムのインデックスを Top 昇順に並べる。</summary>
    public static int[] BuildSortedIndex(IReadOnlyList<MasonryItem> items)
    {
        var sorted = new int[items.Count];
        for (var i = 0; i < items.Count; i++) sorted[i] = i;
        Array.Sort(sorted, (a, b) => items[a].Top.CompareTo(items[b].Top));
        return sorted;
    }

    /// <summary>現在のスクロール位置で生成または維持すべきカードのレイアウトインデックスを返す。</summary>
    public static IReadOnlyList<int> FindVisibleIndices(
        IReadOnlyList<MasonryItem> items,
        IReadOnlyList<int> sortedByTop,
        double scrollTop,
        double viewportHeight,
        double overscanPx,
        double maxCardHeight)
    {
        if (items.Count == 0 || sortedByTop.Count == 0) return [];
        var bottom = scrollTop + viewportHeight;
        var loadTop = scrollTop - overscanPx;
        var loadBottom = bottom + overscanPx;
        var searchStart = loadTop - maxCardHeight;
        var startIdx = LowerBoundByTop(items, sortedByTop, searchStart);
        var visible = new List<int>();

        for (var si = startIdx; si < sortedByTop.Count; si++)
        {
            var index = sortedByTop[si];
            var item = items[index];
            if (item.Top > loadBottom) break;
            if (item.Top + item.Height < loadTop) continue;
            visible.Add(index);
        }

        return visible;
    }

    /// <summary>カードが即時解放すべき範囲外に出ているかを判定する。</summary>
    public static bool IsOutsideReleaseRange(
        MasonryItem item,
        double scrollTop,
        double viewportHeight,
        double releaseMarginPx)
    {
        var bottom = scrollTop + viewportHeight;
        var releaseTop = scrollTop - releaseMarginPx;
        var releaseBottom = bottom + releaseMarginPx;
        return item.Top + item.Height < releaseTop || item.Top > releaseBottom;
    }

    /// <summary>サムネイル要求済み登録の結果と写真状態から、生成キューへ積むべきかを返す。</summary>
    public static bool ShouldQueueThumbnailAfterRequestRegistered(bool wasNewRequest, string photoPath, string? gridThumbPath)
        => wasNewRequest && string.IsNullOrEmpty(gridThumbPath) && !string.IsNullOrEmpty(photoPath);

    /// <summary>現在のスクロール位置から、月ナビゲーションへ通知する先頭写真インデックスを返す。</summary>
    public static int? FindFirstVisibleIndex(
        IReadOnlyList<MasonryItem> items,
        IReadOnlyList<int> sortedByTop,
        double scrollTop)
    {
        if (items.Count == 0 || sortedByTop.Count == 0) return null;
        var startPos = LowerBoundByTop(items, sortedByTop, scrollTop);
        return startPos < sortedByTop.Count
            ? sortedByTop[startPos]
            : sortedByTop[^1];
    }

    /// <summary>先頭表示インデックスが前回通知値から変わっているかを返す。</summary>
    public static bool ShouldNotifyFirstVisibleIndex(int? firstIndex, int lastReportedFirstVisible)
        => firstIndex.HasValue && firstIndex.Value != lastReportedFirstVisible;

    /// <summary>画像デコード時の幅指定を、1 以上の整数に丸める。</summary>
    public static int DecodePixelWidth(double containerWidth)
        => Math.Max(1, (int)containerWidth);

    /// <summary>写真とカード幅から、BitmapImage に渡すソースパスとデコード幅を返す。ソース空なら null。</summary>
    public static MasonryImageRequest? ImageRequest(PhotoThumbnailItem photo, double containerWidth)
    {
        var sourcePath = photo.EffectiveSourcePath ?? string.Empty;
        return string.IsNullOrEmpty(sourcePath)
            ? null
            : new MasonryImageRequest(sourcePath, DecodePixelWidth(containerWidth));
    }

    /// <summary>shimmer ハイライトの幅、開始/終了オフセット、再生時間を返す。</summary>
    public static MasonryShimmerMetrics ShimmerMetrics(double itemWidth)
    {
        var highlightWidth = itemWidth * ShimmerWidthRatio;
        return new MasonryShimmerMetrics(highlightWidth, -highlightWidth, itemWidth, ShimmerDurationMilliseconds);
    }

    /// <summary>画像差し替えに関係する PhotoThumbnailItem のプロパティ名かを返す。</summary>
    public static bool IsImageSourceProperty(string? propertyName)
        => propertyName is nameof(PhotoThumbnailItem.EffectiveSourcePath)
            or nameof(PhotoThumbnailItem.GridThumbPath)
            or nameof(PhotoThumbnailItem.ResolvedPhotoPath);

    /// <summary>新しい画像パスが現在ロード済みのパスと異なるかを返す。</summary>
    public static bool ShouldReloadImage(string newPath, string loadedPath)
        => newPath != loadedPath;

    /// <summary>未ロードカードが現在の overscan 範囲内にあるかを返す。</summary>
    public static bool IsInsideOverscan(double cardTop, double cardHeight, double scrollTop, double viewportHeight, double overscanPx)
    {
        var cardBottom = cardTop + cardHeight;
        var viewTop = scrollTop - overscanPx;
        var viewBottom = scrollTop + viewportHeight + overscanPx;
        return cardBottom >= viewTop && cardTop <= viewBottom;
    }

    /// <summary>写真の選択状態から、選択リングとチェックバッジを表示するかを返す。</summary>
    public static SelectionVisual SelectionVisual(bool isSelected) => new(isSelected);

    /// <summary>PhotoThumbnailItem の変更通知から、Masonry カード側で行う処理を返す。</summary>
    public static MasonryPhotoChangeAction PhotoChangeAction(
        string? propertyName,
        string newPath,
        string loadedPath,
        bool isLoaded,
        bool isInsideOverscan)
    {
        if (propertyName == nameof(PhotoThumbnailItem.IsSelected))
            return MasonryPhotoChangeAction.UpdateSelection;
        if (!IsImageSourceProperty(propertyName))
            return MasonryPhotoChangeAction.None;
        if (!ShouldReloadImage(newPath, loadedPath))
            return MasonryPhotoChangeAction.None;
        if (isLoaded)
            return MasonryPhotoChangeAction.ReloadNow;
        return isInsideOverscan ? MasonryPhotoChangeAction.ReloadNow : MasonryPhotoChangeAction.None;
    }

    /// <summary>Top が threshold 以上になる最初の sortedByTop 位置を二分探索で返す。</summary>
    private static int LowerBoundByTop(
        IReadOnlyList<MasonryItem> items,
        IReadOnlyList<int> sortedByTop,
        double threshold)
    {
        var lo = 0;
        var hi = sortedByTop.Count - 1;
        var result = sortedByTop.Count;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (items[sortedByTop[mid]].Top >= threshold)
            {
                result = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return result;
    }
}

/// <summary>Masonry Canvas に設定する幅と高さ。</summary>
internal sealed record MasonryCanvasSize(double Width, double Height);

/// <summary>Masonry カード画像のロード要求。</summary>
internal sealed record MasonryImageRequest(string SourcePath, int DecodePixelWidth);

/// <summary>Masonry shimmer ハイライトの幅と横移動範囲。</summary>
internal sealed record MasonryShimmerMetrics(double HighlightWidth, double StartOffset, double EndOffset, int DurationMilliseconds);

/// <summary>選択リングとチェックバッジの表示状態。</summary>
internal sealed record SelectionVisual(bool Visible);

/// <summary>写真プロパティ変更時に Masonry カードで行う処理。</summary>
internal enum MasonryPhotoChangeAction
{
    None,
    UpdateSelection,
    ReloadNow,
}
