using System;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Gallery.Controls;

public sealed partial class GalleryGridStage : UserControl
{
    /// <summary>
    /// MasonryView は x:Load=False で遅延初期化される。標準ビューしか使わないユーザでは
    /// 起動時にこの重いコントロール（Canvas + 仮想化マネージャ）が立ち上がらない。
    /// 最初に SetMasonryActive(true) が呼ばれたタイミングで FindName が走り、
    /// その後 OnMasonryRealized が一度だけ発火する。
    /// </summary>
    public Action<GalleryMasonryView>? OnMasonryRealized { get; set; }
    private bool masonryRealized;

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
    public GalleryMasonryView? MasonryViewControlRef => masonryRealized ? MasonryViewControl : null;
    public MonthNav? MonthNavControlRef => MonthNavControl;

    public Visibility EmptyStateVisibility
    {
        get => EmptyStateControl.Visibility;
        set => EmptyStateControl.Visibility = value;
    }

    /// <summary>
    /// Masonry を必要なときだけ生成する。最初の materialize で OnMasonryRealized が
    /// 1 度だけ呼ばれ、GalleryPage がコールバック結線を行う。
    /// </summary>
    private GalleryMasonryView? EnsureMasonryRealized()
    {
        if (masonryRealized) return MasonryViewControl;
        try
        {
            // x:Load=False の要素は FindName で実体化する。
            FindName(nameof(MasonryViewControl));
            masonryRealized = true;
            AppLogger.Trace("GalleryGridStage.EnsureMasonryRealized: materialized");
            if (MasonryViewControl is { } mv)
                OnMasonryRealized?.Invoke(mv);
            return MasonryViewControl;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryGridStage.EnsureMasonryRealized: threw: {ex}");
            return null;
        }
    }

    public void SetMasonryActive(bool active)
    {
        AppLogger.Trace($"GalleryGridStage.SetMasonryActive: enter active={active}");
        try
        {
            if (active)
            {
                var masonry = EnsureMasonryRealized();
                PhotoGridControl.Visibility = Visibility.Collapsed;
                if (masonry is not null) masonry.Visibility = Visibility.Visible;
            }
            else
            {
                PhotoGridControl.Visibility = Visibility.Visible;
                if (masonryRealized) MasonryViewControl.Visibility = Visibility.Collapsed;
            }
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
