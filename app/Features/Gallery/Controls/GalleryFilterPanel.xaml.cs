using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Models;
using Alpheratz.Shared.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Features.Gallery.Controls;

public sealed partial class GalleryFilterPanel : UserControl
{
    private GalleryFiltersState? boundFiltersState;
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

    private DateTime visibleMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private string activeDateField = "from";
    private string draftFrom = "";
    private string draftTo = "";
    private string draftPreset = "none";

    private List<WorldFilterOptionDto> allWorldOptions = [];
    private List<string> allTagOptions = [];

    private static readonly string[] WeekLabels = ["日", "月", "火", "水", "木", "金", "土"];

    public GalleryFilterPanel()
    {
        AppLogger.Trace("GalleryFilterPanel.ctor: enter");
        try
        {
            InitializeComponent();
            buildWeekdayHeaders();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryFilterPanel.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("GalleryFilterPanel.ctor: exit");
    }

    /// <summary>
    /// R2-A-23: コントロールが Unloaded された後も boundFiltersState の PropertyChanged が
    /// このパネルを参照し続けると、フィルタ更新のたびに再描画コードが死んだコントロール上で
    /// 走ってリーク・例外を発生させる。Unloaded で確実にデタッチする。
    /// </summary>
    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (boundFiltersState is not null)
            {
                boundFiltersState.PropertyChanged -= OnFiltersChanged;
                boundFiltersState = null;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryFilterPanel.UserControl_Unloaded: threw: {ex}");
        }
    }

    public void setWorldFilterOptions(UiObservableCollection<WorldFilterOptionDto> options)
    {
        AppLogger.Trace($"GalleryFilterPanel.setWorldFilterOptions: enter count={options.Count}");
        try
        {
            allWorldOptions = options.ToList();
            rebuildWorldCheckboxList();
        }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.setWorldFilterOptions: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterPanel.setWorldFilterOptions: exit");
    }

    public void setMasterTagsSource(UiObservableCollection<string> tags)
    {
        AppLogger.Trace($"GalleryFilterPanel.setMasterTagsSource: enter count={tags.Count}");
        try
        {
            allTagOptions = tags.ToList();
            rebuildTagCheckboxList();
        }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.setMasterTagsSource: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterPanel.setMasterTagsSource: exit");
    }

    public void bindFiltersState(GalleryFiltersState state)
    {
        AppLogger.Trace("GalleryFilterPanel.bindFiltersState: enter");
        try
        {
            if (boundFiltersState is not null)
                boundFiltersState.PropertyChanged -= OnFiltersChanged;

            boundFiltersState = state;
            boundFiltersState.PropertyChanged += OnFiltersChanged;
            syncActiveStates();
            syncBadge();
            syncDateTriggerLabel();
            syncFavoriteToggle();
            syncTagSummary();
            syncWorldSummary();
        }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.bindFiltersState: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterPanel.bindFiltersState: exit");
    }

    public void setFilteredCount(int count)
    {
        FilteredCountRun.Text = count.ToString();
    }

