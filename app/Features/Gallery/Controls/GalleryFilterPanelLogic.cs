using Alpheratz.Models;
using Alpheratz.Shared.Models;

namespace Alpheratz.Features.Gallery.Controls;

/// <summary>
/// GalleryFilterPanel の表示計算を WinUI コントロールから切り離して扱う補助ロジック。
/// 日付範囲、候補絞り込み、ワールド名の表示変換だけを持ち、画面要素には触れない。
/// </summary>
internal static class GalleryFilterPanelLogic
{
    public const string UnknownWorldFilterValue = WorldFilterValues.Unknown;
    public const string UnknownWorldLabel = "ワールド不明";
    public const string AllTagsEmptyLabel = "すべてのタグ";
    public const string AllWorldsEmptyLabel = "すべてのワールド";
    public const string WorldGroupLabel = "訪問済みワールド";
    public const string DateFieldFrom = "from";
    public const string DateFieldTo = "to";
    public const string DatePresetNone = "none";
    public const string DatePresetCustom = "custom";

    public static readonly string[] WeekLabels = ["日", "月", "火", "水", "木", "金", "土"];

    /// <summary>GalleryFiltersState の変更通知から、再同期すべき UI 領域を返す。</summary>
    public static FilterPanelSyncPlan SyncPlanForFilterProperty(string? propertyName)
        => propertyName switch
        {
            nameof(GalleryFiltersState.OrientationFilter)
                or nameof(GalleryFiltersState.SortMode)
                or nameof(GalleryFiltersState.DisplayFolderMode)
                or nameof(GalleryFiltersState.GroupingMode)
                => new FilterPanelSyncPlan(ActiveStates: true),
            nameof(GalleryFiltersState.ActiveFilterCount)
                => new FilterPanelSyncPlan(Badge: true),
            nameof(GalleryFiltersState.TagFilterCounts)
                => new FilterPanelSyncPlan(TagChoices: true),
            "BatchCompleted"
                => FilterPanelSyncPlan.All,
            nameof(GalleryFiltersState.DateFrom)
                or nameof(GalleryFiltersState.DateTo)
                or nameof(GalleryFiltersState.DatePreset)
                => new FilterPanelSyncPlan(DateTrigger: true),
            nameof(GalleryFiltersState.FavoritesOnly)
                => new FilterPanelSyncPlan(FavoriteToggle: true),
            _ => FilterPanelSyncPlan.None,
        };

    /// <summary>タグ/ワールド選択コレクション変更時に必要な再同期領域を返す。</summary>
    public static FilterPanelSyncPlan SyncPlanForSelectionCollectionChanged()
        => new(Badge: true, TagSummary: true, WorldSummary: true, TagChoices: true, WorldChoices: true);

    /// <summary>日付ドラフトまたは適用済み日付から、画面に表示する期間ラベルを作る。</summary>
    public static string FormatDateRangeLabel(string? from, string? to)
    {
        var hasFrom = !string.IsNullOrEmpty(from);
        var hasTo = !string.IsNullOrEmpty(to);
        if (!hasFrom && !hasTo) return "すべての期間";
        return $"{(hasFrom ? from : "...")} ~ {(hasTo ? to : "...")}";
    }

    /// <summary>複数選択フィルタの件数を、未選択時ラベルまたは選択中件数へ変換する。</summary>
    public static string FormatSelectionSummary(int count, string emptyLabel)
        => count == 0 ? emptyLabel : $"{count}件選択中";

    /// <summary>active ボタンに適用するテーマキーと枠線幅を返す。</summary>
    public static FilterPanelButtonStyle ActiveButtonStyle(bool active)
        => active
            ? new FilterPanelButtonStyle("APrimary", "ATextOnPrimary", null, 0, true)
            : new FilterPanelButtonStyle("ASurfaceSoft", "ATextDim", null, 0, true);

    /// <summary>現在のフィルタ値から、各選択ボタンの active 状態を作る。</summary>
    public static FilterPanelActiveState ActiveState(
        string orientationFilter,
        SortMode sortMode,
        DisplayFolderMode displayFolderMode,
        GroupingMode groupingMode)
        => new(
            OrientationAll: orientationFilter == "all",
            OrientationPortrait: orientationFilter == "portrait",
            OrientationLandscape: orientationFilter == "landscape",
            SortDate: sortMode == SortMode.dateDesc,
            SortWorld: sortMode == SortMode.worldAsc,
            FolderAll: displayFolderMode == DisplayFolderMode.all,
            FolderPrimary: displayFolderMode == DisplayFolderMode.primary,
            FolderSecondary: displayFolderMode == DisplayFolderMode.secondary,
            GroupNone: groupingMode == GroupingMode.none,
            GroupWorld: groupingMode == GroupingMode.world);

