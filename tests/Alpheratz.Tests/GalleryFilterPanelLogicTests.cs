using Alpheratz.Features.Gallery;
using Alpheratz.Features.Gallery.Controls;
using Alpheratz.Models;
using Alpheratz.Shared.Models;

namespace Alpheratz.Tests;

/// <summary>
/// GalleryFilterPanel から分離した純粋な表示ロジックを検証するテスト。
///
/// フィルタパネル本体は WinUI の Button、Grid、Popup を大量に扱うため、通常のユニットテストで
/// 画面インスタンスを安全に作るのが難しい。
/// その中でも日付範囲、候補検索、ワールド名変換は UI 要素に依存しない仕様なので、
/// GalleryFilterPanelLogic へ切り出して同じ入力なら同じ表示状態になることを固定する。
/// </summary>
public sealed class GalleryFilterPanelLogicTests
{
    /// <summary>
    /// 日付範囲ラベルが未指定、片側指定、両側指定を同じ規則で表示することを確認する。
    ///
    /// フィルタ適用済みラベルとカレンダードラフト内ラベルは同じ表示規則を使う。
    /// 片側だけ指定された場合は「以降」「以前」を付け、
    /// ユーザーが開始日だけ、または終了日だけを選んだ状態を明示する。
    /// </summary>
    [Fact]
    public void FormatDateRangeLabel_UsesSameTextForEmptyPartialAndFullRanges()
    {
        Assert.Equal("すべての期間", GalleryFilterPanelLogic.FormatDateRangeLabel("", ""));
        Assert.Equal("2026-06-01以降", GalleryFilterPanelLogic.FormatDateRangeLabel("2026-06-01", ""));
        Assert.Equal("2026-06-30以前", GalleryFilterPanelLogic.FormatDateRangeLabel("", "2026-06-30"));
        Assert.Equal("2026-06-01～2026-06-30", GalleryFilterPanelLogic.FormatDateRangeLabel("2026-06-01", "2026-06-30"));
    }

    /// <summary>
    /// タグやワールドの選択件数サマリが、未選択時ラベルと選択中件数へ変換されることを確認する。
    ///
    /// この表示は TagSummaryLabel と WorldSummaryLabel で共通して使われる。
    /// 0 件だけは「すべて」を意味する文言を表示し、1 件以上では個別名を列挙せず件数だけを示す現在仕様を固定する。
    /// </summary>
    [Fact]
    public void FormatSelectionSummary_UsesEmptyLabelUntilAnyItemIsSelected()
    {
        Assert.Equal("すべてのタグ", GalleryFilterPanelLogic.FormatSelectionSummary(0, "すべてのタグ"));
        Assert.Equal("1件を選択中", GalleryFilterPanelLogic.FormatSelectionSummary(1, "すべてのタグ"));
        Assert.Equal("3件を選択中", GalleryFilterPanelLogic.FormatSelectionSummary(3, "すべてのワールド"));
    }

    /// <summary>
    /// フィルタ状態から、パネル上の各選択ボタンが active になる条件を確認する。
    ///
    /// GalleryFilterPanel では orientation、sort、folder、group の各ボタンを個別に塗り替える。
    /// ここでは UI の Brush 代入ではなく、その前段の「どのボタンを active と見なすか」を固定する。
    /// アイコン色のキーも同じ active 判定から決まるため、true/false の両方を検証する。
    /// </summary>
    [Fact]
    public void ActiveState_ReturnsButtonFlagsAndIconKeys()
    {
        var state = GalleryFilterPanelLogic.ActiveState(
            "portrait",
            SortMode.worldAsc,
            DisplayFolderMode.secondary,
            GroupingMode.world);

        Assert.False(state.OrientationAll);
        Assert.True(state.OrientationPortrait);
        Assert.False(state.OrientationLandscape);
        Assert.False(state.SortDate);
        Assert.True(state.SortWorld);
        Assert.False(state.FolderAll);
        Assert.False(state.FolderPrimary);
        Assert.True(state.FolderSecondary);
        Assert.False(state.GroupNone);
        Assert.True(state.GroupWorld);
        Assert.Equal("ATextOnPrimary", GalleryFilterPanelLogic.IconForegroundKey(true));
        Assert.Equal("ATextFaint", GalleryFilterPanelLogic.IconForegroundKey(false));
    }

