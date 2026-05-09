using System;
using System.Collections.ObjectModel;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.Gallery;

public partial class GalleryFiltersState : UiThreadSafeObservableObject
{
    private bool isBatchUpdating;

    public bool IsBatchUpdating => isBatchUpdating;

    private string searchQuery = string.Empty;
    private string debouncedQuery = string.Empty;
    public UiObservableCollection<string> worldFilters { get; } = [];
    private string dateFrom = string.Empty;
    private string dateTo = string.Empty;
    private DatePreset datePreset = DatePreset.none;
    private string orientationFilter = "all";
    private bool favoritesOnly;
    public UiObservableCollection<string> tagFilters { get; } = [];
    public UiObservableCollection<WorldFilterOptionDto> worldFilterOptions { get; } = [];
    private GroupingMode groupingMode = GroupingMode.none;
    private DisplayFolderMode displayFolderMode = DisplayFolderMode.all;
    private SortMode sortMode = SortMode.dateDesc;

    public string SearchQuery
    {
        get => searchQuery;
        set
        {
            if (SetProperty(ref searchQuery, value))
            {
                OnPropertyChanged(nameof(ActiveFilterCount));
            }
        }
    }

    public string DebouncedQuery
    {
        get => debouncedQuery;
        set => SetProperty(ref debouncedQuery, value);
    }

    public string DateFrom
    {
        get => dateFrom;
        set
        {
            if (SetProperty(ref dateFrom, value))
            {
                OnPropertyChanged(nameof(ActiveFilterCount));
            }
        }
    }

    public string DateTo
    {
        get => dateTo;
        set
        {
            if (SetProperty(ref dateTo, value))
            {
                OnPropertyChanged(nameof(ActiveFilterCount));
            }
        }
    }

    public DatePreset DatePreset
    {
        get => datePreset;
        set
        {
            if (SetProperty(ref datePreset, value))
            {
                OnPropertyChanged(nameof(ActiveFilterCount));
            }
        }
    }

    public string OrientationFilter
    {
        get => orientationFilter;
        set
        {
            if (SetProperty(ref orientationFilter, value))
            {
                OnPropertyChanged(nameof(ActiveFilterCount));
            }
        }
    }

    public bool FavoritesOnly
    {
        get => favoritesOnly;
        set
        {
            if (SetProperty(ref favoritesOnly, value))
            {
                OnPropertyChanged(nameof(ActiveFilterCount));
            }
        }
    }

    public GroupingMode GroupingMode
    {
        get => groupingMode;
        set
        {
            if (SetProperty(ref groupingMode, value))
            {
                OnPropertyChanged(nameof(ActiveFilterCount));
            }
        }
    }

    public DisplayFolderMode DisplayFolderMode
    {
        get => displayFolderMode;
        set
        {
            if (SetProperty(ref displayFolderMode, value))
            {
                OnPropertyChanged(nameof(ActiveFilterCount));
            }
        }
    }

    public SortMode SortMode
    {
        get => sortMode;
        set => SetProperty(ref sortMode, value);
    }

    // Hot path during binding refresh; tracing omitted.
    public int ActiveFilterCount
    {
        get
        {
            var count = 0;

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                count++;
            }

            if (worldFilters.Count > 0)
            {
                count++;
            }

            if (!string.IsNullOrWhiteSpace(DateFrom) || !string.IsNullOrWhiteSpace(DateTo) || DatePreset != DatePreset.none)
            {
                count++;
            }

            if (OrientationFilter != "all")
            {
                count++;
            }

            if (FavoritesOnly)
            {
                count++;
            }

            if (tagFilters.Count > 0)
            {
                count++;
            }

            if (GroupingMode != GroupingMode.none)
            {
                count++;
            }

            if (DisplayFolderMode != DisplayFolderMode.all)
            {
                count++;
            }

            return count;
        }
    }

    public void resetFilters()
    {
        AppLogger.Trace("GalleryFiltersState.resetFilters: enter");
        try
        {
            isBatchUpdating = true;
            SearchQuery = string.Empty;
            DebouncedQuery = string.Empty;
            worldFilters.Clear();
            DateFrom = string.Empty;
            DateTo = string.Empty;
            DatePreset = DatePreset.none;
            OrientationFilter = "all";
            FavoritesOnly = false;
            tagFilters.Clear();
            GroupingMode = GroupingMode.none;
            DisplayFolderMode = DisplayFolderMode.all;
            SortMode = SortMode.dateDesc;
            isBatchUpdating = false;
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged("BatchCompleted");
        }
        catch (Exception ex)
        {
            isBatchUpdating = false;
            AppLogger.Error($"GalleryFiltersState.resetFilters: threw: {ex}");
        }
        AppLogger.Trace("GalleryFiltersState.resetFilters: exit");
    }

    public void handleDatePresetSelect(string preset)
    {
        AppLogger.Trace($"GalleryFiltersState.handleDatePresetSelect(string): enter preset={preset}");
        if (!Enum.TryParse<DatePreset>(preset, ignoreCase: true, out var nextPreset))
        {
            AppLogger.Trace("GalleryFiltersState.handleDatePresetSelect(string): unparseable, defaulting to none");
            nextPreset = DatePreset.none;
        }

        handleDatePresetSelect(nextPreset);
        AppLogger.Trace("GalleryFiltersState.handleDatePresetSelect(string): exit");
    }

    public void handleDatePresetSelect(DatePreset preset)
    {
        AppLogger.Trace($"GalleryFiltersState.handleDatePresetSelect: enter preset={preset}");
        try
        {
            DatePreset = preset;

            var range = GalleryDisplayState.getDateRangeFromPreset(preset);
            DateFrom = range.from;
            DateTo = range.to;

            if (preset == DatePreset.none)
            {
                DateFrom = string.Empty;
                DateTo = string.Empty;
            }

            OnPropertyChanged(nameof(ActiveFilterCount));
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryFiltersState.handleDatePresetSelect: threw: {ex}");
        }
        AppLogger.Trace("GalleryFiltersState.handleDatePresetSelect: exit");
    }
}
