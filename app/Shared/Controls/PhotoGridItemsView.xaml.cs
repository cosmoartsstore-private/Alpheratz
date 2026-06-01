using System;
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

public sealed partial class PhotoGridItemsView : UserControl
{
    // 標準グリッドの列数はユーザ要望により 5 列固定。利用可能幅からの算出はやめ、
    // cardW = floor(usable / 5 - CARD_MARGIN_H) で 5 列前提のカード幅を決める。
    // ウィンドウが極端に狭いとカードが小さくなるが、5 列固定を優先する(cardW<100 の
    // 最小ガードは維持)。
    private const int FIXED_COLUMNS = 5;
    // H-4b: 一律 9:16(縦長強制)をやめ 4:3 に緩和。UniformToFill のままでも横長写真の
    // クロップ量が大きく減り、横写真の識別性が上がる。均一セル制約のため一律比率とする。
    private const double IMAGE_ASPECT_H = 3.0 / 4.0;
    private const double INFO_HEIGHT = 56;
    private const double CARD_MARGIN_H = 8;
    private const double CARD_MARGIN_V = 12;
    private const double GRID_PADDING = 24;
    // ReportFirstVisibleIndex のフォールバック列数。実際は wrapGrid.MaximumRowsOrColumns を優先。
    private const int FALLBACK_COLUMN_COUNT = 5;

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
        Unloaded += PhotoGridItemsView_Unloaded;
        // PhotoCardBorderStyle は {ThemeResource} でテーマ追従設計だが、hover/recycle で
        // code-behind が border.Background/BorderBrush に直接代入するため、その瞬間に
        // {ThemeResource} バインディングが local value で上書きされ、以降テーマ切替に
        // 追従しなくなる。テーマ切替時に rest 色を再適用してこれを補正する。
        ActualThemeChanged += OnActualThemeChanged;
        AppLogger.Trace("PhotoGridItemsView.ctor: exit");
    }

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

    private void PhotoGridItemsView_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            UnsubscribeAllCards(PhotoItems);
            ActualThemeChanged -= OnActualThemeChanged;
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.PhotoGridItemsView_Unloaded: {ex}"); }
    }

    /// <summary>
    /// VisualTree を辿って、Image.Tag に保持されている GridImageSubscription を全て解除する。
    /// PhotoThumbnailItem.PropertyChanged に残ったハンドラを切ることで、Page 破棄後も
    /// Photo オブジェクトが View 側からの参照で GC されずに残るのを防ぐ。
    /// </summary>
    private static void UnsubscribeAllCards(DependencyObject root)
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
                img.Source = null;
            }
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++) stack.Push(VisualTreeHelper.GetChild(node, i));
        }
    }

    private void PhotoItems_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var sv = FindChildScrollViewer(PhotoItems);
            if (sv is null) return;
            internalScrollViewer = sv;
            sv.ViewChanged += InternalScrollViewer_ViewChanged;
            sv.PointerWheelChanged += InternalScrollViewer_PointerWheelChanged;
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.PhotoItems_Loaded: {ex}"); }
    }

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
    /// 旧実装は毎呼び出しで `new ScrollViewer()` を fallback 生成しており、
    /// それ自体は VisualTree に組み込まれない無意味なオブジェクトだったため除去。
    /// 取得失敗時は null を返し、呼出側で no-op を選べるようにする。
    /// </summary>
    public ScrollViewer? GridScrollViewerRef
    {
        get
        {
            if (internalScrollViewer is not null) return internalScrollViewer;
            internalScrollViewer = FindChildScrollViewer(PhotoItems);
            return internalScrollViewer;
        }
    }

    /// <summary>
    /// H-4a: 直近に算出した画像領域の幅。shimmer ハイライトの幅(カード幅の 40%)を
    /// カード幅可変に追従させるため StartShimmer / RefreshActiveShimmerSizes で参照する。
    /// </summary>
    private double currentImageWidth;

    private void RecalculateCardSize(double availableWidth)
    {
        if (availableWidth <= 0 || wrapGrid is null) return;
        var usable = availableWidth - GRID_PADDING;
        if (usable <= 0) return;
        // 列数は 5 列固定。利用可能幅を 5 等分してカード幅を決める。
        var columns = FIXED_COLUMNS;
        var cardW = Math.Floor(usable / columns - CARD_MARGIN_H);
        if (cardW < 100) return;
        currentImageWidth = cardW;
        wrapGrid.ItemWidth = cardW + CARD_MARGIN_H;
        wrapGrid.ItemHeight = Math.Floor(cardW * IMAGE_ASPECT_H + INFO_HEIGHT) + CARD_MARGIN_V;
        wrapGrid.MaximumRowsOrColumns = columns;
        // 既に実体化済みのカードの shimmer ハイライト幅も新カード幅に合わせて更新する。
        RefreshActiveShimmerSizes(PhotoItems);
    }

    private void PhotoItemsWrapGrid_Loaded(object sender, RoutedEventArgs e)
    {
        wrapGrid = sender as ItemsWrapGrid;
        var width = PhotoItems.ActualWidth > 0 ? PhotoItems.ActualWidth : lastKnownWidth;
        RecalculateCardSize(width);
    }

    private void GridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        lastKnownWidth = e.NewSize.Width;
        if (wrapGrid is null)
        {
            wrapGrid = FindItemsWrapGrid(PhotoItems);
        }
        RecalculateCardSize(e.NewSize.Width);
    }

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

    public void ScrollToItemIndex(int index)
    {
        try
        {
            if (index < 0 || index >= PhotoItems.Items.Count) return;
            var item = PhotoItems.Items[index];
            PhotoItems.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoGridItemsView.ScrollToItemIndex: threw: {ex}");
        }
    }

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

    private void FavoriteStar_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is AnimatedFavoriteStar star && star.DataContext is PhotoGridItem item)
            {
                star.OnClick = () => OnFavoriteClicked?.Invoke(item);
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.FavoriteStar_Loaded: threw: {ex}"); }
    }

    private void InternalScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        try
        {
            if (sender is ScrollViewer scrollViewer)
            {
                OnGridScroll?.Invoke(scrollViewer.VerticalOffset);
                if (scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset < 600)
                {
                    OnNearBottomReached?.Invoke();
                }
                ReportFirstVisibleIndex(scrollViewer.VerticalOffset);
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.InternalScrollViewer_ViewChanged: {ex}"); }
    }

    private void ReportFirstVisibleIndex(double scrollTop)
    {
        if (wrapGrid is null) return;
        var itemH = wrapGrid.ItemHeight;
        if (itemH <= 0) return;
        var cols = Math.Max(1, wrapGrid.MaximumRowsOrColumns > 0 ? wrapGrid.MaximumRowsOrColumns : FALLBACK_COLUMN_COUNT);
        var row = (int)(scrollTop / itemH);
        var firstIdx = row * cols;
        if (firstIdx == lastReportedFirstVisible) return;
        lastReportedFirstVisible = firstIdx;
        OnFirstVisibleIndexChanged?.Invoke(firstIdx);
    }

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
            if (ThemeHelper.Brush(border, "ABorder") is { } restBorder)
                border.BorderBrush = restBorder;
            if (ThemeHelper.Brush(border, "ASurface") is { } restFill)
                border.Background = restFill;
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.CardBorder_DataContextChanged: {ex}"); }
    }

    private void CardBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is not Border border) return;
            var visual = ElementCompositionPreview.GetElementVisual(border);
            var compositor = visual.Compositor;
            var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
            var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
            offsetAnim.InsertKeyFrame(1f, new Vector3(0, -2f, 0), ease);
            offsetAnim.Duration = TimeSpan.FromMilliseconds(180);
            visual.StartAnimation("Offset", offsetAnim);

            // 色フィードバック: 枠線をアクセント寄りに、背景をわずかに持ち上げる。
            // Y オフセットだけだと視覚的フィードバックが弱いため。
            if (ThemeHelper.Brush(border, "ABorderStrong") is { } hoverBorder)
                border.BorderBrush = hoverBorder;
            if (ThemeHelper.Brush(border, "ASurfaceHover") is { } hoverFill)
                border.Background = hoverFill;
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.CardBorder_PointerEntered: {ex}"); }
    }

    private void CardBorder_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is not Border border) return;
            var visual = ElementCompositionPreview.GetElementVisual(border);
            var compositor = visual.Compositor;
            var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
            var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
            offsetAnim.InsertKeyFrame(1f, Vector3.Zero, ease);
            offsetAnim.Duration = TimeSpan.FromMilliseconds(180);
            visual.StartAnimation("Offset", offsetAnim);

            if (ThemeHelper.Brush(border, "ABorder") is { } restBorder)
                border.BorderBrush = restBorder;
            if (ThemeHelper.Brush(border, "ASurface") is { } restFill)
                border.Background = restFill;
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.CardBorder_PointerExited: {ex}"); }
    }

    /// <summary>
    /// R2-A-5: Image.Tag に (Photo, handler) のタプルを保存することで、
    /// DataContext が null や別オブジェクトに差し替わっても確実に元の Photo から
    /// PropertyChanged を unsubscribe できるようにし、ハンドラリークを防止する。
    /// </summary>
    private sealed record GridImageSubscription(PhotoThumbnailItem Photo, System.ComponentModel.PropertyChangedEventHandler Handler);

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

            // H-4c: recycle で別の写真が入るたびにロード演出を初期状態へ戻す。
            // (画像 Opacity を 0 に、shimmer を再開、エラーアイコンを隠す)
            ResetCardLoadVisuals(img);
            SetImageSource(img, item.Photo);

            System.ComponentModel.PropertyChangedEventHandler handler = (s, e) =>
            {
                if (e.PropertyName is nameof(PhotoThumbnailItem.GridThumbPath)
                    or nameof(PhotoThumbnailItem.EffectiveSourcePath))
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        // サムネイル生成完了でパスが差し替わった場合も再ロード扱いにし、
                        // shimmer→フェードインの演出を改めて適用する。
                        ResetCardLoadVisuals(img);
                        SetImageSource(img, item.Photo);
                    });
                }
            };
            item.Photo.PropertyChanged += handler;
            img.Tag = new GridImageSubscription(item.Photo, handler);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.ThumbImage_DataContextChanged: {ex}"); }
    }

    private static void SetImageSource(Image img, PhotoThumbnailItem photo)
    {
        var path = photo.EffectiveSourcePath;
        if (string.IsNullOrEmpty(path))
        {
            img.Source = null;
            return;
        }
        img.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
        {
            CreateOptions = Microsoft.UI.Xaml.Media.Imaging.BitmapCreateOptions.IgnoreImageCache,
            DecodePixelWidth = 300,
            DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical,
            UriSource = new Uri(path, UriKind.Absolute),
        };
    }

    /// <summary>
    /// H-4c: 画像ロード完了。マソンリーと同じく 200ms でフェードインし shimmer を止める。
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
            fadeIn.Duration = TimeSpan.FromMilliseconds(200);
            imageVisual.StartAnimation("Opacity", fadeIn);

            StopShimmer(img);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.ThumbImage_Opened: {ex}"); }
    }

    private void ThumbImage_Failed(object sender, ExceptionRoutedEventArgs e)
    {
        AppLogger.Error($"PhotoGridItemsView.ThumbImage_Failed: {e.ErrorMessage}");
        try
        {
            if (sender is not Image img) return;
            StopShimmer(img);
            // 単色フォールバック面を残しつつエラーアイコンを表示する。
            if (FindSibling(img, "ShimmerBase") is Border shimmerBase)
                shimmerBase.Opacity = 1;
            if (FindSibling(img, "ErrorIcon") is TextBlock errorIcon)
                errorIcon.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridItemsView.ThumbImage_Failed.fallback: {ex}"); }
    }

    /// <summary>
    /// H-4c: ロード演出を初期状態へ戻す。画像 Opacity=0、エラーアイコン非表示、shimmer 再開。
    /// recycle / サムネイルパス差し替えの双方から呼ぶ。
    /// </summary>
    private void ResetCardLoadVisuals(Image img)
    {
        ElementCompositionPreview.GetElementVisual(img).Opacity = 0f;
        if (FindSibling(img, "ErrorIcon") is TextBlock errorIcon)
            errorIcon.Visibility = Visibility.Collapsed;
        StartShimmer(img);
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

        var cardW = currentImageWidth > 0 ? currentImageWidth : img.ActualWidth;
        if (cardW <= 0) cardW = 300;

        shimmerBase.Opacity = 1;
        shimmerHighlight.Opacity = 1;
        // 幅のみ明示指定し、高さは縦ストレッチ(均一セル高に追従)に任せる。
        shimmerHighlight.Width = cardW * 0.4;

        var shimmerVisual = ElementCompositionPreview.GetElementVisual(shimmerHighlight);
        var compositor = shimmerVisual.Compositor;
        var shimmerAnim = compositor.CreateScalarKeyFrameAnimation();
        shimmerAnim.InsertKeyFrame(0f, (float)(-cardW * 0.4));
        shimmerAnim.InsertKeyFrame(1f, (float)cardW);
        shimmerAnim.Duration = TimeSpan.FromMilliseconds(1500);
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
    /// H-4a: カード幅変更時に、表示中カードの shimmer ハイライトの寸法を再計算する。
    /// アニメ実行中(ロード前)のカードのみ対象。ロード済みカードは Opacity=0 で見えないので影響なし。
    /// </summary>
    private void RefreshActiveShimmerSizes(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Border b && b.Name == "ShimmerHighlight" && b.Opacity > 0)
            {
                var cardW = currentImageWidth > 0 ? currentImageWidth : b.ActualWidth;
                if (cardW > 0) b.Width = cardW * 0.4;
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