    /// <summary>
    /// バッジ、日付トリガー、お気に入りトグル、日付ドラフトの表示状態を確認する。
    ///
    /// これらは code-behind では Visibility、Text、ThemeResource へ反映されるが、
    /// どの値を表示するかは入力値だけで決まる。
    /// バッジ 0 件、日付未指定、お気に入り off、空ドラフトの既定値も含めて現在仕様を固定する。
    /// </summary>
    [Fact]
    public void SmallDisplayHelpers_ReturnTextVisibilityAndThemeKeys()
    {
        Assert.Equal(new FilterBadgeDisplay(false, "0"), GalleryFilterPanelLogic.Badge(0));
        Assert.Equal(new FilterBadgeDisplay(true, "4"), GalleryFilterPanelLogic.Badge(4));

        Assert.Equal(new DateTriggerDisplay("すべての期間", false), GalleryFilterPanelLogic.DateTrigger("", ""));
        Assert.Equal(new DateTriggerDisplay("2026-06-01以降", true),
            GalleryFilterPanelLogic.DateTrigger("2026-06-01", ""));

        Assert.Equal(new FavoriteToggleDisplay(
                true,
                "AFavoriteSolid",
                null,
                "ATextOnPrimary",
                "ATextOnPrimary",
                "FavoriteSelectedButtonStyle"),
            GalleryFilterPanelLogic.FavoriteToggle(true));
        Assert.Equal(new FavoriteToggleDisplay(
                false,
                "ASurfaceSoft",
                null,
                "ATextDim",
                "ATextFaint",
                "GhostButtonStyle"),
            GalleryFilterPanelLogic.FavoriteToggle(false));

        Assert.Equal(new DateDraftDisplay("—", "—", "すべての期間"),
            GalleryFilterPanelLogic.DraftDisplay("", ""));
        Assert.Equal(new DateDraftDisplay("2026-06-01", "2026-06-30", "2026-06-01～2026-06-30"),
            GalleryFilterPanelLogic.DraftDisplay("2026-06-01", "2026-06-30"));
    }

    /// <summary>
    /// 日付プリセット名から、対応するボタンだけが active になることを確認する。
    ///
    /// プリセットは文字列として draftPreset に保持されるため、ボタンごとの比較を helper に集約している。
    /// 未知値や custom ではどのプリセットボタンも active にしない。
    /// </summary>
    [Fact]
    public void PresetState_ActivatesOnlyMatchingPresetButton()
    {
        Assert.Equal(new PresetActiveState(true, false, false, false, false, false),
            GalleryFilterPanelLogic.PresetState("today"));
        Assert.Equal(new PresetActiveState(false, true, false, false, false, false),
            GalleryFilterPanelLogic.PresetState("last7days"));
        Assert.Equal(new PresetActiveState(false, false, true, false, false, false),
            GalleryFilterPanelLogic.PresetState("thisMonth"));
        Assert.Equal(new PresetActiveState(false, false, false, true, false, false),
            GalleryFilterPanelLogic.PresetState("lastMonth"));
        Assert.Equal(new PresetActiveState(false, false, false, false, true, false),
            GalleryFilterPanelLogic.PresetState("halfYear"));
        Assert.Equal(new PresetActiveState(false, false, false, false, false, true),
            GalleryFilterPanelLogic.PresetState("oneYear"));
        Assert.Equal(new PresetActiveState(false, false, false, false, false, false),
            GalleryFilterPanelLogic.PresetState("custom"));
    }