    /// <summary>active 状態からアイコンの前景色リソースキーを返す。</summary>
    public static string IconForegroundKey(bool active) => active ? "ATextOnPrimary" : "ATextFaint";

    /// <summary>有効フィルタ数からバッジ表示状態を作る。</summary>
    public static FilterBadgeDisplay Badge(int count)
        => new(count > 0, count.ToString());

    /// <summary>日付トリガーのラベルとクリアボタン表示状態を作る。</summary>
    public static DateTriggerDisplay DateTrigger(string? from, string? to)
        => new(
            FormatDateRangeLabel(from, to),
            !string.IsNullOrEmpty(from) || !string.IsNullOrEmpty(to));

    /// <summary>お気に入りトグルの状態からテーマリソースキーを返す。</summary>
    public static FavoriteToggleDisplay FavoriteToggle(bool active)
        => active
            ? new FavoriteToggleDisplay(true, "AFavoriteSoft", "AFavoriteBorder", "AFavorite")
            : new FavoriteToggleDisplay(false, "ASurfaceSoft", null, "ATextDim");

    /// <summary>日付ドラフトの表示テキストと範囲ラベルを作る。</summary>
    public static DateDraftDisplay DraftDisplay(string? from, string? to)
        => new(
            string.IsNullOrEmpty(from) ? "---" : from,
            string.IsNullOrEmpty(to) ? "---" : to,
            FormatDateRangeLabel(from, to));

    /// <summary>日付ドラフトのプリセット選択状態を返す。</summary>
    public static PresetActiveState PresetState(string preset)
        => new(
            Today: preset == "today",
            Last7Days: preset == "last7days",
            ThisMonth: preset == "thisMonth",
            LastMonth: preset == "lastMonth",
            HalfYear: preset == "halfYear",
            OneYear: preset == "oneYear");

    /// <summary>現在の開閉状態から、次のドロップダウン表示状態と開いた直後の再構築要否を返す。</summary>
    public static DropdownToggleState ToggleDropdown(bool isOpen)
        => new(IsOpen: !isOpen, ShouldResetSearchAndRebuild: !isOpen);

    /// <summary>日付プリセットを開始日・終了日・表示月へ展開する。未知プリセットは null。</summary>
    public static DateDraft? ApplyPresetToDraft(string preset, DateTime today)
    {
        DateTime from;
        DateTime to;
        switch (preset)
        {
            case "today":
                from = today;
                to = today;
                break;
            case "last7days":
                from = today.AddDays(-6);
                to = today;
                break;
            case "thisMonth":
                from = new DateTime(today.Year, today.Month, 1);
                to = from.AddMonths(1).AddDays(-1);
                break;
            case "lastMonth":
                from = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                to = new DateTime(today.Year, today.Month, 1).AddDays(-1);
                break;
            case "halfYear":
                from = today.AddMonths(-6);
                to = today;
                break;
            case "oneYear":
                from = today.AddYears(-1);
                to = today;
                break;
            default:
                return null;
        }

        return new DateDraft(
            from.ToString("yyyy-MM-dd"),
            to.ToString("yyyy-MM-dd"),
            preset,
            new DateTime(from.Year, from.Month, 1));
    }

    /// <summary>表示月を指定月数だけ移動する。許可範囲外なら元の月を維持する。</summary>
    public static DateTime MoveVisibleMonth(DateTime visibleMonth, int monthOffset, DateTime minMonth, DateTime maxMonth)
    {
        var moved = visibleMonth.AddMonths(monthOffset);
        return moved < minMonth || moved > maxMonth ? visibleMonth : moved;
    }

    /// <summary>日付ドラフトの適用方法を、プリセット適用、解除、カスタム範囲に分類する。</summary>
    public static DateApplyRequest ResolveDateApplyRequest(string preset, string from, string to)
        => preset switch
        {
            DatePresetCustom => new DateApplyRequest(DateApplyKind.CustomRange, DatePresetCustom, from, to),
            DatePresetNone => new DateApplyRequest(DateApplyKind.Preset, DatePresetNone, string.Empty, string.Empty),
            _ => new DateApplyRequest(DateApplyKind.Preset, preset, string.Empty, string.Empty),
        };

