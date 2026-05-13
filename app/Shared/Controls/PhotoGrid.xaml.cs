using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Shared.Controls;

public sealed partial class PhotoGrid : UserControl
{
    public Func<Task>? OnGoToPrevPage { get; set; }
    public Func<Task>? OnGoToNextPage { get; set; }
    public Func<Task>? OnLoadMorePhotos { get; set; }
    public Action<double>? OnRightPanelMeasured { get; set; }
    public Action<double>? OnGridWrapperMeasured { get; set; }
    public Action<PhotoGridItem>? OnPhotoActivated { get; set; }
    public Action<PhotoGridItem>? OnFavoriteClicked { get; set; }
    public Action<double>? OnGridScroll { get; set; }
    public Action<int>? OnGridWheel { get; set; }
    public Action<int>? OnFirstVisibleIndexChanged { get; set; }
    public Action<double>? OnScrollbarTrackClick { get; set; }
    public Action<double>? OnScrollbarDrag { get; set; }

    public void SetItemsSource(object? source)
    {
        ItemsViewControl.SetItemsSource(source);
    }

    public PhotoGrid()
    {
        AppLogger.Trace("PhotoGrid.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoGrid.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("PhotoGrid.ctor: InitializeComponent done");
        try
        {
            SizeChanged += PhotoGrid_SizeChanged;

            CustomScrollbarControl.OnTrackClick = position => ScrollToTrackPosition(position);
            CustomScrollbarControl.OnDrag = position => ScrollToTrackPosition(position);
            ItemsViewControl.OnPhotoActivated = item => OnPhotoActivated?.Invoke(item);
            ItemsViewControl.OnFavoriteClicked = item => OnFavoriteClicked?.Invoke(item);
            ItemsViewControl.OnGridScroll = offset =>
            {
                OnGridScroll?.Invoke(offset);
                UpdateScrollbar();
            };
            ItemsViewControl.OnGridWheel = delta => OnGridWheel?.Invoke(delta);
            ItemsViewControl.OnFirstVisibleIndexChanged = idx => OnFirstVisibleIndexChanged?.Invoke(idx);
            ItemsViewControl.OnNearBottomReached = () => _ = OnLoadMorePhotos?.Invoke();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoGrid.ctor: wiring failed: {ex}");
            throw;
        }
        AppLogger.Trace("PhotoGrid.ctor: exit");
    }

    public void ScrollToTop()
    {
        try { ItemsViewControl.GridScrollViewerRef?.ChangeView(null, 0, null); }
        catch (Exception ex) { AppLogger.Error($"PhotoGrid.ScrollToTop: threw: {ex}"); }
    }

    public void ScrollToPhotoIndex(int index)
    {
        try
        {
            ItemsViewControl.ScrollToItemIndex(index);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGrid.ScrollToPhotoIndex: threw: {ex}"); }
    }

    private void ScrollToTrackPosition(double trackY)
    {
        try
        {
            var sv = ItemsViewControl.GridScrollViewerRef;
            if (sv is null) return;
            var extent = sv.ExtentHeight;
            var viewport = sv.ViewportHeight;
            if (extent <= viewport) return;
            var trackHeight = Math.Max(1, ActualHeight - 48);
            var ratio = Math.Clamp(trackY / trackHeight, 0, 1);
            sv.ChangeView(null, ratio * (extent - viewport), null, true);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGrid.ScrollToTrackPosition: threw: {ex}"); }
    }

    private void UpdateScrollbar()
    {
        try
        {
            var sv = ItemsViewControl.GridScrollViewerRef;
            if (sv is null) return;
            var extent = sv.ExtentHeight;
            var viewport = sv.ViewportHeight;

            if (extent <= 0 || viewport <= 0 || extent <= viewport)
            {
                CustomScrollbarControl.ThumbTop = 0;
                CustomScrollbarControl.ThumbHeight = viewport;
                return;
            }

            var trackHeight = Math.Max(1, ActualHeight - 48);
            var ratio = viewport / extent;
            CustomScrollbarControl.ThumbHeight = Math.Max(18, trackHeight * ratio);
            CustomScrollbarControl.ThumbTop = (sv.VerticalOffset / (extent - viewport)) * Math.Max(0, trackHeight - CustomScrollbarControl.ThumbHeight);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGrid.UpdateScrollbar: threw: {ex}"); }
    }

    private void PhotoGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        AppLogger.Trace($"PhotoGrid.PhotoGrid_SizeChanged: enter w={e.NewSize.Width} h={e.NewSize.Height}");
        try
        {
            OnRightPanelMeasured?.Invoke(e.NewSize.Width);
            OnGridWrapperMeasured?.Invoke(e.NewSize.Height);
            UpdateScrollbar();
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGrid.PhotoGrid_SizeChanged: threw: {ex}"); }
        AppLogger.Trace("PhotoGrid.PhotoGrid_SizeChanged: exit");
    }
}
