using System;
using System.Collections.Generic;
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
    private IReadOnlyDictionary<string, long> tagFilterCounts = new Dictionary<string, long>();
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

    public IReadOnlyDictionary<string, long> TagFilterCounts => tagFilterCounts;

    // バインディング更新で高頻度に読まれるため、通常ログは出さない。
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

    /// <summary>検索語、日付、ワールド、タグなど全フィルタを既定値へ戻す。</summary>
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

    /// <summary>文字列で受け取った日付プリセットを解析し、対応する日付範囲へ反映する。</summary>
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

    /// <summary>日付プリセットを選択し、DateFrom / DateTo を同時に更新する。</summary>
    public void handleDatePresetSelect(DatePreset preset)
    {
        AppLogger.Trace($"GalleryFiltersState.handleDatePresetSelect: enter preset={preset}");
        try
        {
            var range = GalleryDisplayState.getDateRangeFromPreset(preset);
            applyDateRange(
                preset,
                preset == DatePreset.none ? string.Empty : range.from,
                preset == DatePreset.none ? string.Empty : range.to);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryFiltersState.handleDatePresetSelect: threw: {ex}");
        }
        AppLogger.Trace("GalleryFiltersState.handleDatePresetSelect: exit");
    }

    /// <summary>日付範囲を一括更新し、フィルタ再読込通知を 1 回だけ発火する。</summary>
    public void applyDateRange(DatePreset preset, string from, string to)
    {
        AppLogger.Trace($"GalleryFiltersState.applyDateRange: enter preset={preset} from={from} to={to}");
        try
        {
            isBatchUpdating = true;
            DatePreset = preset;
            DateFrom = from;
            DateTo = to;
            isBatchUpdating = false;
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged("BatchCompleted");
        }
        catch (Exception ex)
        {
            isBatchUpdating = false;
            AppLogger.Error($"GalleryFiltersState.applyDateRange: threw: {ex}");
        }
        AppLogger.Trace("GalleryFiltersState.applyDateRange: exit");
    }

    /// <summary>タグ候補ごとの件数を差し替え、バインディングへ通知する。</summary>
    public void setTagFilterCounts(IReadOnlyDictionary<string, long> counts)
    {
        tagFilterCounts = counts ?? new Dictionary<string, long>();
        OnPropertyChanged(nameof(TagFilterCounts));
    }

}
