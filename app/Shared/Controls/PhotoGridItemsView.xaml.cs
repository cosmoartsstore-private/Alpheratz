using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Numerics;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Shared.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Shared.Controls;

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class PhotoGridItemsView : UserControl
{
    public Action<PhotoGridItem>? OnPhotoActivated { get; set; }
    public Action<PhotoGridItem>? OnFavoriteClicked { get; set; }
    public Action<double>? OnGridScroll { get; set; }
    public Action<int>? OnGridWheel { get; set; }
    public Action? OnNearBottomReached { get; set; }
    public Action<int>? OnFirstVisibleIndexChanged { get; set; }
    private int lastReportedFirstVisible = -1;

    private ItemsWrapGrid? wrapGrid;
    private ScrollViewer? internalScrollViewer;
    private double lastKnownWidth;

    // 外部から渡された写真一覧を内部の GridView に接続する。
    public void SetItemsSource(object? source)
    {
        PhotoItems.ItemsSource = source;
    }

    public PhotoGridItemsView()
    {
        AppLogger.Trace("PhotoGridItemsView.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoGridItemsView.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        PhotoItems.Loaded += PhotoItems_Loaded;
        // Page アンマウント時に各 Image.Tag に積んだ PropertyChanged 購読を一括解除する。
        // DataContextChanged は recycle 時には必ず呼ばれるが、Page を捨てるパスでは
        // 個別の DataContextChanged(null) が走らずに済むこともあるため、保険として剥がす。
        Loaded += PhotoGridItemsView_Loaded;
        Unloaded += PhotoGridItemsView_Unloaded;
        // PhotoCardBorderStyle は {ThemeResource} でテーマ追従設計だが、hover/recycle で
        // code-behind が border.Background/BorderBrush に直接代入するため、その瞬間に
        // {ThemeResource} バインディングが local value で上書きされ、以降テーマ切替に
        // 追従しなくなる。テーマ切替時に rest 色を再適用してこれを補正する。
        AppLogger.Trace("PhotoGridItemsView.ctor: exit");
    }

    // 再ロード時もテーマ変更購読を張り直す。重複を避けるため、追加前に同じハンドラを解除する。
    private void PhotoGridItemsView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ActualThemeChanged -= OnActualThemeChanged;
            ActualThemeChanged += OnActualThemeChanged;
            ResubscribeVisibleCards(PhotoItems);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.PhotoGridItemsView_Loaded: {ex}"); }
    }

    // テーマ切替で残ったカードの local value を現在テーマの色に戻す。
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        try { ResetAllCardBrushes(PhotoItems); }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.OnActualThemeChanged: {ex}"); }
    }

    /// <summary>
    /// 表示中の写真カード Border を visual tree から拾い、現在テーマの rest ブラシで
    /// Background/BorderBrush を再代入する。テーマ切替後の最初のフレームで呼ぶ。
    /// </summary>
    private static void ResetAllCardBrushes(DependencyObject root)
    {
        var photoCardStyle = ThemeHelper.AppResource<Style>("PhotoCardBorderStyle");
        if (photoCardStyle is null) return;
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Border border && border.Style == photoCardStyle)
            {
                if (ThemeHelper.Brush(border, "ABorder") is { } restBorder) border.BorderBrush = restBorder;
                if (ThemeHelper.Brush(border, "ASurface") is { } restFill) border.Background = restFill;
            }
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++) stack.Push(VisualTreeHelper.GetChild(node, i));
        }
    }

    // 破棄時に画像購読とテーマ変更ハンドラを解除する。
    private void PhotoGridItemsView_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ActualThemeChanged -= OnActualThemeChanged;
            DetachInternalScrollViewer();
            UnsubscribeAllCards(PhotoItems);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.PhotoGridItemsView_Unloaded: {ex}"); }
    }

    /// <summary>
    /// VisualTree を辿って、Image.Tag に保持されている GridImageSubscription を全て解除する。
    /// PhotoThumbnailItem.PropertyChanged に残ったハンドラを切ることで、Page 破棄後も
    /// Photo オブジェクトが View 側からの参照で GC されずに残るのを防ぐ。
    /// </summary>
    private void UnsubscribeAllCards(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Image img && img.Tag is GridImageSubscription sub)
            {
                sub.Photo.PropertyChanged -= sub.Handler;
                img.Tag = null;
                StopShimmer(img);
                img.Source = null;
            }
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++) stack.Push(VisualTreeHelper.GetChild(node, i));
        }
    }

    /// <summary>再ロード後も表示済みカードの画像更新通知を受け取れるよう購読を復元する。</summary>
    private void ResubscribeVisibleCards(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Image { Name: "ThumbImage", Tag: null, DataContext: PhotoGridItem item } img)
            {
                try { WireThumbImage(img, item); }
                catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.ResubscribeVisibleCards: {ex}"); }
            }
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++) stack.Push(VisualTreeHelper.GetChild(node, i));
        }
    }

    // GridView 内部の ScrollViewer を取得し、スクロール通知を接続する。
    private void PhotoItems_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            DetachInternalScrollViewer();
            var sv = FindChildScrollViewer(PhotoItems);
            if (sv is null) return;
            AttachInternalScrollViewer(sv);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.PhotoItems_Loaded: {ex}"); }
    }

    /// <summary>内部 ScrollViewer の通知を解除し、VisualTree への参照を解放する。</summary>
    private void DetachInternalScrollViewer()
    {
        if (internalScrollViewer is null) return;
        internalScrollViewer.ViewChanged -= InternalScrollViewer_ViewChanged;
        internalScrollViewer.PointerWheelChanged -= InternalScrollViewer_PointerWheelChanged;
        internalScrollViewer = null;
    }

    /// <summary>内部 ScrollViewer の通知を重複なく接続する。</summary>
    private void AttachInternalScrollViewer(ScrollViewer scrollViewer)
    {
        DetachInternalScrollViewer();
        internalScrollViewer = scrollViewer;
        scrollViewer.ViewChanged += InternalScrollViewer_ViewChanged;
        scrollViewer.PointerWheelChanged += InternalScrollViewer_PointerWheelChanged;
    }

    // 指定ノード配下から最初に見つかる ScrollViewer を返す。
    private static ScrollViewer? FindChildScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindChildScrollViewer(child);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>
    /// 内部の ScrollViewer 参照。未取得なら一度だけ探索しキャッシュする。
    /// 見つからない場合に新しい ScrollViewer を作っても VisualTree には組み込まれないため、
    /// 取得失敗時は null を返して呼出側で何もしない。
    /// </summary>
    public ScrollViewer? GridScrollViewerRef
    {
        get
        {
            if (internalScrollViewer is not null) return internalScrollViewer;
            var scrollViewer = FindChildScrollViewer(PhotoItems);
            if (scrollViewer is not null)
                AttachInternalScrollViewer(scrollViewer);
            return internalScrollViewer;
        }
    }

    /// <summary>
    /// 直近に算出した画像領域の幅。shimmer ハイライト幅をカード幅へ追従させるために使う。
    /// </summary>
    private double currentImageWidth;

    // 利用可能幅から通常 6 列のカード寸法を再計算する。
    private void RecalculateCardSize(double availableWidth)
    {
        if (availableWidth <= 0 || wrapGrid is null) return;
        var layout = PhotoGridItemsLayoutLogic.CalculateCardLayout(availableWidth);
        if (layout is null) return;
        currentImageWidth = layout.ImageWidth;
        wrapGrid.ItemWidth = layout.ItemWidth;
        wrapGrid.ItemHeight = layout.ItemHeight;
        wrapGrid.MaximumRowsOrColumns = layout.Columns;
        PhotoItems.Opacity = 1;
        // 既に実体化済みのカードの shimmer ハイライト幅も新カード幅に合わせて更新する。
        RefreshActiveShimmerSizes(PhotoItems);
    }

    // ItemsWrapGrid の実体化後に参照を保持し、初期カード寸法を反映する。
    private void PhotoItemsWrapGrid_Loaded(object sender, RoutedEventArgs e)
    {
        wrapGrid = sender as ItemsWrapGrid;
        var width = PhotoGridItemsLayoutLogic.ResolveInitialGridWidth(PhotoItems.ActualWidth, lastKnownWidth);
        RecalculateCardSize(width);
    }

    // GridView 幅の変更に合わせてカード寸法を更新する。
    private void GridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        lastKnownWidth = e.NewSize.Width;
        if (wrapGrid is null)
        {
            wrapGrid = FindItemsWrapGrid(PhotoItems);
        }
        RecalculateCardSize(e.NewSize.Width);
    }

    // 指定ノード配下から ItemsWrapGrid を探索する。
    private static ItemsWrapGrid? FindItemsWrapGrid(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ItemsWrapGrid grid) return grid;
            var found = FindItemsWrapGrid(child);
            if (found is not null) return found;
        }
        return null;
    }

    // 指定インデックスのカードが先頭付近に来るようにスクロールする。
    public void ScrollToItemIndex(int index)
    {
        try
        {
            if (!PhotoGridItemsLayoutLogic.IsValidItemIndex(index, PhotoItems.Items.Count)) return;
            var item = PhotoItems.Items[index];
            PhotoItems.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoGridItemsView.ScrollToItemIndex: threw: {ex}");
        }
    }

    // カードクリックを外部の写真アクティベート通知へ変換する。
    private void PhotoItems_ItemClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            if (e.ClickedItem is PhotoGridItem item)
            {
                OnPhotoActivated?.Invoke(item);
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.PhotoItems_ItemClick: threw: {ex}"); }
    }

    // DataTemplate 内のお気に入りバッジに、現在カード用のクリック処理を渡す。
    private void FavoriteBadge_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            WireFavoriteBadge(sender as FavoriteCornerBadge);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.FavoriteBadge_Loaded: threw: {ex}"); }
    }

    /// <summary>カード再利用で DataContext が差し替わった時に、バッジクリックの対象写真を更新する。</summary>
    private void FavoriteBadge_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        try
        {
            WireFavoriteBadge(sender as FavoriteCornerBadge);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.FavoriteBadge_DataContextChanged: threw: {ex}"); }
    }

    /// <summary>お気に入りバッジを現在の PhotoGridItem に結線する。対象が無ければクリックを無効化する。</summary>
    private void WireFavoriteBadge(FavoriteCornerBadge? badge)
    {
        if (badge is null) return;
        if (badge.DataContext is PhotoGridItem item)
            badge.OnClick = () => OnFavoriteClicked?.Invoke(item);
        else
            badge.OnClick = null;
    }

    // スクロール位置、終端接近、先頭表示インデックスを親側へ通知する。
    private void InternalScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        try
        {
            if (sender is ScrollViewer scrollViewer)
            {
                OnGridScroll?.Invoke(scrollViewer.VerticalOffset);
                if (PhotoGridItemsLayoutLogic.IsNearBottom(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset))
                {
                    OnNearBottomReached?.Invoke();
                }
                ReportFirstVisibleIndex(scrollViewer.VerticalOffset);
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.InternalScrollViewer_ViewChanged: {ex}"); }
    }

    // 現在のスクロール位置から最初に見えている写真インデックスを概算する。
    private void ReportFirstVisibleIndex(double scrollTop)
    {
        if (wrapGrid is null) return;
        var firstIdx = PhotoGridItemsLayoutLogic.EstimateFirstVisibleIndex(
            scrollTop,
            wrapGrid.ItemHeight,
            wrapGrid.MaximumRowsOrColumns);
        if (!PhotoGridItemsLayoutLogic.ShouldNotifyFirstVisibleIndex(firstIdx, lastReportedFirstVisible)) return;
        var nextFirstVisible = firstIdx.GetValueOrDefault();
        lastReportedFirstVisible = nextFirstVisible;
        OnFirstVisibleIndexChanged?.Invoke(nextFirstVisible);
    }

    // マウスホイール量を親側へ渡し、月ナビゲーション等と同期させる。
    private void InternalScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            OnGridWheel?.Invoke(e.GetCurrentPoint(this).Properties.MouseWheelDelta);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.InternalScrollViewer_PointerWheelChanged: {ex}"); }
    }

    /// <summary>
    /// Recycle 時にホバー残留（BorderBrush/Background が hover 状態のまま）を解除する。
    /// PointerExited はスクロールで pointer が抜けたケースで発火しないことがあるため、
    /// DataContext 差し替えタイミングで明示的にリセットする。
    /// </summary>
    private void CardBorder_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        try
        {
            if (sender is not Border border) return;
            ElementCompositionPreview.GetElementVisual(border).Offset = Vector3.Zero;
            ApplyCardBrushes(border, GridCardVisualState.Rest);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.CardBorder_DataContextChanged: {ex}"); }
    }

    // ポインタが乗ったカードに小さな浮き上がりと色のフィードバックを与える。
    private void CardBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is not Border border) return;
            ApplyCardMotion(border, GridCardVisualState.Hover);
            ApplyCardBrushes(border, GridCardVisualState.Hover);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.CardBorder_PointerEntered: {ex}"); }
    }

    // ポインタが離れたカードを通常位置と通常色に戻す。
    private void CardBorder_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is not Border border) return;
            ApplyCardMotion(border, GridCardVisualState.Rest);
            ApplyCardBrushes(border, GridCardVisualState.Rest);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.CardBorder_PointerExited: {ex}"); }
    }

    /// <summary>カードの hover/rest 移動アニメーションを開始する。</summary>
    private static void ApplyCardMotion(Border border, GridCardVisualState state)
    {
        var cardVisual = PhotoGridItemsLayoutLogic.CardVisual(state);
        var visual = ElementCompositionPreview.GetElementVisual(border);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
        var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
        offsetAnim.InsertKeyFrame(1f, new Vector3(0, (float)cardVisual.OffsetY, 0), ease);
        offsetAnim.Duration = TimeSpan.FromMilliseconds(cardVisual.DurationMilliseconds);
        visual.StartAnimation("Offset", offsetAnim);
    }

    /// <summary>カードの hover/rest 背景と枠線ブラシを現在テーマで適用する。</summary>
    private static void ApplyCardBrushes(Border border, GridCardVisualState state)
    {
        var cardVisual = PhotoGridItemsLayoutLogic.CardVisual(state);
        if (ThemeHelper.Brush(border, cardVisual.BorderKey) is { } borderBrush)
            border.BorderBrush = borderBrush;
        if (ThemeHelper.Brush(border, cardVisual.BackgroundKey) is { } fillBrush)
            border.Background = fillBrush;
    }

    /// <summary>
    /// Image.Tag に購読元と handler を保存し、DataContext 差し替え後も元の Photo から
    /// 確実に PropertyChanged を解除できるようにする。
    /// </summary>
    private sealed record GridImageSubscription(PhotoThumbnailItem Photo, System.ComponentModel.PropertyChangedEventHandler Handler);

    // リサイクルされた Image に新しい写真を接続し、旧写真の購読を解除する。
    private void ThumbImage_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        try
        {
            if (sender is not Image img) return;
            // 旧購読を Tag から取り出してアンサブスクライブ。
            // DataContext 経由ではなくタプル経由なので、recycle で DataContext が
            // 既に新オブジェクトに差し替わっていても旧 Photo から確実に外せる。
            if (img.Tag is GridImageSubscription oldSub)
            {
                oldSub.Photo.PropertyChanged -= oldSub.Handler;
            }

            if (args.NewValue is not PhotoGridItem item)
            {
                img.Source = null;
                img.Tag = null;
                return;
            }

            WireThumbImage(img, item);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.ThumbImage_DataContextChanged: {ex}"); }
    }

    /// <summary>画像を現在の写真へ接続し、サムネイルパス変更時の再読込を購読する。</summary>
    private void WireThumbImage(Image img, PhotoGridItem item)
    {
        ResetCardLoadVisuals(img);
        SetImageSource(img, item.Photo);

        System.ComponentModel.PropertyChangedEventHandler handler = (s, e) =>
        {
            if (!PhotoGridItemsLayoutLogic.IsImageSourceProperty(e.PropertyName)) return;
            DispatcherQueue?.TryEnqueue(() =>
            {
                // 仮想化で Image が別カードへ再利用された後に、旧写真の通知が
                // UI キューへ残る場合がある。現在の DataContext と購読元が同じ時だけ反映する。
                if (!ReferenceEquals(img.DataContext, item)
                    || img.Tag is not GridImageSubscription current
                    || !ReferenceEquals(current.Photo, item.Photo))
                {
                    return;
                }

                // サムネイル生成完了でパスが差し替わった場合も再ロード扱いにし、
                // shimmer→フェードインの演出を改めて適用する。
                ResetCardLoadVisuals(img);
                SetImageSource(img, item.Photo);
            });
        };
        item.Photo.PropertyChanged += handler;
        img.Tag = new GridImageSubscription(item.Photo, handler);
    }

    // 写真の有効な表示パスから BitmapImage を作り、Image.Source に反映する。
    private static void SetImageSource(Image img, PhotoThumbnailItem photo)
    {
        var request = PhotoGridItemsLayoutLogic.ImageRequest(photo);
        if (request is null)
        {
            img.Source = null;
            return;
        }
        img.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
        {
            CreateOptions = Microsoft.UI.Xaml.Media.Imaging.BitmapCreateOptions.IgnoreImageCache,
            DecodePixelWidth = request.DecodePixelWidth,
            DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical,
            UriSource = new Uri(request.SourcePath, UriKind.Absolute),
        };
    }

    /// <summary>
    /// 画像ロード完了。マソンリーと同じく 200ms でフェードインし shimmer を止める。
    /// </summary>
    private void ThumbImage_Opened(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Image img) return;
            var imageVisual = ElementCompositionPreview.GetElementVisual(img);
            var compositor = imageVisual.Compositor;
            var fadeIn = compositor.CreateScalarKeyFrameAnimation();
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
            fadeIn.InsertKeyFrame(1f, 1f, easing);
            fadeIn.Duration = TimeSpan.FromMilliseconds(PhotoGridItemsLayoutLogic.ImageFadeInDurationMilliseconds);
            imageVisual.StartAnimation("Opacity", fadeIn);

            ApplyImageLoadVisual(img, GridImageLoadState.Opened, applyImageOpacity: false);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.ThumbImage_Opened: {ex}"); }
    }

    // 画像ロード失敗時は shimmer を止め、フォールバック面とエラー表示を残す。
    private void ThumbImage_Failed(object sender, ExceptionRoutedEventArgs e)
    {
        AppLogger.Error($"PhotoGridItemsView.ThumbImage_Failed: {e.ErrorMessage}");
        try
        {
            if (sender is not Image img) return;
            ApplyImageLoadVisual(img, GridImageLoadState.Failed);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.ThumbImage_Failed.fallback: {ex}"); }
    }

    /// <summary>
    /// ロード演出を初期状態へ戻す。画像 Opacity=0、エラーアイコン非表示、shimmer 再開。
    /// recycle / サムネイルパス差し替えの双方から呼ぶ。
    /// </summary>
    private void ResetCardLoadVisuals(Image img)
    {
        ApplyImageLoadVisual(img, GridImageLoadState.Reset);
    }

    /// <summary>画像ロード状態に応じて shimmer とエラー表示を反映する。</summary>
    private void ApplyImageLoadVisual(Image img, GridImageLoadState state, bool applyImageOpacity = true)
    {
        var visual = PhotoGridItemsLayoutLogic.ImageLoadVisual(state);
        if (applyImageOpacity)
            ElementCompositionPreview.GetElementVisual(img).Opacity = (float)visual.ImageOpacity;
        if (visual.StopShimmer) StopShimmer(img);
        if (visual.StartShimmer) StartShimmer(img);
        if (FindSibling(img, "ShimmerBase") is Border shimmerBase)
            shimmerBase.Opacity = visual.ShimmerBaseOpacity;
        if (FindSibling(img, "ShimmerHighlight") is Border shimmerHighlight)
            shimmerHighlight.Opacity = visual.ShimmerHighlightOpacity;
        if (FindSibling(img, "ErrorIcon") is TextBlock errorIcon)
            errorIcon.Visibility = visual.ErrorVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// shimmer プレースホルダを表示・アニメ開始する。ハイライト(ASurfaceHover の solid 面)を
    /// カード幅の 40% 幅で左から右へ 1500ms ループでスライドさせる。半透明ティントは使わない。
    /// </summary>
    private void StartShimmer(Image img)
    {
        var shimmerBase = FindSibling(img, "ShimmerBase") as Border;
        var shimmerHighlight = FindSibling(img, "ShimmerHighlight") as Border;
        if (shimmerBase is null || shimmerHighlight is null) return;

        var cardW = PhotoGridItemsLayoutLogic.ResolveShimmerCardWidth(currentImageWidth, img.ActualWidth);

        shimmerBase.Opacity = 1;
        shimmerHighlight.Opacity = 1;
        // 幅のみ明示指定し、高さは縦ストレッチ(均一セル高に追従)に任せる。
        var metrics = PhotoGridItemsLayoutLogic.CalculateShimmerMetrics(cardW);
        shimmerHighlight.Width = metrics.HighlightWidth;

        var shimmerVisual = ElementCompositionPreview.GetElementVisual(shimmerHighlight);
        var compositor = shimmerVisual.Compositor;
        var shimmerAnim = compositor.CreateScalarKeyFrameAnimation();
        shimmerAnim.InsertKeyFrame(0f, (float)metrics.StartOffset);
        shimmerAnim.InsertKeyFrame(1f, (float)metrics.EndOffset);
        shimmerAnim.Duration = TimeSpan.FromMilliseconds(PhotoGridItemsLayoutLogic.ShimmerDurationMilliseconds);
        shimmerAnim.IterationBehavior = AnimationIterationBehavior.Forever;
        shimmerVisual.StartAnimation("Offset.X", shimmerAnim);
    }

    /// <summary>shimmer アニメを止め、ベース・ハイライトを消す。</summary>
    private void StopShimmer(Image img)
    {
        if (FindSibling(img, "ShimmerHighlight") is Border shimmerHighlight)
        {
            try { ElementCompositionPreview.GetElementVisual(shimmerHighlight).StopAnimation("Offset.X"); }
            catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.StopShimmer: {ex}"); }
            shimmerHighlight.Opacity = 0;
        }
        if (FindSibling(img, "ShimmerBase") is Border shimmerBase)
            shimmerBase.Opacity = 0;
    }

    /// <summary>
    /// カード幅変更時に、表示中カードの shimmer ハイライトの寸法を再計算する。
    /// アニメ実行中(ロード前)のカードのみ対象。ロード済みカードは Opacity=0 で見えないので影響なし。
    /// </summary>
    private void RefreshActiveShimmerSizes(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Border b && PhotoGridItemsLayoutLogic.ShouldRefreshShimmerWidth(b.Name, b.Opacity))
            {
                var cardW = PhotoGridItemsLayoutLogic.ResolveShimmerCardWidth(currentImageWidth, b.ActualWidth);
                b.Width = PhotoGridItemsLayoutLogic.CalculateShimmerHighlightWidth(cardW);
            }
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++) stack.Push(VisualTreeHelper.GetChild(node, i));
        }
    }

    /// <summary>
    /// 同一カード内(ThumbHost Grid 配下)の名前付き兄弟要素を返す。
    /// DataTemplate 内の x:Name は code-behind から直接触れないため、Image の親 Grid を辿り
    /// 子を Name で探索する。
    /// </summary>
    private static FrameworkElement? FindSibling(Image img, string name)
    {
        if (VisualTreeHelper.GetParent(img) is not DependencyObject parent) return null;
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(parent, i) is FrameworkElement fe && fe.Name == name)
                return fe;
        }
        return null;
    }
}
