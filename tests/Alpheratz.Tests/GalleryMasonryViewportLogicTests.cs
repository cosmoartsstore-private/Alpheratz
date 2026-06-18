using Alpheratz.Features.Gallery;
using Alpheratz.Features.Gallery.Controls;
using System.Collections.Specialized;

namespace Alpheratz.Tests;

/// <summary>
/// GalleryMasonryView の仮想化範囲計算を検証するテスト。
///
/// GalleryMasonryView 本体は ScrollViewer、Canvas、Image を生成する WinUI コントロールだが、
/// 「どのカードを作るか」「どのカードを解放するか」「月ナビへどのインデックスを通知するか」は
/// レイアウト座標とスクロール値だけで決まる。
/// ここでは UI 要素を起動せず、その数値判定を GalleryMasonryViewportLogic の純粋関数として固定する。
/// </summary>
public sealed class GalleryMasonryViewportLogicTests
{
    /// <summary>
    /// コントロール幅と指定カラム数から、内部幅と実効カラム数が計算されることを確認する。
    ///
    /// GalleryMasonryView は ActualWidth がまだ 0 の初期化直後に ScrollViewer 幅へフォールバックする。
    /// またユーザー指定カラム数が画面幅に対して多すぎる場合は、自動計算された最大数まで絞る。
    /// </summary>
    [Fact]
    public void ComputeColumns_UsesActualWidthFallbackAndClampsRequestedColumns()
    {
        var fromActualWidth = GalleryMasonryViewportLogic.ComputeColumns(
            actualWidth: 1028,
            scrollHostWidth: 640,
            requestedColumnCount: 10);
        var fromScrollHost = GalleryMasonryViewportLogic.ComputeColumns(
            actualWidth: 0,
            scrollHostWidth: 680,
            requestedColumnCount: null);
        var narrow = GalleryMasonryViewportLogic.ComputeColumns(
            actualWidth: 20,
            scrollHostWidth: 0,
            requestedColumnCount: 0);

        Assert.Equal(1000, fromActualWidth.InnerWidth);
        Assert.Equal(3, fromActualWidth.EffectiveColumns);
        Assert.Equal(652, fromScrollHost.InnerWidth);
        Assert.Equal(1, fromScrollHost.EffectiveColumns);
        Assert.Equal(0, narrow.InnerWidth);
        Assert.Equal(1, narrow.EffectiveColumns);
    }

    /// <summary>
    /// カラム数指定とコレクション変更の小さな分岐を確認する。
    ///
    /// カラム数は 1 以上だけを明示指定として扱い、0 以下では自動計算に戻す。
    /// 写真コレクションの Add は既存カードを使い回せるため座標再計算だけにし、
    /// Reset や Remove はカード対応が崩れるので全再構築へ回す。
    /// </summary>
    [Fact]
    public void ResolveRequestedColumnCountAndCollectionChangeRules_ReturnExpectedBranches()
    {
        Assert.Null(GalleryMasonryViewportLogic.ResolveRequestedColumnCount(0));
        Assert.Null(GalleryMasonryViewportLogic.ResolveRequestedColumnCount(-2));
        Assert.Equal(3, GalleryMasonryViewportLogic.ResolveRequestedColumnCount(3));

        Assert.True(GalleryMasonryViewportLogic.ShouldReuseCardsForCollectionChange(NotifyCollectionChangedAction.Add));
        Assert.False(GalleryMasonryViewportLogic.ShouldReuseCardsForCollectionChange(NotifyCollectionChangedAction.Reset));
        Assert.False(GalleryMasonryViewportLogic.ShouldReuseCardsForCollectionChange(NotifyCollectionChangedAction.Remove));
    }

    /// <summary>
    /// Masonry レイアウト結果から Canvas の実寸が計算されることを確認する。
    ///
    /// Canvas 幅は列幅の合計に列間 gap を足した値で、縦は MasonryLayout が返す TotalHeight を使う。
    /// Rebuild と RebuildLayout は同じ計算を使うため、helper 側で固定する。
    /// </summary>
    [Fact]
    public void CanvasSize_UsesColumnCountWidthGapAndTotalHeight()
    {
        var layout = new MasonryLayoutResult(
            Items: [],
            TotalHeight: 980,
            ColumnWidth: 320,
            ColumnCount: 3,
            Gap: 14);

        Assert.Equal(new MasonryCanvasSize(988, 980), GalleryMasonryViewportLogic.CanvasSize(layout));
    }

