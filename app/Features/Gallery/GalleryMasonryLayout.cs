using System;
using System.Collections.Generic;

namespace Alpheratz.Features.Gallery;

/// <summary>マソンリーレイアウト上の1カードの位置・サイズ情報。</summary>
public sealed record MasonryItem(PhotoThumbnailItem Photo, double Top, double Left, double Width, double Height);

/// <summary>マソンリーレイアウトの計算結果。</summary>
public sealed record MasonryLayoutResult(
    IReadOnlyList<MasonryItem> Items,
    double TotalHeight,
    double ColumnWidth,
    int ColumnCount,
    double Gap);

/// <summary>
/// マソンリーレイアウトの座標計算を行う純粋関数クラス。
/// 各写真のアスペクト比に基づいてカード高を算出し、
/// 最も短いカラムに順次配置していく（Greedy 法）。
/// </summary>
public static class GalleryMasonryLayout
{
    public const double Gap = 14;
    public const double MinColumnWidth = 220;
    public const double MinCardHeight = 170;
    public const double MaxCardHeight = 520;

    /// <summary>写真リストとパネル幅からレイアウト座標を一括計算する。</summary>
    public static MasonryLayoutResult Build(
        IReadOnlyList<PhotoThumbnailItem> photos,
        double panelWidth,
        int requestedColumnCount)
    {
        var availableWidth = Math.Max(panelWidth - 8, MinColumnWidth);
        // MinColumnWidth を割り込まない最大カラム数にクランプする
        var maxFitColumns = Math.Max(1, (int)Math.Floor((availableWidth + Gap) / (MinColumnWidth + Gap)));
        var columnCount = Math.Max(1, Math.Min(requestedColumnCount, maxFitColumns));
        var columnWidth = Math.Floor((availableWidth - Gap * (columnCount - 1)) / columnCount);
        if (columnWidth < MinColumnWidth) columnWidth = MinColumnWidth;
        var columnHeights = new double[columnCount];

        var items = new List<MasonryItem>(photos.Count);
        foreach (var photo in photos)
        {
            // 最も短いカラムに配置する（Greedy 法）
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

    /// <summary>写真のアスペクト比を返す。寸法不明時は orientation フィールドで推定する。</summary>
    private static double GetAspectRatio(PhotoThumbnailItem photo)
    {
        var w = photo.ImageWidth ?? 0;
        var h = photo.ImageHeight ?? 0;
        if (w > 0 && h > 0) return (double)w / h;
        if (string.Equals(photo.Orientation, "portrait", StringComparison.Ordinal)) return 9.0 / 16;
        if (string.Equals(photo.Orientation, "landscape", StringComparison.Ordinal)) return 16.0 / 9;
        return 1;
    }

    /// <summary>アスペクト比とカラム幅からカード高を算出し、Min/Max でクランプする。</summary>
    private static double GetCardHeight(PhotoThumbnailItem photo, double columnWidth)
    {
        var ratio = Math.Max(0.2, GetAspectRatio(photo));
        var raw = columnWidth / ratio;
        return Math.Max(MinCardHeight, Math.Min(MaxCardHeight, Math.Round(raw)));
    }
}
