using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Numerics;
using Alpheratz.Core;
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
        public Border? SelectionRing;
        public Border? SelectionBadge;
        public string LoadedPath = string.Empty;
        public bool IsLoaded;
        public DispatcherQueueTimer? PendingReleaseTimer;
        public System.ComponentModel.PropertyChangedEventHandler? PhotoSubscription;
        // ImageFailed 時に該当カードだけの shimmer を止められるよう、生成時の停止処理を保持する。
        public Action? StopShimmer;
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

    /// <summary>カードタップ時のコールバック。GalleryPage が PhotoModal への遷移を設定する。</summary>
    public Action<PhotoThumbnailItem>? OnPhotoTapped { get; set; }

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
        this.SizeChanged += (_, _) => Rebuild();
        // テーマ切替時に既存カードの Border (code-behind で ThemeHelper.Brush から代入済み)
        // を現在テーマで再着色する。カード自体は仮想化されていて、表示中のものはレイアウトを
        // 維持したまま色だけ更新される。
        ActualThemeChanged += OnActualThemeChanged;
        Unloaded += (_, _) => ActualThemeChanged -= OnActualThemeChanged;
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
                if (entry.SelectionTint is not null && ThemeHelper.Brush(entry.Container, "APhotoSelectionTint") is { } tint)
                    entry.SelectionTint.Background = tint;
                if (entry.SelectionRing is not null && ThemeHelper.Brush(entry.Container, "APrimary") is { } primary)
                    entry.SelectionRing.BorderBrush = primary;
                if (entry.SelectionBadge is not null)
                {
                    if (ThemeHelper.Brush(entry.Container, "APrimary") is { } badgeBg)
                        entry.SelectionBadge.Background = badgeBg;
                    if (ThemeHelper.Brush(entry.Container, "APhotoSelectionBadgeBorder") is { } badgeBorder)
                        entry.SelectionBadge.BorderBrush = badgeBorder;
                }
            }
        }
        catch (Exception ex) { AppLogger.Error($"GalleryMasonryView.OnActualThemeChanged: {ex}"); }
    }

    /// <summary>写真コレクションをバインドし、CollectionChanged を購読する。</summary>
    public void SetPhotos(UiObservableCollection<PhotoThumbnailItem> next)
    {
        if (photos is not null)
            photos.CollectionChanged -= OnPhotosChanged;
        photos = next;
        photos.CollectionChanged += OnPhotosChanged;
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
        if (updateVisibilityPending) return;
        updateVisibilityPending = true;
        dispatcherQueue.TryEnqueue(() =>
        {
            updateVisibilityPending = false;
            UpdateVisibility();
        });
    }

    /// <summary>利用可能な幅から内部幅と実効カラム数を算出する。</summary>
    private (double inner, int effectiveCols) ComputeColumns()
        => GalleryMasonryViewportLogic.ComputeColumns(this.ActualWidth, ScrollHost.ActualWidth, requestedColumnCount);

    /// <summary>全カードを破棄してレイアウトをゼロから再構築する。</summary>
    private void Rebuild()
    {
        try
        {
            foreach (var (_, entry) in activeCards)
            {
                entry.PendingReleaseTimer?.Stop();
                entry.PendingReleaseTimer = null;
                if (entry.PhotoSubscription is not null)
                {
                    entry.Photo.PropertyChanged -= entry.PhotoSubscription;
                    entry.PhotoSubscription = null;
                }
            }
            activeCards.Clear();
            MasonryCanvas.Children.Clear();

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
            dispatcherQueue.TryEnqueue(UpdateVisibility);
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
            if (photos is null) return;
            var (inner, effectiveCols) = ComputeColumns();
            if (inner <= 0) return;

            layoutPhotos = new List<PhotoThumbnailItem>(photos);
            currentLayout = GalleryMasonryLayout.Build(layoutPhotos, inner, effectiveCols);

            var canvas = GalleryMasonryViewportLogic.CanvasSize(currentLayout);
            MasonryCanvas.Width = canvas.Width;
            MasonryCanvas.Height = canvas.Height;

            BuildSortedIndex();
            dispatcherQueue.TryEnqueue(UpdateVisibility);
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
            if (currentLayout is null || layoutPhotos is null || sortedByTopIndices is null || currentLayout.Items.Count == 0)
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

                card.PendingReleaseTimer?.Stop();
                card.PendingReleaseTimer = null;
                if (!card.IsLoaded) LoadImage(card);

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
                    entry.PendingReleaseTimer?.Stop();
                    entry.PendingReleaseTimer = null;
                    if (entry.PhotoSubscription is not null)
                    {
                        entry.Photo.PropertyChanged -= entry.PhotoSubscription;
                        entry.PhotoSubscription = null;
                    }
                    entry.Image.Source = null;
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
        try
        {
            var bmp = new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                DecodePixelWidth = request.DecodePixelWidth,
                DecodePixelType = DecodePixelType.Logical,
                UriSource = new Uri(request.SourcePath, UriKind.Absolute),
            };
            entry.Image.Source = bmp;
            entry.LoadedPath = request.SourcePath;
            entry.IsLoaded = true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryMasonryView.LoadImage: failed for {request.SourcePath}: {ex}");
        }
    }

    /// <summary>一定時間後に画像の Source を null にしてメモリを解放するタイマーを設定する。</summary>
    private void ScheduleRelease(CardEntry entry)
    {
        var timer = dispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(ReleaseDelayMs);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            try
            {
                entry.Image.Source = null;
                entry.LoadedPath = string.Empty;
                entry.IsLoaded = false;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"GalleryMasonryView.ReleaseTick: threw: {ex}");
            }
            entry.PendingReleaseTimer = null;
        };
        timer.Start();
        entry.PendingReleaseTimer = timer;
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
        };
        cardGrid.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, item.Width, item.Height),
        };
        border.Child = cardGrid;

        // 画像読み込み前のプレースホルダを配置する。
        var shimmerBase = new Border
        {
            Background = ThemeHelper.Brush(this, "ASurfaceSoft"),
            Width = item.Width,
            Height = item.Height,
        };
        cardGrid.Children.Add(shimmerBase);

        var shimmer = GalleryMasonryViewportLogic.ShimmerMetrics(item.Width);
        var shimmerHighlight = new Border
        {
            Background = ThemeHelper.Brush(this, "ASurfaceHover"),
            Width = shimmer.HighlightWidth,
            Height = item.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        cardGrid.Children.Add(shimmerHighlight);

        // ハイライトを横に流して読み込み中であることを示す。
        var shimmerVisual = ElementCompositionPreview.GetElementVisual(shimmerHighlight);
        var compositor = shimmerVisual.Compositor;
        var shimmerAnim = compositor.CreateScalarKeyFrameAnimation();
        shimmerAnim.InsertKeyFrame(0f, (float)shimmer.StartOffset);
        shimmerAnim.InsertKeyFrame(1f, (float)shimmer.EndOffset);
        shimmerAnim.Duration = TimeSpan.FromMilliseconds(shimmer.DurationMilliseconds);
        shimmerAnim.IterationBehavior = AnimationIterationBehavior.Forever;
        shimmerVisual.StartAnimation("Offset.X", shimmerAnim);

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

        // shimmer 停止ロジックを ImageOpened / ImageFailed の双方から呼べるようローカル関数にする。
        void StopShimmer()
        {
            try
            {
                shimmerVisual.StopAnimation("Offset.X");
                shimmerBase.Opacity = 0;
                shimmerHighlight.Opacity = 0;
            }
            catch (Exception ex) { AppLogger.Error($"GalleryMasonryView.StopShimmer: threw: {ex}"); }
        }

        // 画像読み込み完了時にフェードインし、プレースホルダを止める。
        image.ImageOpened += (_, _) =>
        {
            // 画像を短時間で表示状態へ移す。
            var fadeIn = compositor.CreateScalarKeyFrameAnimation();
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
            fadeIn.InsertKeyFrame(1f, 1f, easing);
            fadeIn.Duration = TimeSpan.FromMilliseconds(200);
            imageVisual.StartAnimation("Opacity", fadeIn);

            // プレースホルダを隠す。
            StopShimmer();
        };

        image.ImageFailed += (_, args) =>
        {
            AppLogger.Warn($"GalleryMasonryView.ImageFailed: {args.ErrorMessage}");
            StopShimmer();
            shimmerBase.Background = ThemeHelper.Brush(this, "ASurfaceSoft");
            shimmerBase.Opacity = 1;
            var errorIcon = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 24,
                Foreground = ThemeHelper.Brush(this, "ATextDisabled"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            cardGrid.Children.Add(errorIcon);
        };

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

        // --- 複数選択モードのチェックバッジ + 選択リング ---
        // PhotoGridItemsView と同じく、写真を覆わない 2px 枠と右上の小さな ✓ で選択を示す。
        // PhotoThumbnailItem.IsSelected の変化を PhotoSubscription で受けて Visibility を切り替える。
        var selection = GalleryMasonryViewportLogic.SelectionVisual(item.Photo.IsSelected);
        var selectionTint = new Border
        {
            Background = ThemeHelper.Brush(this, "APhotoSelectionTint"),
            CornerRadius = new CornerRadius(4),
            Visibility = ToVisibility(selection.Visible),
        };
        cardGrid.Children.Add(selectionTint);

        var selectionRing = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = ThemeHelper.Brush(this, "APrimary"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Visibility = ToVisibility(selection.Visible),
        };
        cardGrid.Children.Add(selectionRing);

        // 選択チェックバッジ。PhotoGridItemsView と統一して角丸スクエアで示す。
        var selectionBadge = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(8),
            Background = ThemeHelper.Brush(this, "APrimary"),
            BorderBrush = ThemeHelper.Brush(this, "APhotoSelectionBadgeBorder"),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            Visibility = ToVisibility(selection.Visible),
            Child = new Alpheratz.Shared.Controls.AppIcon
            {
                IconName = "check",
                IconSize = 13,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        cardGrid.Children.Add(selectionBadge);

        // ホバー時の浮き上がり・拡大・情報表示アニメーションを設定する。
        var borderVisual = ElementCompositionPreview.GetElementVisual(border);
        ElementCompositionPreview.SetIsTranslationEnabled(border, true);
        borderVisual.Properties.InsertVector3("Translation", Vector3.Zero);
        var hoverEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));

        border.PointerEntered += (_, _) =>
        {
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
        };

        border.PointerExited += (_, _) =>
        {
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
            SelectionRing = selectionRing,
            SelectionBadge = selectionBadge,
            StopShimmer = StopShimmer,
        };

        // サムネイル生成完了時に GridThumbPath が更新されるので、自動で画像を差し替える。
        // また IsSelected の変化に応じて選択バッジと枠の表示を切り替える。
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
                    selectionRing.Visibility = v;
                    selectionBadge.Visibility = v;
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