    /// <summary>
    /// MasonryItem の Top 値に基づいて、元配列のインデックスが昇順へ並ぶことを確認する。
    ///
    /// 仮想化ではレイアウト配列そのものを並べ替えると写真インデックスとの対応が壊れるため、
    /// ソート済みの「インデックス配列」だけを持つ。
    /// </summary>
    [Fact]
    public void BuildSortedIndex_ReturnsOriginalIndexesOrderedByTop()
    {
        var items = Items(
            (top: 300, height: 100),
            (top: 0, height: 100),
            (top: 150, height: 100));

        var sorted = GalleryMasonryViewportLogic.BuildSortedIndex(items);

        Assert.Equal([1, 2, 0], sorted);
    }

    /// <summary>
    /// 現在のビューポートと overscan 範囲から、生成・維持対象のカードだけが返ることを確認する。
    ///
    /// 探索開始位置は loadTop から最大カード高を引いた場所にし、
    /// 高いカードが途中から見えるケースも拾えるようにしている。
    /// 一方で loadBottom を超えたカードは、それ以降の Top 昇順アイテムも範囲外なので走査を止める。
    /// </summary>
    [Fact]
    public void FindVisibleIndices_ReturnsCardsIntersectingViewportAndOverscan()
    {
        var items = Items(
            (top: 0, height: 100),
            (top: 220, height: 100),
            (top: 480, height: 260),
            (top: 900, height: 100));
        var sorted = GalleryMasonryViewportLogic.BuildSortedIndex(items);

        var visible = GalleryMasonryViewportLogic.FindVisibleIndices(
            items,
            sorted,
            scrollTop: 300,
            viewportHeight: 200,
            overscanPx: 80,
            maxCardHeight: 900);

        Assert.Equal([1, 2], visible);
    }

    /// <summary>
    /// release margin の外に出たカードだけが即時解放対象になることを確認する。
    ///
    /// overscan 外だが release margin 内にいるカードは、スクロール反転時の再生成を避けるため
    /// すぐには Canvas から外さず、遅延解放タイマーへ回す。
    /// この境界を数値で固定し、早すぎる解放によるチラつきを防ぐ。
    /// </summary>
    [Fact]
    public void IsOutsideReleaseRange_DistinguishesImmediateReleaseFromDelayedReleaseArea()
    {
        var above = Item(top: 50, height: 40);
        var nearAbove = Item(top: 150, height: 40);
        var inside = Item(top: 350, height: 80);
        var below = Item(top: 820, height: 100);

        Assert.True(GalleryMasonryViewportLogic.IsOutsideReleaseRange(above, scrollTop: 300, viewportHeight: 200, releaseMarginPx: 150));
        Assert.False(GalleryMasonryViewportLogic.IsOutsideReleaseRange(nearAbove, scrollTop: 300, viewportHeight: 200, releaseMarginPx: 150));
        Assert.False(GalleryMasonryViewportLogic.IsOutsideReleaseRange(inside, scrollTop: 300, viewportHeight: 200, releaseMarginPx: 150));
        Assert.True(GalleryMasonryViewportLogic.IsOutsideReleaseRange(below, scrollTop: 300, viewportHeight: 200, releaseMarginPx: 150));
    }

    /// <summary>
    /// サムネイル生成要求へ積む条件を確認する。
    ///
    /// GalleryMasonryView はまず requestedThumbs に写真パスを登録し、その登録が新規だった場合だけ
    /// GridThumbPath と PhotoPath を見て生成キューへ積む。
    /// 既にリクエスト済み、サムネイル生成済み、写真パス空のケースでは再要求しない。
    /// </summary>
    [Fact]
    public void ShouldQueueThumbnailAfterRequestRegistered_ReturnsTrueOnlyForNewMissingGridThumb()
    {
        Assert.True(GalleryMasonryViewportLogic.ShouldQueueThumbnailAfterRequestRegistered(true, "C:/photo.jpg", null));
        Assert.True(GalleryMasonryViewportLogic.ShouldQueueThumbnailAfterRequestRegistered(true, "C:/photo.jpg", ""));
        Assert.False(GalleryMasonryViewportLogic.ShouldQueueThumbnailAfterRequestRegistered(false, "C:/photo.jpg", null));
        Assert.False(GalleryMasonryViewportLogic.ShouldQueueThumbnailAfterRequestRegistered(true, "C:/photo.jpg", "C:/thumb.jpg"));
        Assert.False(GalleryMasonryViewportLogic.ShouldQueueThumbnailAfterRequestRegistered(true, "", null));
    }

