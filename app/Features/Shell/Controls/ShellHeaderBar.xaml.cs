using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
        Unloaded += (_, _) => ActualThemeChanged -= OnActualThemeChanged;
        AppLogger.Trace("ShellHeaderBar.ctor: exit");
    }

    /// <summary>テーマ切替時に code-behind で塗ったトグルボタンを再着色する。</summary>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        try
        {
            SyncMultiSelectStyle();
            SyncGroupingStyle();
            SyncViewModeStyle();
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

    /// <summary>
    /// PDQ ハッシュ計算の進捗表示を更新する。
    /// running=false なら非表示にする。進捗情報は HeaderBar に表示し、
    /// ギャラリー表示中でも常に確認できるようにする。
    /// done/total が 0 のときも "PDQ" だけ表示するので、計算開始の合図にもなる。
    /// </summary>
    public void SetPdqProgress(bool running, int done, int total)
    {
        try
        {
            if (!running)
            {
                PdqProgressChip.Visibility = Visibility.Collapsed;
                return;
            }
            var progress = ShellHeaderBarLogic.PdqProgress(running, done, total);
            PdqProgressChip.Visibility = progress.Visible ? Visibility.Visible : Visibility.Collapsed;
            PdqProgressText.Text = progress.Text;
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SetPdqProgress: threw: {ex}"); }
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
                if (ResolveThemeBrush("APrimarySoft") is { } bg) button.Background = bg;
                if (ResolveThemeBrush("ABorderStrong") is { } border) button.BorderBrush = border;
                if (ResolveThemeBrush("APrimary") is { } fg)
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
            var keys = ShellHeaderBarLogic.SearchBoxKeys(focused: true);
            if (ResolveThemeBrush(keys.BorderKey) is { } focusBorder)
                SearchBoxBorder.BorderBrush = focusBorder;
            if (ResolveThemeBrush(keys.FillKey) is { } focusFill)
                SearchBoxBorder.Background = focusFill;
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SearchTextBox_GotFocus: threw: {ex}"); }
    }

    // 検索ボックスからフォーカスが外れたら通常の枠線と背景へ戻す。
    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            var keys = ShellHeaderBarLogic.SearchBoxKeys(focused: false);
            if (ResolveThemeBrush(keys.BorderKey) is { } restBorder)
                SearchBoxBorder.BorderBrush = restBorder;
            if (ResolveThemeBrush(keys.FillKey) is { } restFill)
                SearchBoxBorder.Background = restFill;
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SearchTextBox_LostFocus: threw: {ex}"); }
    }

    // Enter キーで検索テキストのバインディングを確定し、即時検索を要求する。
    private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            if (!ShellHeaderBarLogic.ShouldSubmitSearch(e.Key)) return;
            e.Handled = true;
            SearchTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            OnSearchSubmit?.Invoke();
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SearchTextBox_KeyDown: threw: {ex}"); }
    }

    /// <summary>ActualTheme に応じた ThemeDictionaries から指定キーのブラシを取り出す。</summary>
    private Brush? ResolveThemeBrush(string key) => ThemeHelper.Brush(this, key);
}