    /// <summary>
    /// 日付プリセットが固定の today を基準に、開始日・終了日・表示月へ展開されることを確認する。
    ///
    /// GalleryFilterPanel では DateTime.Today を直接使うが、テストでは基準日を固定して
    /// 月末、先月、半年、1年の境界がぶれないようにする。
    /// 未知プリセットは UI 操作としては来ないが、ヘルパーは null を返して呼び出し側で何もしない。
    /// </summary>
    [Fact]
    public void ApplyPresetToDraft_ExpandsPresetRangesFromProvidedToday()
    {
        var today = new DateTime(2026, 6, 6);

        Assert.Equal(new DateDraft("2026-06-06", "2026-06-06", "today", new DateTime(2026, 6, 1)),
            GalleryFilterPanelLogic.ApplyPresetToDraft("today", today));
        Assert.Equal(new DateDraft("2026-05-31", "2026-06-06", "last7days", new DateTime(2026, 5, 1)),
            GalleryFilterPanelLogic.ApplyPresetToDraft("last7days", today));
        Assert.Equal(new DateDraft("2026-06-01", "2026-06-30", "thisMonth", new DateTime(2026, 6, 1)),
            GalleryFilterPanelLogic.ApplyPresetToDraft("thisMonth", today));
        Assert.Equal(new DateDraft("2026-05-01", "2026-05-31", "lastMonth", new DateTime(2026, 5, 1)),
            GalleryFilterPanelLogic.ApplyPresetToDraft("lastMonth", today));
        Assert.Equal(new DateDraft("2025-12-06", "2026-06-06", "halfYear", new DateTime(2025, 12, 1)),
            GalleryFilterPanelLogic.ApplyPresetToDraft("halfYear", today));
        Assert.Equal(new DateDraft("2025-06-06", "2026-06-06", "oneYear", new DateTime(2025, 6, 1)),
            GalleryFilterPanelLogic.ApplyPresetToDraft("oneYear", today));
        Assert.Null(GalleryFilterPanelLogic.ApplyPresetToDraft("unknown", today));
    }

    /// <summary>
    /// カレンダーの月移動が許可範囲内だけ反映されることを確認する。
    ///
    /// GalleryFilterPanel は 2000年1月より前と「今年+2年」より後へ移動させない。
    /// UI では範囲外クリック時に何も起きないため、helper は元の表示月をそのまま返す。
    /// </summary>
    [Fact]
    public void MoveVisibleMonth_ReturnsMovedMonthOnlyInsideBounds()
    {
        var min = new DateTime(2000, 1, 1);
        var max = new DateTime(2028, 1, 1);

        Assert.Equal(new DateTime(2026, 5, 1),
            GalleryFilterPanelLogic.MoveVisibleMonth(new DateTime(2026, 6, 1), -1, min, max));
        Assert.Equal(new DateTime(2026, 7, 1),
            GalleryFilterPanelLogic.MoveVisibleMonth(new DateTime(2026, 6, 1), 1, min, max));
        Assert.Equal(min, GalleryFilterPanelLogic.MoveVisibleMonth(min, -1, min, max));
        Assert.Equal(max, GalleryFilterPanelLogic.MoveVisibleMonth(max, 1, min, max));
    }

    /// <summary>
    /// 日付ドラフト適用時に、プリセット適用とカスタム範囲適用が区別されることを確認する。
    ///
    /// プリセットや none は GalleryFiltersState の日付文字列を直接触らず、既存の
    /// OnDatePresetSelect 経路へ渡す。
    /// custom だけはドラフト中の from/to をそのまま実フィルタへ書き込む。
    /// </summary>
    [Fact]
    public void ResolveDateApplyRequest_ClassifiesPresetClearAndCustomRange()
    {
        Assert.Equal(new DateApplyRequest(DateApplyKind.Preset, "today", "", ""),
            GalleryFilterPanelLogic.ResolveDateApplyRequest("today", "2026-06-01", "2026-06-30"));
        Assert.Equal(new DateApplyRequest(DateApplyKind.Preset, "none", "", ""),
            GalleryFilterPanelLogic.ResolveDateApplyRequest("none", "2026-06-01", "2026-06-30"));
        Assert.Equal(new DateApplyRequest(DateApplyKind.CustomRange, "custom", "2026-06-01", "2026-06-30"),
            GalleryFilterPanelLogic.ResolveDateApplyRequest("custom", "2026-06-01", "2026-06-30"));
    }