    /// <summary>
    /// スクロール位置から月ナビゲーションへ通知する先頭写真インデックスが選ばれることを確認する。
    ///
    /// スクロール位置以上の Top を持つ最初のカードを返し、末尾より下までスクロールされた場合は最後のカードを返す。
    /// 空レイアウトでは通知しないため null になる。
    /// </summary>
    [Fact]
    public void FindFirstVisibleIndex_ReturnsNextCardAtOrAfterScrollTopOrLastCard()
    {
        var items = Items(
            (top: 0, height: 100),
            (top: 240, height: 100),
            (top: 120, height: 100));
        var sorted = GalleryMasonryViewportLogic.BuildSortedIndex(items);

        Assert.Equal(2, GalleryMasonryViewportLogic.FindFirstVisibleIndex(items, sorted, scrollTop: 100));
        Assert.Equal(1, GalleryMasonryViewportLogic.FindFirstVisibleIndex(items, sorted, scrollTop: 500));
        Assert.Null(GalleryMasonryViewportLogic.FindFirstVisibleIndex([], [], scrollTop: 0));
    }

    /// <summary>
    /// 月ナビ通知、画像デコード幅、画像ソース変更に関する補助判定を確認する。
    ///
    /// 先頭表示インデックスは null や前回値と同じ場合は通知しない。
    /// 画像デコード幅は 1 未満に落とさず、PropertyChanged では画像ソースに関わるプロパティだけを処理する。
    /// </summary>
    [Fact]
    public void ImageAndFirstVisibleHelpers_ReturnExpectedSmallDecisions()
    {
        Assert.False(GalleryMasonryViewportLogic.ShouldNotifyFirstVisibleIndex(null, -1));
        Assert.False(GalleryMasonryViewportLogic.ShouldNotifyFirstVisibleIndex(4, 4));
        Assert.True(GalleryMasonryViewportLogic.ShouldNotifyFirstVisibleIndex(4, 3));

        Assert.Equal(1, GalleryMasonryViewportLogic.DecodePixelWidth(0));
        Assert.Equal(1, GalleryMasonryViewportLogic.DecodePixelWidth(0.9));
        Assert.Equal(240, GalleryMasonryViewportLogic.DecodePixelWidth(240.8));

        Assert.True(GalleryMasonryViewportLogic.IsImageSourceProperty(nameof(PhotoThumbnailItem.EffectiveSourcePath)));
        Assert.True(GalleryMasonryViewportLogic.IsImageSourceProperty(nameof(PhotoThumbnailItem.GridThumbPath)));
        Assert.True(GalleryMasonryViewportLogic.IsImageSourceProperty(nameof(PhotoThumbnailItem.ResolvedPhotoPath)));
        Assert.False(GalleryMasonryViewportLogic.IsImageSourceProperty(nameof(PhotoThumbnailItem.IsSelected)));

        Assert.True(GalleryMasonryViewportLogic.ShouldReloadImage("C:/new.jpg", "C:/old.jpg"));
        Assert.False(GalleryMasonryViewportLogic.ShouldReloadImage("C:/same.jpg", "C:/same.jpg"));

        Assert.True(GalleryMasonryViewportLogic.ShouldRetryImageLoad(1, isInsideOverscan: true));
        Assert.True(GalleryMasonryViewportLogic.ShouldRetryImageLoad(GalleryMasonryViewportLogic.MaxImageLoadRetries, isInsideOverscan: true));
        Assert.False(GalleryMasonryViewportLogic.ShouldRetryImageLoad(0, isInsideOverscan: true));
        Assert.False(GalleryMasonryViewportLogic.ShouldRetryImageLoad(GalleryMasonryViewportLogic.MaxImageLoadRetries + 1, isInsideOverscan: true));
        Assert.False(GalleryMasonryViewportLogic.ShouldRetryImageLoad(1, isInsideOverscan: false));

        Assert.True(GalleryMasonryViewportLogic.IsCurrentImageLoadCallback(
            callbackVersion: 4,
            currentVersion: 4,
            callbackPath: "C:/cache/thumb.jpg",
            loadingPath: "c:/cache/thumb.jpg"));
        Assert.False(GalleryMasonryViewportLogic.IsCurrentImageLoadCallback(
            callbackVersion: 3,
            currentVersion: 4,
            callbackPath: "C:/cache/thumb.jpg",
            loadingPath: "C:/cache/thumb.jpg"));
        Assert.False(GalleryMasonryViewportLogic.IsCurrentImageLoadCallback(
            callbackVersion: 4,
            currentVersion: 4,
            callbackPath: "C:/old.jpg",
            loadingPath: "C:/cache/thumb.jpg"));
    }

