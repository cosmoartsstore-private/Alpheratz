using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace Alpheratz.Features.Shell.Controls;

/// <summary>
/// 画面上部のヘッダーバー。
/// 構成：
///   [検索条件] [検索 TextBox] [ビューモード] [グループ化] [複数選択] [設定]
/// 3 つの切替ボタン (ビューモード / グループ化 / 複数選択) は
/// アクティブ時にアクセント色 (APrimarySoft 背景 + ABorderStrong 枠 + APrimary 前景) に
/// 切り替わる。masonry (ViewMode.gallery) 表示中はグループ化が無効化される。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class ShellHeaderBar : UserControl
{
    // ===== コールバック =====
    /// <summary>「検索条件」ボタン押下時に発火。ShellPage 側で検索条件オーバーレイを開閉する。</summary>
    public Action? OnToggleFilter { get; set; }
    /// <summary>設定ボタンが押されたとき発火。</summary>
    public Action? OnShowSettings { get; set; }
    /// <summary>複数選択トグル押下時に発火。</summary>
    public Action? OnToggleMultiSelect { get; set; }
    /// <summary>グループ化モード変更時に発火 (none / world のトグル)。</summary>
    public Func<GroupingMode, Task>? OnGroupingChange { get; set; }
    /// <summary>ビューモード切替時に発火 ("gallery" / "standard")。</summary>
    public Func<string, Task>? OnViewModeChange { get; set; }
    /// <summary>検索ボックスで Enter が押されたとき発火。</summary>
    public Action? OnSearchSubmit { get; set; }

    // ===== 内部状態 =====
    // 状態同期は上位 (ShellPage) から SetMultiSelectActive / SetGroupingMode / SetViewMode で
    // 反映される。HeaderBar 自身は VM を持たない表示専用コントロール。
    private bool isMultiSelectActive;
    private GroupingMode currentGroupingMode = GroupingMode.none;
    private ViewMode currentViewMode = ViewMode.standard;
    private UiObservableCollection<WorldFilterOptionDto>? boundWorldOptions;
    private List<WorldFilterOptionDto> allWorldOptions = [];
    private IReadOnlyList<HeaderWorldSuggestion> lastWorldNameSuggestions = [];
    private int highlightedWorldSuggestionIndex = -1;
    private string activeSearchQueryText = string.Empty;
    private string pendingSuggestionQuery = string.Empty;
    private bool isSearchTextBoxFocused;
    private bool suppressSearchTextChange;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? searchSuggestionTimer;
    public UiObservableCollection<HeaderWorldSuggestion> WorldNameSuggestions { get; } = [];

    // ヘッダー UI を初期化し、現在状態に合わせたトグル表示へ同期する。
    public ShellHeaderBar()
    {
        AppLogger.Trace("ShellHeaderBar.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ShellHeaderBar.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        SyncMultiSelectStyle();
        SyncGroupingStyle();
        SyncViewModeStyle();
        ActualThemeChanged += OnActualThemeChanged;
        Unloaded += (_, _) =>
        {
            ActualThemeChanged -= OnActualThemeChanged;
            DetachWorldFilterOptions();
            searchSuggestionTimer?.Stop();
        };
        AppLogger.Trace("ShellHeaderBar.ctor: exit");
    }

    /// <summary>テーマ切替時に code-behind で塗ったトグルボタンを再着色する。</summary>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        try
        {
            RefreshThemeBoundVisuals();
            DispatcherQueue?.TryEnqueue(RefreshThemeBoundVisuals);
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.OnActualThemeChanged: {ex}"); }
    }


    // -----------------------------------------------------------------------
    // 状態同期 API (ShellPage から呼ばれる)
    // -----------------------------------------------------------------------

    /// <summary>複数選択モードのトグル状態を反映する。</summary>
    public void SetMultiSelectActive(bool active)
    {
        try
        {
            isMultiSelectActive = active;
            SyncMultiSelectStyle();
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SetMultiSelectActive: threw: {ex}"); }
    }

    /// <summary>グループ化モードのトグル状態を反映する。</summary>
    public void SetGroupingMode(GroupingMode mode)
    {
        try
        {
            currentGroupingMode = mode;
            SyncGroupingStyle();
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SetGroupingMode: threw: {ex}"); }
    }

    /// <summary>
    /// ビューモードのトグル状態を反映する。
    /// masonry (gallery) ではグループ化は無効になる仕様なので、グループ化トグルの
    /// 有効/無効も併せて更新する。
    /// </summary>
    public void SetViewMode(ViewMode mode)
    {
        try
        {
            currentViewMode = mode;
            SyncViewModeStyle();
            SyncGroupingStyle();
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SetViewMode: threw: {ex}"); }
    }

    /// <summary>オーバーレイ表示中に、ヘッダー内の操作だけを無効化する。</summary>
    public void SetControlsInteractive(bool interactive)
    {
        try
        {
            ContentRoot.IsHitTestVisible = interactive;
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SetControlsInteractive: threw: {ex}"); }
    }

    /// <summary>ヘッダー検索の候補に使うワールド一覧を購読し、検索語に応じた候補を更新する。</summary>
    public void SetWorldFilterOptions(UiObservableCollection<WorldFilterOptionDto> options)
    {
        try
        {
            DetachWorldFilterOptions();
            boundWorldOptions = options;
            boundWorldOptions.CollectionChanged += OnWorldOptionsChanged;
            allWorldOptions = options.ToList();
            RefreshWorldNameSuggestions();
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SetWorldFilterOptions: threw: {ex}"); }
    }

    private void DetachWorldFilterOptions()
    {
        if (boundWorldOptions is not null)
        {
            boundWorldOptions.CollectionChanged -= OnWorldOptionsChanged;
            boundWorldOptions = null;
        }
    }

    private void OnWorldOptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        try
        {
            if (boundWorldOptions is not null)
                allWorldOptions = boundWorldOptions.ToList();
            RefreshWorldNameSuggestions();
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.OnWorldOptionsChanged: threw: {ex}"); }
    }

    // -----------------------------------------------------------------------
    // ボタンクリックハンドラ
    // -----------------------------------------------------------------------

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        try { OnToggleFilter?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.FilterButton_Click: threw: {ex}"); }
    }

    // 設定ボタンのクリックを ShellPage 側の設定表示要求へ渡す。
    private void SettingsGearBtn_Click(object sender, RoutedEventArgs e)
    {
        try { OnShowSettings?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SettingsGearBtn_Click: threw: {ex}"); }
    }

    // 複数選択ボタンのクリックを選択モード切替要求として渡す。
    private void MultiSelectBtn_Click(object sender, RoutedEventArgs e)
    {
        try { OnToggleMultiSelect?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.MultiSelectBtn_Click: threw: {ex}"); }
    }

    // グループ化ボタンを none/world のトグルとして処理する。
    private async void GroupingBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 現在 world なら none、none なら world に切り替える。
            if (OnGroupingChange is not null)
                await OnGroupingChange(ShellHeaderBarLogic.NextGroupingMode(currentGroupingMode)).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.GroupingBtn_Click: threw: {ex}"); }
    }

    // 表示モードボタンを standard/gallery のトグルとして処理する。
    private async void ViewModeBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 現在 standard なら gallery、gallery なら standard に切り替える。
            if (OnViewModeChange is not null)
                await OnViewModeChange(ShellHeaderBarLogic.NextViewModeName(currentViewMode)).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.ViewModeBtn_Click: threw: {ex}"); }
    }

    // -----------------------------------------------------------------------
    // 状態 → 見た目の同期 (active 配色 / disabled の dim)
    // -----------------------------------------------------------------------

    private void SyncMultiSelectStyle()
    {
        ApplyActiveStyle(MultiSelectBtn, MultiSelectIcon, isMultiSelectActive);
    }

    // グループ化ボタンのアクティブ色と、masonry 中の無効状態を同期する。
    private void SyncGroupingStyle()
    {
        var state = ShellHeaderBarLogic.GroupingToggleState(currentGroupingMode, currentViewMode);
        ApplyActiveStyle(GroupingBtn, GroupingIcon, state.Active);
        // masonry 表示中はグループ化を無効化 (UI 側の制約)
        GroupingBtn.IsEnabled = state.Enabled;
        GroupingBtn.Opacity = state.Opacity;
    }

    // 表示モードボタンのアイコンとアクティブ色を現在モードへ同期する。
    private void SyncViewModeStyle()
    {
        var state = ShellHeaderBarLogic.ViewModeToggleState(currentViewMode);
        // 現状のモードを示すアイコン: standard → "grid"、gallery → "gallery"
        ViewModeIcon.IconName = state.IconName ?? "grid";
        ApplyActiveStyle(ViewModeBtn, ViewModeIcon, state.Active);
    }

    /// <summary>
    /// トグルボタンを「アクティブ配色」または「通常配色」に切り替える。
    /// アクティブ: APrimarySoft 背景 + ABorderStrong 枠 + APrimary 前景。
    /// 通常:        Transparent 背景 + Transparent 枠 + ATextDim 前景 (HeaderIconButtonStyle 既定)。
    /// アイコン色も合わせて更新する。
    /// </summary>
    private void ApplyActiveStyle(Button button, Shared.Controls.AppIcon icon, bool active)
    {
        try
        {
            if (active)
            {
                button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                button.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                if (ResolveThemeBrush("AHeaderToggleAccent") is { } fg)
                {
                    button.Foreground = fg;
                    icon.Foreground = fg;
                }
            }
            else
            {
                button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                button.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                if (ResolveThemeBrush("ATextDim") is { } restFg)
                {
                    button.Foreground = restFg;
                    icon.Foreground = restFg;
                }
            }
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.ApplyActiveStyle: threw: {ex}"); }
    }

    /// <summary>
    /// 検索ボックスフォーカス時に外側 Border をアクセント色枠線 + 通常サーフェイス背景に切り替え、
    /// 「いま入力対象」が一目で分かるようにする。
    /// </summary>
    private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            isSearchTextBoxFocused = true;
            activeSearchQueryText = SearchTextBox.Text ?? string.Empty;
            SyncSearchBoxChrome(focused: true);
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SearchTextBox_GotFocus: threw: {ex}"); }
    }

    // 検索ボックスからフォーカスが外れたら通常の枠線と背景へ戻す。
    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            isSearchTextBoxFocused = false;
            SyncSearchBoxChrome(focused: false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SearchTextBox_LostFocus: threw: {ex}"); }
    }

    private void RefreshThemeBoundVisuals()
    {
        SyncMultiSelectStyle();
        SyncGroupingStyle();
        SyncViewModeStyle();
        SyncSearchBoxChrome(IsSearchBoxFocused());
    }

    private bool IsSearchBoxFocused()
        => isSearchTextBoxFocused || SearchTextBox.FocusState != FocusState.Unfocused;

    private void SyncSearchBoxChrome(bool focused)
    {
        var keys = ShellHeaderBarLogic.SearchBoxKeys(focused);
        if (ResolveThemeBrush(keys.BorderKey) is { } border)
            SearchBoxBorder.BorderBrush = border;
        if (ResolveThemeBrush(keys.FillKey) is { } fill)
            SearchBoxBorder.Background = fill;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (suppressSearchTextChange) return;
            activeSearchQueryText = SearchTextBox.Text ?? string.Empty;
            if (!IsSearchBoxFocused())
            {
                CloseSearchSuggestions();
                return;
            }
            ScheduleWorldNameSuggestions(activeSearchQueryText);
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SearchTextBox_TextChanged: threw: {ex}"); }
    }

    private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            if (e.Key is VirtualKey.Down or VirtualKey.Up)
            {
                MoveHighlightedWorldSuggestion(e.Key == VirtualKey.Down ? 1 : -1);
                e.Handled = true;
                return;
            }
            if (e.Key == VirtualKey.Escape)
            {
                CloseSearchSuggestions();
                e.Handled = true;
                return;
            }
            if (ShellHeaderBarLogic.ShouldSubmitSearch(e.Key))
            {
                SubmitSearch(SearchSuggestionListView.SelectedItem as HeaderWorldSuggestion);
                e.Handled = true;
            }
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SearchTextBox_KeyDown: threw: {ex}"); }
    }

    private void SearchSuggestionListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            SubmitSearch(e.ClickedItem as HeaderWorldSuggestion);
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SearchSuggestionListView_ItemClick: threw: {ex}"); }
    }

    private void SearchSuggestionPopup_Closed(object sender, object e)
    {
        highlightedWorldSuggestionIndex = -1;
        SearchSuggestionListView.SelectedIndex = -1;
    }

    /// <summary>入力確定後だけ候補を再計算するため、IME 変換中の細かい TextChanged を短く待つ。</summary>
    private void ScheduleWorldNameSuggestions(string query)
    {
        pendingSuggestionQuery = query;
        if (!ShellHeaderBarLogic.ShouldShowWorldSuggestions(query))
        {
            searchSuggestionTimer?.Stop();
            RefreshWorldNameSuggestions(query, open: false);
            return;
        }

        searchSuggestionTimer ??= CreateSearchSuggestionTimer();
        searchSuggestionTimer.Stop();
        searchSuggestionTimer.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateSearchSuggestionTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(220);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RefreshWorldNameSuggestions(pendingSuggestionQuery, open: true);
        };
        return timer;
    }

    private void RefreshWorldNameSuggestions(string? query = null, bool open = false)
    {
        var sourceQuery = query ?? SearchTextBox.Text;
        var suggestions = ShellHeaderBarLogic.BuildWorldNameSuggestions(allWorldOptions, sourceQuery, maxCount: 5);
        lastWorldNameSuggestions = suggestions;
        WorldNameSuggestions.ReplaceAll(suggestions);
        highlightedWorldSuggestionIndex = -1;
        SearchSuggestionListView.SelectedIndex = -1;
        if (open && suggestions.Count > 0 && IsSearchBoxFocused())
        {
            OpenSearchSuggestions();
            return;
        }
        CloseSearchSuggestions();
    }

    /// <summary>候補 Popup の位置を検索ボックス直下に合わせる。</summary>
    private void OpenSearchSuggestions()
    {
        if (SearchBoxBorder.ActualWidth > 0)
            SearchSuggestionSurface.Width = SearchBoxBorder.ActualWidth;

        var point = SearchBoxBorder.TransformToVisual(null)
            .TransformPoint(new Point(0, SearchBoxBorder.ActualHeight + 6));
        SearchSuggestionPopup.HorizontalOffset = point.X;
        SearchSuggestionPopup.VerticalOffset = point.Y;
        SearchSuggestionPopup.IsOpen = true;
    }

    private void CloseSearchSuggestions()
    {
        SearchSuggestionPopup.IsOpen = false;
        highlightedWorldSuggestionIndex = -1;
        SearchSuggestionListView.SelectedIndex = -1;
    }

    /// <summary>上下キーの候補移動を ListView 選択だけに反映し、検索欄の文字列は変更しない。</summary>
    private void MoveHighlightedWorldSuggestion(int delta)
    {
        if (WorldNameSuggestions.Count == 0) return;
        if (!SearchSuggestionPopup.IsOpen)
            OpenSearchSuggestions();

        highlightedWorldSuggestionIndex = highlightedWorldSuggestionIndex < 0
            ? (delta > 0 ? 0 : WorldNameSuggestions.Count - 1)
            : (highlightedWorldSuggestionIndex + delta + WorldNameSuggestions.Count) % WorldNameSuggestions.Count;
        SearchSuggestionListView.SelectedIndex = highlightedWorldSuggestionIndex;
        SearchSuggestionListView.ScrollIntoView(WorldNameSuggestions[highlightedWorldSuggestionIndex]);
    }

    /// <summary>候補クリックまたは Enter による検索確定を処理する。</summary>
    private void SubmitSearch(HeaderWorldSuggestion? suggestion)
    {
        if (suggestion is not null)
        {
            suppressSearchTextChange = true;
            SearchTextBox.Text = suggestion.DisplayName;
            suppressSearchTextChange = false;
        }

        activeSearchQueryText = SearchTextBox.Text ?? string.Empty;
        CloseSearchSuggestions();
        SearchTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        OnSearchSubmit?.Invoke();
    }

    private Brush? ResolveThemeBrush(string key) => ThemeHelper.Brush(this, key);
}