    /// <summary>
    /// 表示月と選択範囲からカレンダーセルの行、列、月内判定、範囲判定が作られることを確認する。
    ///
    /// カレンダー UI は前月末と翌月初の余白日も同じグリッドに表示する。
    /// 2026年6月は月曜開始なので、先頭セルが前月の日曜になり、
    /// 選択範囲 6/10-6/12 の開始、範囲内、終了がそれぞれ別セルとして判定される。
    /// </summary>
    [Fact]
    public void BuildCalendarDays_CreatesGridCellsWithCurrentMonthAndRangeFlags()
    {
        var cells = GalleryFilterPanelLogic.BuildCalendarDays(
            new DateTime(2026, 6, 1),
            "2026-06-10",
            "2026-06-12");

        Assert.Equal(35, cells.Count);
        Assert.Equal(new DateTime(2026, 5, 31), cells[0].Date);
        Assert.False(cells[0].InCurrentMonth);
        Assert.Equal(0, cells[0].Row);
        Assert.Equal(0, cells[0].Column);

        var start = Assert.Single(cells, cell => cell.Date == new DateTime(2026, 6, 10));
        var middle = Assert.Single(cells, cell => cell.Date == new DateTime(2026, 6, 11));
        var end = Assert.Single(cells, cell => cell.Date == new DateTime(2026, 6, 12));

        Assert.True(start.InCurrentMonth);
        Assert.True(start.IsStart);
        Assert.True(start.InRange);
        Assert.False(middle.IsStart);
        Assert.False(middle.IsEnd);
        Assert.True(middle.InRange);
        Assert.True(end.IsEnd);
        Assert.True(end.InRange);
    }

    /// <summary>
    /// カレンダー日付クリックが from/to ドラフトと次の入力対象を更新することを確認する。
    ///
    /// from 入力中に既存の to より後の日付を選ぶと、逆転した範囲を残さないため to を空にする。
    /// to 入力中に from より前の日付を選ぶと、ユーザー操作を無効化せず from/to を入れ替えて正しい範囲にする。
    /// </summary>
    [Fact]
    public void SelectCalendarDate_UpdatesDraftRangeWithoutLeavingReversedDates()
    {
        var fromAfterTo = GalleryFilterPanelLogic.SelectCalendarDate(
            "from",
            "2026-06-01",
            "2026-06-10",
            new DateTime(2026, 6, 20));
        var toBeforeFrom = GalleryFilterPanelLogic.SelectCalendarDate(
            "to",
            "2026-06-10",
            "",
            new DateTime(2026, 6, 1));
        var toAfterFrom = GalleryFilterPanelLogic.SelectCalendarDate(
            "to",
            "2026-06-10",
            "",
            new DateTime(2026, 6, 20));

        Assert.Equal(new DateSelectionDraft("2026-06-20", "", "to", "custom"), fromAfterTo);
        Assert.Equal(new DateSelectionDraft("2026-06-01", "2026-06-10", "to", "custom"), toBeforeFrom);
        Assert.Equal(new DateSelectionDraft("2026-06-10", "2026-06-20", "to", "custom"), toAfterFrom);
    }

    /// <summary>
    /// タグ候補の検索が空白を無視し、大文字小文字を区別せず元の候補順を保つことを確認する。
    ///
    /// タグドロップダウンでは検索語を入力しても、候補の表示順はタグマスタ順のままにする。
    /// 空文字や空白だけの場合は全候補を返し、部分一致では該当タグだけを残す。
    /// </summary>
    [Fact]
    public void FilterTags_TrimsQueryAndMatchesCaseInsensitively()
    {
        var tags = new[] { "Night", "world-hop", "Friends", "sunset" };

        Assert.Equal(tags, GalleryFilterPanelLogic.FilterTags(tags, " "));
        Assert.Equal(["Friends"], GalleryFilterPanelLogic.FilterTags(tags, "  fri  "));
        Assert.Equal(["world-hop"], GalleryFilterPanelLogic.FilterTags(tags, "HOP"));
    }

