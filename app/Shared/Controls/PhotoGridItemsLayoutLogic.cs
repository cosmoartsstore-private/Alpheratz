using Alpheratz.Features.Gallery;

namespace Alpheratz.Shared.Controls;

/// <summary>
/// PhotoGridItemsView の標準グリッド表示に使う寸法・スクロール判定ロジック。
/// WinUI コントロールには触れず、数値入力だけから表示計算を行う。
/// </summary>
internal static class PhotoGridItemsLayoutLogic
{
    public const int FixedColumns = 5;
    public const double ImageAspectHeight = 3.0 / 4.0;
    public const double InfoHeight = 56;
    public const double CardMarginHorizontal = 8;
    public const double CardMarginVertical = 12;
    public const double GridPadding = 24;
    public const int FallbackColumnCount = 5;
    public const double NearBottomThreshold = 600;
    public const double ShimmerWidthRatio = 0.4;
    public const double DefaultShimmerCardWidth = 300;
    public const int ShimmerDurationMilliseconds = 1500;
    public const int ImageFadeInDurationMilliseconds = 200;

    /// <summary>利用可能幅から 5 列固定のカード寸法を計算する。幅不足時は null。</summary>
    public static PhotoGridCardLayout? CalculateCardLayout(double availableWidth)
    {
        if (availableWidth <= 0) return null;
        var usable = availableWidth - GridPadding;
        if (usable <= 0) return null;
        var cardWidth = Math.Floor(usable / FixedColumns - CardMarginHorizontal);
        if (cardWidth < 100) return null;
        return new PhotoGridCardLayout(
            cardWidth,
            cardWidth + CardMarginHorizontal,
            Math.Floor(cardWidth * ImageAspectHeight + InfoHeight) + CardMarginVertical,
            FixedColumns);
    }

    /// <summary>スクロール位置が追加読み込みを促す終端近くかを返す。</summary>
    public static bool IsNearBottom(double scrollableHeight, double verticalOffset)
        => scrollableHeight - verticalOffset < NearBottomThreshold;

    /// <summary>スクロール位置から最初に見えているとみなす写真インデックスを概算する。</summary>
    public static int? EstimateFirstVisibleIndex(double scrollTop, double itemHeight, int maximumRowsOrColumns)
    {
        if (itemHeight <= 0) return null;
        var columns = Math.Max(1, maximumRowsOrColumns > 0 ? maximumRowsOrColumns : FallbackColumnCount);
        var row = (int)(scrollTop / itemHeight);
        return row * columns;
    }

    /// <summary>shimmer 計算に使うカード幅を、既知幅・実測幅・既定値の優先順で決める。</summary>
    public static double ResolveShimmerCardWidth(double currentImageWidth, double actualWidth)
    {
        if (currentImageWidth > 0) return currentImageWidth;
        if (actualWidth > 0) return actualWidth;
        return DefaultShimmerCardWidth;
    }

    /// <summary>カード幅から shimmer ハイライト幅を計算する。</summary>
    public static double CalculateShimmerHighlightWidth(double cardWidth)
        => cardWidth * ShimmerWidthRatio;

    /// <summary>初期レイアウトに使う幅を、GridView 実測値、直近幅の優先順で決める。</summary>
    public static double ResolveInitialGridWidth(double actualWidth, double lastKnownWidth)
        => actualWidth > 0 ? actualWidth : lastKnownWidth;

    /// <summary>指定インデックスが ItemsSource の範囲内かを返す。</summary>
    public static bool IsValidItemIndex(int index, int itemCount)
        => index >= 0 && index < itemCount;

    /// <summary>先頭表示インデックスが前回通知値から変わっているかを返す。</summary>
    public static bool ShouldNotifyFirstVisibleIndex(int? firstIndex, int lastReportedFirstVisible)
        => firstIndex.HasValue && firstIndex.Value != lastReportedFirstVisible;

    /// <summary>画像差し替えに関係する PhotoThumbnailItem のプロパティ名かを返す。</summary>
    public static bool IsImageSourceProperty(string? propertyName)
        => propertyName is nameof(PhotoThumbnailItem.GridThumbPath)
            or nameof(PhotoThumbnailItem.EffectiveSourcePath);

