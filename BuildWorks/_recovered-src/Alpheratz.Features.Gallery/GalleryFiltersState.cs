using System;
using System.Collections.Generic;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Shared.Models;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Gallery;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class GalleryFiltersState : UiThreadSafeObservableObject
{
	private bool isBatchUpdating;

	private string searchQuery = string.Empty;

	private string debouncedQuery = string.Empty;

	private string dateFrom = string.Empty;

	private string dateTo = string.Empty;

	private DatePreset datePreset;

	private string orientationFilter = "all";

	private bool favoritesOnly;

	private IReadOnlyDictionary<string, long> tagFilterCounts = new Dictionary<string, long>();

	private GroupingMode groupingMode;

	private DisplayFolderMode displayFolderMode;

	private SortMode sortMode;

	public bool IsBatchUpdating => isBatchUpdating;

	public UiObservableCollection<string> worldFilters { get; } = new UiObservableCollection<string>();

	public UiObservableCollection<string> tagFilters { get; } = new UiObservableCollection<string>();

	public UiObservableCollection<WorldFilterOptionDto> worldFilterOptions { get; } = new UiObservableCollection<WorldFilterOptionDto>();

	public IReadOnlyDictionary<string, long> TagFilterCounts => tagFilterCounts;

	public string SearchQuery
	{
		get
		{
			return searchQuery;
		}
		set
		{
			SetProperty(ref searchQuery, value, "SearchQuery");
		}
	}

	public string DebouncedQuery
	{
		get
		{
			return debouncedQuery;
		}
		set
		{
			SetProperty(ref debouncedQuery, value, "DebouncedQuery");
		}
	}

	public string DateFrom
	{
		get
		{
			return dateFrom;
		}
		set
		{
			if (SetProperty(ref dateFrom, value, "DateFrom"))
			{
				OnPropertyChanged("ActiveFilterCount");
			}
		}
	}

	public string DateTo
	{
		get
		{
			return dateTo;
		}
		set
		{
			if (SetProperty(ref dateTo, value, "DateTo"))
			{
				OnPropertyChanged("ActiveFilterCount");
			}
		}
	}

	public DatePreset DatePreset
	{
		get
		{
			return datePreset;
		}
		set
		{
			if (SetProperty(ref datePreset, value, "DatePreset"))
			{
				OnPropertyChanged("ActiveFilterCount");
			}
		}
	}

	public string OrientationFilter
	{
		get
		{
			return orientationFilter;
		}
		set
		{
			if (SetProperty(ref orientationFilter, value, "OrientationFilter"))
			{
				OnPropertyChanged("ActiveFilterCount");
			}
		}
	}

	public bool FavoritesOnly
	{
		get
		{
			return favoritesOnly;
		}
		set
		{
			if (SetProperty(ref favoritesOnly, value, "FavoritesOnly"))
			{
				OnPropertyChanged("ActiveFilterCount");
			}
		}
	}

	public GroupingMode GroupingMode
	{
		get
		{
			return groupingMode;
		}
		set
		{
			SetProperty(ref groupingMode, value, "GroupingMode");
		}
	}

	public DisplayFolderMode DisplayFolderMode
	{
		get
		{
			return displayFolderMode;
		}
		set
		{
			SetProperty(ref displayFolderMode, value, "DisplayFolderMode");
		}
	}

	public SortMode SortMode
	{
		get
		{
			return sortMode;
		}
		set
		{
			SetProperty(ref sortMode, value, "SortMode");
		}
	}

	public int ActiveFilterCount
	{
		get
		{
			int num = 0;
			if (worldFilters.Count > 0)
			{
				num++;
			}
			if (!string.IsNullOrWhiteSpace(DateFrom) || !string.IsNullOrWhiteSpace(DateTo) || DatePreset != DatePreset.none)
			{
				num++;
			}
			if (OrientationFilter != "all")
			{
				num++;
			}
			if (FavoritesOnly)
			{
				num++;
			}
			if (tagFilters.Count > 0)
			{
				num++;
			}
			return num;
		}
	}

	public void setTagFilterCounts(IReadOnlyDictionary<string, long> counts)
	{
		tagFilterCounts = counts ?? new Dictionary<string, long>();
		OnPropertyChanged("TagFilterCounts");
	}

	public void resetFilters()
	{
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
			OnPropertyChanged("ActiveFilterCount");
			OnPropertyChanged("BatchCompleted");
		}
		catch (Exception value)
		{
			isBatchUpdating = false;
			AppLogger.Error($"GalleryFiltersState.resetFilters: threw: {value}");
		}
	}

	public void handleDatePresetSelect(string preset)
	{
		if (!Enum.TryParse<DatePreset>(preset, ignoreCase: true, out var result))
		{
			result = DatePreset.none;
		}
		handleDatePresetSelect(result);
	}

	public void handleDatePresetSelect(DatePreset preset)
	{
		try
		{
			DatePreset = preset;
			DatePresetRange dateRangeFromPreset = GalleryDisplayState.getDateRangeFromPreset(preset);
			DateFrom = dateRangeFromPreset.from;
			DateTo = dateRangeFromPreset.to;
			if (preset == DatePreset.none)
			{
				DateFrom = string.Empty;
				DateTo = string.Empty;
			}
			OnPropertyChanged("ActiveFilterCount");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFiltersState.handleDatePresetSelect: threw: {value}");
		}
	}

	public void applySearchCommands(SearchCommandResult commands)
	{
		if (!commands.HasCommands)
		{
			return;
		}
		try
		{
			isBatchUpdating = true;
			if (commands.DateFrom != null)
			{
				DateFrom = commands.DateFrom;
				DatePreset = DatePreset.custom;
			}
			if (commands.DateTo != null)
			{
				DateTo = commands.DateTo;
				DatePreset = DatePreset.custom;
			}
			if (commands.OrientationFilter != null)
			{
				OrientationFilter = commands.OrientationFilter;
			}
			if (commands.FavoritesOnly.HasValue)
			{
				FavoritesOnly = commands.FavoritesOnly.Value;
			}
			foreach (string tag in commands.Tags)
			{
				if (!tagFilters.Contains(tag))
				{
					tagFilters.Add(tag);
				}
			}
			if (commands.FolderMode != null)
			{
				string folderMode = commands.FolderMode;
				DisplayFolderMode displayFolderMode = ((folderMode == "primary") ? DisplayFolderMode.primary : ((folderMode == "secondary") ? DisplayFolderMode.secondary : DisplayFolderMode.all));
				DisplayFolderMode = displayFolderMode;
			}
			if (commands.SortMode != null)
			{
				string folderMode = commands.SortMode;
				SortMode sortMode = ((!(folderMode == "date") && folderMode == "world") ? SortMode.worldAsc : SortMode.dateDesc);
				SortMode = sortMode;
			}
			isBatchUpdating = false;
			OnPropertyChanged("ActiveFilterCount");
			OnPropertyChanged("BatchCompleted");
		}
		catch (Exception value)
		{
			isBatchUpdating = false;
			AppLogger.Error($"GalleryFiltersState.applySearchCommands: threw: {value}");
		}
	}
}
