using Alpheratz.Features.Gallery;
using Alpheratz.Shared.Controls;

namespace Alpheratz.Tests;

/// <summary>
/// PhotoGridItemsView から分離した標準グリッドの数値計算を検証するテスト。
///
/// 標準グリッドは WinUI の GridView/ItemsWrapGrid 上で描画されるが、カード寸法や
/// スクロール位置からの先頭インデックス推定は単純な数値計算で決まる。
/// ここでは UI コントロールを生成せず、6列基準のレスポンシブレイアウト、追加読み込み境界、
/// shimmer 幅のフォールバックを純粋関数として固定する。
/// </summary>
public sealed class PhotoGridItemsLayoutLogicTests
{
    /// <summary>
    /// 利用可能幅から 6 列基準のカード寸法が計算されることを確認する。
    ///
    /// 現在の仕様では利用可能幅から左右余白を引き、通常 6 列で均等割りした値を画像幅にする。
    /// カードの ItemHeight は 16:9 の画像高と情報表示領域を足して決める。
    /// 幅不足時は列数を減らし、カードが潰れないようにする。
    /// </summary>
    [Fact]
    public void CalculateCardLayout_UsesSixColumnsAndLandscapeSixteenNineThumbnails()
    {
        var layout = GalleryLayout(1024);

        Assert.Equal(158, layout.ImageWidth);
        Assert.Equal(166, layout.ItemWidth);
        Assert.Equal(156, layout.ItemHeight);
        Assert.Equal(6, layout.Columns);
        Assert.Null(PhotoGridItemsLayoutLogic.CalculateCardLayout(0));

        var narrow = GalleryLayout(200);
        Assert.Equal(1, narrow.Columns);
        Assert.Equal(168, narrow.ImageWidth);
        Assert.Equal(176, narrow.ItemWidth);
    }

    /// <summary>
    /// 代表幅の列数とカード寸法を固定値で照合し、各セルが利用可能幅に収まることを確認する。
    /// </summary>
    [Theory]
    [InlineData(200, 1, 168, 176, 162)]
    [InlineData(320, 2, 140, 148, 146)]
    [InlineData(640, 5, 115, 123, 132)]
    [InlineData(700, 6, 104, 112, 126)]
    [InlineData(1024, 6, 158, 166, 156)]
    [InlineData(1920, 6, 308, 316, 241)]
    public void CalculateCardLayout_KeepsResponsiveCellsInsideAvailableWidth(double availableWidth, int columns, double imageWidth, double itemWidth, double itemHeight)
    {
        var layout = GalleryLayout(availableWidth);
        var usable = availableWidth - 24;

        Assert.InRange(layout.Columns, 1, 6);
        Assert.True(layout.ImageWidth > 0);
        Assert.True(layout.ItemWidth > 0);
        Assert.True(layout.ItemHeight > 56);
        Assert.True(layout.ItemWidth * layout.Columns <= usable);

        Assert.Equal((columns, imageWidth, itemWidth, itemHeight), (layout.Columns, layout.ImageWidth, layout.ItemWidth, layout.ItemHeight));
    }

    /// <summary>最小セルと列数変更の直前・境界で、返却寸法を本体の計算から独立した値へ照合する。</summary>
    [Theory]
    [InlineData(24.5, 1, 1, 1, 68)]
    [InlineData(131.999, 1, 99, 107, 123)]
    [InlineData(132, 1, 100, 108, 124)]
    [InlineData(239.999, 1, 207, 215, 184)]
    [InlineData(240, 2, 100, 108, 124)]
    [InlineData(347.999, 2, 153, 161, 154)]
    [InlineData(348, 3, 100, 108, 124)]
    [InlineData(455.999, 3, 135, 143, 143)]
    [InlineData(456, 4, 100, 108, 124)]
    [InlineData(563.999, 4, 126, 134, 138)]
    [InlineData(564, 5, 100, 108, 124)]
    [InlineData(671.999, 5, 121, 129, 136)]
    [InlineData(672, 6, 100, 108, 124)]
    [InlineData(780, 6, 118, 126, 134)]
    public void CalculateCardLayout_FixesMinimumCellAndColumnTransitionDimensions(double availableWidth, int columns, double imageWidth, double itemWidth, double itemHeight)
    {
        var layout = GalleryLayout(availableWidth);
        Assert.Equal((columns, imageWidth, itemWidth, itemHeight), (layout.Columns, layout.ImageWidth, layout.ItemWidth, layout.ItemHeight));
    }

