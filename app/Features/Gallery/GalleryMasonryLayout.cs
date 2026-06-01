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

    /// <summary>
    /// 1カラムの最小幅。自動列数の算出基準も兼ねる。
    /// 320px にすることで標準的な最大化ウィンドウ（約1920px）で5列前後になり、
    /// ウィンドウ幅に応じて列数が増減する（狭ければ減り、広ければ増える）。
    /// </summary>
    public const double MinColumnWidth = 320;

    /// <summary>
    /// 仮想化の二分探索が遡る「想定される最大カード高」の上限（探索マージン専用）。
    /// 実際のカード高はアスペクト比から決まるので、ここは取りこぼし防止の安全側の上限。
    /// </summary>
    public const double MaxCardHeight = 900;

    // カードは写真の実アスペクト比そのままで描画する（マソンリーの本質）。
    // 破損データや極端なパノラマ対策として比率だけを安全範囲にクランプする。
    // ピクセル単位の高さクランプは比率を歪めてサムネイルがクロップされる原因になるため使わない。
    private const double MinAspect = 0.5;   // これ以上は縦長にしない (1:2)
    private const double MaxAspect = 2.0;   // これ以上は横長にしない (2:1)

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

    /// <summary>
    /// カラム幅と写真のアスペクト比からカード高を算出する。
    /// カード比率＝写真比率になるため UniformToFill でもクロップが発生せず、
    /// ギャラリーのサムネイルとモーダルの表示が一致する。
    /// </summary>
    private static double GetCardHeight(PhotoThumbnailItem photo, double columnWidth)
    {
        var ratio = Math.Clamp(GetAspectRatio(photo), MinAspect, MaxAspect);
        return Math.Round(columnWidth / ratio);
    }
}