    private void OnFiltersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryFiltersState.OrientationFilter)
            or nameof(GalleryFiltersState.SortMode)
            or nameof(GalleryFiltersState.DisplayFolderMode)
            or nameof(GalleryFiltersState.GroupingMode))
        {
            syncActiveStates();
        }

        if (e.PropertyName is nameof(GalleryFiltersState.ActiveFilterCount))
        {
            syncBadge();
        }

        if (e.PropertyName is nameof(GalleryFiltersState.DateFrom)
            or nameof(GalleryFiltersState.DateTo)
            or nameof(GalleryFiltersState.DatePreset))
        {
            syncDateTriggerLabel();
        }

        if (e.PropertyName is nameof(GalleryFiltersState.FavoritesOnly))
        {
            syncFavoriteToggle();
        }
    }

    private void syncActiveStates()
    {
        if (boundFiltersState is null) return;
        try
        {
            setActive(OrientationAllBtn, boundFiltersState.OrientationFilter == "all");
            setActive(OrientationPortraitBtn, boundFiltersState.OrientationFilter == "portrait");
            setActive(OrientationLandscapeBtn, boundFiltersState.OrientationFilter == "landscape");

            setActive(SortDateBtn, boundFiltersState.SortMode == SortMode.dateDesc);
            setActive(SortWorldBtn, boundFiltersState.SortMode == SortMode.worldAsc);

            setActive(FolderAllBtn, boundFiltersState.DisplayFolderMode == DisplayFolderMode.all);
            setActive(FolderPrimaryBtn, boundFiltersState.DisplayFolderMode == DisplayFolderMode.primary);
            setActive(FolderSecondaryBtn, boundFiltersState.DisplayFolderMode == DisplayFolderMode.secondary);

            setActive(GroupNoneBtn, boundFiltersState.GroupingMode == GroupingMode.none);
            setActive(GroupWorldBtn, boundFiltersState.GroupingMode == GroupingMode.world);
        }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.syncActiveStates: threw: {ex}"); }
    }

    private void syncBadge()
    {
        if (boundFiltersState is null) return;
        var count = boundFiltersState.ActiveFilterCount;
        if (count > 0)
        {
            BadgeBorder.Visibility = Visibility.Visible;
            BadgeText.Text = count.ToString();
        }
        else
        {
            BadgeBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void syncDateTriggerLabel()
    {
        if (boundFiltersState is null) return;
        var from = boundFiltersState.DateFrom;
        var to = boundFiltersState.DateTo;
        if (!string.IsNullOrEmpty(from) || !string.IsNullOrEmpty(to))
        {
            DateRangeLabel.Text = $"{(string.IsNullOrEmpty(from) ? "..." : from)} ~ {(string.IsNullOrEmpty(to) ? "..." : to)}";
            DateClearBtn.Visibility = Visibility.Visible;
        }
        else
        {
            DateRangeLabel.Text = "すべての期間";
            DateClearBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void syncFavoriteToggle()
    {
        if (boundFiltersState is null) return;
        var active = boundFiltersState.FavoritesOnly;
        FavoriteStarIcon.Liked = active;
        if (active)
        {
            FavoriteToggleBtn.Background = (Brush)Application.Current.Resources["AFavoriteSoft"];
            FavoriteToggleBtn.BorderBrush = (Brush)Application.Current.Resources["AFavoriteBorder"];
            FavoriteLabel.Foreground = (Brush)Application.Current.Resources["AFavorite"];
        }
        else
        {
            FavoriteToggleBtn.Background = (Brush)Application.Current.Resources["ASurfaceSoft"];
            FavoriteToggleBtn.BorderBrush = (Brush)Application.Current.Resources["ABorder"];
            FavoriteLabel.Foreground = (Brush)Application.Current.Resources["ATextDim"];
        }
    }

    private void syncTagSummary()
    {
        if (boundFiltersState is null) return;
        var count = boundFiltersState.tagFilters.Count;
        TagSummaryLabel.Text = count == 0 ? "すべてのタグ" : $"{count}件選択中";
    }

    private void syncWorldSummary()
    {
        if (boundFiltersState is null) return;
        var count = boundFiltersState.worldFilters.Count;
        WorldSummaryLabel.Text = count == 0 ? "すべてのワールド" : $"{count}件選択中";
    }

    private static void setActive(Button btn, bool active)
    {
        var resources = Application.Current.Resources;
        if (active)
        {
            btn.Background = (Brush)resources["APrimarySoft"];
            btn.Foreground = (Brush)resources["APrimary"];
            btn.BorderBrush = (Brush)resources["ABorderStrong"];
        }
        else
        {
            btn.Background = (Brush)resources["ASurfaceSoft"];
            btn.Foreground = (Brush)resources["ATextDim"];
            btn.BorderBrush = (Brush)resources["ABorder"];
        }
    }

    // ── Header ──
    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterPanel.ResetFilters_Click");
        try { OnResetFilters?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.ResetFilters_Click: {ex}"); }
    }

    // ── Sort ──
    private void SortDate_Click(object sender, RoutedEventArgs e)
    {
        try { OnSortSelect?.Invoke(SortMode.dateDesc); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.SortDate_Click: {ex}"); }
    }

    private void SortWorld_Click(object sender, RoutedEventArgs e)
    {
        try { OnSortSelect?.Invoke(SortMode.worldAsc); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.SortWorld_Click: {ex}"); }
    }

    // ── Date trigger ──
    private void DateTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (boundFiltersState is null) return;
        draftFrom = boundFiltersState.DateFrom;
        draftTo = boundFiltersState.DateTo;
        draftPreset = boundFiltersState.DatePreset.ToString();
        activeDateField = "from";

        var isOpen = DatePickerPopup.Visibility == Visibility.Visible;
        DatePickerPopup.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;

        if (!isOpen)
        {
            syncDraftUI();
            buildCalendar();
        }
    }

    private void DateClear_Click(object sender, RoutedEventArgs e)
    {
        try { OnDatePresetSelect?.Invoke("none"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.DateClear_Click: {ex}"); }
    }

    // ── From/To chips ──
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
        FromValueText.Text = string.IsNullOrEmpty(draftFrom) ? "---" : draftFrom;
        ToValueText.Text = string.IsNullOrEmpty(draftTo) ? "---" : draftTo;

        var label = (!string.IsNullOrEmpty(draftFrom) || !string.IsNullOrEmpty(draftTo))
            ? $"{(string.IsNullOrEmpty(draftFrom) ? "..." : draftFrom)} ~ {(string.IsNullOrEmpty(draftTo) ? "..." : draftTo)}"
            : "すべての期間";
        CalendarRangeLabel.Text = label;

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

    // ── Presets ──
    private void applyPresetToDraft(string preset)
    {
        var today = DateTime.Today;
        DateTime from, to;

        switch (preset)
        {
            case "today":
                from = today; to = today; break;
            case "last7days":
                from = today.AddDays(-6); to = today; break;
            case "thisMonth":
                from = new DateTime(today.Year, today.Month, 1);
                to = from.AddMonths(1).AddDays(-1); break;
            case "lastMonth":
                from = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                to = new DateTime(today.Year, today.Month, 1).AddDays(-1); break;
            case "halfYear":
                from = today.AddMonths(-6); to = today; break;
            case "oneYear":
                from = today.AddYears(-1); to = today; break;
            default: return;
        }

        draftPreset = preset;
        draftFrom = from.ToString("yyyy-MM-dd");
        draftTo = to.ToString("yyyy-MM-dd");
        visibleMonth = new DateTime(from.Year, from.Month, 1);
        syncDraftUI();
        buildCalendar();
    }

    private void PresetToday_Click(object sender, RoutedEventArgs e) => applyPresetToDraft("today");
    private void PresetLast7Days_Click(object sender, RoutedEventArgs e) => applyPresetToDraft("last7days");
    private void PresetThisMonth_Click(object sender, RoutedEventArgs e) => applyPresetToDraft("thisMonth");
    private void PresetLastMonth_Click(object sender, RoutedEventArgs e) => applyPresetToDraft("lastMonth");
    private void PresetHalfYear_Click(object sender, RoutedEventArgs e) => applyPresetToDraft("halfYear");
    private void PresetOneYear_Click(object sender, RoutedEventArgs e) => applyPresetToDraft("oneYear");

    // ── Calendar navigation ──
    private void MonthPrev_Click(object sender, RoutedEventArgs e)
    {
        visibleMonth = visibleMonth.AddMonths(-1);
        buildCalendar();
    }

    private void MonthNext_Click(object sender, RoutedEventArgs e)
    {
        visibleMonth = visibleMonth.AddMonths(1);
        buildCalendar();
    }

    private void CalendarClear_Click(object sender, RoutedEventArgs e)
    {
        draftPreset = "none";
        draftFrom = "";
        draftTo = "";
        syncDraftUI();
        buildCalendar();
    }

    private void CalendarApply_Click(object sender, RoutedEventArgs e)
    {
        if (boundFiltersState is null) return;
        try
        {
            if (draftPreset != "custom" && draftPreset != "none")
            {
                OnDatePresetSelect?.Invoke(draftPreset);
            }
            else if (draftPreset == "none")
            {
                OnDatePresetSelect?.Invoke("none");
            }
            else
            {
                boundFiltersState.DateFrom = draftFrom;
                boundFiltersState.DateTo = draftTo;
                boundFiltersState.DatePreset = DatePreset.custom;
            }
            DatePickerPopup.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.CalendarApply_Click: {ex}"); }
    }

    // ── Calendar rendering ──
    private void buildWeekdayHeaders()
    {
        WeekdayHeaderGrid.ColumnDefinitions.Clear();
        WeekdayHeaderGrid.Children.Clear();
        for (int i = 0; i < 7; i++)
        {
            WeekdayHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tb = new TextBlock
            {
                Text = WeekLabels[i],
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
                Foreground = (Brush)Application.Current.Resources["ATextFaint"],
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Grid.SetColumn(tb, i);
            WeekdayHeaderGrid.Children.Add(tb);
        }
    }

    private void buildCalendar()
    {
        MonthLabel.Text = $"{visibleMonth.Year}年 {visibleMonth.Month}月";

        CalendarDayGrid.ColumnDefinitions.Clear();
        CalendarDayGrid.RowDefinitions.Clear();
        CalendarDayGrid.Children.Clear();

        for (int i = 0; i < 7; i++)
            CalendarDayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var firstOfMonth = new DateTime(visibleMonth.Year, visibleMonth.Month, 1);
        int startDow = (int)firstOfMonth.DayOfWeek;
        int daysInMonth = DateTime.DaysInMonth(visibleMonth.Year, visibleMonth.Month);

        var startDate = firstOfMonth.AddDays(-startDow);
        int totalCells = ((startDow + daysInMonth + 6) / 7) * 7;

        int rows = totalCells / 7;
        for (int r = 0; r < rows; r++)
            CalendarDayGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var activeStart = parseDate(draftFrom);
        var activeEnd = parseDate(draftTo);

        for (int i = 0; i < totalCells; i++)
        {
            var cellDate = startDate.AddDays(i);
            bool inCurrentMonth = cellDate.Month == visibleMonth.Month && cellDate.Year == visibleMonth.Year;
            bool isStart = activeStart.HasValue && cellDate.Date == activeStart.Value.Date;
            bool isEnd = activeEnd.HasValue && cellDate.Date == activeEnd.Value.Date;
            bool inRange = activeStart.HasValue && activeEnd.HasValue
                           && cellDate.Date >= activeStart.Value.Date && cellDate.Date <= activeEnd.Value.Date;

            var resources = Application.Current.Resources;
            Brush bg;
            Brush fg;

            if (isStart || isEnd)
            {
                bg = (Brush)resources["APrimary"];
                fg = new SolidColorBrush(Colors.White);
            }
            else if (inRange)
            {
                bg = (Brush)resources["APrimarySoft"];
                fg = (Brush)resources["AText"];
            }
            else
            {
                bg = new SolidColorBrush(Colors.Transparent);
                fg = inCurrentMonth ? (Brush)resources["AText"] : (Brush)resources["ATextDisabled"];
            }

            var btn = new Button
            {
                Content = cellDate.Day.ToString(),
                Tag = cellDate,
                MinWidth = 0,
                MinHeight = 32,
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = bg,
                Foreground = fg,
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                FontSize = 11,
                FontWeight = (isStart || isEnd)
                    ? Microsoft.UI.Text.FontWeights.ExtraBold
                    : Microsoft.UI.Text.FontWeights.SemiBold,
            };

            btn.Click += DayCell_Click;

            Grid.SetRow(btn, i / 7);
            Grid.SetColumn(btn, i % 7);
            CalendarDayGrid.Children.Add(btn);
        }
    }

    private void DayCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DateTime clicked) return;

        var clickedStr = clicked.ToString("yyyy-MM-dd");
        draftPreset = "custom";

        if (activeDateField == "from")
        {
            draftFrom = clickedStr;
            if (!string.IsNullOrEmpty(draftTo) && string.Compare(clickedStr, draftTo, StringComparison.Ordinal) > 0)
                draftTo = "";
            activeDateField = "to";
        }
        else
        {
            if (!string.IsNullOrEmpty(draftFrom) && string.Compare(clickedStr, draftFrom, StringComparison.Ordinal) < 0)
            {
                draftTo = draftFrom;
                draftFrom = clickedStr;
            }
            else
            {
                draftTo = clickedStr;
            }
        }

        syncDraftUI();
        buildCalendar();
    }

    private static DateTime? parseDate(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateTime.TryParse(value, out var dt) ? dt.Date : null;
    }

    // ── Orientation ──
    private void OrientationAll_Click(object sender, RoutedEventArgs e)
    {
        try { OnOrientationSelect?.Invoke("all"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.OrientationAll_Click: {ex}"); }
    }

    private void OrientationPortrait_Click(object sender, RoutedEventArgs e)
    {
        try { OnOrientationSelect?.Invoke("portrait"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.OrientationPortrait_Click: {ex}"); }
    }

    private void OrientationLandscape_Click(object sender, RoutedEventArgs e)
    {
        try { OnOrientationSelect?.Invoke("landscape"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.OrientationLandscape_Click: {ex}"); }
    }

    // ── Tags dropdown ──
    private void TagTrigger_Click(object sender, RoutedEventArgs e)
    {
        var isOpen = TagDropdownPanel.Visibility == Visibility.Visible;
        TagDropdownPanel.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
        if (!isOpen)
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
        var query = TagSearchBox?.Text?.Trim() ?? "";
        var filtered = string.IsNullOrEmpty(query)
            ? allTagOptions
            : allTagOptions.Where(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        addCheckboxItem(TagCheckboxList, "すべてのタグ", null,
            boundFiltersState?.tagFilters.Count == 0,
            () => { clearAllTagFilters(); });

        foreach (var tag in filtered)
        {
            var isChecked = boundFiltersState?.tagFilters.Contains(tag) ?? false;
            var capturedTag = tag;
            addCheckboxItem(TagCheckboxList, tag, null, isChecked,
                () => { toggleTagFilter(capturedTag); });
        }

        TagCountLabel.Text = $"{allTagOptions.Count} タグ";
    }

    private void toggleTagFilter(string tag)
    {
        if (boundFiltersState is null) return;
        if (boundFiltersState.tagFilters.Contains(tag))
            OnTagFilterRemove?.Invoke(tag);
        else
            OnTagFilterAdd?.Invoke(tag);

        syncTagSummary();
        rebuildTagCheckboxList();
    }

    private void clearAllTagFilters()
    {
        if (boundFiltersState is null) return;
        var tags = boundFiltersState.tagFilters.ToList();
        foreach (var t in tags)
            OnTagFilterRemove?.Invoke(t);
        syncTagSummary();
        rebuildTagCheckboxList();
    }

    // ── World dropdown ──
    private void WorldTrigger_Click(object sender, RoutedEventArgs e)
    {
        var isOpen = WorldDropdownPanel.Visibility == Visibility.Visible;
        WorldDropdownPanel.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
        if (!isOpen)
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
        var query = WorldSearchBox?.Text?.Trim() ?? "";
        var filtered = string.IsNullOrEmpty(query)
            ? allWorldOptions
            : allWorldOptions.Where(w => w.world_name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false).ToList();

        var totalCount = allWorldOptions.Sum(w => w.count);
        addCheckboxItem(WorldCheckboxList, "すべてのワールド", $"{totalCount}枚",
            boundFiltersState?.worldFilters.Count == 0,
            () => { clearAllWorldFilters(); });

        if (filtered.Count > 0)
        {
            addSeparator(WorldCheckboxList);
            addGroupLabel(WorldCheckboxList, "訪問済みワールド");

            foreach (var opt in filtered)
            {
                if (opt.world_name is null) continue;
                var isChecked = boundFiltersState?.worldFilters.Contains(opt.world_name) ?? false;
                var capturedName = opt.world_name;
                addCheckboxItem(WorldCheckboxList, opt.world_name, $"{opt.count}枚", isChecked,
                    () => { toggleWorldFilter(capturedName); });
            }
        }

        WorldCountLabel.Text = $"{allWorldOptions.Count} ワールド";
    }

    private void toggleWorldFilter(string worldName)
    {
        if (boundFiltersState is null) return;
        if (boundFiltersState.worldFilters.Contains(worldName))
            OnWorldFilterRemove?.Invoke(worldName);
        else
            OnWorldFilterAdd?.Invoke(worldName);

        syncWorldSummary();
        rebuildWorldCheckboxList();
    }

    private void clearAllWorldFilters()
    {
        if (boundFiltersState is null) return;
        var worlds = boundFiltersState.worldFilters.ToList();
        foreach (var w in worlds)
            OnWorldFilterRemove?.Invoke(w);
        syncWorldSummary();
        rebuildWorldCheckboxList();
    }

    // ── Shared checkbox item builder ──
    private void addCheckboxItem(StackPanel parent, string label, string? countText, bool isChecked, Action onToggle)
    {
        var resources = Application.Current.Resources;

        var grid = new Grid { Padding = new Thickness(10, 8, 10, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (countText is not null)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = isChecked ? (Brush)resources["APrimary"] : (Brush)resources["ATextFaint"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(nameBlock, 0);
        grid.Children.Add(nameBlock);

        int checkCol = 1;
        if (countText is not null)
        {
            var countBlock = new TextBlock
            {
                Text = countText,
                FontSize = 11,
                FontFamily = (FontFamily)resources["AFontMono"],
                Foreground = (Brush)resources["ATextDisabled"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            Grid.SetColumn(countBlock, 1);
            grid.Children.Add(countBlock);
            checkCol = 2;
        }

        var checkBorder = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            BorderBrush = isChecked ? (Brush)resources["ABorderStrong"] : (Brush)resources["ABorder"],
            Background = isChecked ? (Brush)resources["APrimarySoft"] : (Brush)resources["ASurfaceSoft"],
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (isChecked)
        {
            checkBorder.Child = new TextBlock
            {
                Text = "✓",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
                Foreground = (Brush)resources["APrimary"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        Grid.SetColumn(checkBorder, checkCol);
        grid.Children.Add(checkBorder);

        var itemBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = isChecked ? (Brush)resources["APrimarySoft"] : new SolidColorBrush(Colors.Transparent),
            BorderBrush = isChecked ? (Brush)resources["ABorderStrong"] : new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(1),
            Child = grid,
        };

        var btn = new Button
        {
            Content = null,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };

        btn.Content = itemBorder;
        btn.Click += (_, _) => onToggle();

        parent.Children.Add(btn);
    }

    private static void addSeparator(StackPanel parent)
    {
        parent.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(4, 4, 4, 4),
            Background = (Brush)Application.Current.Resources["ABorder"],
        });
    }

    private static void addGroupLabel(StackPanel parent, string text)
    {
        parent.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
            Foreground = (Brush)Application.Current.Resources["ATextFaint"],
            Margin = new Thickness(10, 4, 0, 2),
        });
    }

    // ── Favorites ──
    private void FavoriteToggle_Click(object sender, RoutedEventArgs e)
    {
        if (boundFiltersState is null) return;
        boundFiltersState.FavoritesOnly = !boundFiltersState.FavoritesOnly;
    }

    // ── Folder / Grouping (hidden, kept for compatibility) ──
    private void FolderAll_Click(object sender, RoutedEventArgs e)
    {
        try { OnDisplayFolderSelect?.Invoke(DisplayFolderMode.all); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.FolderAll_Click: {ex}"); }
    }

    private void FolderPrimary_Click(object sender, RoutedEventArgs e)
    {
        try { OnDisplayFolderSelect?.Invoke(DisplayFolderMode.primary); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.FolderPrimary_Click: {ex}"); }
    }

    private void FolderSecondary_Click(object sender, RoutedEventArgs e)
    {
        try { OnDisplayFolderSelect?.Invoke(DisplayFolderMode.secondary); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.FolderSecondary_Click: {ex}"); }
    }

    public void SetGroupingEnabled(bool enabled)
    {
        GroupWorldBtn.IsEnabled = enabled;
    }

    private void GroupNone_Click(object sender, RoutedEventArgs e)
    {
        try { OnGroupingSelect?.Invoke(GroupingMode.none); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.GroupNone_Click: {ex}"); }
    }

    private void GroupWorld_Click(object sender, RoutedEventArgs e)
    {
        try { OnGroupingSelect?.Invoke(GroupingMode.world); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.GroupWorld_Click: {ex}"); }
    }

    // Legacy ComboBox-related methods removed — now handled by dropdown checkbox lists.
    // Keeping method signatures so callers don't break at compile time.
}