    /// <summary>表示月と選択範囲から、カレンダーに必要な日付セル情報を作る。</summary>
    public static IReadOnlyList<CalendarDayCell> BuildCalendarDays(DateTime visibleMonth, string draftFrom, string draftTo)
    {
        var firstOfMonth = new DateTime(visibleMonth.Year, visibleMonth.Month, 1);
        var startDow = (int)firstOfMonth.DayOfWeek;
        var daysInMonth = DateTime.DaysInMonth(visibleMonth.Year, visibleMonth.Month);
        var startDate = firstOfMonth.AddDays(-startDow);
        var totalCells = ((startDow + daysInMonth + 6) / 7) * 7;
        var activeStart = ParseDate(draftFrom);
        var activeEnd = ParseDate(draftTo);
        var cells = new List<CalendarDayCell>(totalCells);

        for (var i = 0; i < totalCells; i++)
        {
            var cellDate = startDate.AddDays(i);
            var isStart = activeStart.HasValue && cellDate.Date == activeStart.Value.Date;
            var isEnd = activeEnd.HasValue && cellDate.Date == activeEnd.Value.Date;
            var inRange = activeStart.HasValue
                && activeEnd.HasValue
                && cellDate.Date >= activeStart.Value.Date
                && cellDate.Date <= activeEnd.Value.Date;

            cells.Add(new CalendarDayCell(
                cellDate,
                i / 7,
                i % 7,
                cellDate.Month == visibleMonth.Month && cellDate.Year == visibleMonth.Year,
                isStart,
                isEnd,
                inRange));
        }

        return cells;
    }

    /// <summary>カレンダーでクリックされた日付を、from/to ドラフトと次の入力対象へ反映する。</summary>
    public static DateSelectionDraft SelectCalendarDate(string activeDateField, string draftFrom, string draftTo, DateTime clicked)
    {
        var clickedStr = clicked.ToString("yyyy-MM-dd");
        if (activeDateField == DateFieldFrom)
        {
            var nextTo = !string.IsNullOrEmpty(draftTo)
                && string.Compare(clickedStr, draftTo, StringComparison.Ordinal) > 0
                    ? string.Empty
                    : draftTo;
            return new DateSelectionDraft(clickedStr, nextTo, DateFieldTo, DatePresetCustom);
        }

        if (!string.IsNullOrEmpty(draftFrom)
            && string.Compare(clickedStr, draftFrom, StringComparison.Ordinal) < 0)
        {
            return new DateSelectionDraft(clickedStr, draftFrom, activeDateField, DatePresetCustom);
        }

        return new DateSelectionDraft(draftFrom, clickedStr, activeDateField, DatePresetCustom);
    }

    /// <summary>カレンダー日付セルの範囲状態から、テーマキーとフォント太さを返す。</summary>
    public static CalendarDayVisual CalendarDayVisual(CalendarDayCell cell)
    {
        if (cell.IsStart || cell.IsEnd)
        {
            return new CalendarDayVisual(
                BackgroundKey: "APrimary",
                BackgroundTransparent: false,
                ForegroundKey: null,
                ForegroundWhite: true,
                FontWeight: FilterPanelFontWeight.ExtraBold);
        }

        if (cell.InRange)
        {
            return new CalendarDayVisual(
                BackgroundKey: "APrimarySoft",
                BackgroundTransparent: false,
                ForegroundKey: "AText",
                ForegroundWhite: false,
                FontWeight: FilterPanelFontWeight.SemiBold);
        }

        return new CalendarDayVisual(
            BackgroundKey: null,
            BackgroundTransparent: true,
            ForegroundKey: cell.InCurrentMonth ? "AText" : "ATextDisabled",
            ForegroundWhite: false,
            FontWeight: FilterPanelFontWeight.SemiBold);
    }

