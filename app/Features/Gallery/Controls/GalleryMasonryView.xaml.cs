using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Numerics;
using Alpheratz.Core;
using Alpheratz.Shared.Controls;
using Alpheratz.Shared.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Alpheratz.Features.Gallery.Controls;

/// <summary>
/// Canvas ベースの手動仮想化マソンリーレイアウト。
///
/// WinUI 3 標準の ItemsRepeater は 27,000 枚超で OOM (Visual Tree が肥大しすぎ) になる
/// ため、自前で「ビューポート付近のカードだけ実体化し、遠方のカードは破棄する」仮想化を実装。
///
/// 内部は 3 層に分かれる：
///   1. レイアウト計算 (GalleryMasonryLayout) — 各写真の (top, left, width, height) を算出
///   2. ビューポート判定 (UpdateVisibility) — 二分探索で可視範囲のレイアウトインデックスを抽出
///   3. カード実体管理 (activeCards) — 入場・退場をスクロール量に追従させて適用
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class GalleryMasonryView : UserControl
{
    /// <summary>
    /// ビューポート外のこの範囲まで先読みしてカードを生成する。
    /// 400px = 平均カード約 1.5 段分。スクロール中にすぐ見える距離をプリロードすることで
    /// 「真っ白なまま画像が遅延してくる」体験を抑える。大きくしすぎるとメモリ消費が増える。
    /// </summary>
    private const double OverscanPx = 400;

    /// <summary>
    /// この範囲を超えたカードを破棄する。OverscanPx より十分大きく (3 倍 = 1200px) することで、
    /// 戻り方向の小スクロールでも前段で破棄したカードを再生成しなくて済み、チラつきを防ぐ。
    /// </summary>
    private const double ReleaseMarginPx = 1200;

    /// <summary>
    /// OverscanPx～ReleaseMarginPx 間のカードは「準退場」扱いで、この時間だけ破棄を遅延する。
    /// スクロール反転の頻度が高い操作で、即破棄＋即再生成のバタつきを抑えるためのヒステリシス。
    /// </summary>
    private const int ReleaseDelayMs = 250;

    private UiObservableCollection<PhotoThumbnailItem>? photos;
    private int? requestedColumnCount;

    private MasonryLayoutResult? currentLayout;
    private List<PhotoThumbnailItem>? layoutPhotos;

    /// <summary>実体化済みカードの管理情報。Index はレイアウト配列上の位置。</summary>
    private sealed class CardEntry
    {
        public required int Index;
        public required Border Container;
        public required Image Image;
        public required PhotoThumbnailItem Photo;
        public Border? SelectionTint;
        public Border? SelectionGlow;
        public Border? SelectionRing;
        public FavoriteCornerBadge? FavoriteBadge;
        public TextBlock? ErrorIcon;
        public Grid? MediaHost;
        public Border? SkeletonPlaceholder;
        public BitmapImage? CurrentBitmap;
        public string LoadedPath = string.Empty;
        public string LoadingPath = string.Empty;
        public string FailedPath = string.Empty;
        public int LoadVersion;
        public int LoadFailureCount;
        public bool IsLoaded;
        public bool IsLoading;
        public BitmapImage? CallbackBitmap;
        public RoutedEventHandler? BitmapOpenedHandler;
        public ExceptionRoutedEventHandler? BitmapFailedHandler;
        public DispatcherQueueTimer? PendingReleaseTimer;
        public Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object>? PendingReleaseTickHandler;
        public DispatcherQueueTimer? PendingRetryTimer;
        public Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object>? PendingRetryTickHandler;
        public System.ComponentModel.PropertyChangedEventHandler? PhotoSubscription;
        // 画像ロードの状態変化を、カード生成時に作ったローカル UI 要素へ反映する。
        public Action? PrepareImageLoad;
        public Action? ShowImageOpened;
        public Action? ShowImageError;
        public Action? StopSkeletonPulse;
    }

    /// <summary>現在 Canvas 上に実体化されているカード（キーはレイアウトインデックス）。</summary>
    private readonly Dictionary<int, CardEntry> activeCards = [];

    /// <summary>サムネイル生成をリクエスト済みのパスセット。同一パスの重複リクエストを防ぐ。</summary>
    private readonly HashSet<string> requestedThumbs = [];
    private readonly DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    private bool updateVisibilityPending;
    private int[]? sortedByTopIndices;
    private readonly HashSet<int> visibleSet = [];
    private readonly List<PhotoThumbnailItem> thumbsNeededBuf = [];
    private readonly List<int> toRemoveBuf = [];
    private bool isControlLoaded;
    private bool isPhotosSubscribed;
    private bool isThemeSubscribed;
    private int lifecycleVersion;

    /// <summary>カードタップ時のコールバック。GalleryPage が PhotoModal への遷移を設定する。</summary>
    public Action<PhotoThumbnailItem>? OnPhotoTapped { get; set; }

    /// <summary>お気に入りコーナーバッジのクリックを親へ通知する。</summary>
    public Action<PhotoThumbnailItem>? OnFavoriteClicked { get; set; }

    /// <summary>ビューポート内にサムネイル未生成の写真があるとき呼ばれるコールバック。</summary>
    public Action<IReadOnlyList<PhotoThumbnailItem>>? OnThumbnailsNeeded { get; set; }

    public Action<int>? OnFirstVisibleIndexChanged { get; set; }
    private int lastReportedFirstVisible = -1;

    // 手動仮想化 Canvas を初期化し、サイズ・テーマ変更時の再構築を接続する。
    public GalleryMasonryView()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryMasonryView.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        SizeChanged += Control_SizeChanged;
        Loaded += Control_Loaded;
        Unloaded += Control_Unloaded;
    }

    // 表示ツリーに接続されている間だけ、外部コレクションとテーマ変更を購読する。
    private void Control_Loaded(object sender, RoutedEventArgs e)
    {
        if (isControlLoaded) return;
        isControlLoaded = true;
        lifecycleVersion++;
        SubscribeToPhotos();
        if (!isThemeSubscribed)
        {
            ActualThemeChanged += OnActualThemeChanged;
            isThemeSubscribed = true;
        }
        Rebuild();
    }

    // アンロード後に画像イベントやタイマーがカードを保持し続けないよう、全資源を解放する。
    private void Control_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!isControlLoaded) return;
        isControlLoaded = false;
        lifecycleVersion++;
        updateVisibilityPending = false;
        UnsubscribeFromPhotos();
        if (isThemeSubscribed)
        {
            ActualThemeChanged -= OnActualThemeChanged;
            isThemeSubscribed = false;
        }
        requestedThumbs.Clear();
        ClearActiveCards();
    }

    private void Control_SizeChanged(object sender, SizeChangedEventArgs e) => Rebuild();

    private void SubscribeToPhotos()
    {
        if (photos is null || isPhotosSubscribed) return;
        photos.CollectionChanged += OnPhotosChanged;
        isPhotosSubscribed = true;
    }

    private void UnsubscribeFromPhotos()
    {
        if (photos is null || !isPhotosSubscribed) return;
        photos.CollectionChanged -= OnPhotosChanged;
        isPhotosSubscribed = false;
    }

    // テーマ切替時に、既に実体化済みのカード色を現在テーマへ塗り直す。
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        try
        {
            foreach (var entry in activeCards.Values)
            {
                if (ThemeHelper.Brush(entry.Container, "ASurface") is { } bg)
                    entry.Container.Background = bg;
                if (ThemeHelper.Brush(entry.Container, "ABorder") is { } br)
                    entry.Container.BorderBrush = br;
                if (entry.MediaHost is not null && ThemeHelper.Brush(entry.Container, "ASurfaceSoft") is { } mediaBg)
                    entry.MediaHost.Background = mediaBg;
                if (entry.SkeletonPlaceholder is not null && ThemeHelper.Brush(entry.Container, "ASurfaceSofter") is { } skeletonBg)
                    entry.SkeletonPlaceholder.Background = skeletonBg;
                if (entry.SelectionTint is not null && ThemeHelper.Brush(entry.Container, "APhotoSelectionTint") is { } tint)
                    entry.SelectionTint.Background = tint;
                if (entry.SelectionGlow is not null && ThemeHelper.Brush(entry.Container, "APhotoSelectionGlow") is { } glow)
                    entry.SelectionGlow.BorderBrush = glow;
                if (entry.SelectionRing is not null && ThemeHelper.Brush(entry.Container, "APhotoSelectionRing") is { } primary)
                    entry.SelectionRing.BorderBrush = primary;
                if (entry.ErrorIcon is not null && ThemeHelper.Brush(entry.Container, "ATextDisabled") is { } errorBrush)
                    entry.ErrorIcon.Foreground = errorBrush;
            }
        }
        catch (Exception ex) { AppLogger.Error($"GalleryMasonryView.OnActualThemeChanged: {ex}"); }
    }

    /// <summary>写真コレクションをバインドし、CollectionChanged を購読する。</summary>
    public void SetPhotos(UiObservableCollection<PhotoThumbnailItem> next)
    {
        UnsubscribeFromPhotos();
        photos = next;
        if (isControlLoaded)
            SubscribeToPhotos();
        requestedThumbs.Clear();
        Rebuild();
    }

    /// <summary>表示カラム数を外部から指定する。0 以下で自動計算に戻る。</summary>
    public void SetColumnCount(int count)
    {
        var next = GalleryMasonryViewportLogic.ResolveRequestedColumnCount(count);
        if (requestedColumnCount == next) return;
        requestedColumnCount = next;
        Rebuild();
    }

    // マソンリーのスクロール位置を先頭へ戻す。
    public void ScrollToTop()
    {
        try { ScrollHost.ChangeView(null, 0, null); }
        catch (Exception ex) { AppLogger.Error($"GalleryMasonryView.ScrollToTop: threw: {ex}"); }
    }

    /// <summary>指定インデックスの写真までスクロールする（月ナビゲーション用）。</summary>
    public void ScrollToPhotoIndex(int index)
    {
        try
        {
            if (currentLayout is null || index < 0 || index >= currentLayout.Items.Count) return;
            var top = currentLayout.Items[index].Top;
            ScrollHost.ChangeView(null, top, null);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryMasonryView.ScrollToPhotoIndex: threw: {ex}");
        }
    }

    /// <summary>
    /// コレクション変更時のハンドラ。
    /// Add（loadMorePhotos からの逐次追加）はレイアウトだけ再計算してカードを再利用。
    /// Reset（フィルタ変更等の全差し替え）は全カード破棄して再構築。
    /// </summary>
    private void OnPhotosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (GalleryMasonryViewportLogic.ShouldReuseCardsForCollectionChange(e.Action))
        {
            RebuildLayout();
            return;
        }
        requestedThumbs.Clear();
        Rebuild();
    }

    // スクロールホストのサイズ変更ではレイアウトを作り直す。
    private void ScrollHost_SizeChanged(object sender, SizeChangedEventArgs e) => Rebuild();
    // スクロール位置の変更では可視カードだけを更新する。
    private void ScrollHost_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => RequestUpdateVisibility();

    // 連続スクロール中の可視更新を DispatcherQueue 上で 1 回にまとめる。
    private void RequestUpdateVisibility()
    {
        if (!isControlLoaded || updateVisibilityPending) return;
        updateVisibilityPending = true;
        var requestedLifecycleVersion = lifecycleVersion;
        if (!dispatcherQueue.TryEnqueue(() =>
        {
            updateVisibilityPending = false;
            if (!isControlLoaded || requestedLifecycleVersion != lifecycleVersion)
                return;
            UpdateVisibility();
        }))
        {
            updateVisibilityPending = false;
        }
    }

    /// <summary>利用可能な幅から内部幅と実効カラム数を算出する。</summary>
    private (double inner, int effectiveCols) ComputeColumns()
        => GalleryMasonryViewportLogic.ComputeColumns(this.ActualWidth, ScrollHost.ActualWidth, requestedColumnCount);

    private static void StopReleaseTimer(CardEntry entry)
    {
        var timer = entry.PendingReleaseTimer;
        if (timer is not null)
        {
            if (entry.PendingReleaseTickHandler is not null)
                timer.Tick -= entry.PendingReleaseTickHandler;
            timer.Stop();
        }
        entry.PendingReleaseTimer = null;
        entry.PendingReleaseTickHandler = null;
    }

    private static void StopRetryTimer(CardEntry entry)
    {
        var timer = entry.PendingRetryTimer;
        if (timer is not null)
        {
            if (entry.PendingRetryTickHandler is not null)
                timer.Tick -= entry.PendingRetryTickHandler;
            timer.Stop();
        }
        entry.PendingRetryTimer = null;
        entry.PendingRetryTickHandler = null;
    }

    // 画像ソースと、その完了通知を待つイベント購読を同じ世代として破棄する。
    private static void ReleaseImage(CardEntry entry)
    {
        StopRetryTimer(entry);
        DetachBitmapHandlers(entry);
        entry.LoadVersion++;
        entry.Image.Source = null;
        entry.CurrentBitmap = null;
        entry.LoadedPath = string.Empty;
        entry.LoadingPath = string.Empty;
        entry.IsLoaded = false;
        entry.IsLoading = false;
        entry.StopSkeletonPulse?.Invoke();
    }

    private static void ReleaseCard(CardEntry entry)
    {
        StopReleaseTimer(entry);
        ReleaseImage(entry);
        if (entry.PhotoSubscription is not null)
        {
            entry.Photo.PropertyChanged -= entry.PhotoSubscription;
            entry.PhotoSubscription = null;
        }
    }

    private void ClearActiveCards()
    {
        foreach (var entry in activeCards.Values)
            ReleaseCard(entry);
        activeCards.Clear();
        MasonryCanvas.Children.Clear();
        visibleSet.Clear();
        thumbsNeededBuf.Clear();
        toRemoveBuf.Clear();
    }

    /// <summary>全カードを破棄してレイアウトをゼロから再構築する。</summary>
    private void Rebuild()
    {
        try
        {
            if (!isControlLoaded) return;
            ClearActiveCards();

            var (inner, effectiveCols) = ComputeColumns();

            if (inner <= 0 || photos is null)
            {
                currentLayout = null;
                layoutPhotos = null;
                sortedByTopIndices = null;
                MasonryCanvas.Width = 0;
                MasonryCanvas.Height = 0;
                return;
            }

            layoutPhotos = new List<PhotoThumbnailItem>(photos);
            currentLayout = GalleryMasonryLayout.Build(layoutPhotos, inner, effectiveCols);

            var canvas = GalleryMasonryViewportLogic.CanvasSize(currentLayout);
            MasonryCanvas.Width = canvas.Width;
            MasonryCanvas.Height = canvas.Height;

            BuildSortedIndex();
            var requestedLifecycleVersion = lifecycleVersion;
            dispatcherQueue.TryEnqueue(() =>
            {
                if (isControlLoaded && requestedLifecycleVersion == lifecycleVersion)
                    UpdateVisibility();
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryMasonryView.Rebuild: threw: {ex}");
        }
    }

    /// <summary>
    /// 既存カードを残したままレイアウト座標だけ再計算する。
    /// Add イベント（逐次ロード）時に全カード破棄を避けるための軽量パス。
    /// Canvas の幅・高さを更新し、次の UpdateVisibility で新カードを生成する。
    /// </summary>
    private void RebuildLayout()
    {
        try
        {
            if (!isControlLoaded || photos is null) return;
            var (inner, effectiveCols) = ComputeColumns();
            if (inner <= 0) return;

            layoutPhotos = new List<PhotoThumbnailItem>(photos);
            currentLayout = GalleryMasonryLayout.Build(layoutPhotos, inner, effectiveCols);

            var canvas = GalleryMasonryViewportLogic.CanvasSize(currentLayout);
            MasonryCanvas.Width = canvas.Width;
            MasonryCanvas.Height = canvas.Height;

            BuildSortedIndex();
            var requestedLifecycleVersion = lifecycleVersion;
            dispatcherQueue.TryEnqueue(() =>
            {
                if (isControlLoaded && requestedLifecycleVersion == lifecycleVersion)
                    UpdateVisibility();
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryMasonryView.RebuildLayout: threw: {ex}");
        }
    }

    // ビューポート検索を速くするため、カードを Top 昇順のインデックス配列にする。
    private void BuildSortedIndex()
    {
        if (currentLayout is null || currentLayout.Items.Count == 0)
        {
            sortedByTopIndices = null;
            return;
        }
        sortedByTopIndices = GalleryMasonryViewportLogic.BuildSortedIndex(currentLayout.Items);
    }

    /// <summary>
    /// スクロール位置に基づいてカードの生成・画像ロード・解放を管理する。
    /// ソート済みインデックスを二分探索してビューポート付近のアイテムだけを走査する。
    /// </summary>
    private void UpdateVisibility()
    {
        try
        {
            if (!isControlLoaded
                || currentLayout is null
                || layoutPhotos is null
                || sortedByTopIndices is null
                || currentLayout.Items.Count == 0)
                return;

            var top = ScrollHost.VerticalOffset;

            visibleSet.Clear();
            thumbsNeededBuf.Clear();

            var items = currentLayout.Items;
            var sorted = sortedByTopIndices;
            var visibleIndices = GalleryMasonryViewportLogic.FindVisibleIndices(
                items,
                sorted,
                top,
                ScrollHost.ViewportHeight,
                OverscanPx,
                GalleryMasonryLayout.MaxCardHeight);

            foreach (var i in visibleIndices)
            {
                var item = items[i];
                visibleSet.Add(i);

                if (!activeCards.TryGetValue(i, out var card))
                {
                    card = CreateCard(i, item);
                    activeCards[i] = card;
                    MasonryCanvas.Children.Add(card.Container);
                }

                StopReleaseTimer(card);
                if (!card.IsLoaded && ShouldAttemptImageLoad(card, card.Photo.EffectiveSourcePath ?? string.Empty))
                    LoadImage(card);

                var wasNewThumbRequest = requestedThumbs.Add(layoutPhotos[i].PhotoPath);
                if (GalleryMasonryViewportLogic.ShouldQueueThumbnailAfterRequestRegistered(
                    wasNewThumbRequest,
                    layoutPhotos[i].PhotoPath,
                    layoutPhotos[i].GridThumbPath))
                {
                    thumbsNeededBuf.Add(layoutPhotos[i]);
                }
            }

            toRemoveBuf.Clear();
            foreach (var (index, entry) in activeCards)
            {
                if (visibleSet.Contains(index)) continue;

                var item = items[index];
                if (GalleryMasonryViewportLogic.IsOutsideReleaseRange(item, top, ScrollHost.ViewportHeight, ReleaseMarginPx))
                {
                    ReleaseCard(entry);
                    MasonryCanvas.Children.Remove(entry.Container);
                    toRemoveBuf.Add(index);
                }
                else if (entry.IsLoaded && entry.PendingReleaseTimer is null)
                {
                    ScheduleRelease(entry);
                }
            }
            foreach (var index in toRemoveBuf) activeCards.Remove(index);

            if (thumbsNeededBuf.Count > 0)
                OnThumbnailsNeeded?.Invoke(thumbsNeededBuf);

            ReportFirstVisibleIndex(top);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryMasonryView.UpdateVisibility: threw: {ex}");
        }
    }

    // 現在のスクロール位置から最初に見えている写真インデックスを通知する。
    private void ReportFirstVisibleIndex(double scrollTop)
    {
        if (currentLayout is null || sortedByTopIndices is null) return;
        var firstIdx = GalleryMasonryViewportLogic.FindFirstVisibleIndex(
            currentLayout.Items,
            sortedByTopIndices,
            scrollTop);
        if (!GalleryMasonryViewportLogic.ShouldNotifyFirstVisibleIndex(firstIdx, lastReportedFirstVisible)) return;
        var nextFirstVisible = firstIdx.GetValueOrDefault();
        lastReportedFirstVisible = nextFirstVisible;
        OnFirstVisibleIndexChanged?.Invoke(nextFirstVisible);
    }

    /// <summary>
    /// カードに BitmapImage をセットする。
    /// DecodePixelWidth をカード幅に合わせることでデコード時のメモリ消費を抑える。
    /// </summary>
    private void LoadImage(CardEntry entry)
    {
        var request = GalleryMasonryViewportLogic.ImageRequest(entry.Photo, entry.Container.Width);
        if (request is null) return;
        if (entry.IsLoading && SamePath(entry.LoadingPath, request.SourcePath))
            return;

        StopRetryTimer(entry);
        DetachBitmapHandlers(entry);

        if (!SamePath(entry.FailedPath, request.SourcePath))
        {
            entry.FailedPath = string.Empty;
            entry.LoadFailureCount = 0;
        }

        var loadVersion = ++entry.LoadVersion;
        entry.LoadingPath = request.SourcePath;
        entry.LoadedPath = string.Empty;
        entry.CurrentBitmap = null;
        entry.IsLoaded = false;
        entry.IsLoading = true;
        entry.PrepareImageLoad?.Invoke();

        try
        {
            var bmp = new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                DecodePixelWidth = request.DecodePixelWidth,
                DecodePixelType = DecodePixelType.Logical,
                UriSource = new Uri(request.SourcePath, UriKind.Absolute),
            };
            entry.CurrentBitmap = bmp;
            RoutedEventHandler? openedHandler = null;
            ExceptionRoutedEventHandler? failedHandler = null;
            openedHandler = (_, _) =>
            {
                DetachBitmapHandlers(entry, bmp, openedHandler, failedHandler);
                if (!IsCurrentImageLoad(entry, bmp, request.SourcePath, loadVersion))
                    return;
                HandleImageLoadOpened(entry, request.SourcePath);
            };
            failedHandler = (_, args) =>
            {
                DetachBitmapHandlers(entry, bmp, openedHandler, failedHandler);
                if (!IsCurrentImageLoad(entry, bmp, request.SourcePath, loadVersion))
                    return;
                AppLogger.Warn($"GalleryMasonryView.ImageFailed: {args.ErrorMessage}");
                HandleImageLoadFailure(entry, request.SourcePath);
            };
            entry.CallbackBitmap = bmp;
            entry.BitmapOpenedHandler = openedHandler;
            entry.BitmapFailedHandler = failedHandler;
            bmp.ImageOpened += openedHandler;
            bmp.ImageFailed += failedHandler;
            entry.Image.Source = bmp;
        }
        catch (Exception ex)
        {
            HandleImageLoadFailure(entry, request.SourcePath);
            AppLogger.Error($"GalleryMasonryView.LoadImage: failed for {request.SourcePath}: {ex}");
        }
    }

    /// <summary>未ロードカードへ新規ロードを試みるかを返す。</summary>
    private static bool ShouldAttemptImageLoad(CardEntry entry, string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || entry.PendingRetryTimer is not null)
            return false;
        if (entry.IsLoading && SamePath(entry.LoadingPath, sourcePath))
            return false;
        return !SamePath(entry.FailedPath, sourcePath)
            || entry.LoadFailureCount <= GalleryMasonryViewportLogic.MaxImageLoadRetries;
    }

    private static bool SamePath(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentImageLoad(CardEntry entry, BitmapImage bitmap, string path, int loadVersion)
        => ReferenceEquals(entry.CurrentBitmap, bitmap)
            && GalleryMasonryViewportLogic.IsCurrentImageLoadCallback(
                loadVersion,
                entry.LoadVersion,
                path,
                entry.LoadingPath);

    private static void DetachBitmapHandlers(CardEntry entry)
    {
        if (entry.CallbackBitmap is not { } bitmap)
            return;
        DetachBitmapHandlers(entry, bitmap, entry.BitmapOpenedHandler, entry.BitmapFailedHandler);
    }

    private static void DetachBitmapHandlers(
        CardEntry entry,
        BitmapImage bitmap,
        RoutedEventHandler? openedHandler,
        ExceptionRoutedEventHandler? failedHandler)
    {
        if (openedHandler is not null)
            bitmap.ImageOpened -= openedHandler;
        if (failedHandler is not null)
            bitmap.ImageFailed -= failedHandler;
        if (ReferenceEquals(entry.CallbackBitmap, bitmap))
        {
            entry.CallbackBitmap = null;
            entry.BitmapOpenedHandler = null;
            entry.BitmapFailedHandler = null;
        }
    }

    private void HandleImageLoadOpened(CardEntry entry, string openedPath)
    {
        StopRetryTimer(entry);
        entry.LoadedPath = openedPath;
        entry.FailedPath = string.Empty;
        entry.LoadFailureCount = 0;
        entry.IsLoaded = true;
        entry.IsLoading = false;
        if (entry.ErrorIcon is not null)
            entry.ErrorIcon.Visibility = Visibility.Collapsed;
        entry.ShowImageOpened?.Invoke();
    }

    /// <summary>ImageFailed / BitmapImage 生成失敗を同じカード状態へ反映する。</summary>
    private void HandleImageLoadFailure(CardEntry entry, string failedPath)
    {
        ReleaseImage(entry);
        if (string.IsNullOrEmpty(failedPath))
        {
            entry.ShowImageError?.Invoke();
            return;
        }

        if (!SamePath(entry.FailedPath, failedPath))
        {
            entry.FailedPath = failedPath;
            entry.LoadFailureCount = 0;
        }
        entry.LoadFailureCount++;
        entry.ShowImageError?.Invoke();
        ScheduleImageRetry(entry, failedPath);
    }

    /// <summary>一時的なデコード失敗に備えて、表示範囲内のカードだけ短い遅延後に再読込する。</summary>
    private void ScheduleImageRetry(CardEntry entry, string failedPath)
    {
        if (!isControlLoaded)
            return;
        var insideOverscan = GalleryMasonryViewportLogic.IsInsideOverscan(
            Canvas.GetTop(entry.Container),
            entry.Container.Height,
            ScrollHost.VerticalOffset,
            ScrollHost.ViewportHeight,
            OverscanPx);
        if (!GalleryMasonryViewportLogic.ShouldRetryImageLoad(entry.LoadFailureCount, insideOverscan))
            return;

        var timer = dispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(GalleryMasonryViewportLogic.ImageLoadRetryDelayMilliseconds);
        timer.IsRepeating = false;
        Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object>? tickHandler = null;
        tickHandler = (_, _) =>
        {
            if (!ReferenceEquals(entry.PendingRetryTimer, timer))
            {
                if (tickHandler is not null)
                    timer.Tick -= tickHandler;
                timer.Stop();
                return;
            }
            StopRetryTimer(entry);
            if (!isControlLoaded
                || !activeCards.TryGetValue(entry.Index, out var current)
                || !ReferenceEquals(current, entry))
                return;
            var currentPath = entry.Photo.EffectiveSourcePath ?? string.Empty;
            if (!SamePath(entry.FailedPath, failedPath) || !SamePath(currentPath, failedPath))
                return;
            var stillInsideOverscan = GalleryMasonryViewportLogic.IsInsideOverscan(
                Canvas.GetTop(entry.Container),
                entry.Container.Height,
                ScrollHost.VerticalOffset,
                ScrollHost.ViewportHeight,
                OverscanPx);
            if (!stillInsideOverscan)
                return;
            LoadImage(entry);
        };
        timer.Tick += tickHandler;
        entry.PendingRetryTimer = timer;
        entry.PendingRetryTickHandler = tickHandler;
        timer.Start();
    }

    /// <summary>一定時間後に画像の Source を null にしてメモリを解放するタイマーを設定する。</summary>
    private void ScheduleRelease(CardEntry entry)
    {
        StopReleaseTimer(entry);
        var timer = dispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(ReleaseDelayMs);
        timer.IsRepeating = false;
        Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object>? tickHandler = null;
        tickHandler = (_, _) =>
        {
            if (!ReferenceEquals(entry.PendingReleaseTimer, timer))
            {
                if (tickHandler is not null)
                    timer.Tick -= tickHandler;
                timer.Stop();
                return;
            }
            StopReleaseTimer(entry);
            if (!isControlLoaded
                || !activeCards.TryGetValue(entry.Index, out var current)
                || !ReferenceEquals(current, entry))
                return;

            var returnedToOverscan = GalleryMasonryViewportLogic.IsInsideOverscan(
                Canvas.GetTop(entry.Container),
                entry.Container.Height,
                ScrollHost.VerticalOffset,
                ScrollHost.ViewportHeight,
                OverscanPx);
            if (returnedToOverscan)
                return;
            try
            {
                ReleaseImage(entry);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"GalleryMasonryView.ReleaseTick: threw: {ex}");
            }
        };
        timer.Tick += tickHandler;
        entry.PendingReleaseTimer = timer;
        entry.PendingReleaseTickHandler = tickHandler;
        timer.Start();
    }

    /// <summary>
    /// レイアウト情報からカードの Border + Image を生成し、Canvas 上に配置する。
    /// サムネイルパス変更時に自動で画像を差し替える PropertyChanged ハンドラも登録する。
    /// </summary>
    private CardEntry CreateCard(int index, MasonryItem item)
    {
        var border = new Border
        {
            Width = item.Width,
            Height = item.Height,
            CornerRadius = new CornerRadius(4),
            Background = ThemeHelper.Brush(this, "ASurface"),
            BorderBrush = ThemeHelper.Brush(this, "ABorder"),
            BorderThickness = new Thickness(1),
        };
        Canvas.SetLeft(border, item.Left);
        Canvas.SetTop(border, item.Top);

        // Border の子を Grid にし、画像・プレースホルダ・情報表示を重ねる。
        var cardGrid = new Grid
        {
            Width = item.Width,
            Height = item.Height,
            Background = ThemeHelper.Brush(this, "ASurfaceSoft"),
        };
        cardGrid.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, item.Width, item.Height),
        };
        border.Child = cardGrid;

        // 画像読み込み前は、面全体がゆっくり明滅するMaterial UI型のPulseを表示する。
        var skeletonPlaceholder = new Border
        {
            Background = ThemeHelper.Brush(this, "ASurfaceSofter"),
            Width = item.Width,
            Height = item.Height,
        };
        cardGrid.Children.Add(skeletonPlaceholder);
        SkeletonPulseAnimation.Start(skeletonPlaceholder);

        var compositor = ElementCompositionPreview.GetElementVisual(skeletonPlaceholder).Compositor;

        // 写真本体を UniformToFill で表示する。
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            Width = item.Width,
            Height = item.Height,
        };
        cardGrid.Children.Add(image);

        // 読み込み完了時にフェードインできるよう初期不透明度を 0 にする。
        var imageVisual = ElementCompositionPreview.GetElementVisual(image);
        imageVisual.Opacity = 0f;

        var errorIcon = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 24,
            Foreground = ThemeHelper.Brush(this, "ATextDisabled"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        cardGrid.Children.Add(errorIcon);

        // 画像ロードの開始・終了・失敗を、同じカードのプレースホルダへ反映する。
        void StartSkeletonPulse()
        {
            try
            {
                skeletonPlaceholder.Background = ThemeHelper.Brush(this, "ASurfaceSofter");
                SkeletonPulseAnimation.Start(skeletonPlaceholder);
            }
            catch (Exception ex) { AppLogger.Error($"GalleryMasonryView.StartSkeletonPulse: threw: {ex}"); }
        }

        void StopSkeletonPulse()
        {
            try
            {
                SkeletonPulseAnimation.Stop(skeletonPlaceholder);
            }
            catch (Exception ex) { AppLogger.Error($"GalleryMasonryView.StopSkeletonPulse: threw: {ex}"); }
        }

        void PrepareImageLoad()
        {
            try
            {
                errorIcon.Visibility = Visibility.Collapsed;
                imageVisual.StopAnimation("Opacity");
                imageVisual.Opacity = 0f;
                StartSkeletonPulse();
            }
            catch (Exception ex) { AppLogger.Error($"GalleryMasonryView.PrepareImageLoad: threw: {ex}"); }
        }

        void ShowImageError()
        {
            try
            {
                StopSkeletonPulse();
                imageVisual.StopAnimation("Opacity");
                imageVisual.Opacity = 0f;
                skeletonPlaceholder.Background = ThemeHelper.Brush(this, "ASurfaceSofter");
                skeletonPlaceholder.Opacity = 1;
                errorIcon.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { AppLogger.Error($"GalleryMasonryView.ShowImageError: threw: {ex}"); }
        }

        void ShowImageOpened()
        {
            try
            {
                var fadeIn = compositor.CreateScalarKeyFrameAnimation();
                var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
                fadeIn.InsertKeyFrame(1f, 1f, easing);
                fadeIn.Duration = TimeSpan.FromMilliseconds(200);
                imageVisual.StartAnimation("Opacity", fadeIn);
                StopSkeletonPulse();
            }
            catch (Exception ex) { AppLogger.Error($"GalleryMasonryView.ShowImageOpened: threw: {ex}"); }
        }

        // ワールド名と撮影時刻の情報表示を画像下部に重ねる。
        var overlayPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(10, 20, 10, 10),
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0.5, 0),
                EndPoint = new Windows.Foundation.Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(0x00, 0x00, 0x00, 0x00), Offset = 0 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(0xC8, 0x00, 0x00, 0x00), Offset = 1 },
                },
            },
        };

        var worldNameBlock = new TextBlock
        {
            Text = item.Photo.WorldName ?? string.Empty,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        overlayPanel.Children.Add(worldNameBlock);

        var timestampBlock = new TextBlock
        {
            Text = item.Photo.Timestamp ?? string.Empty,
            FontSize = 10,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)),
            FontFamily = ThemeHelper.AppResource<FontFamily>("AFontMono"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        overlayPanel.Children.Add(timestampBlock);
        cardGrid.Children.Add(overlayPanel);

        // 情報表示はホバー時だけ出すため初期不透明度を 0 にする。
        var overlayVisual = ElementCompositionPreview.GetElementVisual(overlayPanel);
        overlayVisual.Opacity = 0f;

        var favoriteBadge = new FavoriteCornerBadge
        {
            BadgeSize = 36,
            IconSize = 12,
            Liked = item.Photo.IsFavorite,
            Interactive = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        favoriteBadge.OnClick = () => OnFavoriteClicked?.Invoke(item.Photo);
        cardGrid.Children.Add(favoriteBadge);

        // ギャラリーモードでは写真を優先し、操作時だけお気に入りバッジを表示する。
        // Composition の不透明度だけを変えることで、キーボードのフォーカス対象は維持する。
        var favoriteVisual = ElementCompositionPreview.GetElementVisual(favoriteBadge);
        favoriteVisual.Opacity = 0f;

        // --- 複数選択モードの選択リング ---
        // チェックバッジを使わず、カード全体の二重枠で選択を示す。
        // PhotoThumbnailItem.IsSelected の変化を PhotoSubscription で受けて Visibility を切り替える。
        var selection = GalleryMasonryViewportLogic.SelectionVisual(item.Photo.IsSelected);
        var selectionTint = new Border
        {
            Background = ThemeHelper.Brush(this, "APhotoSelectionTint"),
            CornerRadius = new CornerRadius(4),
            Visibility = ToVisibility(selection.Visible),
        };
        cardGrid.Children.Add(selectionTint);

        var selectionGlow = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = ThemeHelper.Brush(this, "APhotoSelectionGlow"),
            BorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(7),
            Opacity = 0.7,
            Visibility = ToVisibility(selection.Visible),
        };
        cardGrid.Children.Add(selectionGlow);

        var selectionRing = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = ThemeHelper.Brush(this, "APhotoSelectionRing"),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(5),
            Visibility = ToVisibility(selection.Visible),
        };
        cardGrid.Children.Add(selectionRing);

        // ホバー時の浮き上がり・拡大・情報表示アニメーションを設定する。
        var borderVisual = ElementCompositionPreview.GetElementVisual(border);
        ElementCompositionPreview.SetIsTranslationEnabled(border, true);
        borderVisual.Properties.InsertVector3("Translation", Vector3.Zero);
        var hoverEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
        var isPointerOver = false;
        var isFavoriteFocused = false;

        void AnimateFavoriteOpacity(float targetOpacity)
        {
            var animation = compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(1f, targetOpacity, hoverEasing);
            animation.Duration = TimeSpan.FromMilliseconds(180);
            favoriteVisual.StartAnimation("Opacity", animation);
        }

        favoriteBadge.GotFocus += (_, _) =>
        {
            isFavoriteFocused = true;
            AnimateFavoriteOpacity(1f);
        };
        favoriteBadge.LostFocus += (_, _) =>
        {
            isFavoriteFocused = false;
            if (!isPointerOver) AnimateFavoriteOpacity(0f);
        };

        border.PointerEntered += (_, _) =>
        {
            isPointerOver = true;
            var liftAnim = compositor.CreateVector3KeyFrameAnimation();
            liftAnim.InsertKeyFrame(1f, new Vector3(0f, -2f, 0f), hoverEasing);
            liftAnim.Duration = TimeSpan.FromMilliseconds(200);
            borderVisual.StartAnimation("Translation", liftAnim);

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.InsertKeyFrame(1f, new Vector3(1.04f, 1.04f, 1f), hoverEasing);
            scaleAnim.Duration = TimeSpan.FromMilliseconds(300);
            imageVisual.CenterPoint = new Vector3((float)(item.Width / 2), (float)(item.Height / 2), 0f);
            imageVisual.StartAnimation("Scale", scaleAnim);

            var overlayFadeIn = compositor.CreateScalarKeyFrameAnimation();
            overlayFadeIn.InsertKeyFrame(1f, 1f, hoverEasing);
            overlayFadeIn.Duration = TimeSpan.FromMilliseconds(180);
            overlayVisual.StartAnimation("Opacity", overlayFadeIn);
            AnimateFavoriteOpacity(1f);
        };

        border.PointerExited += (_, _) =>
        {
            isPointerOver = false;
            var liftAnim = compositor.CreateVector3KeyFrameAnimation();
            liftAnim.InsertKeyFrame(1f, Vector3.Zero, hoverEasing);
            liftAnim.Duration = TimeSpan.FromMilliseconds(200);
            borderVisual.StartAnimation("Translation", liftAnim);

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), hoverEasing);
            scaleAnim.Duration = TimeSpan.FromMilliseconds(300);
            imageVisual.StartAnimation("Scale", scaleAnim);

            var overlayFadeOut = compositor.CreateScalarKeyFrameAnimation();
            overlayFadeOut.InsertKeyFrame(1f, 0f, hoverEasing);
            overlayFadeOut.Duration = TimeSpan.FromMilliseconds(180);
            overlayVisual.StartAnimation("Opacity", overlayFadeOut);
            if (!isFavoriteFocused) AnimateFavoriteOpacity(0f);
        };

        border.Tapped += (_, _) =>
        {
            try { OnPhotoTapped?.Invoke(item.Photo); }
            catch (Exception ex) { AppLogger.Error($"GalleryMasonryView.PhotoTapped: threw: {ex}"); }
        };

        var entry = new CardEntry
        {
            Index = index,
            Container = border,
            Image = image,
            Photo = item.Photo,
            SelectionTint = selectionTint,
            SelectionGlow = selectionGlow,
            SelectionRing = selectionRing,
            FavoriteBadge = favoriteBadge,
            ErrorIcon = errorIcon,
            MediaHost = cardGrid,
            SkeletonPlaceholder = skeletonPlaceholder,
            PrepareImageLoad = PrepareImageLoad,
            ShowImageOpened = ShowImageOpened,
            ShowImageError = ShowImageError,
            StopSkeletonPulse = StopSkeletonPulse,
        };
        // サムネイル生成完了時に GridThumbPath が更新されるので、自動で画像を差し替える。
        // また IsSelected / IsFavorite の変化に応じて各バッジ表示を切り替える。
        entry.PhotoSubscription = (s, e) =>
        {
            var newPath = entry.Photo.EffectiveSourcePath ?? string.Empty;
            var insideOverscan = GalleryMasonryViewportLogic.IsInsideOverscan(
                Canvas.GetTop(entry.Container),
                entry.Container.Height,
                ScrollHost.VerticalOffset,
                ScrollHost.ViewportHeight,
                OverscanPx);
            switch (GalleryMasonryViewportLogic.PhotoChangeAction(
                e.PropertyName,
                newPath,
                entry.LoadedPath,
                entry.IsLoaded,
                insideOverscan))
            {
                case MasonryPhotoChangeAction.UpdateSelection:
                    var nextSelection = GalleryMasonryViewportLogic.SelectionVisual(entry.Photo.IsSelected);
                    var v = ToVisibility(nextSelection.Visible);
                    selectionTint.Visibility = v;
                    selectionGlow.Visibility = v;
                    selectionRing.Visibility = v;
                    return;
                case MasonryPhotoChangeAction.UpdateFavorite:
                    favoriteBadge.Liked = entry.Photo.IsFavorite;
                    return;
                case MasonryPhotoChangeAction.ReloadNow:
                    LoadImage(entry);
                    return;
            }
        };
        item.Photo.PropertyChanged += entry.PhotoSubscription;

        return entry;
    }

    /// <summary>bool の表示状態を WinUI の Visibility へ変換する。</summary>
    private static Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
