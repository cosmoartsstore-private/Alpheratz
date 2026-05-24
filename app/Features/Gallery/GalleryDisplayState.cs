using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    // ギャラリー領域の実測サイズ。MasonryView や標準グリッドの列数・行高計算に使う。
    // PhotoGrid 側の SizeChanged から rightPanelRef/gridWrapperRef 経由で更新される。
    [ObservableProperty] private double panelWidth = 800;
    [ObservableProperty] private double gridWrapperHeight = 600;

    public string groupedPhotoLabel => "ワールド";
    public bool isGroupingUnavailableInMasonry => ViewMode == ViewMode.gallery;

    public int measuredColumnCount => Math.Max(1, (int)Math.Floor(PanelWidth / CARD_WIDTH));
    public double gridHeight => Math.Max(200, GridWrapperHeight);
    public int standardColumnCount => ViewMode == ViewMode.standard ? STANDARD_GRID_COLUMN_COUNT : measuredColumnCount;
    public int standardColumnWidth => Math.Max(180, (int)Math.Floor(PanelWidth / Math.Max(1, standardColumnCount)));
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
            setGroupingMode(nextGroupingMode);
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
            setDisplayFolderMode(nextDisplayFolderMode);
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
