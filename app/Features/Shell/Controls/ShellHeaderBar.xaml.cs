using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Features.Shell.Controls;

/// <summary>
/// 画面上部のヘッダーバー。
/// 構成：
///   [検索条件 pill] [検索 TextBox] [ビューモード] [グループ化] [複数選択] [設定]
/// LeftRail から移植した 3 つの binary トグル (ビューモード / グループ化 / 複数選択) は
/// アクティブ時にアクセント色 (APrimarySoft 背景 + ABorderStrong 枠 + APrimary 前景) に
/// 切り替わる。masonry (ViewMode.gallery) 表示中はグループ化が無効化される。
/// </summary>
public sealed partial class ShellHeaderBar : UserControl
{
    // ===== コールバック =====
    /// <summary>フィルタオーバーレイ開閉ボタンが押されたとき発火。</summary>
    public Action? OnToggleFilter { get; set; }
    /// <summary>設定ボタンが押されたとき発火。</summary>
    public Action? OnShowSettings { get; set; }
    /// <summary>複数選択トグル押下時に発火。</summary>
    public Action? OnToggleMultiSelect { get; set; }
    /// <summary>グループ化モード変更時に発火 (none / world のトグル)。</summary>
    public Action<GroupingMode>? OnGroupingChange { get; set; }
    /// <summary>ビューモード切替時に発火 ("gallery" / "standard")。</summary>
    public Func<string, Task>? OnViewModeChange { get; set; }

    // ===== 内部状態 =====
    // 状態同期は上位 (ShellPage) から SetMultiSelectActive / SetGroupingMode / SetViewMode で
    // 反映される。HeaderBar 自身は VM を持たない purely-view。
    private bool isMultiSelectActive;
    private GroupingMode currentGroupingMode = GroupingMode.none;
    private ViewMode currentViewMode = ViewMode.standard;

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
    /// PDQ ハッシュ計算の進捗を表示する pill chip を更新する。
    /// running=false なら chip を非表示にする。旧 RightRail (削除済み) で表示していた
    /// 進捗情報を HeaderBar に移植し、画面に常時見える形にした。
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
            PdqProgressChip.Visibility = Visibility.Visible;
            PdqProgressText.Text = total > 0
                ? $"PDQ {done} / {total}"
                : "PDQ";
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SetPdqProgress: threw: {ex}"); }
    }

    // -----------------------------------------------------------------------
    // ボタンクリックハンドラ
    // -----------------------------------------------------------------------

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        try { OnToggleFilter?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.FilterButton_Click: threw: {ex}"); }
    }

    private void SettingsGearBtn_Click(object sender, RoutedEventArgs e)
    {
        try { OnShowSettings?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SettingsGearBtn_Click: threw: {ex}"); }
    }

    private void MultiSelectBtn_Click(object sender, RoutedEventArgs e)
    {
        try { OnToggleMultiSelect?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.MultiSelectBtn_Click: threw: {ex}"); }
    }

    private void GroupingBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // binary トグル: 現在 world なら none、none なら world。
            var next = currentGroupingMode == GroupingMode.world ? GroupingMode.none : GroupingMode.world;
            OnGroupingChange?.Invoke(next);
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.GroupingBtn_Click: threw: {ex}"); }
    }

    private async void ViewModeBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // binary トグル: 現在 standard なら gallery、gallery なら standard。
            var next = currentViewMode == ViewMode.gallery ? "standard" : "gallery";
            if (OnViewModeChange is not null)
                await OnViewModeChange(next).ConfigureAwait(false);
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

    private void SyncGroupingStyle()
    {
        var groupingActive = currentGroupingMode == GroupingMode.world;
        ApplyActiveStyle(GroupingBtn, GroupingIcon, groupingActive);
        // masonry 表示中はグループ化を無効化 (UI 側の制約)
        var disabled = currentViewMode == ViewMode.gallery;
        GroupingBtn.IsEnabled = !disabled;
        GroupingBtn.Opacity = disabled ? 0.4 : 1.0;
    }

    private void SyncViewModeStyle()
    {
        var galleryActive = currentViewMode == ViewMode.gallery;
        // 現状のモードを示すアイコン: standard → "grid"、gallery → "gallery"
        ViewModeIcon.IconName = galleryActive ? "gallery" : "grid";
        ApplyActiveStyle(ViewModeBtn, ViewModeIcon, galleryActive);
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
            if (ResolveThemeBrush("ABorderStrong") is { } focusBorder)
                SearchBoxBorder.BorderBrush = focusBorder;
            if (ResolveThemeBrush("ASurface") is { } focusFill)
                SearchBoxBorder.Background = focusFill;
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SearchTextBox_GotFocus: threw: {ex}"); }
    }

    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ResolveThemeBrush("ABorder") is { } restBorder)
                SearchBoxBorder.BorderBrush = restBorder;
            if (ResolveThemeBrush("ASurfaceSoft") is { } restFill)
                SearchBoxBorder.Background = restFill;
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SearchTextBox_LostFocus: threw: {ex}"); }
    }

    /// <summary>ActualTheme に応じた ThemeDictionaries から指定キーのブラシを取り出す。</summary>
    private Brush? ResolveThemeBrush(string key) => ThemeHelper.Brush(this, key);
}