    /// <summary>負の幅と左右余白しかない幅は、描画対象のカード寸法を返さない。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void CalculateCardLayout_RejectsWidthWithoutUsableArea(double availableWidth)
    {
        Assert.Null(PhotoGridItemsLayoutLogic.CalculateCardLayout(availableWidth));
    }

    /// <summary>
    /// スクロール終端の 600px 手前を追加読み込み境界として扱うことを確認する。
    ///
    /// GalleryPhotosState の loadMorePhotos はこの通知を受けて次ページを読む。
    /// 境界は「残りが 600px 未満」なので、ちょうど 600px 残っている場合はまだ発火しない。
    /// </summary>
    [Fact]
    public void IsNearBottom_UsesStrictSixHundredPixelThreshold()
    {
        Assert.False(PhotoGridItemsLayoutLogic.IsNearBottom(scrollableHeight: 3000, verticalOffset: 2400));
        Assert.True(PhotoGridItemsLayoutLogic.IsNearBottom(scrollableHeight: 3000, verticalOffset: 2401));
        Assert.True(PhotoGridItemsLayoutLogic.IsNearBottom(scrollableHeight: 3000, verticalOffset: 2999));
    }

    /// <summary>
    /// スクロール位置から行番号を求め、列数を掛けて先頭写真インデックスを概算することを確認する。
    ///
    /// ItemsWrapGrid の実列数が取得できる場合はその値を使い、未設定または 0 の場合は 6 列基準の
    /// フォールバックを使う。ItemHeight が 0 以下なら計算不能なので null を返す。
    /// </summary>
    [Fact]
    public void EstimateFirstVisibleIndex_UsesRowAndColumnCountWithFallback()
    {
        Assert.Equal(0, PhotoGridItemsLayoutLogic.EstimateFirstVisibleIndex(scrollTop: 0, itemHeight: 200, maximumRowsOrColumns: 5));
        Assert.Equal(10, PhotoGridItemsLayoutLogic.EstimateFirstVisibleIndex(scrollTop: 450, itemHeight: 200, maximumRowsOrColumns: 5));
        Assert.Equal(18, PhotoGridItemsLayoutLogic.EstimateFirstVisibleIndex(scrollTop: 650, itemHeight: 200, maximumRowsOrColumns: 0));
        Assert.Null(PhotoGridItemsLayoutLogic.EstimateFirstVisibleIndex(scrollTop: 100, itemHeight: 0, maximumRowsOrColumns: 5));
    }

    /// <summary>
    /// shimmer のカード幅が現在幅、実測幅、既定幅の優先順で決まり、ハイライトが 40% 幅になることを確認する。
    ///
    /// recycle 直後や初回表示では Image.ActualWidth がまだ 0 のことがある。
    /// その場合でも読み込み中プレースホルダが消えないよう、最終的に 300px を既定幅として使う。
    /// </summary>
    [Fact]
    public void ShimmerWidthHelpers_ChooseStableFallbacksAndFortyPercentHighlight()
    {
        Assert.Equal(180, PhotoGridItemsLayoutLogic.ResolveShimmerCardWidth(currentImageWidth: 180, actualWidth: 240));
        Assert.Equal(240, PhotoGridItemsLayoutLogic.ResolveShimmerCardWidth(currentImageWidth: 0, actualWidth: 240));
        Assert.Equal(300, PhotoGridItemsLayoutLogic.ResolveShimmerCardWidth(currentImageWidth: 0, actualWidth: 0));
        Assert.Equal(120, PhotoGridItemsLayoutLogic.CalculateShimmerHighlightWidth(300));
    }