    /// <summary>
    /// Masonry カード画像のロード要求が、表示ソースパスとデコード幅を返すことを確認する。
    ///
    /// PhotoThumbnailItem.EffectiveSourcePath は GridThumbPath を優先し、空なら PhotoPath へフォールバックする。
    /// ソースがない写真では BitmapImage を作らないため null を返す。
    /// デコード幅はカード幅から 1 以上の整数に丸める。
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
            new MasonryImageRequest(@"C:\thumbs\grid.jpg", 320),
            GalleryMasonryViewportLogic.ImageRequest(withThumb, 320.8));
        Assert.Equal(
            new MasonryImageRequest(@"C:\photos\source.jpg", 1),
            GalleryMasonryViewportLogic.ImageRequest(withSourceOnly, 0));
        Assert.Null(GalleryMasonryViewportLogic.ImageRequest(empty, 320));
    }

    /// <summary>
    /// shimmer ハイライトがカード幅の 40% になり、左外から右端まで移動することを確認する。
    ///
    /// GalleryMasonryView は読み込み前プレースホルダに横流れのハイライトを出す。
    /// 幅と開始/終了オフセットを helper で固定し、UI 側は Composition animation に値を渡すだけにする。
    /// </summary>
    [Fact]
    public void ShimmerMetrics_ReturnHighlightWidthOffsetsAndDuration()
    {
        Assert.Equal(
            new MasonryShimmerMetrics(
                HighlightWidth: 128,
                StartOffset: -128,
                EndOffset: 320,
                DurationMilliseconds: GalleryMasonryViewportLogic.ShimmerDurationMilliseconds),
            GalleryMasonryViewportLogic.ShimmerMetrics(320));
    }

    /// <summary>
    /// 選択状態から、選択リングとチェックバッジの表示状態が決まることを確認する。
    ///
    /// Masonry カードは PhotoThumbnailItem.IsSelected の変化に合わせてリングとバッジを同時に切り替える。
    /// 表示可否だけを helper で扱い、Visibility 変換は WinUI 側に残す。
    /// </summary>
    [Fact]
    public void SelectionVisual_ReturnsVisibleFlagFromSelectionState()
    {
        Assert.Equal(new SelectionVisual(true), GalleryMasonryViewportLogic.SelectionVisual(true));
        Assert.Equal(new SelectionVisual(false), GalleryMasonryViewportLogic.SelectionVisual(false));
    }

    /// <summary>
    /// PhotoThumbnailItem の変更通知から、Masonry カードが行う処理種別が決まることを確認する。
    ///
    /// IsSelected / IsFavorite は画像ロードとは無関係に各バッジ表示だけを更新する。
    /// 画像ソースに関わらないプロパティや同じパスへの変更は何もしない。
    /// 画像パスが変わった場合は、ロード済みカードまたは overscan 範囲内の未ロードカードだけを即時ロードする。
    /// </summary>
    [Fact]
    public void PhotoChangeAction_ReturnsSelectionReloadOrNoop()
    {
        Assert.Equal(
            MasonryPhotoChangeAction.UpdateSelection,
            GalleryMasonryViewportLogic.PhotoChangeAction(
                nameof(PhotoThumbnailItem.IsSelected),
                "C:/new.jpg",
                "C:/old.jpg",
                isLoaded: false,
                isInsideOverscan: false));
        Assert.Equal(
            MasonryPhotoChangeAction.UpdateFavorite,
            GalleryMasonryViewportLogic.PhotoChangeAction(
                nameof(PhotoThumbnailItem.IsFavorite),
                "C:/new.jpg",
                "C:/old.jpg",
                isLoaded: false,
                isInsideOverscan: false));
        Assert.Equal(
            MasonryPhotoChangeAction.None,
            GalleryMasonryViewportLogic.PhotoChangeAction(
                nameof(PhotoThumbnailItem.WorldName),
                "C:/new.jpg",
                "C:/old.jpg",
                isLoaded: true,
                isInsideOverscan: true));
        Assert.Equal(
            MasonryPhotoChangeAction.None,
            GalleryMasonryViewportLogic.PhotoChangeAction(
                nameof(PhotoThumbnailItem.GridThumbPath),
                "C:/same.jpg",
                "C:/same.jpg",
                isLoaded: true,
                isInsideOverscan: true));
        Assert.Equal(
            MasonryPhotoChangeAction.ReloadNow,
            GalleryMasonryViewportLogic.PhotoChangeAction(
                nameof(PhotoThumbnailItem.GridThumbPath),
                "C:/new.jpg",
                "C:/old.jpg",
                isLoaded: true,
                isInsideOverscan: false));
        Assert.Equal(
            MasonryPhotoChangeAction.ReloadNow,
            GalleryMasonryViewportLogic.PhotoChangeAction(
                nameof(PhotoThumbnailItem.GridThumbPath),
                "C:/new.jpg",
                "C:/old.jpg",
                isLoaded: false,
                isInsideOverscan: true));
        Assert.Equal(
            MasonryPhotoChangeAction.None,
            GalleryMasonryViewportLogic.PhotoChangeAction(
                nameof(PhotoThumbnailItem.GridThumbPath),
                "C:/new.jpg",
                "C:/old.jpg",
                isLoaded: false,
                isInsideOverscan: false));
    }

    /// <summary>
    /// 未ロードカードが overscan 範囲内にある場合だけ遅延ロード対象になることを確認する。
    ///
    /// PropertyChanged でサムネイルパスが後から届いたとき、表示範囲近くのカードだけ LoadImage する。
    /// 遠方カードを即ロードしないことで、仮想化時のメモリ使用量を抑える。
    /// </summary>
    [Fact]
    public void IsInsideOverscan_ReturnsTrueOnlyWhenCardIntersectsOverscannedViewport()
    {
        Assert.True(GalleryMasonryViewportLogic.IsInsideOverscan(
            cardTop: 150,
            cardHeight: 50,
            scrollTop: 300,
            viewportHeight: 200,
            overscanPx: 150));
        Assert.True(GalleryMasonryViewportLogic.IsInsideOverscan(
            cardTop: 650,
            cardHeight: 80,
            scrollTop: 300,
            viewportHeight: 200,
            overscanPx: 150));
        Assert.False(GalleryMasonryViewportLogic.IsInsideOverscan(
            cardTop: 20,
            cardHeight: 40,
            scrollTop: 300,
            viewportHeight: 200,
            overscanPx: 150));
        Assert.False(GalleryMasonryViewportLogic.IsInsideOverscan(
            cardTop: 740,
            cardHeight: 50,
            scrollTop: 300,
            viewportHeight: 200,
            overscanPx: 150));
    }

    /// <summary>
    /// 複数の MasonryItem を短く作る。
    /// PhotoThumbnailItem は仮想化計算では参照されないが、MasonryItem の必須値なので最小構成で用意する。
    /// </summary>
    private static IReadOnlyList<MasonryItem> Items(params (double top, double height)[] specs)
        => specs.Select(spec => Item(spec.top, spec.height)).ToList();

    /// <summary>
    /// 1つの MasonryItem を作る。
    /// Left と Width は今回の仮想化判定に影響しないため、固定値を使う。
    /// </summary>
    private static MasonryItem Item(double top, double height)
        => new(new PhotoThumbnailItem(), top, 0, 320, height);
}
