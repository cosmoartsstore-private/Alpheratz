using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Shared.Controls;

/// <summary>標準グリッド表示とカスタムスクロールバーを束ねる共有コントロール。</summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
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

    // 写真一覧の ItemsSource を内部の GridView ラッパーへ渡す。
    public void SetItemsSource(object? source)
    {
        ItemsViewControl.SetItemsSource(source);
    }

    // 標準グリッドを初期化し、内部コントロールのイベントを外部コールバックへ中継する。
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

    // 内部 ScrollViewer の位置を先頭へ戻す。
    public void ScrollToTop()
    {
        try { ItemsViewControl.GridScrollViewerRef?.ChangeView(null, 0, null); }
        catch (Exception ex) { AppLogger.Error($"PhotoGrid.ScrollToTop: threw: {ex}"); }
    }

    // 指定インデックスの写真へ内部一覧をスクロールする。
    public void ScrollToPhotoIndex(int index)
    {
        try
        {
            ItemsViewControl.ScrollToItemIndex(index);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGrid.ScrollToPhotoIndex: threw: {ex}"); }
    }

    // カスタムスクロールバー上の Y 位置を ScrollViewer の縦オフセットへ変換する。
    private void ScrollToTrackPosition(double trackY)
    {
        try
        {
            var sv = ItemsViewControl.GridScrollViewerRef;
            if (sv is null) return;
            var extent = sv.ExtentHeight;
            var viewport = sv.ViewportHeight;
            var offset = PhotoGridLogic.TrackPositionToOffset(trackY, ActualHeight, extent, viewport);
            if (offset is null) return;
            sv.ChangeView(null, offset.Value, null, true);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGrid.ScrollToTrackPosition: threw: {ex}"); }
    }

    // 内部 ScrollViewer の表示範囲からカスタム Thumb の位置と高さを更新する。
    private void UpdateScrollbar()
    {
        try
        {
            var sv = ItemsViewControl.GridScrollViewerRef;
            if (sv is null) return;
            var extent = sv.ExtentHeight;
            var viewport = sv.ViewportHeight;
            var thumb = PhotoGridLogic.ScrollbarThumb(ActualHeight, extent, viewport, sv.VerticalOffset);
            CustomScrollbarControl.ThumbTop = thumb.Top;
            CustomScrollbarControl.ThumbHeight = thumb.Height;
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGrid.UpdateScrollbar: threw: {ex}"); }
    }

    // グリッド寸法の変化を親へ通知し、スクロールバー表示も再計算する。
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