    /// <summary>
    /// タグドロップダウンのチェック行が、全解除行、検索結果、選択状態、件数表示を含めて作られることを確認する。
    ///
    /// UI 側は返された行を Button として並べるだけにしている。
    /// そのため「すべてのタグ」行は FilterValue を null にし、個別タグ行だけがトグル対象の値を持つ。
    /// 件数が未集計のタグは現在仕様どおり 0枚 と表示する。
    /// </summary>
    [Fact]
    public void BuildTagChoices_ReturnsAllRowFilteredRowsAndCountLabel()
    {
        var choices = GalleryFilterPanelLogic.BuildTagChoices(
            ["Night", "world-hop", "Friends"],
            "i",
            ["Night"],
            new Dictionary<string, long> { ["Night"] = 7 });

        Assert.Equal("登録タグ：3件", choices.CountLabel);
        Assert.Equal(3, choices.Rows.Count);
        Assert.Equal(new FilterChoiceRow("すべてのタグ", null, false, null), choices.Rows[0]);
        Assert.Equal(new FilterChoiceRow("Night", "7枚", true, "Night"), choices.Rows[1]);
        Assert.Equal(new FilterChoiceRow("Friends", "0枚", false, "Friends"), choices.Rows[2]);

        var emptySelection = GalleryFilterPanelLogic.BuildTagChoices(["A"], "", [], new Dictionary<string, long>());
        Assert.True(emptySelection.Rows[0].IsChecked);
    }

    /// <summary>
    /// ワールド候補の検索、未解決ワールド表示、合計枚数計算を確認する。
    ///
    /// DB では world_name が null または空白の写真を「ワールド不明」として扱う。
    /// フィルタ内部値は空文字のままだとコレクション上で扱いにくいため "unknown" に正規化し、
    /// 表示名検索ではその日本語ラベルにも一致することを固定する。
    /// </summary>
    [Fact]
    public void WorldHelpers_NormalizeUnknownWorldsAndFilterByDisplayName()
    {
        var worlds = new[]
        {
            new WorldFilterOptionDto { world_name = "Aqua Garden", count = 4 },
            new WorldFilterOptionDto { world_name = "", count = 2 },
            new WorldFilterOptionDto { world_name = "Night Market", count = 9 },
        };

        var filtered = GalleryFilterPanelLogic.FilterWorldOptions(worlds, "night");
        var unknown = GalleryFilterPanelLogic.FilterWorldOptions(worlds, "不明");

        Assert.Equal(WorldFilterValues.Unknown, GalleryFilterPanelLogic.GetWorldFilterValue(""));
        Assert.Equal("ワールド不明", GalleryFilterPanelLogic.GetWorldDisplayName(null));
        Assert.Equal(15, GalleryFilterPanelLogic.TotalWorldCount(worlds));
        Assert.Single(filtered);
        Assert.Equal("Night Market", filtered[0].world_name);
        Assert.Single(unknown);
        Assert.Equal("", unknown[0].world_name);
    }

    /// <summary>
    /// ワールドドロップダウンのチェック行が、合計行、訪問済みグループ有無、表示名、内部値を含むことを確認する。
    ///
    /// ワールド不明は表示名を日本語へ置き換えつつ、フィルタ内部値は unknown に正規化する。
    /// 検索結果が 0 件でも「すべてのワールド」行は残し、グループ見出しは出さない。
    /// </summary>
    [Fact]
    public void BuildWorldChoices_ReturnsAllRowVisitedRowsAndSearchState()
    {
        var worlds = new[]
        {
            new WorldFilterOptionDto { world_name = "Aqua Garden", count = 4 },
            new WorldFilterOptionDto { world_name = "", count = 2 },
            new WorldFilterOptionDto { world_name = "Night Market", count = 9 },
        };

        var choices = GalleryFilterPanelLogic.BuildWorldChoices(worlds, "不明", [WorldFilterValues.Unknown]);
        var noMatch = GalleryFilterPanelLogic.BuildWorldChoices(worlds, "missing", []);

        Assert.Equal("ワールド：3件", choices.CountLabel);
        Assert.True(choices.HasVisitedWorlds);
        Assert.Equal(new FilterChoiceRow("すべてのワールド", "15枚", false, null), choices.Rows[0]);
        Assert.Equal(new FilterChoiceRow("ワールド不明", "2枚", true, WorldFilterValues.Unknown), choices.Rows[1]);

        Assert.False(noMatch.HasVisitedWorlds);
        Assert.Single(noMatch.Rows);
        Assert.True(noMatch.Rows[0].IsChecked);
    }