    /// <summary>yyyy-MM-dd 形式を想定して日付を読む。空や不正値は null。</summary>
    public static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateTime.TryParse(value, out var dt) ? dt.Date : null;
    }

    /// <summary>タグ候補を検索語で絞り込む。検索語が空なら元の候補順を保つ。</summary>
    public static IReadOnlyList<string> FilterTags(IEnumerable<string> allTags, string? query)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        return string.IsNullOrEmpty(trimmed)
            ? allTags.ToList()
            : allTags.Where(tag => tag.Contains(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>タグドロップダウンに表示するチェック行を作る。</summary>
    public static FilterChoiceList BuildTagChoices(
        IEnumerable<string> allTags,
        string? query,
        IEnumerable<string> selectedTags,
        IReadOnlyDictionary<string, long> tagCounts)
    {
        var selected = selectedTags.ToHashSet(StringComparer.Ordinal);
        var rows = new List<FilterChoiceRow>
        {
            new("すべてのタグ", null, selected.Count == 0, null),
        };

        foreach (var tag in FilterTags(allTags, query))
        {
            var countText = tagCounts.TryGetValue(tag, out var count) ? $"{count}枚" : "0枚";
            rows.Add(new FilterChoiceRow(tag, countText, selected.Contains(tag), tag));
        }

        return new FilterChoiceList(rows, $"{allTags.Count()} タグ");
    }

    /// <summary>ワールド候補を表示名で絞り込む。検索語が空なら元の候補順を保つ。</summary>
    public static IReadOnlyList<WorldFilterOptionDto> FilterWorldOptions(IEnumerable<WorldFilterOptionDto> allWorlds, string? query)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        return string.IsNullOrEmpty(trimmed)
            ? allWorlds.ToList()
            : allWorlds
                .Where(world => GetWorldDisplayName(world.world_name).Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    /// <summary>ワールドドロップダウンに表示するチェック行を作る。</summary>
    public static WorldChoiceList BuildWorldChoices(
        IEnumerable<WorldFilterOptionDto> allWorlds,
        string? query,
        IEnumerable<string> selectedWorlds)
    {
        var worlds = allWorlds.ToList();
        var selected = selectedWorlds.ToHashSet(StringComparer.Ordinal);
        var filtered = FilterWorldOptions(worlds, query);
        var rows = new List<FilterChoiceRow>
        {
            new("すべてのワールド", $"{TotalWorldCount(worlds)}枚", selected.Count == 0, null),
        };

        foreach (var opt in filtered)
        {
            var filterValue = GetWorldFilterValue(opt.world_name);
            rows.Add(new FilterChoiceRow(
                GetWorldDisplayName(opt.world_name),
                $"{opt.count}枚",
                selected.Contains(filterValue),
                filterValue));
        }

        return new WorldChoiceList(rows, $"{worlds.Count} ワールド", filtered.Count > 0);
    }

    /// <summary>DB の world_name 値をフィルタ内部値へ変換する。</summary>
    public static string GetWorldFilterValue(string? worldName)
        => string.IsNullOrWhiteSpace(worldName) ? UnknownWorldFilterValue : worldName;

    /// <summary>DB の world_name 値を UI 表示名へ変換する。</summary>
    public static string GetWorldDisplayName(string? worldName)
        => string.IsNullOrWhiteSpace(worldName) ? UnknownWorldLabel : worldName;

    /// <summary>ワールド候補の写真枚数を合計する。</summary>
    public static long TotalWorldCount(IEnumerable<WorldFilterOptionDto> allWorlds)
        => allWorlds.Sum(world => world.count);

    /// <summary>現在の選択状態から、個別フィルタを追加するか削除するかを返す。</summary>
    public static FilterToggleAction ToggleAction(IEnumerable<string> selectedValues, string value)
        => selectedValues.Contains(value) ? FilterToggleAction.Remove : FilterToggleAction.Add;

    /// <summary>全解除操作で削除対象にする値を、列挙中の変更に影響されない配列として返す。</summary>
    public static IReadOnlyList<string> ValuesToClear(IEnumerable<string> selectedValues)
        => selectedValues.ToArray();

    /// <summary>チェックリスト 1 行のテーマキー、列位置、チェックマーク表示を返す。</summary>
    public static FilterCheckboxVisual CheckboxVisual(bool isChecked, bool hasCountText)
        => new(
            NameForegroundKey: isChecked ? "APrimary" : "ATextFaint",
            CountForegroundKey: "ATextDisabled",
            CheckBorderKey: isChecked ? "APrimary" : "ABorder",
            CheckBackgroundKey: isChecked ? "APrimary" : "ASurface",
            ItemBackgroundKey: null,
            ItemBorderKey: null,
            CheckmarkForegroundKey: "ATextOnPrimary",
            CheckColumn: 0,
            CheckmarkVisible: isChecked);
}

/// <summary>フィルタパネルで再同期すべき UI 領域。</summary>
internal sealed record FilterPanelSyncPlan(
    bool ActiveStates = false,
    bool Badge = false,
    bool DateTrigger = false,
    bool FavoriteToggle = false,
    bool TagSummary = false,
    bool WorldSummary = false,
    bool TagChoices = false,
    bool WorldChoices = false)
{
    public static readonly FilterPanelSyncPlan None = new();
    public static readonly FilterPanelSyncPlan All = new(
        ActiveStates: true,
        Badge: true,
        DateTrigger: true,
        FavoriteToggle: true,
        TagSummary: true,
        WorldSummary: true,
        TagChoices: true,
        WorldChoices: true);
}

/// <summary>active ボタンに適用するテーマキーと枠線幅。</summary>
internal sealed record FilterPanelButtonStyle(
    string BackgroundKey,
    string ForegroundKey,
    string? BorderKey,
    double BorderThickness,
    bool BorderTransparent);

/// <summary>フィルタパネル内の各選択ボタンの active 状態。</summary>
internal sealed record FilterPanelActiveState(
    bool OrientationAll,
    bool OrientationPortrait,
    bool OrientationLandscape,
    bool SortDate,
    bool SortWorld,
    bool FolderAll,
    bool FolderPrimary,
    bool FolderSecondary,
    bool GroupNone,
    bool GroupWorld);

/// <summary>有効フィルタ数バッジの表示状態。</summary>
internal sealed record FilterBadgeDisplay(bool Visible, string Text);

/// <summary>日付トリガーボタンの表示状態。</summary>
internal sealed record DateTriggerDisplay(string Label, bool ClearVisible);

/// <summary>お気に入りトグルの表示に使うテーマリソースキー。</summary>
internal sealed record FavoriteToggleDisplay(bool Liked, string BackgroundKey, string? BorderKey, string LabelForegroundKey);

/// <summary>日付ドラフト入力欄の表示状態。</summary>
internal sealed record DateDraftDisplay(string FromText, string ToText, string RangeLabel);

/// <summary>日付プリセットボタンの active 状態。</summary>
internal sealed record PresetActiveState(
    bool Today,
    bool Last7Days,
    bool ThisMonth,
    bool LastMonth,
    bool HalfYear,
    bool OneYear);

/// <summary>日付プリセット適用後のドラフト状態。</summary>
internal sealed record DateDraft(string From, string To, string Preset, DateTime VisibleMonth);

/// <summary>ドロップダウン開閉操作後の状態。</summary>
internal sealed record DropdownToggleState(bool IsOpen, bool ShouldResetSearchAndRebuild);

/// <summary>日付ドラフトを適用するときの処理種別。</summary>
internal enum DateApplyKind
{
    Preset,
    CustomRange,
}

/// <summary>日付ドラフトの適用要求。</summary>
internal sealed record DateApplyRequest(DateApplyKind Kind, string Preset, string From, string To);

/// <summary>カレンダーに表示する1日分の状態。</summary>
internal sealed record CalendarDayCell(
    DateTime Date,
    int Row,
    int Column,
    bool InCurrentMonth,
    bool IsStart,
    bool IsEnd,
    bool InRange);

/// <summary>カレンダー日付セルに適用するテーマキーとフォント太さ。</summary>
internal sealed record CalendarDayVisual(
    string? BackgroundKey,
    bool BackgroundTransparent,
    string? ForegroundKey,
    bool ForegroundWhite,
    FilterPanelFontWeight FontWeight);

/// <summary>フィルタパネル内で扱うフォント太さ。</summary>
internal enum FilterPanelFontWeight
{
    SemiBold,
    ExtraBold,
}

/// <summary>カレンダー日付選択後のドラフト状態。</summary>
internal sealed record DateSelectionDraft(string From, string To, string ActiveDateField, string Preset);

/// <summary>ドロップダウンに表示する1つのチェック行。</summary>
internal sealed record FilterChoiceRow(string Label, string? CountText, bool IsChecked, string? FilterValue);

/// <summary>タグドロップダウン全体の表示状態。</summary>
internal sealed record FilterChoiceList(IReadOnlyList<FilterChoiceRow> Rows, string CountLabel);

/// <summary>ワールドドロップダウン全体の表示状態。</summary>
internal sealed record WorldChoiceList(IReadOnlyList<FilterChoiceRow> Rows, string CountLabel, bool HasVisitedWorlds);

/// <summary>個別フィルタのトグル操作種別。</summary>
internal enum FilterToggleAction
{
    Add,
    Remove,
}

/// <summary>チェックリスト 1 行に適用するテーマキーと列位置。</summary>
internal sealed record FilterCheckboxVisual(
    string NameForegroundKey,
    string CountForegroundKey,
    string CheckBorderKey,
    string CheckBackgroundKey,
    string? ItemBackgroundKey,
    string? ItemBorderKey,
    string CheckmarkForegroundKey,
    int CheckColumn,
    bool CheckmarkVisible);
