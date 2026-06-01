using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using Alpheratz.Core;
using Alpheratz.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Gallery;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class GalleryDisplayState : UiThreadSafeObservableObject
{
	private const int CARD_WIDTH = 270;

	private const int STANDARD_GRID_COLUMN_COUNT = 6;

	private const int STANDARD_GRID_VISIBLE_ROW_COUNT = 4;

	[ObservableProperty]
	private ViewMode viewMode;

	[ObservableProperty]
	private bool isMasonryEnabled;

	[ObservableProperty]
	private string? viewPreparationLabel;

	[ObservableProperty]
	private double panelWidth = 800.0;

	[ObservableProperty]
	private double gridWrapperHeight = 600.0;

	private int viewPreparationTokenRef;

	private CancellationTokenSource? viewPreparationTimeoutRef;

	public string groupedPhotoLabel => "ワールド";

	public bool isGroupingUnavailableInMasonry => ViewMode == ViewMode.gallery;

	public int measuredColumnCount => Math.Max(1, (int)Math.Floor(PanelWidth / 270.0));

	public double gridHeight => Math.Max(200.0, GridWrapperHeight);

	public int standardColumnCount
	{
		get
		{
			if (ViewMode != ViewMode.standard)
			{
				return measuredColumnCount;
			}
			return 6;
		}
	}

	public int standardColumnWidth => Math.Max(180, (int)Math.Floor(PanelWidth / (double)Math.Max(1, standardColumnCount)));

	public int standardRowHeight => Math.Max(150, (int)Math.Floor(gridHeight / 4.0));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ViewMode ViewMode
	{
		get
		{
			return viewMode;
		}
		set
		{
			if (!EqualityComparer<ViewMode>.Default.Equals(viewMode, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ViewMode);
				viewMode = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ViewMode);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsMasonryEnabled
	{
		get
		{
			return isMasonryEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(isMasonryEnabled, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsMasonryEnabled);
				isMasonryEnabled = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsMasonryEnabled);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? ViewPreparationLabel
	{
		get
		{
			return viewPreparationLabel;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(viewPreparationLabel, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ViewPreparationLabel);
				viewPreparationLabel = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ViewPreparationLabel);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public double PanelWidth
	{
		get
		{
			return panelWidth;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(panelWidth, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PanelWidth);
				panelWidth = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PanelWidth);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public double GridWrapperHeight
	{
		get
		{
			return gridWrapperHeight;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(gridWrapperHeight, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GridWrapperHeight);
				gridWrapperHeight = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GridWrapperHeight);
			}
		}
	}

	public IReadOnlyList<PhotoGridItem> buildDisplayPhotoItems(IReadOnlyList<PhotoThumbnailItem> displayPhotos)
	{
		return displayPhotos.Select((PhotoThumbnailItem photo) => new PhotoGridItem
		{
			Photo = photo
		}).ToArray();
	}

	public void rightPanelRef(double width)
	{
		try
		{
			PanelWidth = width;
			OnPropertyChanged("measuredColumnCount");
			OnPropertyChanged("standardColumnCount");
			OnPropertyChanged("standardColumnWidth");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryDisplayState.rightPanelRef: threw: {value}");
		}
	}

	public void gridWrapperRef(double height)
	{
		try
		{
			GridWrapperHeight = height;
			OnPropertyChanged("gridHeight");
			OnPropertyChanged("standardRowHeight");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryDisplayState.gridWrapperRef: threw: {value}");
		}
	}

	public int beginViewPreparation(string label)
	{
		int result = ++viewPreparationTokenRef;
		ViewPreparationLabel = label;
		return result;
	}

	public void finishViewPreparation(int token)
	{
		if (viewPreparationTokenRef == token)
		{
			ViewPreparationLabel = null;
		}
	}

	public void clearPendingViewPreparations()
	{
		try
		{
			viewPreparationTimeoutRef?.Cancel();
			viewPreparationTimeoutRef?.Dispose();
			viewPreparationTimeoutRef = null;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryDisplayState.clearPendingViewPreparations: threw: {value}");
		}
	}

	public void prepareGroupingModeChange(GroupingMode currentGroupingMode, GroupingMode nextGroupingMode, Action<GroupingMode> setGroupingMode)
	{
		if (currentGroupingMode == nextGroupingMode)
		{
			return;
		}
		try
		{
			clearPendingViewPreparations();
			setGroupingMode(nextGroupingMode);
			ViewPreparationLabel = null;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryDisplayState.prepareGroupingModeChange: threw: {value}");
		}
	}

	public void prepareDisplayFolderModeChange(DisplayFolderMode currentDisplayFolderMode, DisplayFolderMode nextDisplayFolderMode, Action<DisplayFolderMode> setDisplayFolderMode)
	{
		if (currentDisplayFolderMode == nextDisplayFolderMode)
		{
			return;
		}
		try
		{
			clearPendingViewPreparations();
			setDisplayFolderMode(nextDisplayFolderMode);
			ViewPreparationLabel = null;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryDisplayState.prepareDisplayFolderModeChange: threw: {value}");
		}
	}

	public static DatePresetRange getDateRangeFromPreset(DatePreset preset)
	{
		DateTime today = DateTime.Today;
		return preset switch
		{
			DatePreset.today => new DatePresetRange(formatDate(today), formatDate(today)), 
			DatePreset.last7days => new DatePresetRange(formatDate(today.AddDays(-6.0)), formatDate(today)), 
			DatePreset.thisMonth => new DatePresetRange(formatDate(new DateTime(today.Year, today.Month, 1)), formatDate(new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)))), 
			DatePreset.halfYear => new DatePresetRange(formatDate(today.AddMonths(-6)), formatDate(today)), 
			DatePreset.oneYear => new DatePresetRange(formatDate(today.AddYears(-1)), formatDate(today)), 
			DatePreset.lastMonth => new DatePresetRange(formatDate(new DateTime(today.Year, today.Month, 1).AddMonths(-1)), formatDate(new DateTime(today.Year, today.Month, 1).AddDays(-1.0))), 
			_ => new DatePresetRange("", ""), 
		};
		static string formatDate(DateTime date)
		{
			return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		}
	}
}