    /// <summary>
    /// GridView 初期幅、スクロール対象インデックス、先頭表示インデックス通知の補助判定を確認する。
    ///
    /// ItemsWrapGrid の Loaded 時は GridView.ActualWidth がまだ 0 の場合があるため直近幅へフォールバックする。
    /// ScrollToItemIndex は範囲外の値を無視し、先頭表示インデックス通知は null または前回値と同じ場合は出さない。
    /// </summary>
    [Fact]
    public void ViewportSmallHelpers_ReturnStableWidthIndexAndNotificationDecisions()
    {
        Assert.Equal(900, PhotoGridItemsLayoutLogic.ResolveInitialGridWidth(actualWidth: 900, lastKnownWidth: 720));
        Assert.Equal(720, PhotoGridItemsLayoutLogic.ResolveInitialGridWidth(actualWidth: 0, lastKnownWidth: 720));

        Assert.True(PhotoGridItemsLayoutLogic.IsValidItemIndex(0, 3));
        Assert.True(PhotoGridItemsLayoutLogic.IsValidItemIndex(2, 3));
        Assert.False(PhotoGridItemsLayoutLogic.IsValidItemIndex(-1, 3));
        Assert.False(PhotoGridItemsLayoutLogic.IsValidItemIndex(3, 3));

        Assert.False(PhotoGridItemsLayoutLogic.ShouldNotifyFirstVisibleIndex(null, -1));
        Assert.False(PhotoGridItemsLayoutLogic.ShouldNotifyFirstVisibleIndex(10, 10));
        Assert.True(PhotoGridItemsLayoutLogic.ShouldNotifyFirstVisibleIndex(10, 5));
    }

    /// <summary>
    /// 画像ソース変更に関わるプロパティ名、画像ソースパス、デコード幅を確認する。
    ///
    /// ThumbImage_DataContextChanged の購読ハンドラは、GridThumbPath または EffectiveSourcePath の変更だけで
    /// shimmer を戻して再ロードする。
    /// 画像パスは PhotoThumbnailItem.EffectiveSourcePath を使い、空の場合は BitmapImage を作らない。
    /// </summary>
    [Fact]
    public void ImageSourceHelpers_ReturnOnlyRelevantPropertyAndUsableSourcePath()
    {
        var photo = new PhotoThumbnailItem { PhotoPath = "C:/photos/source.jpg" };
        var empty = new PhotoThumbnailItem();

        Assert.True(PhotoGridItemsLayoutLogic.IsImageSourceProperty(nameof(PhotoThumbnailItem.GridThumbPath)));
        Assert.True(PhotoGridItemsLayoutLogic.IsImageSourceProperty(nameof(PhotoThumbnailItem.EffectiveSourcePath)));
        Assert.False(PhotoGridItemsLayoutLogic.IsImageSourceProperty(nameof(PhotoThumbnailItem.IsSelected)));

        Assert.Equal(@"C:\photos\source.jpg", PhotoGridItemsLayoutLogic.ResolveImageSourcePath(photo));
        Assert.Null(PhotoGridItemsLayoutLogic.ResolveImageSourcePath(empty));
        Assert.True(PhotoGridItemsLayoutLogic.HasImageSource(PhotoGridItemsLayoutLogic.ResolveImageSourcePath(photo)));
        Assert.False(PhotoGridItemsLayoutLogic.HasImageSource(PhotoGridItemsLayoutLogic.ResolveImageSourcePath(empty)));
        Assert.Equal(300, PhotoGridItemsLayoutLogic.GridDecodePixelWidth);
    }

    /// <summary>
    /// 標準グリッド画像のロード要求が、表示ソースパスとデコード幅を返すことを確認する。
    ///
    /// PhotoThumbnailItem.EffectiveSourcePath は GridThumbPath を優先し、サムネイル未生成時は PhotoPath へ戻る。
    /// ソースがない写真では BitmapImage を生成しないため null を返す。
    /// デコード幅は標準グリッド用の固定値 300px を使う。
    /// </summary>
    [Fact]
    public void ImageRequest_ReturnsSourcePathAndDecodeWidthOnlyWhenSourceExists()
    {
        var withThumb = new PhotoThumbnailItem
        {
            PhotoPath = "C:/photos/source.jpg",
            GridThumbPath = "C:/thumbs/grid.jpg",
        };
        var withSourceOnly = new PhotoThumbnailItem { PhotoPath = "C:/photos/source.jpg" };
        var empty = new PhotoThumbnailItem();

        Assert.Equal(
            new GridImageRequest(@"C:\thumbs\grid.jpg", 300),
            PhotoGridItemsLayoutLogic.ImageRequest(withThumb));
        Assert.Equal(
            new GridImageRequest(@"C:\photos\source.jpg", 300),
            PhotoGridItemsLayoutLogic.ImageRequest(withSourceOnly));
        Assert.Null(PhotoGridItemsLayoutLogic.ImageRequest(empty));
    }

