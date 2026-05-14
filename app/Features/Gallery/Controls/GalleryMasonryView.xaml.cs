using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Numerics;
using Alpheratz.Core;
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
        public string LoadedPath = string.Empty;
        public bool IsLoaded;
        public DispatcherQueueTimer? PendingReleaseTimer;
        public System.ComponentModel.PropertyChangedEventHandler? PhotoSubscription;
        // R2-A-19: ImageFailed 時に該当カード固有の shimmer を停止できるよう、生成時の Visual を保持する。
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
        var next = count > 0 ? (int?)count : null;
        if (requestedColumnCount == next) return;
        requestedColumnCount = next;
        Rebuild();
    }

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
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            RebuildLayout();
            return;
        }
        requestedThumbs.Clear();
        Rebuild();
    }

    private void ScrollHost_SizeChanged(object sender, SizeChangedEventArgs e) => Rebuild();
    private void ScrollHost_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => RequestUpdateVisibility();

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
    {
        var availableWidth = this.ActualWidth;
        if (availableWidth <= 0) availableWidth = ScrollHost.ActualWidth;
        var inner = Math.Max(0, availableWidth - 28);
        var autoCols = inner > 0
            ? Math.Max(1, (int)Math.Floor((inner + GalleryMasonryLayout.Gap) / (GalleryMasonryLayout.MinColumnWidth + GalleryMasonryLayout.Gap)))
            : 1;
        var effectiveCols = requestedColumnCount.HasValue
            ? Math.Max(1, Math.Min(requestedColumnCount.Value, autoCols))
            : autoCols;
        return (inner, effectiveCols);
    }

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

            MasonryCanvas.Width = currentLayout.ColumnCount * currentLayout.ColumnWidth
                + (currentLayout.ColumnCount - 1) * currentLayout.Gap;
            MasonryCanvas.Height = currentLayout.TotalHeight;

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

            MasonryCanvas.Width = currentLayout.ColumnCount * currentLayout.ColumnWidth
                + (currentLayout.ColumnCount - 1) * currentLayout.Gap;
            MasonryCanvas.Height = currentLayout.TotalHeight;

            BuildSortedIndex();
            dispatcherQueue.TryEnqueue(UpdateVisibility);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryMasonryView.RebuildLayout: threw: {ex}");
        }
    }

    private void BuildSortedIndex()
    {
        if (currentLayout is null || currentLayout.Items.Count == 0)
        {
            sortedByTopIndices = null;
            return;
        }
        var items = currentLayout.Items;
        var sorted = new int[items.Count];
        for (int i = 0; i < items.Count; i++) sorted[i] = i;
        Array.Sort(sorted, (a, b) => items[a].Top.CompareTo(items[b].Top));
        sortedByTopIndices = sorted;
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
            var bottom = top + ScrollHost.ViewportHeight;
            var loadTop = top - OverscanPx;
            var loadBottom = bottom + OverscanPx;
            var releaseTop = top - ReleaseMarginPx;
            var releaseBottom = bottom + ReleaseMarginPx;

            visibleSet.Clear();
            thumbsNeededBuf.Clear();

            var items = currentLayout.Items;
            var sorted = sortedByTopIndices;
            var n = sorted.Length;

            var searchStart = loadTop - GalleryMasonryLayout.MaxCardHeight;
            var lo = 0;
            var hi = n - 1;
            var startIdx = n;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                if (items[sorted[mid]].Top >= searchStart) { startIdx = mid; hi = mid - 1; }
                else lo = mid + 1;
            }

            for (int si = startIdx; si < n; si++)
            {
                var i = sorted[si];
                var item = items[i];
                if (item.Top > loadBottom) break;
                var cardBottom = item.Top + item.Height;
                if (cardBottom < loadTop) continue;

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

                if (requestedThumbs.Add(layoutPhotos[i].PhotoPath)
                    && string.IsNullOrEmpty(layoutPhotos[i].GridThumbPath)
                    && !string.IsNullOrEmpty(layoutPhotos[i].PhotoPath))
                {
                    thumbsNeededBuf.Add(layoutPhotos[i]);
                }
            }

            toRemoveBuf.Clear();
            foreach (var (index, entry) in activeCards)
            {
                if (visibleSet.Contains(index)) continue;

                var item = items[index];
                var cardBottom = item.Top + item.Height;
                if (cardBottom < releaseTop || item.Top > releaseBottom)
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

    private void ReportFirstVisibleIndex(double scrollTop)
    {
        if (currentLayout is null || sortedByTopIndices is null) return;
        var items = currentLayout.Items;
        var sorted = sortedByTopIndices;
        var n = sorted.Length;
        if (n == 0) return;

        var lo = 0;
        var hi = n - 1;
        var startPos = n;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (items[sorted[mid]].Top >= scrollTop) { startPos = mid; hi = mid - 1; }
            else lo = mid + 1;
        }

        int firstIdx;
        if (startPos < n)
            firstIdx = sorted[startPos];
        else if (n > 0)
            firstIdx = sorted[n - 1];
        else
            return;

        if (firstIdx != lastReportedFirstVisible)
        {
            lastReportedFirstVisible = firstIdx;
            OnFirstVisibleIndexChanged?.Invoke(firstIdx);
        }
    }

    /// <summary>
    /// カードに BitmapImage をセットする。
    /// DecodePixelWidth をカード幅に合わせることでデコード時のメモリ消費を抑える。
    /// </summary>
    private void LoadImage(CardEntry entry)
    {
        var sourcePath = entry.Photo.EffectiveSourcePath ?? string.Empty;
        if (string.IsNullOrEmpty(sourcePath)) return;
        try
        {
            var bmp = new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                DecodePixelWidth = Math.Max(1, (int)entry.Container.Width),
                DecodePixelType = DecodePixelType.Logical,
                UriSource = new Uri(sourcePath, UriKind.Absolute),
            };
            entry.Image.Source = bmp;
            entry.LoadedPath = sourcePath;
            entry.IsLoaded = true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryMasonryView.LoadImage: failed for {sourcePath}: {ex}");
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
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.Resources["ASurface"],
            BorderBrush = (Brush)Application.Current.Resources["ABorder"],
            BorderThickness = new Thickness(1),
        };
        Canvas.SetLeft(border, item.Left);
        Canvas.SetTop(border, item.Top);

        // Use a Grid as the border's child to allow image + overlay stacking
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

        // --- Shimmer placeholder ---
        var shimmerBase = new Border
        {
            Background = (Brush)Application.Current.Resources["ASurfaceSoft"],
            Width = item.Width,
            Height = item.Height,
        };
        cardGrid.Children.Add(shimmerBase);

        var shimmerHighlight = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
            Width = item.Width * 0.4,
            Height = item.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        cardGrid.Children.Add(shimmerHighlight);

        // Animate shimmer highlight sliding across
        var shimmerVisual = ElementCompositionPreview.GetElementVisual(shimmerHighlight);
        var compositor = shimmerVisual.Compositor;
        var shimmerAnim = compositor.CreateScalarKeyFrameAnimation();
        shimmerAnim.InsertKeyFrame(0f, (float)(-item.Width * 0.4));
        shimmerAnim.InsertKeyFrame(1f, (float)item.Width);
        shimmerAnim.Duration = TimeSpan.FromMilliseconds(1500);
        shimmerAnim.IterationBehavior = AnimationIterationBehavior.Forever;
        shimmerVisual.StartAnimation("Offset.X", shimmerAnim);

        // --- Image ---
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            Width = item.Width,
            Height = item.Height,
        };
        cardGrid.Children.Add(image);

        // Set initial image opacity to 0 for fade-in effect
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

        // Image fade-in on load + stop shimmer
        image.ImageOpened += (_, _) =>
        {
            // Fade in the image
            var fadeIn = compositor.CreateScalarKeyFrameAnimation();
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
            fadeIn.InsertKeyFrame(1f, 1f, easing);
            fadeIn.Duration = TimeSpan.FromMilliseconds(200);
            imageVisual.StartAnimation("Opacity", fadeIn);

            // Hide shimmer
            StopShimmer();
        };

        image.ImageFailed += (_, args) =>
        {
            AppLogger.Warn($"GalleryMasonryView.ImageFailed: {args.ErrorMessage}");
            StopShimmer();
            shimmerBase.Background = (Brush)Application.Current.Resources["ASurfaceSoft"];
            shimmerBase.Opacity = 1;
            var errorIcon = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 24,
                Foreground = (Brush)Application.Current.Resources["ATextDisabled"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            cardGrid.Children.Add(errorIcon);
        };

        // --- Info overlay (WorldName + Timestamp) ---
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
            FontFamily = (FontFamily)Application.Current.Resources["AFontMono"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        overlayPanel.Children.Add(timestampBlock);
        cardGrid.Children.Add(overlayPanel);

        // Set overlay initial opacity to 0
        var overlayVisual = ElementCompositionPreview.GetElementVisual(overlayPanel);
        overlayVisual.Opacity = 0f;

        // --- Hover effects ---
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
            StopShimmer = StopShimmer,
        };

        // サムネイル生成完了時に GridThumbPath が更新されるので、自動で画像を差し替える
        entry.PhotoSubscription = (s, e) =>
        {
            if (e.PropertyName != nameof(PhotoThumbnailItem.EffectiveSourcePath)
                && e.PropertyName != nameof(PhotoThumbnailItem.GridThumbPath)
                && e.PropertyName != nameof(PhotoThumbnailItem.ResolvedPhotoPath))
                return;
            var newPath = entry.Photo.EffectiveSourcePath ?? string.Empty;
            if (newPath == entry.LoadedPath) return;
            if (entry.IsLoaded)
            {
                LoadImage(entry);
                return;
            }
            var cardTop = Canvas.GetTop(entry.Container);
            var cardBottom = cardTop + entry.Container.Height;
            var viewTop = ScrollHost.VerticalOffset - OverscanPx;
            var viewBottom = ScrollHost.VerticalOffset + ScrollHost.ViewportHeight + OverscanPx;
            if (cardBottom >= viewTop && cardTop <= viewBottom)
                LoadImage(entry);
        };
        item.Photo.PropertyChanged += entry.PhotoSubscription;

        return entry;
    }
}