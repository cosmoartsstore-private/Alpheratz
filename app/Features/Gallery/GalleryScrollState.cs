using System;
using Alpheratz.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.Gallery;

public partial class GalleryScrollState : UiThreadSafeObservableObject
{
    [ObservableProperty] private double scrollTop;
    private double pendingScrollTop;

    public double totalRows { get; private set; }
    public double totalHeight { get; private set; }
    public double maxScrollTop { get; private set; }

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
