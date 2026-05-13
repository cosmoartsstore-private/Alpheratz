using System;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Gallery.Controls;

public sealed partial class GalleryGridStage : UserControl
{
    public GalleryGridStage()
    {
        AppLogger.Trace("GalleryGridStage.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryGridStage.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("GalleryGridStage.ctor: exit");
    }

    public object? GridDataContext
    {
        get => PhotoGridControl.DataContext;
        set
        {
            AppLogger.Trace($"GalleryGridStage.GridDataContext: set to {value?.GetType().Name ?? "null"}");
            PhotoGridControl.DataContext = value;
            AppLogger.Trace("GalleryGridStage.GridDataContext: set done");
        }
    }

    public void SetGridItemsSource(object? source)
    {
        PhotoGridControl.SetItemsSource(source);
    }

    public Shared.Controls.PhotoGrid? PhotoGridControlRef => PhotoGridControl;
    public GalleryMasonryView? MasonryViewControlRef => MasonryViewControl;
    public MonthNav? MonthNavControlRef => MonthNavControl;

    public Visibility EmptyStateVisibility
    {
        get => EmptyStateControl.Visibility;
        set => EmptyStateControl.Visibility = value;
    }

    public void SetMasonryActive(bool active)
    {
        AppLogger.Trace($"GalleryGridStage.SetMasonryActive: enter active={active}");
        try
        {
            PhotoGridControl.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            MasonryViewControl.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            MonthNavControl.Visibility = Visibility.Visible;
            MonthNavColumn.Width = new GridLength(56);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryGridStage.SetMasonryActive: threw: {ex}");
        }
        AppLogger.Trace("GalleryGridStage.SetMasonryActive: exit");
    }

    /// <summary>
    /// ロード中スピナーと EmptyState を排他で出し分ける。
    /// - IsLoading=true → 写真グリッド + LoadingVeil
    /// - IsLoading=false && TotalCount=0 → EmptyState 表示・グリッド/Masonry/MonthNav 非表示
    /// - IsLoading=false && TotalCount>0 → グリッド表示のみ
    /// </summary>
    public void UpdateLoadingState(bool isLoading, int totalCount)
    {
        try
        {
            LoadingVeil.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            var showEmpty = !isLoading && totalCount == 0;
            EmptyStateControl.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
            MonthNavControl.Visibility = showEmpty ? Visibility.Collapsed : Visibility.Visible;
            MonthNavColumn.Width = showEmpty ? new GridLength(0) : new GridLength(56);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryGridStage.UpdateLoadingState: threw: {ex}");
        }
    }
}
