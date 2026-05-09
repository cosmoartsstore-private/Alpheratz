using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Alpheratz.Core;
using Alpheratz.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.Gallery;

public readonly record struct DatePresetRange(string from, string to);

public partial class GalleryDisplayState : UiThreadSafeObservableObject
{
    private const int CARD_WIDTH = 270;
    private const int STANDARD_GRID_COLUMN_COUNT = 6;
    private const int STANDARD_GRID_VISIBLE_ROW_COUNT = 4;

    [ObservableProperty] private ViewMode viewMode = ViewMode.standard;
    [ObservableProperty] private bool isMasonryEnabled;
    [ObservableProperty] private string? viewPreparationLabel;

    // TS: useGridDimensions
    [ObservableProperty] private double panelWidth = 800;
    [ObservableProperty] private double gridWrapperHeight = 600;

    private int viewPreparationTokenRef;
    private CancellationTokenSource? viewPreparationTimeoutRef;

    public string groupedPhotoLabel => "ワールド";
    public bool isGroupingUnavailableInMasonry => viewMode == ViewMode.gallery;

    public int measuredColumnCount => Math.Max(1, (int)Math.Floor(panelWidth / CARD_WIDTH));
    public double gridHeight => Math.Max(200, gridWrapperHeight);
    public int standardColumnCount => viewMode == ViewMode.standard ? STANDARD_GRID_COLUMN_COUNT : measuredColumnCount;
    public int standardColumnWidth => Math.Max(180, (int)Math.Floor(panelWidth / Math.Max(1, standardColumnCount)));
    public int standardRowHeight => Math.Max(150, (int)Math.Floor(gridHeight / STANDARD_GRID_VISIBLE_ROW_COUNT));

    // Hot path during binding refresh; tracing would drown the log.
    public IReadOnlyList<PhotoGridItem> buildDisplayPhotoItems(IReadOnlyList<PhotoThumbnailItem> displayPhotos)
        => displayPhotos.Select(photo => new PhotoGridItem { Photo = photo }).ToArray();

    public void rightPanelRef(double width)
    {
        AppLogger.Trace($"GalleryDisplayState.rightPanelRef: enter width={width}");
        try
        {
            PanelWidth = width;
            OnPropertyChanged(nameof(measuredColumnCount));
            OnPropertyChanged(nameof(standardColumnCount));
            OnPropertyChanged(nameof(standardColumnWidth));
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryDisplayState.rightPanelRef: threw: {ex}");
        }
        AppLogger.Trace("GalleryDisplayState.rightPanelRef: exit");
    }

    public void gridWrapperRef(double height)
    {
        AppLogger.Trace($"GalleryDisplayState.gridWrapperRef: enter height={height}");
        try
        {
            GridWrapperHeight = height;
            OnPropertyChanged(nameof(gridHeight));
            OnPropertyChanged(nameof(standardRowHeight));
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryDisplayState.gridWrapperRef: threw: {ex}");
        }
        AppLogger.Trace("GalleryDisplayState.gridWrapperRef: exit");
    }

    public int beginViewPreparation(string label)
    {
        AppLogger.Trace($"GalleryDisplayState.beginViewPreparation: enter label={label}");
        var nextToken = viewPreparationTokenRef + 1;
        viewPreparationTokenRef = nextToken;
        ViewPreparationLabel = label;
        AppLogger.Trace($"GalleryDisplayState.beginViewPreparation: exit token={nextToken}");
        return nextToken;
    }

    public void finishViewPreparation(int token)
    {
        AppLogger.Trace($"GalleryDisplayState.finishViewPreparation: enter token={token} current={viewPreparationTokenRef}");
        if (viewPreparationTokenRef == token)
        {
            ViewPreparationLabel = null;
            AppLogger.Trace("GalleryDisplayState.finishViewPreparation: cleared label");
        }
        AppLogger.Trace("GalleryDisplayState.finishViewPreparation: exit");
    }

    public void clearPendingViewPreparations()
    {
        AppLogger.Trace("GalleryDisplayState.clearPendingViewPreparations: enter");
        try
        {
            viewPreparationTimeoutRef?.Cancel();
            viewPreparationTimeoutRef?.Dispose();
            viewPreparationTimeoutRef = null;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryDisplayState.clearPendingViewPreparations: threw: {ex}");
        }
        AppLogger.Trace("GalleryDisplayState.clearPendingViewPreparations: exit");
    }

    public void prepareGroupingModeChange(GroupingMode currentGroupingMode, GroupingMode nextGroupingMode, Action<GroupingMode> setGroupingMode)
    {
        AppLogger.Trace($"GalleryDisplayState.prepareGroupingModeChange: enter current={currentGroupingMode} next={nextGroupingMode}");
        if (currentGroupingMode == nextGroupingMode)
        {
            AppLogger.Trace("GalleryDisplayState.prepareGroupingModeChange: skip (no change)");
            return;
        }

        try
        {
            clearPendingViewPreparations();
            setGroupingMode(nextGroupingMode);
            ViewPreparationLabel = null;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryDisplayState.prepareGroupingModeChange: threw: {ex}");
        }
        AppLogger.Trace("GalleryDisplayState.prepareGroupingModeChange: exit");
    }

    public void prepareDisplayFolderModeChange(DisplayFolderMode currentDisplayFolderMode, DisplayFolderMode nextDisplayFolderMode, Action<DisplayFolderMode> setDisplayFolderMode)
    {
        AppLogger.Trace($"GalleryDisplayState.prepareDisplayFolderModeChange: enter current={currentDisplayFolderMode} next={nextDisplayFolderMode}");
        if (currentDisplayFolderMode == nextDisplayFolderMode)
        {
            AppLogger.Trace("GalleryDisplayState.prepareDisplayFolderModeChange: skip (no change)");
            return;
        }

        try
        {
            clearPendingViewPreparations();
            setDisplayFolderMode(nextDisplayFolderMode);
            ViewPreparationLabel = null;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryDisplayState.prepareDisplayFolderModeChange: threw: {ex}");
        }
        AppLogger.Trace("GalleryDisplayState.prepareDisplayFolderModeChange: exit");
    }

    public static DatePresetRange getDateRangeFromPreset(DatePreset preset)
    {
        AppLogger.Trace($"GalleryDisplayState.getDateRangeFromPreset: enter preset={preset}");
        var today = DateTime.Today;

        static string formatDate(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var range = preset switch
        {
            DatePreset.today => new DatePresetRange(formatDate(today), formatDate(today)),
            DatePreset.last7days => new DatePresetRange(formatDate(today.AddDays(-6)), formatDate(today)),
            DatePreset.thisMonth => new DatePresetRange(
                formatDate(new DateTime(today.Year, today.Month, 1)),
                formatDate(new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)))),
            DatePreset.halfYear => new DatePresetRange(formatDate(today.AddMonths(-6)), formatDate(today)),
            DatePreset.oneYear => new DatePresetRange(formatDate(today.AddYears(-1)), formatDate(today)),
            DatePreset.lastMonth => new DatePresetRange(
                formatDate(new DateTime(today.Year, today.Month, 1).AddMonths(-1)),
                formatDate(new DateTime(today.Year, today.Month, 1).AddDays(-1))),
            _ => new DatePresetRange("", ""),
        };
        AppLogger.Trace($"GalleryDisplayState.getDateRangeFromPreset: exit from={range.from} to={range.to}");
        return range;
    }
}
