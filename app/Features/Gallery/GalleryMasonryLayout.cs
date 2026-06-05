using System;
using System.Collections.Generic;

namespace Alpheratz.Features.Gallery;

/// <summary>Masonry レイアウト上の1カードの位置とサイズ。</summary>
public sealed record MasonryItem(PhotoThumbnailItem Photo, double Top, double Left, double Width, double Height);

/// <summary>Masonry レイアウト計算の結果。</summary>
public sealed record MasonryLayoutResult(
    IReadOnlyList<MasonryItem> Items,
    double TotalHeight,
    double ColumnWidth,
    int ColumnCount,
    double Gap);

/// <summary>
/// Masonry レイアウトの座標を計算する純粋ロジック。
/// 写真の縦横比からカード高を求め、現在最も低い列へ順に配置する。
/// </summary>
public static class GalleryMasonryLayout
{
    public const double Gap = 14;

    /// <summary>
    /// 1列の最小幅。列数の自動制限にも使う。
    /// 画面幅が足りない場合は列数を減らし、カードが潰れないようにする。
    /// </summary>
    public const double MinColumnWidth = 320;

    /// <summary>
    /// 仮想化の探索範囲に使う最大カード高。
    /// 実際のカード高は写真の縦横比から計算する。
    /// </summary>
    public const double MaxCardHeight = 900;

    // 極端な縦横比だけを抑え、写真本来の比率を優先する。
    private const double MinAspect = 0.5;   // これ以上は縦長にしない (1:2)
    private const double MaxAspect = 2.0;   // これ以上は横長にしない (2:1)

    /// <summary>写真リストとパネル幅からレイアウト座標を一括計算する。</summary>
    public static MasonryLayoutResult Build(
        IReadOnlyList<PhotoThumbnailItem> photos,
        double panelWidth,
        int requestedColumnCount)
    {
        var availableWidth = Math.Max(panelWidth - 8, MinColumnWidth);
        var maxFitColumns = Math.Max(1, (int)Math.Floor((availableWidth + Gap) / (MinColumnWidth + Gap)));
        var columnCount = Math.Max(1, Math.Min(requestedColumnCount, maxFitColumns));
        var columnWidth = Math.Floor((availableWidth - Gap * (columnCount - 1)) / columnCount);
        if (columnWidth < MinColumnWidth) columnWidth = MinColumnWidth;
        var columnHeights = new double[columnCount];

        var items = new List<MasonryItem>(photos.Count);
        foreach (var photo in photos)
        {
            // 最も低い列へ置くことで、列ごとの高さの偏りを抑える。
            var targetColumn = 0;
            for (var i = 1; i < columnCount; i++)
            {
                if (columnHeights[i] < columnHeights[targetColumn]) targetColumn = i;
            }

            var height = GetCardHeight(photo, columnWidth);
            var top = columnHeights[targetColumn];
            var left = targetColumn * (columnWidth + Gap);
            columnHeights[targetColumn] += height + Gap;

            items.Add(new MasonryItem(photo, top, left, columnWidth, height));
        }

        var maxColumnHeight = 0.0;
        foreach (var h in columnHeights) if (h > maxColumnHeight) maxColumnHeight = h;
        var totalHeight = Math.Max(0, maxColumnHeight - (photos.Count > 0 ? Gap : 0));

        return new MasonryLayoutResult(items, totalHeight, columnWidth, columnCount, Gap);
    }

    /// <summary>写真の縦横比を返す。寸法がない場合は orientation から推定する。</summary>
    private static double GetAspectRatio(PhotoThumbnailItem photo)
    {
        var w = photo.ImageWidth ?? 0;
        var h = photo.ImageHeight ?? 0;
        if (w > 0 && h > 0) return (double)w / h;
        if (string.Equals(photo.Orientation, "portrait", StringComparison.Ordinal)) return 9.0 / 16;
        if (string.Equals(photo.Orientation, "landscape", StringComparison.Ordinal)) return 16.0 / 9;
        return 1;
    }

    /// <summary>
    /// 列幅と写真の縦横比からカード高を計算する。
    /// 画像比率を保つため、UniformToFill のような切り抜きはしない。
    /// </summary>
    private static double GetCardHeight(PhotoThumbnailItem photo, double columnWidth)
    {
        var ratio = Math.Clamp(GetAspectRatio(photo), MinAspect, MaxAspect);
        return Math.Round(columnWidth / ratio);
    }
}