    /// <summary>
    /// 個別フィルタのトグル操作と全解除対象のコピーを確認する。
    ///
    /// code-behind は helper が返す Add/Remove に従ってコールバックを呼び分ける。
    /// 全解除では選択コレクションを列挙しながら削除コールバックを呼ぶため、先に配列へコピーして
    /// コレクション変更の影響を受けないようにしている。
    /// </summary>
    [Fact]
    public void ToggleActionAndValuesToClear_UseCurrentSelectionWithoutExposingEnumeration()
    {
        var selected = new List<string> { "Night", "Friends" };
        var clearValues = GalleryFilterPanelLogic.ValuesToClear(selected);

        selected.Clear();

        Assert.Equal(FilterToggleAction.Remove, GalleryFilterPanelLogic.ToggleAction(["Night"], "Night"));
        Assert.Equal(FilterToggleAction.Add, GalleryFilterPanelLogic.ToggleAction(["Night"], "Friends"));
        Assert.Equal(["Night", "Friends"], clearValues);
    }

    /// <summary>
    /// GalleryFiltersState の PropertyChanged から、再同期する UI 領域だけが選ばれることを確認する。
    ///
    /// code-behind では各プロパティ通知ごとにボタン、バッジ、日付ラベル、候補リストを再描画している。
    /// すべてを毎回更新すると検索リストやカレンダーの再構築が過剰になるため、
    /// helper が必要な領域だけを plan として返す仕様を固定する。
    /// </summary>
    [Fact]
    public void SyncPlanForFilterProperty_ReturnsOnlyRequiredRefreshTargets()
    {
        Assert.Equal(
            new FilterPanelSyncPlan(ActiveStates: true),
            GalleryFilterPanelLogic.SyncPlanForFilterProperty(nameof(GalleryFiltersState.SortMode)));
        Assert.Equal(
            new FilterPanelSyncPlan(Badge: true),
            GalleryFilterPanelLogic.SyncPlanForFilterProperty(nameof(GalleryFiltersState.ActiveFilterCount)));
        Assert.Equal(
            new FilterPanelSyncPlan(TagChoices: true),
            GalleryFilterPanelLogic.SyncPlanForFilterProperty(nameof(GalleryFiltersState.TagFilterCounts)));
        Assert.Equal(
            new FilterPanelSyncPlan(DateTrigger: true),
            GalleryFilterPanelLogic.SyncPlanForFilterProperty(nameof(GalleryFiltersState.DateFrom)));
        Assert.Equal(
            new FilterPanelSyncPlan(FavoriteToggle: true),
            GalleryFilterPanelLogic.SyncPlanForFilterProperty(nameof(GalleryFiltersState.FavoritesOnly)));
        Assert.Equal(FilterPanelSyncPlan.All, GalleryFilterPanelLogic.SyncPlanForFilterProperty("BatchCompleted"));
        Assert.Equal(FilterPanelSyncPlan.None, GalleryFilterPanelLogic.SyncPlanForFilterProperty("Other"));
    }

    /// <summary>
    /// タグ/ワールドの選択コレクション変更では、件数・サマリ・候補リストをまとめて更新することを確認する。
    ///
    /// 選択状態が変わると ActiveFilterCount、タグサマリ、ワールドサマリ、チェック行の checked 表示が同時に変わる。
    /// そのため PropertyChanged とは別に、CollectionChanged 用の固定 plan を持つ。
    /// </summary>
    [Fact]
    public void SyncPlanForSelectionCollectionChanged_ReturnsSelectionRefreshTargets()
    {
        Assert.Equal(
            new FilterPanelSyncPlan(
                Badge: true,
                TagSummary: true,
                WorldSummary: true,
                TagChoices: true,
                WorldChoices: true),
            GalleryFilterPanelLogic.SyncPlanForSelectionCollectionChanged());
    }

