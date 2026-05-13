using System;
using System.Numerics;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Shared.Controls;

public sealed partial class PhotoGridItemsView : UserControl
{
    private const int COLUMN_COUNT = 5;
    private const double IMAGE_ASPECT_H = 9.0 / 16.0;
    private const double INFO_HEIGHT = 56;
    private const double CARD_MARGIN_H = 8;
    private const double CARD_MARGIN_V = 12;
    private const double GRID_PADDING = 24;

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
        AppLogger.Trace("PhotoGridItemsView.ctor: exit");
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

    public ScrollViewer GridScrollViewerRef =>
        internalScrollViewer ?? FindChildScrollViewer(PhotoItems) ?? new ScrollViewer();

    private void RecalculateCardSize(double availableWidth)
    {
        if (availableWidth <= 0 || wrapGrid is null) return;
        var usable = availableWidth - GRID_PADDING;
        var cardW = Math.Floor(usable / COLUMN_COUNT - CARD_MARGIN_H);
        if (cardW < 100) return;
        wrapGrid.ItemWidth = cardW + CARD_MARGIN_H;
        wrapGrid.ItemHeight = Math.Floor(cardW * IMAGE_ASPECT_H + INFO_HEIGHT) + CARD_MARGIN_V;
        wrapGrid.MaximumRowsOrColumns = COLUMN_COUNT;
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
        var cols = Math.Max(1, wrapGrid.MaximumRowsOrColumns > 0 ? wrapGrid.MaximumRowsOrColumns : COLUMN_COUNT);
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
    /// FrameworkElement の ActualTheme に対応した ThemeDictionaries からブラシを取り出す。
    /// Application.Current.Resources["X"] では ThemeDictionaries 内のキーは解決されないため、
    /// 明示的に ThemeDictionaries 経由で取得する必要がある。
    /// </summary>
    private static Brush? ResolveThemeBrush(FrameworkElement element, string key)
    {
        try
        {
            var themeKey = element.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
            if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var raw)
                && raw is ResourceDictionary dict
                && dict.TryGetValue(key, out var value)
                && value is Brush brush)
                return brush;
        }
        catch { }
        return null;
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
            if (ResolveThemeBrush(border, "ABorder") is { } restBorder)
                border.BorderBrush = restBorder;
            if (ResolveThemeBrush(border, "ASurface") is { } restFill)
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
            if (ResolveThemeBrush(border, "ABorderStrong") is { } hoverBorder)
                border.BorderBrush = hoverBorder;
            if (ResolveThemeBrush(border, "ASurfaceHover") is { } hoverFill)
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

            if (ResolveThemeBrush(border, "ABorder") is { } restBorder)
                border.BorderBrush = restBorder;
            if (ResolveThemeBrush(border, "ASurface") is { } restFill)
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

            SetImageSource(img, item.Photo);

            System.ComponentModel.PropertyChangedEventHandler handler = (s, e) =>
            {
                if (e.PropertyName is nameof(PhotoThumbnailItem.GridThumbPath)
                    or nameof(PhotoThumbnailItem.EffectiveSourcePath))
                {
                    DispatcherQueue?.TryEnqueue(() => SetImageSource(img, item.Photo));
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

    private void ThumbImage_Failed(object sender, ExceptionRoutedEventArgs e)
    {
        AppLogger.Error($"PhotoGridItemsView.ThumbImage_Failed: {e.ErrorMessage}");
    }
}