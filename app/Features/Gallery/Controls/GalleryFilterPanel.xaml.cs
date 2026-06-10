using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Models;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Features.Gallery.Controls;

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class GalleryFilterPanel : UserControl
{
    private GalleryFiltersState? boundFiltersState;
    private UiObservableCollection<WorldFilterOptionDto>? boundWorldOptions;
    private UiObservableCollection<string>? boundMasterTags;
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

    // インスタンス生成時の「今月」で初期化。アプリ起動から日を跨いでも UI 操作で
    // visibleMonth は更新されるので問題なし（コンストラクタ評価で十分）。
    private DateTime visibleMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string activeDateField = GalleryFilterPanelLogic.DateFieldFrom;
    private string draftFrom = "";
    private string draftTo = "";
    private string draftPreset = "none";

    private List<WorldFilterOptionDto> allWorldOptions = [];
    private List<string> allTagOptions = [];

    /// <summary>フィルタパネルを初期化し、初期表示に必要な曜日ヘッダを構築する。</summary>
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
        ActualThemeChanged += OnActualThemeChanged;
        AppLogger.Trace("GalleryFilterPanel.ctor: exit");
    }

    /// <summary>
    /// テーマ切替時に code-behind で設定したブラシを再適用する。
    /// {ThemeResource} はランタイム解決されるが、setActive/syncFavoriteToggle で直接代入した
    /// SolidColorBrush は古いテーマの値が残るため、明示的に再評価する必要がある。
    /// </summary>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        try
        {
            RefreshThemeBoundVisuals();
            DispatcherQueue?.TryEnqueue(RefreshThemeBoundVisuals);
        }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.OnActualThemeChanged: {ex}"); }
    }

    private void RefreshThemeBoundVisuals()
    {
        ApplyPanelSurfaceBrushes();
        syncActiveStates();
        syncFavoriteToggle();
        buildWeekdayHeaders();
        rebuildTagCheckboxList();
        rebuildWorldCheckboxList();
        if (DatePickerPopup.Visibility == Visibility.Visible)
            buildCalendar();
    }

    /// <summary>Collapsed 配下でも検索条件パネルのテーマ色を現在選択テーマへ明示反映する。</summary>
    public void ApplyThemeNow(ElementTheme theme)
    {
        RequestedTheme = theme;
        ThemeHelper.NotifySelectedThemeChanged(theme);
        RefreshThemeBoundVisuals();
        DispatcherQueue?.TryEnqueue(RefreshThemeBoundVisuals);
    }

    /// <summary>カード化した検索条件パネルの土台・カード・区切り線を現在テーマのブラシで塗り直す。</summary>
    private void ApplyPanelSurfaceBrushes()
    {
        var background = ResolveThemeBrush(this, "AFilterPanelBackground");
        var sectionLine = ResolveThemeBrush(this, "AFilterPanelSectionLine")
            ?? ResolveThemeBrush(this, "AFilterPanelBorder");
        var border = ResolveThemeBrush(this, "AFilterPanelBorder");

        if (background is not null)
        {
            FilterPanelRoot.Background = background;
            FilterPanelHeader.Background = background;
            FilterPanelFooter.Background = background;
        }
        if (border is not null)
        {
            FilterPanelRoot.BorderBrush = border;
            FilterPanelHeader.BorderBrush = border;
            FilterPanelFooter.BorderBrush = border;
        }

        foreach (var item in FindDescendants<Border>(this))
        {
            if (Equals(item.Tag, "FilterPanelCard"))
            {
                if (background is not null) item.Background = background;
                if (sectionLine is not null) item.BorderBrush = sectionLine;
            }
            else if (Equals(item.Tag, "FilterPanelDivider"))
            {
                if (sectionLine is not null) item.Background = sectionLine;
            }
        }
    }

    /// <summary>
    /// Unloaded 後も boundFiltersState から参照され続けないよう、購読を解除する。
    /// 死んだコントロール上で再描画が走るとリークや例外の原因になる。
    /// </summary>
    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (boundFiltersState is not null)
            {
                boundFiltersState.PropertyChanged -= OnFiltersChanged;
                boundFiltersState.worldFilters.CollectionChanged -= OnFilterCollectionChanged;
                boundFiltersState.tagFilters.CollectionChanged -= OnFilterCollectionChanged;
                boundFiltersState = null;
            }
            if (boundWorldOptions is not null)
            {
                boundWorldOptions.CollectionChanged -= OnWorldOptionsChanged;
                boundWorldOptions = null;
            }
            if (boundMasterTags is not null)
            {
                boundMasterTags.CollectionChanged -= OnMasterTagsChanged;
                boundMasterTags = null;
            }
            ActualThemeChanged -= OnActualThemeChanged;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryFilterPanel.UserControl_Unloaded: threw: {ex}");
        }
    }

    /// <summary>ワールドフィルタ候補の参照を差し替え、一覧表示を再構築する。</summary>
    public void setWorldFilterOptions(UiObservableCollection<WorldFilterOptionDto> options)
    {
        AppLogger.Trace($"GalleryFilterPanel.setWorldFilterOptions: enter count={options.Count}");
        try
        {
            if (boundWorldOptions is not null)
                boundWorldOptions.CollectionChanged -= OnWorldOptionsChanged;

            boundWorldOptions = options;
            boundWorldOptions.CollectionChanged += OnWorldOptionsChanged;
            allWorldOptions = options.ToList();
            rebuildWorldCheckboxList();
        }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.setWorldFilterOptions: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterPanel.setWorldFilterOptions: exit");
    }

    /// <summary>タグマスタの参照を差し替え、タグチェックリストを再構築する。</summary>
    public void setMasterTagsSource(UiObservableCollection<string> tags)
    {
        AppLogger.Trace($"GalleryFilterPanel.setMasterTagsSource: enter count={tags.Count}");
        try
        {
            if (boundMasterTags is not null)
                boundMasterTags.CollectionChanged -= OnMasterTagsChanged;

            boundMasterTags = tags;
            boundMasterTags.CollectionChanged += OnMasterTagsChanged;
            allTagOptions = tags.ToList();
            rebuildTagCheckboxList();
        }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.setMasterTagsSource: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterPanel.setMasterTagsSource: exit");
    }

    /// <summary>フィルタ状態をパネルへバインドし、変更通知を UI へ反映する。</summary>
    public void bindFiltersState(GalleryFiltersState state)
    {
        AppLogger.Trace("GalleryFilterPanel.bindFiltersState: enter");
        try
        {
            if (boundFiltersState is not null)
            {
                boundFiltersState.PropertyChanged -= OnFiltersChanged;
                boundFiltersState.worldFilters.CollectionChanged -= OnFilterCollectionChanged;
                boundFiltersState.tagFilters.CollectionChanged -= OnFilterCollectionChanged;
            }

            boundFiltersState = state;
            boundFiltersState.PropertyChanged += OnFiltersChanged;
            boundFiltersState.worldFilters.CollectionChanged += OnFilterCollectionChanged;
            boundFiltersState.tagFilters.CollectionChanged += OnFilterCollectionChanged;
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

    /// <summary>ワールド候補の増減に合わせてワールドチェックリストを作り直す。</summary>
    private void OnWorldOptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        try
        {
            if (boundWorldOptions is not null)
                allWorldOptions = boundWorldOptions.ToList();
            rebuildWorldCheckboxList();
        }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.OnWorldOptionsChanged: threw: {ex}"); }
    }

    /// <summary>タグマスタの増減に合わせてタグチェックリストを作り直す。</summary>
    private void OnMasterTagsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        try
        {
            if (boundMasterTags is not null)
                allTagOptions = boundMasterTags.ToList();
            rebuildTagCheckboxList();
        }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.OnMasterTagsChanged: threw: {ex}"); }
    }

    /// <summary>選択中タグ/ワールドの変更に合わせてパネルの表示状態を同期する。</summary>
    private void OnFilterCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        try
        {
            ApplySyncPlan(GalleryFilterPanelLogic.SyncPlanForSelectionCollectionChanged());
        }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.OnFilterCollectionChanged: threw: {ex}"); }
    }

    /// <summary>現在のフィルタ後件数をパネル上の件数表示へ反映する。</summary>
    public void setFilteredCount(int count)
    {
        FilteredCountRun.Text = count.ToString();
    }

    /// <summary>GalleryFiltersState のプロパティ変更に応じて関連 UI を同期する。</summary>
    private void OnFiltersChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplySyncPlan(GalleryFilterPanelLogic.SyncPlanForFilterProperty(e.PropertyName));
    }

    /// <summary>helper が返した同期 plan に従って必要な UI だけを更新する。</summary>
    private void ApplySyncPlan(FilterPanelSyncPlan plan)
    {
        if (plan.ActiveStates) syncActiveStates();
        if (plan.Badge) syncBadge();
        if (plan.DateTrigger) syncDateTriggerLabel();
        if (plan.FavoriteToggle) syncFavoriteToggle();
        if (plan.TagSummary) syncTagSummary();
        if (plan.WorldSummary) syncWorldSummary();
        if (plan.TagChoices) rebuildTagCheckboxList();
        if (plan.WorldChoices) rebuildWorldCheckboxList();
    }

    /// <summary>現在のフィルタ値から各ボタンやセクションの active 表示を更新する。</summary>
    private void syncActiveStates()
    {
        if (boundFiltersState is null) return;
        try
        {
            var state = GalleryFilterPanelLogic.ActiveState(
                boundFiltersState.OrientationFilter,
                boundFiltersState.SortMode,
                boundFiltersState.DisplayFolderMode,
                boundFiltersState.GroupingMode);

            setActive(OrientationAllBtn, state.OrientationAll);
            setActive(OrientationPortraitBtn, state.OrientationPortrait);
            setActive(OrientationLandscapeBtn, state.OrientationLandscape);
            OrientationAllIcon.Foreground = ResolveThemeBrushOrTransparent(OrientationAllIcon, GalleryFilterPanelLogic.IconForegroundKey(state.OrientationAll));
            OrientationLandscapeIcon.Foreground = ResolveThemeBrushOrTransparent(OrientationLandscapeIcon, GalleryFilterPanelLogic.IconForegroundKey(state.OrientationLandscape));
            OrientationPortraitIcon.Foreground = ResolveThemeBrushOrTransparent(OrientationPortraitIcon, GalleryFilterPanelLogic.IconForegroundKey(state.OrientationPortrait));

            setActive(SortDateBtn, state.SortDate);
            setActive(SortWorldBtn, state.SortWorld);

            setActive(FolderAllBtn, state.FolderAll);
            setActive(FolderPrimaryBtn, state.FolderPrimary);
            setActive(FolderSecondaryBtn, state.FolderSecondary);

            setActive(GroupNoneBtn, state.GroupNone);
            setActive(GroupWorldBtn, state.GroupWorld);
        }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.syncActiveStates: threw: {ex}"); }
    }

    /// <summary>有効フィルタ数のバッジ表示を更新する。</summary>
    private void syncBadge()
    {
        if (boundFiltersState is null) return;
        var badge = GalleryFilterPanelLogic.Badge(boundFiltersState.ActiveFilterCount);
        BadgeBorder.Visibility = ToVisibility(badge.Visible);
        BadgeText.Text = badge.Text;
    }

    /// <summary>日付フィルタのトリガーボタンに表示するラベルを更新する。</summary>
    private void syncDateTriggerLabel()
    {
        if (boundFiltersState is null) return;
        var from = boundFiltersState.DateFrom;
        var to = boundFiltersState.DateTo;
        var state = GalleryFilterPanelLogic.DateTrigger(from, to);
        DateRangeLabel.Text = state.Label;
        DateClearBtn.Visibility = ToVisibility(state.ClearVisible);
    }

    /// <summary>お気に入りフィルタのトグル表示を現在値に合わせる。</summary>
    private void syncFavoriteToggle()
    {
        if (boundFiltersState is null) return;
        var state = GalleryFilterPanelLogic.FavoriteToggle(boundFiltersState.FavoritesOnly);
        FavoriteStarIcon.Liked = state.Liked;
        FavoriteToggleBtn.Background = ResolveThemeBrush(FavoriteToggleBtn, state.BackgroundKey);
        FavoriteToggleBtn.BorderBrush = state.BorderKey is null
            ? new SolidColorBrush(Colors.Transparent)
            : ResolveThemeBrush(FavoriteToggleBtn, state.BorderKey);
        FavoriteLabel.Foreground = ResolveThemeBrush(FavoriteLabel, state.LabelForegroundKey);
    }

    /// <summary>選択中タグの件数サマリを更新する。</summary>
    private void syncTagSummary()
    {
        if (boundFiltersState is null) return;
        var count = boundFiltersState.tagFilters.Count;
        TagSummaryLabel.Text = GalleryFilterPanelLogic.FormatSelectionSummary(count, GalleryFilterPanelLogic.AllTagsEmptyLabel);
    }

    /// <summary>選択中ワールドの件数サマリを更新する。</summary>
    private void syncWorldSummary()
    {
        if (boundFiltersState is null) return;
        var count = boundFiltersState.worldFilters.Count;
        WorldSummaryLabel.Text = GalleryFilterPanelLogic.FormatSelectionSummary(count, GalleryFilterPanelLogic.AllWorldsEmptyLabel);
    }

    /// <summary>ボタンの active 用スタイルクラスを有効/無効にする。</summary>
    private static void setActive(Button btn, bool active)
    {
        var styleKey = active ? "PrimaryButtonStyle" : "GhostButtonStyle";
        if (ThemeHelper.AppResource<Style>(styleKey) is { } buttonStyle)
            btn.Style = buttonStyle;

        var style = GalleryFilterPanelLogic.ActiveButtonStyle(active);
        btn.Background = ResolveThemeBrush(btn, style.BackgroundKey);
        btn.Foreground = ResolveThemeBrush(btn, style.ForegroundKey);
        btn.BorderBrush = style.BorderTransparent || style.BorderKey is null
            ? new SolidColorBrush(Colors.Transparent)
            : ResolveThemeBrush(btn, style.BorderKey);
        btn.BorderThickness = new Thickness(style.BorderThickness);
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

    /// <summary>ワールド名順ソートと日付順ソートを切り替える。</summary>
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
        activeDateField = GalleryFilterPanelLogic.DateFieldFrom;

        var toggle = GalleryFilterPanelLogic.ToggleDropdown(DatePickerPopup.Visibility == Visibility.Visible);
        DatePickerPopup.Visibility = ToVisibility(toggle.IsOpen);

        if (toggle.ShouldResetSearchAndRebuild)
        {
            syncDraftUI();
            buildCalendar();
        }
    }

    /// <summary>日付フィルタを解除し、日付ドラフトも空にする。</summary>
    private void DateClear_Click(object sender, RoutedEventArgs e)
    {
        try { OnDatePresetSelect?.Invoke("none"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.DateClear_Click: {ex}"); }
    }

    // ── From/To chips ──
    private void FromChip_Click(object sender, RoutedEventArgs e)
    {
        activeDateField = GalleryFilterPanelLogic.DateFieldFrom;
        syncDraftChipHighlight();
    }

    /// <summary>日付ドラフトの終了日入力へフォーカスを移す。</summary>
    private void ToChip_Click(object sender, RoutedEventArgs e)
    {
        activeDateField = GalleryFilterPanelLogic.DateFieldTo;
        syncDraftChipHighlight();
    }

    /// <summary>日付ドラフトで現在入力中の from/to チップを強調する。</summary>
    private void syncDraftChipHighlight()
    {
        setActive(FromChipBtn, activeDateField == GalleryFilterPanelLogic.DateFieldFrom);
        setActive(ToChipBtn, activeDateField == GalleryFilterPanelLogic.DateFieldTo);
    }

    /// <summary>日付ドラフトの入力値をテキストとカレンダー表示へ反映する。</summary>
    private void syncDraftUI()
    {
        var state = GalleryFilterPanelLogic.DraftDisplay(draftFrom, draftTo);
        FromValueText.Text = state.FromText;
        ToValueText.Text = state.ToText;
        CalendarRangeLabel.Text = state.RangeLabel;

        syncDraftChipHighlight();
        syncPresetHighlight();
    }

    /// <summary>現在の日付プリセットに対応するボタンを強調する。</summary>
    private void syncPresetHighlight()
    {
        var state = GalleryFilterPanelLogic.PresetState(draftPreset);
        setActive(PresetTodayBtn, state.Today);
        setActive(PresetLast7Btn, state.Last7Days);
        setActive(PresetThisMonthBtn, state.ThisMonth);
        setActive(PresetLastMonthBtn, state.LastMonth);
        setActive(PresetHalfYearBtn, state.HalfYear);
        setActive(PresetOneYearBtn, state.OneYear);
    }

    // ── Presets ──
    private void applyPresetToDraft(string preset)
    {
        var draft = GalleryFilterPanelLogic.ApplyPresetToDraft(preset, DateTime.Today);
        if (draft is null) return;
        draftPreset = preset;
        draftFrom = draft.From;
        draftTo = draft.To;
        visibleMonth = draft.VisibleMonth;
        syncDraftUI();
        buildCalendar();
    }

    /// <summary>日付ドラフトへ「今日」プリセットを適用する。</summary>
    private void PresetToday_Click(object sender, RoutedEventArgs e) => applyPresetToDraft("today");

    /// <summary>日付ドラフトへ「過去7日」プリセットを適用する。</summary>
    private void PresetLast7Days_Click(object sender, RoutedEventArgs e) => applyPresetToDraft("last7days");

    /// <summary>日付ドラフトへ「今月」プリセットを適用する。</summary>
    private void PresetThisMonth_Click(object sender, RoutedEventArgs e) => applyPresetToDraft("thisMonth");

    /// <summary>日付ドラフトへ「先月」プリセットを適用する。</summary>
    private void PresetLastMonth_Click(object sender, RoutedEventArgs e) => applyPresetToDraft("lastMonth");

    /// <summary>日付ドラフトへ「半年」プリセットを適用する。</summary>
    private void PresetHalfYear_Click(object sender, RoutedEventArgs e) => applyPresetToDraft("halfYear");

    /// <summary>日付ドラフトへ「1年」プリセットを適用する。</summary>
    private void PresetOneYear_Click(object sender, RoutedEventArgs e) => applyPresetToDraft("oneYear");

    // ── Calendar navigation ──
    private static readonly DateTime CalendarMinMonth = new(2000, 1, 1);
    // static readonly の初期化式は静的コンストラクタ＝アプリ起動時刻で固定される。
    // 0 時跨ぎでアプリを起動しっぱなしにすると「今年 + 2 年」のラインが進まないので、
    // 都度評価する property にする。
    private static DateTime CalendarMaxMonth => new(DateTime.Today.Year + 2, 1, 1);

    /// <summary>カレンダー表示月を1か月戻す。</summary>
    private void MonthPrev_Click(object sender, RoutedEventArgs e)
    {
        var prev = GalleryFilterPanelLogic.MoveVisibleMonth(visibleMonth, -1, CalendarMinMonth, CalendarMaxMonth);
        if (prev == visibleMonth) return;
        visibleMonth = prev;
        buildCalendar();
    }

    /// <summary>カレンダー表示月を1か月進める。</summary>
    private void MonthNext_Click(object sender, RoutedEventArgs e)
    {
        var next = GalleryFilterPanelLogic.MoveVisibleMonth(visibleMonth, 1, CalendarMinMonth, CalendarMaxMonth);
        if (next == visibleMonth) return;
        visibleMonth = next;
        buildCalendar();
    }

    /// <summary>カレンダーの選択範囲と日付ドラフトをクリアする。</summary>
    private void CalendarClear_Click(object sender, RoutedEventArgs e)
    {
        draftPreset = GalleryFilterPanelLogic.DatePresetNone;
        draftFrom = "";
        draftTo = "";
        syncDraftUI();
        buildCalendar();
    }

    /// <summary>カレンダーで選んだ日付ドラフトを実フィルタへ適用する。</summary>
    private void CalendarApply_Click(object sender, RoutedEventArgs e)
    {
        if (boundFiltersState is null) return;
        try
        {
            var request = GalleryFilterPanelLogic.ResolveDateApplyRequest(draftPreset, draftFrom, draftTo);
            if (request.Kind == DateApplyKind.CustomRange)
            {
                boundFiltersState.applyDateRange(DatePreset.custom, request.From, request.To);
            }
            else
            {
                OnDatePresetSelect?.Invoke(request.Preset);
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
                Text = GalleryFilterPanelLogic.WeekLabels[i],
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
                Foreground = ResolveThemeBrush(WeekdayHeaderGrid, "ATextFaint"),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Grid.SetColumn(tb, i);
            WeekdayHeaderGrid.Children.Add(tb);
        }
    }

    /// <summary>表示月と選択範囲に基づいてカレンダーの日付セルを再構築する。</summary>
    private void buildCalendar()
    {
        MonthLabel.Text = $"{visibleMonth.Year}年 {visibleMonth.Month}月";

        CalendarDayGrid.ColumnDefinitions.Clear();
        CalendarDayGrid.RowDefinitions.Clear();
        CalendarDayGrid.Children.Clear();

        for (int i = 0; i < 7; i++)
            CalendarDayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cells = GalleryFilterPanelLogic.BuildCalendarDays(visibleMonth, draftFrom, draftTo);

        int rows = cells.Count / 7;
        for (int r = 0; r < rows; r++)
            CalendarDayGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        foreach (var cell in cells)
        {
            var visual = GalleryFilterPanelLogic.CalendarDayVisual(cell);

            var btn = new Button
            {
                Content = cell.Date.Day.ToString(),
                Tag = cell.Date,
                MinWidth = 0,
                MinHeight = 32,
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = visual.BackgroundTransparent || visual.BackgroundKey is null
                    ? new SolidColorBrush(Colors.Transparent)
                    : ResolveThemeBrush(CalendarDayGrid, visual.BackgroundKey),
                Foreground = visual.ForegroundWhite || visual.ForegroundKey is null
                    ? new SolidColorBrush(Colors.White)
                    : ResolveThemeBrush(CalendarDayGrid, visual.ForegroundKey),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                FontSize = 11,
                FontWeight = visual.FontWeight == FilterPanelFontWeight.ExtraBold
                    ? Microsoft.UI.Text.FontWeights.ExtraBold
                    : Microsoft.UI.Text.FontWeights.SemiBold,
            };

            btn.Click += DayCell_Click;

            Grid.SetRow(btn, cell.Row);
            Grid.SetColumn(btn, cell.Column);
            CalendarDayGrid.Children.Add(btn);
        }
    }

    /// <summary>クリックされた日付セルを from/to のドラフト選択へ反映する。</summary>
    private void DayCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DateTime clicked) return;

        var draft = GalleryFilterPanelLogic.SelectCalendarDate(activeDateField, draftFrom, draftTo, clicked);
        draftPreset = draft.Preset;
        draftFrom = draft.From;
        draftTo = draft.To;
        activeDateField = draft.ActiveDateField;

        syncDraftUI();
        buildCalendar();
    }

    // ── Orientation ──
    private void OrientationAll_Click(object sender, RoutedEventArgs e)
    {
        try { OnOrientationSelect?.Invoke("all"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.OrientationAll_Click: {ex}"); }
    }

    /// <summary>縦向きフィルタの選択/解除を切り替える。</summary>
    private void OrientationPortrait_Click(object sender, RoutedEventArgs e)
    {
        try { OnOrientationSelect?.Invoke("portrait"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.OrientationPortrait_Click: {ex}"); }
    }

    /// <summary>横向きフィルタの選択/解除を切り替える。</summary>
    private void OrientationLandscape_Click(object sender, RoutedEventArgs e)
    {
        try { OnOrientationSelect?.Invoke("landscape"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.OrientationLandscape_Click: {ex}"); }
    }

    // ── Tags dropdown ──
    private void TagTrigger_Click(object sender, RoutedEventArgs e)
    {
        var toggle = GalleryFilterPanelLogic.ToggleDropdown(TagDropdownPanel.Visibility == Visibility.Visible);
        TagDropdownPanel.Visibility = ToVisibility(toggle.IsOpen);
        if (toggle.ShouldResetSearchAndRebuild)
        {
            TagSearchBox.Text = "";
            rebuildTagCheckboxList();
        }
    }

    /// <summary>タグ検索文字列の変更に合わせてタグチェックリストを絞り込む。</summary>
    private void TagSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        rebuildTagCheckboxList();
    }

    /// <summary>タグ検索語と選択状態に基づいてタグチェックリストを再構築する。</summary>
    private void rebuildTagCheckboxList()
    {
        TagCheckboxList.Children.Clear();
        var query = TagSearchBox?.Text?.Trim() ?? "";
        var choices = GalleryFilterPanelLogic.BuildTagChoices(
            allTagOptions,
            query,
            boundFiltersState?.tagFilters ?? Enumerable.Empty<string>(),
            boundFiltersState?.TagFilterCounts ?? new Dictionary<string, long>());

        foreach (var row in choices.Rows)
        {
            var capturedValue = row.FilterValue;
            addCheckboxItem(TagCheckboxList, row.Label, row.CountText, row.IsChecked,
                () =>
                {
                    if (capturedValue is null) clearAllTagFilters();
                    else toggleTagFilter(capturedValue);
                });
        }

        TagCountLabel.Text = choices.CountLabel;
    }

    /// <summary>指定タグのフィルタ選択状態を切り替える。</summary>
    private void toggleTagFilter(string tag)
    {
        if (boundFiltersState is null) return;
        if (GalleryFilterPanelLogic.ToggleAction(boundFiltersState.tagFilters, tag) == FilterToggleAction.Remove)
        {
            OnTagFilterRemove?.Invoke(tag);
        }
        else
        {
            OnTagFilterAdd?.Invoke(tag);
        }

        syncTagSummary();
        rebuildTagCheckboxList();
    }

    /// <summary>選択中のタグフィルタをすべて解除する。</summary>
    private void clearAllTagFilters()
    {
        if (boundFiltersState is null) return;
        foreach (var t in GalleryFilterPanelLogic.ValuesToClear(boundFiltersState.tagFilters))
            OnTagFilterRemove?.Invoke(t);
        syncTagSummary();
        rebuildTagCheckboxList();
    }

    // ── World dropdown ──
    private void WorldTrigger_Click(object sender, RoutedEventArgs e)
    {
        var toggle = GalleryFilterPanelLogic.ToggleDropdown(WorldDropdownPanel.Visibility == Visibility.Visible);
        WorldDropdownPanel.Visibility = ToVisibility(toggle.IsOpen);
        if (toggle.ShouldResetSearchAndRebuild)
        {
            WorldSearchBox.Text = "";
            rebuildWorldCheckboxList();
        }
    }

    /// <summary>ワールド検索文字列の変更に合わせてワールドチェックリストを絞り込む。</summary>
    private void WorldSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        rebuildWorldCheckboxList();
    }

    /// <summary>ワールド検索語と選択状態に基づいてワールドチェックリストを再構築する。</summary>
    private void rebuildWorldCheckboxList()
    {
        WorldCheckboxList.Children.Clear();
        var query = WorldSearchBox?.Text?.Trim() ?? "";
        var choices = GalleryFilterPanelLogic.BuildWorldChoices(
            allWorldOptions,
            query,
            boundFiltersState?.worldFilters ?? Enumerable.Empty<string>());

        var allRow = choices.Rows[0];
        addCheckboxItem(WorldCheckboxList, allRow.Label, allRow.CountText, allRow.IsChecked,
            () => { clearAllWorldFilters(); });

        if (choices.HasVisitedWorlds)
        {
            addSeparator(WorldCheckboxList);
            addGroupLabel(WorldCheckboxList, GalleryFilterPanelLogic.WorldGroupLabel);

            foreach (var row in choices.Rows.Skip(1))
            {
                var capturedValue = row.FilterValue;
                if (capturedValue is null) continue;
                addCheckboxItem(WorldCheckboxList, row.Label, row.CountText, row.IsChecked,
                    () => { toggleWorldFilter(capturedValue); });
            }
        }

        WorldCountLabel.Text = choices.CountLabel;
    }

    /// <summary>指定ワールドのフィルタ選択状態を切り替える。</summary>
    private void toggleWorldFilter(string worldName)
    {
        if (boundFiltersState is null) return;
        if (GalleryFilterPanelLogic.ToggleAction(boundFiltersState.worldFilters, worldName) == FilterToggleAction.Remove)
        {
            OnWorldFilterRemove?.Invoke(worldName);
        }
        else
        {
            OnWorldFilterAdd?.Invoke(worldName);
        }

        syncWorldSummary();
        rebuildWorldCheckboxList();
    }

    /// <summary>選択中のワールドフィルタをすべて解除する。</summary>
    private void clearAllWorldFilters()
    {
        if (boundFiltersState is null) return;
        foreach (var w in GalleryFilterPanelLogic.ValuesToClear(boundFiltersState.worldFilters))
            OnWorldFilterRemove?.Invoke(w);
        syncWorldSummary();
        rebuildWorldCheckboxList();
    }

    // ── Shared checkbox item builder ──
    private void addCheckboxItem(StackPanel parent, string label, string? countText, bool isChecked, Action onToggle)
    {
        var visual = GalleryFilterPanelLogic.CheckboxVisual(isChecked, countText is not null);
        var grid = new Grid
        {
            Padding = new Thickness(10, 8, 10, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (countText is not null)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ResolveThemeBrush(parent, visual.NameForegroundKey),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(10, 0, 0, 0),
        };
        Grid.SetColumn(nameBlock, 1);
        grid.Children.Add(nameBlock);

        if (countText is not null)
        {
            var countBlock = new TextBlock
            {
                Text = countText,
                FontSize = 11,
                FontFamily = ThemeHelper.AppResource<FontFamily>("AFontMono"),
                Foreground = ResolveThemeBrush(parent, visual.CountForegroundKey),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                MinWidth = 36,
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(countBlock, 2);
            grid.Children.Add(countBlock);
        }

        var checkBorder = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            BorderBrush = ResolveThemeBrush(parent, visual.CheckBorderKey),
            Background = ResolveThemeBrush(parent, visual.CheckBackgroundKey),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (visual.CheckmarkVisible)
        {
            checkBorder.Child = new Alpheratz.Shared.Controls.AppIcon
            {
                IconName = "check",
                IconSize = 12,
                Foreground = ResolveThemeBrushOrTransparent(parent, visual.CheckmarkForegroundKey),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        Grid.SetColumn(checkBorder, 0);
        grid.Children.Add(checkBorder);

        var itemBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = visual.ItemBackgroundKey is null
                ? new SolidColorBrush(Colors.Transparent)
                : ResolveThemeBrush(parent, visual.ItemBackgroundKey),
            BorderBrush = visual.ItemBorderKey is null
                ? new SolidColorBrush(Colors.Transparent)
                : ResolveThemeBrush(parent, visual.ItemBorderKey),
            BorderThickness = new Thickness(0),
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

    /// <summary>メニュー内へ区切り線を追加する。</summary>
    private static void addSeparator(StackPanel parent)
    {
        parent.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(4, 4, 4, 4),
            Background = ResolveThemeBrush(parent, "ABorder"),
        });
    }

    /// <summary>メニュー内へグループ見出しラベルを追加する。</summary>
    private static void addGroupLabel(StackPanel parent, string text)
    {
        parent.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
            Foreground = ResolveThemeBrush(parent, "ATextFaint"),
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

    /// <summary>プライマリフォルダのみ表示するフィルタを切り替える。</summary>
    private void FolderPrimary_Click(object sender, RoutedEventArgs e)
    {
        try { OnDisplayFolderSelect?.Invoke(DisplayFolderMode.primary); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.FolderPrimary_Click: {ex}"); }
    }

    /// <summary>セカンダリフォルダのみ表示するフィルタを切り替える。</summary>
    private void FolderSecondary_Click(object sender, RoutedEventArgs e)
    {
        try { OnDisplayFolderSelect?.Invoke(DisplayFolderMode.secondary); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.FolderSecondary_Click: {ex}"); }
    }

    /// <summary>グルーピング操作の有効/無効を外部から切り替える。</summary>
    public void SetGroupingEnabled(bool enabled)
    {
        GroupWorldBtn.IsEnabled = enabled;
    }

    /// <summary>グルーピングなしへ切り替える。</summary>
    private void GroupNone_Click(object sender, RoutedEventArgs e)
    {
        try { OnGroupingSelect?.Invoke(GroupingMode.none); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.GroupNone_Click: {ex}"); }
    }

    /// <summary>ワールド単位のグルーピングへ切り替える。</summary>
    private void GroupWorld_Click(object sender, RoutedEventArgs e)
    {
        try { OnGroupingSelect?.Invoke(GroupingMode.world); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterPanel.GroupWorld_Click: {ex}"); }
    }

    // 旧 ComboBox 関連の処理はチェックリスト型ドロップダウンへ統合済み。
    // 呼び出し側の互換性を保つため、公開面だけ残している。

    /// <summary>bool の表示状態を WinUI の Visibility へ変換する。</summary>
    private static Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>現在のテーマで使う Brush を取得する。未定義キーなら null を返す。</summary>
    private static Brush? ResolveThemeBrush(FrameworkElement element, string key)
        => ThemeHelper.BrushForSelectedTheme(key) ?? ThemeHelper.Brush(element, key);

    /// <summary>必須 Brush プロパティ用に、テーマ未定義時は透明ブラシへフォールバックする。</summary>
    private static Brush ResolveThemeBrushOrTransparent(FrameworkElement element, string key)
        => ResolveThemeBrush(element, key) ?? new SolidColorBrush(Colors.Transparent);

    /// <summary>指定要素の VisualTree から型が一致する子孫を列挙する。</summary>
    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindDescendants<T>(child))
                yield return nested;
        }
    }
}