    /// <summary>
    /// shimmer アニメーションの幅と横移動範囲、表示中 shimmer の再計算対象を確認する。
    ///
    /// ハイライトはカード幅の 40% とし、開始位置はハイライト幅ぶん左、終了位置はカード右端にする。
    /// 幅変更時の再計算は ShimmerHighlight かつ表示中の Border だけに限定する。
    /// </summary>
    [Fact]
    public void ShimmerMetricsAndRefreshPredicate_ReturnExpectedAnimationValues()
    {
        Assert.Equal(new ShimmerAnimationMetrics(120, -120, 300),
            PhotoGridItemsLayoutLogic.CalculateShimmerMetrics(300));
        Assert.True(PhotoGridItemsLayoutLogic.ShouldRefreshShimmerWidth("ShimmerHighlight", 1));
        Assert.False(PhotoGridItemsLayoutLogic.ShouldRefreshShimmerWidth("ShimmerHighlight", 0));
        Assert.False(PhotoGridItemsLayoutLogic.ShouldRefreshShimmerWidth("ShimmerBase", 1));
    }

    /// <summary>
    /// カードの rest/hover 表示に使うテーマキーと移動量を確認する。
    ///
    /// recycle や PointerExited では通常の枠線・背景へ戻し、PointerEntered では強い枠線・hover 背景へ切り替える。
    /// hover 時の Y オフセットは -2px、通常時は 0px へ戻す。
    /// </summary>
    [Fact]
    public void CardVisual_ReturnsThemeKeysAndMotionForRestAndHoverStates()
    {
        Assert.Equal(
            new GridCardVisual("ABorder", "ASurface", 0, 180),
            PhotoGridItemsLayoutLogic.CardVisual(GridCardVisualState.Rest));
        Assert.Equal(
            new GridCardVisual("ABorderStrong", "ASurfaceHover", -2, 180),
            PhotoGridItemsLayoutLogic.CardVisual(GridCardVisualState.Hover));
    }

    /// <summary>
    /// 画像ロード状態ごとの shimmer、エラー表示、画像 opacity の扱いを確認する。
    ///
    /// Reset は recycle や再ロード開始時の状態で、画像を透明にして shimmer を開始し、エラー表示を隠す。
    /// Opened は shimmer を止めて画像を表示状態へ移す。
    /// Failed は shimmer を止めつつフォールバック面とエラーアイコンを表示する。
    /// </summary>
    [Fact]
    public void ImageLoadVisual_ReturnsResetOpenedAndFailedVisualStates()
    {
        Assert.Equal(
            new GridImageLoadVisual(
                ImageOpacity: 0,
                ShimmerBaseOpacity: 1,
                ShimmerHighlightOpacity: 1,
                ErrorVisible: false,
                StartShimmer: true,
                StopShimmer: false),
            PhotoGridItemsLayoutLogic.ImageLoadVisual(GridImageLoadState.Reset));

        Assert.Equal(
            new GridImageLoadVisual(
                ImageOpacity: 1,
                ShimmerBaseOpacity: 0,
                ShimmerHighlightOpacity: 0,
                ErrorVisible: false,
                StartShimmer: false,
                StopShimmer: true),
            PhotoGridItemsLayoutLogic.ImageLoadVisual(GridImageLoadState.Opened));

        Assert.Equal(
            new GridImageLoadVisual(
                ImageOpacity: 0,
                ShimmerBaseOpacity: 1,
                ShimmerHighlightOpacity: 0,
                ErrorVisible: true,
                StartShimmer: false,
                StopShimmer: true),
            PhotoGridItemsLayoutLogic.ImageLoadVisual(GridImageLoadState.Failed));
    }

    /// <summary>
    /// カードレイアウト計算結果を null でない値として取得する。
    /// テスト本体では寸法の意味を読みやすくするため、この補助で null チェックをまとめる。
    /// </summary>
    private static PhotoGridCardLayout GalleryLayout(double availableWidth)
        => Assert.IsType<PhotoGridCardLayout>(PhotoGridItemsLayoutLogic.CalculateCardLayout(availableWidth));
}