    /// <summary>
    /// active ボタンのテーマキーと、ドロップダウン開閉後の副作用が決まることを確認する。
    ///
    /// active ボタンは青背景と白文字を使い、非 active では薄い面色と透明枠へ戻す。
    /// ドロップダウンは閉じている状態から開いた時だけ検索欄をクリアして候補を再構築し、
    /// 開いている状態から閉じる時は既存検索語を触らない。
    /// </summary>
    [Fact]
    public void ButtonStyleAndDropdownToggle_ReturnDisplayKeysAndOpenSideEffect()
    {
        Assert.Equal(
            new FilterPanelButtonStyle("APrimary", "ATextOnPrimary", null, 0, true),
            GalleryFilterPanelLogic.ActiveButtonStyle(true));
        Assert.Equal(
            new FilterPanelButtonStyle("ASurfaceSoft", "ATextDim", null, 0, true),
            GalleryFilterPanelLogic.ActiveButtonStyle(false));

        Assert.Equal(new DropdownToggleState(true, true), GalleryFilterPanelLogic.ToggleDropdown(isOpen: false));
        Assert.Equal(new DropdownToggleState(false, false), GalleryFilterPanelLogic.ToggleDropdown(isOpen: true));
    }

    /// <summary>
    /// カレンダー日付セルの状態から、背景・文字色・フォント太さの表示キーが選ばれることを確認する。
    ///
    /// 範囲の開始/終了日は primary 背景と白文字で強調する。
    /// 範囲内の日は primary soft 背景と通常文字色にし、範囲外の日は透明背景で、
    /// 表示月外の日だけ disabled 文字色へ落とす。
    /// </summary>
    [Fact]
    public void CalendarDayVisual_ReturnsThemeKeysForRangeAndOutOfMonthCells()
    {
        Assert.Equal(
            new CalendarDayVisual("APrimary", false, null, true, FilterPanelFontWeight.ExtraBold),
            GalleryFilterPanelLogic.CalendarDayVisual(new CalendarDayCell(
                new DateTime(2026, 6, 10), 1, 3, true, true, false, true)));
        Assert.Equal(
            new CalendarDayVisual("APrimarySoft", false, "AText", false, FilterPanelFontWeight.SemiBold),
            GalleryFilterPanelLogic.CalendarDayVisual(new CalendarDayCell(
                new DateTime(2026, 6, 11), 1, 4, true, false, false, true)));
        Assert.Equal(
            new CalendarDayVisual(null, true, "AText", false, FilterPanelFontWeight.SemiBold),
            GalleryFilterPanelLogic.CalendarDayVisual(new CalendarDayCell(
                new DateTime(2026, 6, 13), 1, 6, true, false, false, false)));
        Assert.Equal(
            new CalendarDayVisual(null, true, "ATextDisabled", false, FilterPanelFontWeight.SemiBold),
            GalleryFilterPanelLogic.CalendarDayVisual(new CalendarDayCell(
                new DateTime(2026, 5, 31), 0, 0, false, false, false, false)));
    }

    /// <summary>
    /// チェックリスト行の checked 状態と件数表示有無から、テーマキーとチェック列が決まることを確認する。
    ///
    /// タグ/ワールド行のチェックは左端に固定し、選択状態はチェックボックス自体で示す。
    /// 行背景と行枠線を使うとリストが重く見えるため、選択時も行の面色は変えない。
    /// </summary>
    [Fact]
    public void CheckboxVisual_ReturnsThemeKeysCheckColumnAndCheckmarkState()
    {
        Assert.Equal(
            new FilterCheckboxVisual(
                "APrimary",
                "ATextDisabled",
                "APrimary",
                "APrimary",
                null,
                null,
                "ATextOnPrimary",
                CheckColumn: 0,
                CheckmarkVisible: true),
            GalleryFilterPanelLogic.CheckboxVisual(isChecked: true, hasCountText: true));

        Assert.Equal(
            new FilterCheckboxVisual(
                "ATextFaint",
                "ATextDisabled",
                "ABorder",
                "ASurface",
                null,
                null,
                "ATextOnPrimary",
                CheckColumn: 0,
                CheckmarkVisible: false),
            GalleryFilterPanelLogic.CheckboxVisual(isChecked: false, hasCountText: false));
    }
}
