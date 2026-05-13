using System;
using Alpheratz.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// 標準グリッドのスクロール位置とスクロール可能範囲を保持するステート。
/// 写真枚数・列数・行高から論理的なグリッド全体高を算出し、ユーザのスクロール量を
/// 0 ～ maxScrollTop にクランプする責務を持つ。
/// (MasonryView は別途自前のスクロール管理を持っているのでこのステートは使わない)
/// </summary>
public partial class GalleryScrollState : UiThreadSafeObservableObject
{
    [ObservableProperty] private double scrollTop;
    private double pendingScrollTop;

    /// <summary>論理行数 (ceil(photos / columns))。</summary>
    public double totalRows { get; private set; }
    /// <summary>論理的なグリッド全高 (totalRows * ROW_HEIGHT)。</summary>
    public double totalHeight { get; private set; }
    /// <summary>スクロール可能な最大位置 (totalHeight - viewportHeight)。</summary>
    public double maxScrollTop { get; private set; }

    /// <summary>
    /// 写真数・列数・行高の変化に応じてスクロール範囲を再計算する。
    /// disableProgrammaticBounds=true のときは ScrollTop を強制クランプしない（ユーザが
    /// 慣性スクロール中など、UI 側で値を直接管理しているケース向け）。
    /// </summary>
    public void Recalculate(int photosLength, int columnCount, double gridHeight, double ROW_HEIGHT, bool disableProgrammaticBounds = false)
    {
        AppLogger.Trace($"GalleryScrollState.Recalculate: enter photos={photosLength} cols={columnCount} h={gridHeight} rowH={ROW_HEIGHT} dpb={disableProgrammaticBounds}");
        try
        {
            totalRows = Math.Ceiling(photosLength / (double)Math.Max(1, columnCount));
            totalHeight = totalRows * ROW_HEIGHT;
            maxScrollTop = Math.Max(0, totalHeight - gridHeight);

            if (disableProgrammaticBounds)
            {
                AppLogger.Trace("GalleryScrollState.Recalculate: exit (bounds disabled)");
                return;
            }

            var nextScrollTop = Math.Max(0, Math.Min(maxScrollTop, pendingScrollTop));
            pendingScrollTop = nextScrollTop;
            ScrollTop = nextScrollTop;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryScrollState.Recalculate: threw: {ex}");
        }
        AppLogger.Trace("GalleryScrollState.Recalculate: exit");
    }

    /// <summary>ScrollViewer の現在位置を受け取って ScrollTop を更新する。スクロール中の hot path。</summary>
    public void handleGridScroll(double currentScrollTop)
    {
        // Hot path during scroll; only trace meaningful state changes.
        try
        {
            pendingScrollTop = currentScrollTop;
            ScrollTop = pendingScrollTop;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryScrollState.handleGridScroll: threw: {ex}");
        }
    }

    /// <summary>マウスホイールデルタで ScrollTop を 0 ～ maxScrollTop にクランプして増減する。</summary>
    public void handleGridWheel(double deltaY, bool disableProgrammaticBounds = false)
    {
        // Hot path during wheel input; only error log on throw.
        try
        {
            if (disableProgrammaticBounds || maxScrollTop <= 0)
            {
                return;
            }

            var nextScrollTop = Math.Max(0, Math.Min(maxScrollTop, ScrollTop + deltaY));
            pendingScrollTop = nextScrollTop;
            ScrollTop = nextScrollTop;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryScrollState.handleGridWheel: threw: {ex}");
        }
    }

    /// <summary>ScrollViewer 再装着時に既存スクロール位置をクランプしつつ反映する。</summary>
    public void onGridRef(double currentScrollTop, bool disableProgrammaticBounds = false)
    {
        AppLogger.Trace($"GalleryScrollState.onGridRef: enter currentScrollTop={currentScrollTop} dpb={disableProgrammaticBounds}");
        try
        {
            if (disableProgrammaticBounds)
            {
                ScrollTop = currentScrollTop;
                AppLogger.Trace("GalleryScrollState.onGridRef: exit (bounds disabled)");
                return;
            }

            var nextScrollTop = Math.Max(0, Math.Min(maxScrollTop, pendingScrollTop));
            pendingScrollTop = nextScrollTop;
            ScrollTop = nextScrollTop;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryScrollState.onGridRef: threw: {ex}");
        }
        AppLogger.Trace("GalleryScrollState.onGridRef: exit");
    }
}