    /// <summary>写真からグリッド表示に使う画像ソースパスを取得する。</summary>
    public static string? ResolveImageSourcePath(PhotoThumbnailItem photo) => photo.EffectiveSourcePath;

    /// <summary>画像ソースパスが BitmapImage へ渡せる値かを返す。</summary>
    public static bool HasImageSource(string? path) => !string.IsNullOrEmpty(path);

    /// <summary>グリッドサムネイルのデコード幅を返す。</summary>
    public static int GridDecodePixelWidth => 300;

    /// <summary>写真から BitmapImage に渡すソースパスとデコード幅を返す。ソース空なら null。</summary>
    public static GridImageRequest? ImageRequest(PhotoThumbnailItem photo)
    {
        var path = ResolveImageSourcePath(photo);
        return HasImageSource(path)
            ? new GridImageRequest(path!, GridDecodePixelWidth)
            : null;
    }

    /// <summary>カード Border の通常/hover 表示に使うテーマリソースキーと移動量を返す。</summary>
    public static GridCardVisual CardVisual(GridCardVisualState state)
        => state == GridCardVisualState.Hover
            ? new GridCardVisual("ABorderStrong", "ASurfaceHover", -2, 180)
            : new GridCardVisual("ABorder", "ASurface", 0, 180);

    /// <summary>画像ロード状態に応じて shimmer とエラー表示をどう扱うかを返す。</summary>
    public static GridImageLoadVisual ImageLoadVisual(GridImageLoadState state)
        => state switch
        {
            GridImageLoadState.Reset => new GridImageLoadVisual(
                ImageOpacity: 0,
                ShimmerBaseOpacity: 1,
                ShimmerHighlightOpacity: 1,
                ErrorVisible: false,
                StartShimmer: true,
                StopShimmer: false),
            GridImageLoadState.Opened => new GridImageLoadVisual(
                ImageOpacity: 1,
                ShimmerBaseOpacity: 0,
                ShimmerHighlightOpacity: 0,
                ErrorVisible: false,
                StartShimmer: false,
                StopShimmer: true),
            GridImageLoadState.Failed => new GridImageLoadVisual(
                ImageOpacity: 0,
                ShimmerBaseOpacity: 1,
                ShimmerHighlightOpacity: 0,
                ErrorVisible: true,
                StartShimmer: false,
                StopShimmer: true),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

    /// <summary>shimmer アニメーションに使うハイライト幅と開始/終了オフセットを返す。</summary>
    public static ShimmerAnimationMetrics CalculateShimmerMetrics(double cardWidth)
    {
        var highlightWidth = CalculateShimmerHighlightWidth(cardWidth);
        return new ShimmerAnimationMetrics(highlightWidth, -highlightWidth, cardWidth);
    }

    /// <summary>表示中カードの shimmer 幅を再計算すべき Border かを返す。</summary>
    public static bool ShouldRefreshShimmerWidth(string name, double opacity)
        => name == "ShimmerHighlight" && opacity > 0;
}

/// <summary>PhotoGridItemsView のカード寸法計算結果。</summary>
internal sealed record PhotoGridCardLayout(
    double ImageWidth,
    double ItemWidth,
    double ItemHeight,
    int Columns);

/// <summary>標準グリッド画像のロード要求。</summary>
internal sealed record GridImageRequest(string SourcePath, int DecodePixelWidth);

/// <summary>カード Border の表示状態。</summary>
internal enum GridCardVisualState
{
    Rest,
    Hover,
}

/// <summary>カード Border に適用するテーマキーと移動量。</summary>
internal sealed record GridCardVisual(string BorderKey, string BackgroundKey, double OffsetY, int DurationMilliseconds);

/// <summary>画像ロード演出の状態。</summary>
internal enum GridImageLoadState
{
    Reset,
    Opened,
    Failed,
}

/// <summary>画像ロード状態に応じた opacity と shimmer/error 表示。</summary>
internal sealed record GridImageLoadVisual(
    double ImageOpacity,
    double ShimmerBaseOpacity,
    double ShimmerHighlightOpacity,
    bool ErrorVisible,
    bool StartShimmer,
    bool StopShimmer);

/// <summary>shimmer ハイライトの幅と横移動範囲。</summary>
internal sealed record ShimmerAnimationMetrics(double HighlightWidth, double StartOffset, double EndOffset);
