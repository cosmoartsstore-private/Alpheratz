using System;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Gallery.Controls;

/// <summary>標準グリッド、Masonry、MonthNav、EmptyState を束ねるギャラリー表示ステージ。</summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
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

    // ギャラリー表示領域を初期化する。MasonryView は必要時まで実体化しない。
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

    /// <summary>標準グリッドの DataContext を外部から差し替えるためのプロパティ。</summary>
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

    // 標準グリッドに表示する ItemsSource を渡す。
    public void SetGridItemsSource(object? source)
    {
        PhotoGridControl.SetItemsSource(source);
    }

    /// <summary>GalleryPage が標準グリッドへ callback を結線するための参照。</summary>
    public Shared.Controls.PhotoGrid? PhotoGridControlRef => PhotoGridControl;
    /// <summary>実体化済みのときだけ GalleryPage へ MasonryView 参照を返す。</summary>
    public GalleryMasonryView? MasonryViewControlRef => masonryRealized ? MasonryViewControl : null;
    /// <summary>GalleryPage が月グループを同期するための MonthNav 参照。</summary>
    public MonthNav? MonthNavControlRef => MonthNavControl;

    /// <summary>外部から EmptyState の表示状態を読み書きするためのプロパティ。</summary>
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

    // 表示モードに応じて標準グリッドと MasonryView の表示を切り替える。
    public void SetMasonryActive(bool active)
    {
        AppLogger.Trace($"GalleryGridStage.SetMasonryActive: enter active={active}");
        try
        {
            var state = GalleryGridStageLogic.ViewModeDisplay(active);
            if (active)
            {
                var masonry = EnsureMasonryRealized();
                PhotoGridControl.Visibility = ToVisibility(state.PhotoGridVisible);
                if (masonry is not null) masonry.Visibility = ToVisibility(state.MasonryVisible);
            }
            else
            {
                PhotoGridControl.Visibility = ToVisibility(state.PhotoGridVisible);
                if (masonryRealized) MasonryViewControl.Visibility = ToVisibility(state.MasonryVisible);
            }
            ApplyMonthNav(state.MonthNavVisible, state.MonthNavWidth);
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
            var state = GalleryGridStageLogic.LoadingDisplay(isLoading, totalCount);
            LoadingVeil.Visibility = ToVisibility(state.LoadingVisible);
            EmptyStateControl.Visibility = ToVisibility(state.EmptyVisible);
            ApplyMonthNav(state.MonthNavVisible, state.MonthNavWidth);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryGridStage.UpdateLoadingState: threw: {ex}");
        }
    }

    /// <summary>bool の表示状態を WinUI の Visibility へ変換する。</summary>
    private static Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>MonthNav の表示状態と列幅をまとめて適用する。</summary>
    private void ApplyMonthNav(bool visible, double width)
    {
        MonthNavControl.Visibility = ToVisibility(visible);
        MonthNavColumn.Width = new GridLength(width);
    }
}
