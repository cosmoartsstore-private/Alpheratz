using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Shared.Controls;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Gallery.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class GalleryFilterPanel : UserControl, IComponentConnector
{
	private GalleryFiltersState? boundFiltersState;

	private DateTime visibleMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

	private string activeDateField = "from";

	private string draftFrom = "";

	private string draftTo = "";

	private string draftPreset = "none";

	private List<WorldFilterOptionDto> allWorldOptions = new List<WorldFilterOptionDto>();

	private List<string> allTagOptions = new List<string>();

	private static readonly string[] WeekLabels = new string[7] { "日", "月", "火", "水", "木", "金", "土" };

	private static readonly DateTime CalendarMinMonth = new DateTime(2000, 1, 1);

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Run FilteredCountRun;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border ActiveChipsBorder;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button FolderAllBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button FolderPrimaryBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button FolderSecondaryBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button GroupNoneBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button GroupWorldBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button FavoriteToggleBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private AnimatedFavoriteStar FavoriteStarIcon;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock FavoriteLabel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button WorldTriggerBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border WorldDropdownPanel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock WorldCountLabel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private StackPanel WorldCheckboxList;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBox WorldSearchBox;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock WorldSummaryLabel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button TagTriggerBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border TagDropdownPanel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock TagCountLabel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private StackPanel TagCheckboxList;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBox TagSearchBox;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock TagSummaryLabel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button OrientationAllBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button OrientationLandscapeBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button OrientationPortraitBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock OrientationPortraitIcon;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock OrientationLandscapeIcon;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock OrientationAllIcon;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border DatePickerPopup;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid WeekdayHeaderGrid;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid CalendarDayGrid;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button MonthPrevBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock MonthLabel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button MonthNextBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button PresetTodayBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button PresetLast7Btn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button PresetThisMonthBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button PresetLastMonthBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button PresetHalfYearBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button PresetOneYearBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button FromChipBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button ToChipBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock ToValueText;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock FromValueText;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock CalendarRangeLabel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button DateTriggerBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button DateClearBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock DateRangeLabel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button SortDateBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button SortWorldBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private WrapPanel ActiveChipsPanel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border BadgeBorder;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock BadgeText;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public Action? OnResetFilters { get; set; }

	public Action<string>? OnDatePresetSelect { get; set; }

	public Action<string>? OnOrientationSelect { get; set; }

	public Action<SortMode>? OnSortSelect { get; set; }

	public Action<DisplayFolderMode>? OnDisplayFolderSelect { get; set; }

	public Action<GroupingMode>? OnGroupingSelect { get; set; }

	public Action<string>? OnWorldFilterAdd { get; set; }

	public Action<string>? OnWorldFilterRemove { get; set; }

	public Action<string>? OnTagFilterAdd { get; set; }

	public Action<string>? OnTagFilterRemove { get; set; }

	private static DateTime CalendarMaxMonth => new DateTime(DateTime.Today.Year + 2, 1, 1);

	public GalleryFilterPanel()
	{
		try
		{
			InitializeComponent();
			buildWeekdayHeaders();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.ctor: InitializeComponent failed: {value}");
			throw;
		}
		base.ActualThemeChanged += OnActualThemeChanged;
	}

	private void OnActualThemeChanged(FrameworkElement sender, object args)
	{
		try
		{
			syncActiveStates();
			syncFavoriteToggle();
			buildWeekdayHeaders();
			rebuildTagCheckboxList();
			rebuildWorldCheckboxList();
			syncActiveChips();
			if (DatePickerPopup.Visibility == Visibility.Visible)
			{
				buildCalendar();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.OnActualThemeChanged: {value}");
		}
	}

	private void UserControl_Unloaded(object sender, RoutedEventArgs e)
	{
		try
		{
			if (boundFiltersState != null)
			{
				boundFiltersState.PropertyChanged -= OnFiltersChanged;
				boundFiltersState = null;
			}
			base.ActualThemeChanged -= OnActualThemeChanged;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.UserControl_Unloaded: threw: {value}");
		}
	}

	public void setWorldFilterOptions(UiObservableCollection<WorldFilterOptionDto> options)
	{
		try
		{
			allWorldOptions = options.ToList();
			rebuildWorldCheckboxList();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.setWorldFilterOptions: threw: {value}");
		}
	}

	public void setMasterTagsSource(UiObservableCollection<string> tags)
	{
		try
		{
			allTagOptions = tags.ToList();
			rebuildTagCheckboxList();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.setMasterTagsSource: threw: {value}");
		}
	}

	public void bindFiltersState(GalleryFiltersState state)
	{
		try
		{
			if (boundFiltersState != null)
			{
				boundFiltersState.PropertyChanged -= OnFiltersChanged;
			}
			boundFiltersState = state;
			boundFiltersState.PropertyChanged += OnFiltersChanged;
			syncActiveStates();
			syncBadge();
			syncDateTriggerLabel();
			syncFavoriteToggle();
			syncTagSummary();
			syncWorldSummary();
			syncActiveChips();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.bindFiltersState: threw: {value}");
		}
	}

	public void setFilteredCount(int count)
	{
		FilteredCountRun.Text = count.ToString();
	}

	private void OnFiltersChanged(object? sender, PropertyChangedEventArgs e)
	{
		bool flag;
		switch (e.PropertyName)
		{
		case "OrientationFilter":
		case "SortMode":
		case "DisplayFolderMode":
		case "GroupingMode":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			syncActiveStates();
		}
		if (e.PropertyName == "ActiveFilterCount")
		{
			syncBadge();
			syncActiveChips();
		}
		switch (e.PropertyName)
		{
		case "DateFrom":
		case "DateTo":
		case "DatePreset":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			syncDateTriggerLabel();
			syncActiveChips();
		}
		if (e.PropertyName == "OrientationFilter")
		{
			syncActiveChips();
		}
		if (e.PropertyName == "FavoritesOnly")
		{
			syncFavoriteToggle();
			syncActiveChips();
		}
		if (e.PropertyName == "TagFilterCounts")
		{
			rebuildTagCheckboxList();
		}
	}

	private void syncActiveStates()
	{
		if (boundFiltersState == null)
		{
			return;
		}
		try
		{
			setActive(OrientationAllBtn, boundFiltersState.OrientationFilter == "all");
			setActive(OrientationPortraitBtn, boundFiltersState.OrientationFilter == "portrait");
			setActive(OrientationLandscapeBtn, boundFiltersState.OrientationFilter == "landscape");
			OrientationAllIcon.Foreground = ThemeHelper.Brush(OrientationAllIcon, (boundFiltersState.OrientationFilter == "all") ? "APrimary" : "ATextFaint");
			OrientationLandscapeIcon.Foreground = ThemeHelper.Brush(OrientationLandscapeIcon, (boundFiltersState.OrientationFilter == "landscape") ? "APrimary" : "ATextFaint");
			OrientationPortraitIcon.Foreground = ThemeHelper.Brush(OrientationPortraitIcon, (boundFiltersState.OrientationFilter == "portrait") ? "APrimary" : "ATextFaint");
			setActive(SortDateBtn, boundFiltersState.SortMode == SortMode.dateDesc);
			setActive(SortWorldBtn, boundFiltersState.SortMode == SortMode.worldAsc);
			setActive(FolderAllBtn, boundFiltersState.DisplayFolderMode == DisplayFolderMode.all);
			setActive(FolderPrimaryBtn, boundFiltersState.DisplayFolderMode == DisplayFolderMode.primary);
			setActive(FolderSecondaryBtn, boundFiltersState.DisplayFolderMode == DisplayFolderMode.secondary);
			setActive(GroupNoneBtn, boundFiltersState.GroupingMode == GroupingMode.none);
			setActive(GroupWorldBtn, boundFiltersState.GroupingMode == GroupingMode.world);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.syncActiveStates: threw: {value}");
		}
	}

	private void syncBadge()
	{
		if (boundFiltersState != null)
		{
			int activeFilterCount = boundFiltersState.ActiveFilterCount;
			if (activeFilterCount > 0)
			{
				BadgeBorder.Visibility = Visibility.Visible;
				BadgeText.Text = activeFilterCount.ToString();
			}
			else
			{
				BadgeBorder.Visibility = Visibility.Collapsed;
			}
		}
	}

	private void syncDateTriggerLabel()
	{
		if (boundFiltersState != null)
		{
			string dateFrom = boundFiltersState.DateFrom;
			string dateTo = boundFiltersState.DateTo;
			if (!string.IsNullOrEmpty(dateFrom) || !string.IsNullOrEmpty(dateTo))
			{
				DateRangeLabel.Text = (string.IsNullOrEmpty(dateFrom) ? "..." : dateFrom) + " ~ " + (string.IsNullOrEmpty(dateTo) ? "..." : dateTo);
				DateClearBtn.Visibility = Visibility.Visible;
			}
			else
			{
				DateRangeLabel.Text = "すべての期間";
				DateClearBtn.Visibility = Visibility.Collapsed;
			}
		}
	}

	private void syncFavoriteToggle()
	{
		if (boundFiltersState != null)
		{
			bool favoritesOnly = boundFiltersState.FavoritesOnly;
			FavoriteStarIcon.Liked = favoritesOnly;
			if (favoritesOnly)
			{
				FavoriteToggleBtn.Background = ThemeHelper.Brush(FavoriteToggleBtn, "AFavoriteSoft");
				FavoriteToggleBtn.BorderBrush = ThemeHelper.Brush(FavoriteToggleBtn, "AFavoriteBorder");
				FavoriteLabel.Foreground = ThemeHelper.Brush(FavoriteLabel, "AFavorite");
			}
			else
			{
				FavoriteToggleBtn.Background = ThemeHelper.Brush(FavoriteToggleBtn, "ASurfaceSoft");
				FavoriteToggleBtn.BorderBrush = new SolidColorBrush(Colors.Transparent);
				FavoriteLabel.Foreground = ThemeHelper.Brush(FavoriteLabel, "ATextDim");
			}
		}
	}

	private void syncTagSummary()
	{
		if (boundFiltersState != null)
		{
			int count = boundFiltersState.tagFilters.Count;
			TagSummaryLabel.Text = ((count == 0) ? "すべてのタグ" : $"{count}件選択中");
		}
	}

	private void syncWorldSummary()
	{
		if (boundFiltersState != null)
		{
			int count = boundFiltersState.worldFilters.Count;
			WorldSummaryLabel.Text = ((count == 0) ? "すべてのワールド" : $"{count}件選択中");
		}
	}

	private void syncActiveChips()
	{
		if (boundFiltersState == null)
		{
			return;
		}
		try
		{
			ActiveChipsPanel.Children.Clear();
			if (boundFiltersState.OrientationFilter != "all")
			{
				string label = ((boundFiltersState.OrientationFilter == "portrait") ? "向き: 縦" : ((boundFiltersState.OrientationFilter == "landscape") ? "向き: 横" : "向き"));
				addActiveChip(label, delegate
				{
					OnOrientationSelect?.Invoke("all");
				});
			}
			if (boundFiltersState.FavoritesOnly)
			{
				addActiveChip("お気に入り", delegate
				{
					boundFiltersState.FavoritesOnly = false;
				});
			}
			string dateFrom = boundFiltersState.DateFrom;
			string dateTo = boundFiltersState.DateTo;
			if (!string.IsNullOrEmpty(dateFrom) || !string.IsNullOrEmpty(dateTo) || boundFiltersState.DatePreset != DatePreset.none)
			{
				string text = ((!string.IsNullOrEmpty(dateFrom) || !string.IsNullOrEmpty(dateTo)) ? ((string.IsNullOrEmpty(dateFrom) ? "..." : dateFrom) + "〜" + (string.IsNullOrEmpty(dateTo) ? "..." : dateTo)) : boundFiltersState.DatePreset.ToString());
				addActiveChip("日付: " + text, delegate
				{
					OnDatePresetSelect?.Invoke("none");
				});
			}
			foreach (string item in boundFiltersState.worldFilters.ToList())
			{
				string captured = item;
				addActiveChip("ワールド: " + captured, delegate
				{
					removeWorldFilterFromChip(captured);
				});
			}
			foreach (string item2 in boundFiltersState.tagFilters.ToList())
			{
				string captured2 = item2;
				addActiveChip("タグ: " + captured2, delegate
				{
					removeTagFilterFromChip(captured2);
				});
			}
			ActiveChipsBorder.Visibility = ((ActiveChipsPanel.Children.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.syncActiveChips: threw: {value}");
		}
	}

	private void removeWorldFilterFromChip(string worldName)
	{
		if (boundFiltersState != null)
		{
			if (boundFiltersState.worldFilters.Contains(worldName))
			{
				OnWorldFilterRemove?.Invoke(worldName);
			}
			syncWorldSummary();
			syncActiveChips();
			rebuildWorldCheckboxList();
		}
	}

	private void removeTagFilterFromChip(string tag)
	{
		if (boundFiltersState != null)
		{
			if (boundFiltersState.tagFilters.Contains(tag))
			{
				OnTagFilterRemove?.Invoke(tag);
			}
			syncTagSummary();
			syncActiveChips();
			rebuildTagCheckboxList();
		}
	}

	private void addActiveChip(string label, Action onRemove)
	{
		Grid grid = new Grid
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		TextBlock textBlock = new TextBlock
		{
			Text = label,
			FontSize = 11.0,
			FontWeight = FontWeights.Bold,
			Foreground = ThemeHelper.Brush(ActiveChipsPanel, "AText"),
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		Grid.SetColumn(textBlock, 0);
		grid.Children.Add(textBlock);
		TextBlock textBlock2 = new TextBlock
		{
			Text = "×",
			FontSize = 12.0,
			FontWeight = FontWeights.Bold,
			Foreground = ThemeHelper.Brush(ActiveChipsPanel, "ATextFaint"),
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(6.0, 0.0, 0.0, 0.0)
		};
		Grid.SetColumn(textBlock2, 1);
		grid.Children.Add(textBlock2);
		Border content = new Border
		{
			Padding = new Thickness(10.0, 5.0, 8.0, 5.0),
			CornerRadius = new CornerRadius(8.0),
			Background = ThemeHelper.Brush(ActiveChipsPanel, "ASurfaceSoft"),
			BorderBrush = ThemeHelper.Brush(ActiveChipsPanel, "ABorder"),
			BorderThickness = new Thickness(1.0),
			Child = grid
		};
		Button button = new Button
		{
			Content = content,
			Padding = new Thickness(0.0),
			Margin = new Thickness(0.0),
			Background = new SolidColorBrush(Colors.Transparent),
			BorderThickness = new Thickness(0.0)
		};
		button.Click += delegate
		{
			onRemove();
		};
		ActiveChipsPanel.Children.Add(button);
	}

	private static void setActive(Button btn, bool active)
	{
		if (active)
		{
			btn.Background = ThemeHelper.Brush(btn, "APrimarySoft");
			btn.Foreground = ThemeHelper.Brush(btn, "APrimaryText");
			btn.BorderBrush = ThemeHelper.Brush(btn, "ABorderStrong");
			btn.BorderThickness = new Thickness(1.0);
		}
		else
		{
			btn.Background = ThemeHelper.Brush(btn, "ASurfaceSoft");
			btn.Foreground = ThemeHelper.Brush(btn, "ATextDim");
			btn.BorderBrush = new SolidColorBrush(Colors.Transparent);
			btn.BorderThickness = new Thickness(0.0);
		}
	}

	private void ResetFilters_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnResetFilters?.Invoke();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.ResetFilters_Click: {value}");
		}
	}

	private void SortDate_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnSortSelect?.Invoke(SortMode.dateDesc);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.SortDate_Click: {value}");
		}
	}

	private void SortWorld_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnSortSelect?.Invoke(SortMode.worldAsc);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.SortWorld_Click: {value}");
		}
	}

	private void DateTrigger_Click(object sender, RoutedEventArgs e)
	{
		if (boundFiltersState != null)
		{
			draftFrom = boundFiltersState.DateFrom;
			draftTo = boundFiltersState.DateTo;
			draftPreset = boundFiltersState.DatePreset.ToString();
			activeDateField = "from";
			bool flag = DatePickerPopup.Visibility == Visibility.Visible;
			DatePickerPopup.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
			if (!flag)
			{
				syncDraftUI();
				buildCalendar();
			}
		}
	}

	private void DateClear_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnDatePresetSelect?.Invoke("none");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.DateClear_Click: {value}");
		}
	}

	private void FromChip_Click(object sender, RoutedEventArgs e)
	{
		activeDateField = "from";
		syncDraftChipHighlight();
	}

	private void ToChip_Click(object sender, RoutedEventArgs e)
	{
		activeDateField = "to";
		syncDraftChipHighlight();
	}

	private void syncDraftChipHighlight()
	{
		setActive(FromChipBtn, activeDateField == "from");
		setActive(ToChipBtn, activeDateField == "to");
	}

	private void syncDraftUI()
	{
		FromValueText.Text = (string.IsNullOrEmpty(draftFrom) ? "---" : draftFrom);
		ToValueText.Text = (string.IsNullOrEmpty(draftTo) ? "---" : draftTo);
		string text = ((!string.IsNullOrEmpty(draftFrom) || !string.IsNullOrEmpty(draftTo)) ? ((string.IsNullOrEmpty(draftFrom) ? "..." : draftFrom) + " ~ " + (string.IsNullOrEmpty(draftTo) ? "..." : draftTo)) : "すべての期間");
		CalendarRangeLabel.Text = text;
		syncDraftChipHighlight();
		syncPresetHighlight();
	}

	private void syncPresetHighlight()
	{
		setActive(PresetTodayBtn, draftPreset == "today");
		setActive(PresetLast7Btn, draftPreset == "last7days");
		setActive(PresetThisMonthBtn, draftPreset == "thisMonth");
		setActive(PresetLastMonthBtn, draftPreset == "lastMonth");
		setActive(PresetHalfYearBtn, draftPreset == "halfYear");
		setActive(PresetOneYearBtn, draftPreset == "oneYear");
	}

	private void applyPreset(string preset)
	{
		if (boundFiltersState == null)
		{
			return;
		}
		try
		{
			OnDatePresetSelect?.Invoke(preset);
			draftPreset = boundFiltersState.DatePreset.ToString();
			draftFrom = boundFiltersState.DateFrom;
			draftTo = boundFiltersState.DateTo;
			DateTime? dateTime = parseDate(draftFrom);
			if (dateTime.HasValue)
			{
				visibleMonth = new DateTime(dateTime.Value.Year, dateTime.Value.Month, 1);
			}
			activeDateField = "from";
			syncDraftUI();
			buildCalendar();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.applyPreset: {value}");
		}
	}

	private void PresetToday_Click(object sender, RoutedEventArgs e)
	{
		applyPreset("today");
	}

	private void PresetLast7Days_Click(object sender, RoutedEventArgs e)
	{
		applyPreset("last7days");
	}

	private void PresetThisMonth_Click(object sender, RoutedEventArgs e)
	{
		applyPreset("thisMonth");
	}

	private void PresetLastMonth_Click(object sender, RoutedEventArgs e)
	{
		applyPreset("lastMonth");
	}

	private void PresetHalfYear_Click(object sender, RoutedEventArgs e)
	{
		applyPreset("halfYear");
	}

	private void PresetOneYear_Click(object sender, RoutedEventArgs e)
	{
		applyPreset("oneYear");
	}

	private void MonthPrev_Click(object sender, RoutedEventArgs e)
	{
		DateTime dateTime = visibleMonth.AddMonths(-1);
		if (!(dateTime < CalendarMinMonth))
		{
			visibleMonth = dateTime;
			buildCalendar();
		}
	}

	private void MonthNext_Click(object sender, RoutedEventArgs e)
	{
		DateTime dateTime = visibleMonth.AddMonths(1);
		if (!(dateTime > CalendarMaxMonth))
		{
			visibleMonth = dateTime;
			buildCalendar();
		}
	}

	private void CalendarClear_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnDatePresetSelect?.Invoke("none");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.CalendarClear_Click: {value}");
		}
	}

	private void CalendarClose_Click(object sender, RoutedEventArgs e)
	{
		DatePickerPopup.Visibility = Visibility.Collapsed;
	}

	private void buildWeekdayHeaders()
	{
		WeekdayHeaderGrid.ColumnDefinitions.Clear();
		WeekdayHeaderGrid.Children.Clear();
		for (int i = 0; i < 7; i++)
		{
			WeekdayHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
			TextBlock textBlock = new TextBlock
			{
				Text = WeekLabels[i],
				FontSize = 10.0,
				FontWeight = FontWeights.ExtraBold,
				Foreground = ThemeHelper.Brush(WeekdayHeaderGrid, "ATextFaint"),
				HorizontalAlignment = HorizontalAlignment.Center
			};
			Grid.SetColumn(textBlock, i);
			WeekdayHeaderGrid.Children.Add(textBlock);
		}
	}

	private void buildCalendar()
	{
		MonthLabel.Text = $"{visibleMonth.Year}年 {visibleMonth.Month}月";
		CalendarDayGrid.ColumnDefinitions.Clear();
		CalendarDayGrid.RowDefinitions.Clear();
		CalendarDayGrid.Children.Clear();
		for (int i = 0; i < 7; i++)
		{
			CalendarDayGrid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
		}
		DateTime dateTime = new DateTime(visibleMonth.Year, visibleMonth.Month, 1);
		int dayOfWeek = (int)dateTime.DayOfWeek;
		int num = DateTime.DaysInMonth(visibleMonth.Year, visibleMonth.Month);
		DateTime dateTime2 = dateTime.AddDays(-dayOfWeek);
		int num2 = (dayOfWeek + num + 6) / 7 * 7;
		int num3 = num2 / 7;
		for (int j = 0; j < num3; j++)
		{
			CalendarDayGrid.RowDefinitions.Add(new RowDefinition
			{
				Height = GridLength.Auto
			});
		}
		DateTime? dateTime3 = parseDate(draftFrom);
		DateTime? dateTime4 = parseDate(draftTo);
		for (int k = 0; k < num2; k++)
		{
			DateTime dateTime5 = dateTime2.AddDays(k);
			bool flag = dateTime5.Month == visibleMonth.Month && dateTime5.Year == visibleMonth.Year;
			bool flag2 = dateTime3.HasValue && dateTime5.Date == dateTime3.Value.Date;
			bool flag3 = dateTime4.HasValue && dateTime5.Date == dateTime4.Value.Date;
			bool flag4 = dateTime3.HasValue && dateTime4.HasValue && dateTime5.Date >= dateTime3.Value.Date && dateTime5.Date <= dateTime4.Value.Date;
			Brush background;
			Brush foreground;
			if (flag2 || flag3)
			{
				background = ThemeHelper.Brush(CalendarDayGrid, "APrimary");
				foreground = new SolidColorBrush(Colors.White);
			}
			else if (flag4)
			{
				background = ThemeHelper.Brush(CalendarDayGrid, "APrimarySoft");
				foreground = ThemeHelper.Brush(CalendarDayGrid, "AText");
			}
			else
			{
				background = new SolidColorBrush(Colors.Transparent);
				foreground = (flag ? ThemeHelper.Brush(CalendarDayGrid, "AText") : ThemeHelper.Brush(CalendarDayGrid, "ATextDisabled"));
			}
			Button button = new Button
			{
				Content = dateTime5.Day.ToString(),
				Tag = dateTime5,
				MinWidth = 0.0,
				MinHeight = 32.0,
				Width = 32.0,
				Height = 32.0,
				Padding = new Thickness(0.0),
				HorizontalAlignment = HorizontalAlignment.Center,
				Background = background,
				Foreground = foreground,
				BorderBrush = new SolidColorBrush(Colors.Transparent),
				BorderThickness = new Thickness(0.0),
				CornerRadius = new CornerRadius(6.0),
				FontSize = 11.0,
				FontWeight = ((flag2 || flag3) ? FontWeights.ExtraBold : FontWeights.SemiBold)
			};
			button.Click += DayCell_Click;
			Grid.SetRow(button, k / 7);
			Grid.SetColumn(button, k % 7);
			CalendarDayGrid.Children.Add(button);
		}
	}

	private void DayCell_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is Button { Tag: var tag }) || !(tag is DateTime dateTime))
		{
			return;
		}
		string strA = dateTime.ToString("yyyy-MM-dd");
		draftPreset = "custom";
		if (activeDateField == "from")
		{
			draftFrom = strA;
			if (!string.IsNullOrEmpty(draftTo) && string.Compare(strA, draftTo, StringComparison.Ordinal) > 0)
			{
				draftTo = "";
			}
			activeDateField = "to";
		}
		else if (!string.IsNullOrEmpty(draftFrom) && string.Compare(strA, draftFrom, StringComparison.Ordinal) < 0)
		{
			draftTo = draftFrom;
			draftFrom = strA;
		}
		else
		{
			draftTo = strA;
		}
		commitCustomDraftToState();
		syncDraftUI();
		buildCalendar();
	}

	private void commitCustomDraftToState()
	{
		if (boundFiltersState == null)
		{
			return;
		}
		try
		{
			boundFiltersState.DateFrom = draftFrom;
			boundFiltersState.DateTo = draftTo;
			boundFiltersState.DatePreset = DatePreset.custom;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.commitCustomDraftToState: {value}");
		}
	}

	private static DateTime? parseDate(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return null;
		}
		if (!DateTime.TryParse(value, out var result))
		{
			return null;
		}
		return result.Date;
	}

	private void OrientationAll_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnOrientationSelect?.Invoke("all");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.OrientationAll_Click: {value}");
		}
	}

	private void OrientationPortrait_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnOrientationSelect?.Invoke("portrait");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.OrientationPortrait_Click: {value}");
		}
	}

	private void OrientationLandscape_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnOrientationSelect?.Invoke("landscape");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.OrientationLandscape_Click: {value}");
		}
	}

	private void TagTrigger_Click(object sender, RoutedEventArgs e)
	{
		bool flag = TagDropdownPanel.Visibility == Visibility.Visible;
		TagDropdownPanel.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		if (!flag)
		{
			TagSearchBox.Text = "";
			rebuildTagCheckboxList();
		}
	}

	private void TagSearch_TextChanged(object sender, TextChangedEventArgs e)
	{
		rebuildTagCheckboxList();
	}

	private void rebuildTagCheckboxList()
	{
		TagCheckboxList.Children.Clear();
		string query = TagSearchBox?.Text?.Trim() ?? "";
		object obj = (string.IsNullOrEmpty(query) ? ((object)allTagOptions) : ((object)allTagOptions.Where((string t) => t.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList()));
		StackPanel tagCheckboxList = TagCheckboxList;
		GalleryFiltersState? galleryFiltersState = boundFiltersState;
		addCheckboxItem(tagCheckboxList, "すべてのタグ", null, galleryFiltersState != null && galleryFiltersState.tagFilters.Count == 0, delegate
		{
			clearAllTagFilters();
		});
		IReadOnlyDictionary<string, long> readOnlyDictionary = boundFiltersState?.TagFilterCounts;
		foreach (string item in (List<string>)obj)
		{
			bool isChecked = boundFiltersState?.tagFilters.Contains(item) ?? false;
			string capturedTag = item;
			long value;
			string countText = ((readOnlyDictionary != null && readOnlyDictionary.TryGetValue(item, out value)) ? $"{value}枚" : "0枚");
			addCheckboxItem(TagCheckboxList, item, countText, isChecked, delegate
			{
				toggleTagFilter(capturedTag);
			});
		}
		TagCountLabel.Text = $"{allTagOptions.Count} タグ";
	}

	private void toggleTagFilter(string tag)
	{
		if (boundFiltersState != null)
		{
			if (boundFiltersState.tagFilters.Contains(tag))
			{
				OnTagFilterRemove?.Invoke(tag);
			}
			else
			{
				OnTagFilterAdd?.Invoke(tag);
			}
			syncTagSummary();
			syncActiveChips();
			rebuildTagCheckboxList();
		}
	}

	private void clearAllTagFilters()
	{
		if (boundFiltersState == null)
		{
			return;
		}
		foreach (string item in boundFiltersState.tagFilters.ToList())
		{
			OnTagFilterRemove?.Invoke(item);
		}
		syncTagSummary();
		syncActiveChips();
		rebuildTagCheckboxList();
	}

	private void WorldTrigger_Click(object sender, RoutedEventArgs e)
	{
		bool flag = WorldDropdownPanel.Visibility == Visibility.Visible;
		WorldDropdownPanel.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		if (!flag)
		{
			WorldSearchBox.Text = "";
			rebuildWorldCheckboxList();
		}
	}

	private void WorldSearch_TextChanged(object sender, TextChangedEventArgs e)
	{
		rebuildWorldCheckboxList();
	}

	private void rebuildWorldCheckboxList()
	{
		WorldCheckboxList.Children.Clear();
		string query = WorldSearchBox?.Text?.Trim() ?? "";
		List<WorldFilterOptionDto> list = (string.IsNullOrEmpty(query) ? allWorldOptions : allWorldOptions.Where((WorldFilterOptionDto w) => w.world_name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false).ToList());
		long value = allWorldOptions.Sum((WorldFilterOptionDto w) => w.count);
		StackPanel worldCheckboxList = WorldCheckboxList;
		string countText = $"{value}枚";
		GalleryFiltersState? galleryFiltersState = boundFiltersState;
		addCheckboxItem(worldCheckboxList, "すべてのワールド", countText, galleryFiltersState != null && galleryFiltersState.worldFilters.Count == 0, delegate
		{
			clearAllWorldFilters();
		});
		if (list.Count > 0)
		{
			addSeparator(WorldCheckboxList);
			addGroupLabel(WorldCheckboxList, "訪問済みワールド");
			foreach (WorldFilterOptionDto item in list)
			{
				if (item.world_name != null)
				{
					bool isChecked = boundFiltersState?.worldFilters.Contains(item.world_name) ?? false;
					string capturedName = item.world_name;
					addCheckboxItem(WorldCheckboxList, item.world_name, $"{item.count}枚", isChecked, delegate
					{
						toggleWorldFilter(capturedName);
					});
				}
			}
		}
		WorldCountLabel.Text = $"{allWorldOptions.Count} ワールド";
	}

	private void toggleWorldFilter(string worldName)
	{
		if (boundFiltersState != null)
		{
			if (boundFiltersState.worldFilters.Contains(worldName))
			{
				OnWorldFilterRemove?.Invoke(worldName);
			}
			else
			{
				OnWorldFilterAdd?.Invoke(worldName);
			}
			syncWorldSummary();
			syncActiveChips();
			rebuildWorldCheckboxList();
		}
	}

	private void clearAllWorldFilters()
	{
		if (boundFiltersState == null)
		{
			return;
		}
		foreach (string item in boundFiltersState.worldFilters.ToList())
		{
			OnWorldFilterRemove?.Invoke(item);
		}
		syncWorldSummary();
		syncActiveChips();
		rebuildWorldCheckboxList();
	}

	private void addCheckboxItem(StackPanel parent, string label, string? countText, bool isChecked, Action onToggle)
	{
		Grid grid = new Grid
		{
			Padding = new Thickness(10.0, 8.0, 10.0, 8.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		if (countText != null)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = GridLength.Auto
			});
		}
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		TextBlock textBlock = new TextBlock
		{
			Text = label,
			FontSize = 12.0,
			FontWeight = FontWeights.Bold,
			Foreground = ThemeHelper.Brush(parent, isChecked ? "APrimaryText" : "ATextFaint"),
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		Grid.SetColumn(textBlock, 0);
		grid.Children.Add(textBlock);
		int value = 1;
		if (countText != null)
		{
			TextBlock textBlock2 = new TextBlock
			{
				Text = countText,
				FontSize = 11.0,
				FontFamily = ThemeHelper.AppResource<FontFamily>("AFontMono"),
				Foreground = ThemeHelper.Brush(parent, "ATextDisabled"),
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(8.0, 0.0, 8.0, 0.0)
			};
			Grid.SetColumn(textBlock2, 1);
			grid.Children.Add(textBlock2);
			value = 2;
		}
		Border border = new Border
		{
			Width = 18.0,
			Height = 18.0,
			CornerRadius = new CornerRadius(6.0),
			BorderThickness = new Thickness(1.0),
			BorderBrush = ThemeHelper.Brush(parent, isChecked ? "ABorderStrong" : "ABorder"),
			Background = ThemeHelper.Brush(parent, isChecked ? "APrimarySoft" : "ASurfaceSoft"),
			VerticalAlignment = VerticalAlignment.Center
		};
		if (isChecked)
		{
			border.Child = new TextBlock
			{
				Text = "✓",
				FontSize = 10.0,
				FontWeight = FontWeights.ExtraBold,
				Foreground = ThemeHelper.Brush(parent, "APrimaryText"),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
		}
		Grid.SetColumn(border, value);
		grid.Children.Add(border);
		Border content = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			Background = (isChecked ? ThemeHelper.Brush(parent, "APrimarySoft") : new SolidColorBrush(Colors.Transparent)),
			BorderBrush = (isChecked ? ThemeHelper.Brush(parent, "ABorderStrong") : new SolidColorBrush(Colors.Transparent)),
			BorderThickness = new Thickness(1.0),
			Child = grid
		};
		Button button = new Button
		{
			Content = null,
			Padding = new Thickness(0.0),
			Margin = new Thickness(0.0),
			Background = new SolidColorBrush(Colors.Transparent),
			BorderThickness = new Thickness(0.0),
			HorizontalAlignment = HorizontalAlignment.Stretch,
			HorizontalContentAlignment = HorizontalAlignment.Stretch
		};
		button.Content = content;
		button.Click += delegate
		{
			onToggle();
		};
		parent.Children.Add(button);
	}

	private static void addSeparator(StackPanel parent)
	{
		parent.Children.Add(new Border
		{
			Height = 1.0,
			Margin = new Thickness(4.0, 4.0, 4.0, 4.0),
			Background = ThemeHelper.Brush(parent, "ABorder")
		});
	}

	private static void addGroupLabel(StackPanel parent, string text)
	{
		parent.Children.Add(new TextBlock
		{
			Text = text,
			FontSize = 10.0,
			FontWeight = FontWeights.ExtraBold,
			Foreground = ThemeHelper.Brush(parent, "ATextFaint"),
			Margin = new Thickness(10.0, 4.0, 0.0, 2.0)
		});
	}

	private void FavoriteToggle_Click(object sender, RoutedEventArgs e)
	{
		if (boundFiltersState != null)
		{
			boundFiltersState.FavoritesOnly = !boundFiltersState.FavoritesOnly;
		}
	}

	private void FolderAll_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnDisplayFolderSelect?.Invoke(DisplayFolderMode.all);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.FolderAll_Click: {value}");
		}
	}

	private void FolderPrimary_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnDisplayFolderSelect?.Invoke(DisplayFolderMode.primary);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.FolderPrimary_Click: {value}");
		}
	}

	private void FolderSecondary_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnDisplayFolderSelect?.Invoke(DisplayFolderMode.secondary);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.FolderSecondary_Click: {value}");
		}
	}

	public void SetGroupingEnabled(bool enabled)
	{
		GroupWorldBtn.IsEnabled = enabled;
	}

	private void GroupNone_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnGroupingSelect?.Invoke(GroupingMode.none);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.GroupNone_Click: {value}");
		}
	}

	private void GroupWorld_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnGroupingSelect?.Invoke(GroupingMode.world);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterPanel.GroupWorld_Click: {value}");
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/Gallery/Controls/GalleryFilterPanel.xaml");
			Application.LoadComponent(this, resourceLocator, ComponentResourceLocation.Application);
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			target.As<UserControl>().Unloaded += UserControl_Unloaded;
			break;
		case 2:
			FilteredCountRun = target.As<Run>();
			break;
		case 3:
			ActiveChipsBorder = target.As<Border>();
			break;
		case 4:
			FolderAllBtn = target.As<Button>();
			FolderAllBtn.Click += FolderAll_Click;
			break;
		case 5:
			FolderPrimaryBtn = target.As<Button>();
			FolderPrimaryBtn.Click += FolderPrimary_Click;
			break;
		case 6:
			FolderSecondaryBtn = target.As<Button>();
			FolderSecondaryBtn.Click += FolderSecondary_Click;
			break;
		case 7:
			GroupNoneBtn = target.As<Button>();
			GroupNoneBtn.Click += GroupNone_Click;
			break;
		case 8:
			GroupWorldBtn = target.As<Button>();
			GroupWorldBtn.Click += GroupWorld_Click;
			break;
		case 9:
			FavoriteToggleBtn = target.As<Button>();
			FavoriteToggleBtn.Click += FavoriteToggle_Click;
			break;
		case 10:
			FavoriteStarIcon = target.As<AnimatedFavoriteStar>();
			break;
		case 11:
			FavoriteLabel = target.As<TextBlock>();
			break;
		case 12:
			WorldTriggerBtn = target.As<Button>();
			WorldTriggerBtn.Click += WorldTrigger_Click;
			break;
		case 13:
			WorldDropdownPanel = target.As<Border>();
			break;
		case 14:
			WorldCountLabel = target.As<TextBlock>();
			break;
		case 15:
			WorldCheckboxList = target.As<StackPanel>();
			break;
		case 16:
			WorldSearchBox = target.As<TextBox>();
			WorldSearchBox.TextChanged += WorldSearch_TextChanged;
			break;
		case 17:
			WorldSummaryLabel = target.As<TextBlock>();
			break;
		case 18:
			TagTriggerBtn = target.As<Button>();
			TagTriggerBtn.Click += TagTrigger_Click;
			break;
		case 19:
			TagDropdownPanel = target.As<Border>();
			break;
		case 20:
			TagCountLabel = target.As<TextBlock>();
			break;
		case 21:
			TagCheckboxList = target.As<StackPanel>();
			break;
		case 22:
			TagSearchBox = target.As<TextBox>();
			TagSearchBox.TextChanged += TagSearch_TextChanged;
			break;
		case 23:
			TagSummaryLabel = target.As<TextBlock>();
			break;
		case 24:
			OrientationAllBtn = target.As<Button>();
			OrientationAllBtn.Click += OrientationAll_Click;
			break;
		case 25:
			OrientationLandscapeBtn = target.As<Button>();
			OrientationLandscapeBtn.Click += OrientationLandscape_Click;
			break;
		case 26:
			OrientationPortraitBtn = target.As<Button>();
			OrientationPortraitBtn.Click += OrientationPortrait_Click;
			break;
		case 27:
			OrientationPortraitIcon = target.As<TextBlock>();
			break;
		case 28:
			OrientationLandscapeIcon = target.As<TextBlock>();
			break;
		case 29:
			OrientationAllIcon = target.As<TextBlock>();
			break;
		case 30:
			DatePickerPopup = target.As<Border>();
			break;
		case 31:
			target.As<Button>().Click += CalendarClear_Click;
			break;
		case 32:
			target.As<Button>().Click += CalendarClose_Click;
			break;
		case 33:
			WeekdayHeaderGrid = target.As<Grid>();
			break;
		case 34:
			CalendarDayGrid = target.As<Grid>();
			break;
		case 35:
			MonthPrevBtn = target.As<Button>();
			MonthPrevBtn.Click += MonthPrev_Click;
			break;
		case 36:
			MonthLabel = target.As<TextBlock>();
			break;
		case 37:
			MonthNextBtn = target.As<Button>();
			MonthNextBtn.Click += MonthNext_Click;
			break;
		case 38:
			PresetTodayBtn = target.As<Button>();
			PresetTodayBtn.Click += PresetToday_Click;
			break;
		case 39:
			PresetLast7Btn = target.As<Button>();
			PresetLast7Btn.Click += PresetLast7Days_Click;
			break;
		case 40:
			PresetThisMonthBtn = target.As<Button>();
			PresetThisMonthBtn.Click += PresetThisMonth_Click;
			break;
		case 41:
			PresetLastMonthBtn = target.As<Button>();
			PresetLastMonthBtn.Click += PresetLastMonth_Click;
			break;
		case 42:
			PresetHalfYearBtn = target.As<Button>();
			PresetHalfYearBtn.Click += PresetHalfYear_Click;
			break;
		case 43:
			PresetOneYearBtn = target.As<Button>();
			PresetOneYearBtn.Click += PresetOneYear_Click;
			break;
		case 44:
			FromChipBtn = target.As<Button>();
			FromChipBtn.Click += FromChip_Click;
			break;
		case 45:
			ToChipBtn = target.As<Button>();
			ToChipBtn.Click += ToChip_Click;
			break;
		case 46:
			ToValueText = target.As<TextBlock>();
			break;
		case 47:
			FromValueText = target.As<TextBlock>();
			break;
		case 48:
			CalendarRangeLabel = target.As<TextBlock>();
			break;
		case 49:
			DateTriggerBtn = target.As<Button>();
			DateTriggerBtn.Click += DateTrigger_Click;
			break;
		case 50:
			DateClearBtn = target.As<Button>();
			DateClearBtn.Click += DateClear_Click;
			break;
		case 51:
			DateRangeLabel = target.As<TextBlock>();
			break;
		case 52:
			SortDateBtn = target.As<Button>();
			SortDateBtn.Click += SortDate_Click;
			break;
		case 53:
			SortWorldBtn = target.As<Button>();
			SortWorldBtn.Click += SortWorld_Click;
			break;
		case 54:
			ActiveChipsPanel = target.As<WrapPanel>();
			break;
		case 55:
			target.As<Button>().Click += ResetFilters_Click;
			break;
		case 56:
			BadgeBorder = target.As<Border>();
			break;
		case 57:
			BadgeText = target.As<TextBlock>();
			break;
		}
		_contentLoaded = true;
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public IComponentConnector GetBindingConnector(int connectionId, object target)
	{
		return null;
	}
}
