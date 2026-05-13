using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Shell.Controls;

/// <summary>
/// 画面左側の縦型ナビゲーションレール。アクティブスクリーン、ビューモード、グルーピング、
/// マルチセレクトのトグルを担当する。XAML はボタンの並びだけで、状態同期は code-behind から行う。
/// 上位の ShellPage が On... コールバックを設定する purely-view 構成。
/// </summary>
public sealed partial class ShellLeftRail : UserControl
{
    /// <summary>ギャラリー画面表示ボタンが押されたとき発火。</summary>
    public Action? OnShowGallery { get; set; }
    /// <summary>タグマスタ画面表示ボタンが押されたとき発火。</summary>
    public Action? OnShowTagMaster { get; set; }
    /// <summary>テンプレート画面表示ボタンが押されたとき発火。</summary>
    public Action? OnShowTemplate { get; set; }
    /// <summary>マルチセレクトモード切替ボタンが押されたとき発火。</summary>
    public Action? OnToggleMultiSelect { get; set; }
    /// <summary>グルーピングモード変更時に発火 (none / world)。</summary>
    public Action<GroupingMode>? OnGroupingChange { get; set; }
    /// <summary>ビューモード切替時に発火 ("gallery" / "standard")。await で UI 待機できるよう Task 返却。</summary>
    public Func<string, Task>? OnViewModeChange { get; set; }

    private MainScreen currentScreen = MainScreen.gallery;
    private bool isMultiSelectActive;
    private GroupingMode currentGroupingMode = GroupingMode.none;
    private ViewMode currentViewMode = ViewMode.standard;

    public ShellLeftRail()
    {
        AppLogger.Trace("ShellLeftRail.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ShellLeftRail.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        SetActiveScreen(MainScreen.gallery);
        SyncGroupingButtons();
        SyncViewModeButtons();
        AppLogger.Trace("ShellLeftRail.ctor: exit");
    }

    public void SetActiveScreen(MainScreen screen)
    {
        try
        {
            currentScreen = screen;
            bool isGallery = screen == MainScreen.gallery;

            BackToGalleryBtn.Visibility = Visibility.Collapsed;

            MultiSelectBtn.IsEnabled = isGallery;
            if (!isGallery && !isMultiSelectActive)
                MultiSelectBtn.Style = (Style)Application.Current.Resources["RailButtonDisabledStyle"];

            SetSectionEnabled(GroupingSection, isGallery);
            SetSectionEnabled(ViewModeSection, isGallery);

            TagMasterBtn.Style = screen == MainScreen.tagMaster
                ? (Style)Application.Current.Resources["RailButtonActiveStyle"]
                : (Style)Application.Current.Resources["RailButtonStyle"];
            TemplateBtn.Style = screen == MainScreen.template
                ? (Style)Application.Current.Resources["RailButtonActiveStyle"]
                : (Style)Application.Current.Resources["RailButtonStyle"];
        }
        catch (Exception ex) { AppLogger.Error($"ShellLeftRail.SetActiveScreen: threw: {ex}"); }
    }

    public void SetMultiSelectActive(bool active)
    {
        try
        {
            isMultiSelectActive = active;
            MultiSelectBtn.Style = active
                ? (Style)Application.Current.Resources["RailButtonActiveStyle"]
                : (Style)Application.Current.Resources["RailButtonStyle"];
        }
        catch (Exception ex) { AppLogger.Error($"ShellLeftRail.SetMultiSelectActive: threw: {ex}"); }
    }

    public void SetGroupingMode(GroupingMode mode)
    {
        try
        {
            currentGroupingMode = mode;
            SyncGroupingButtons();
        }
        catch (Exception ex) { AppLogger.Error($"ShellLeftRail.SetGroupingMode: threw: {ex}"); }
    }

    public void SetViewMode(ViewMode mode)
    {
        try
        {
            currentViewMode = mode;
            SyncViewModeButtons();
            SyncGroupingButtons();
        }
        catch (Exception ex) { AppLogger.Error($"ShellLeftRail.SetViewMode: threw: {ex}"); }
    }

    private void SyncGroupingButtons()
    {
        var isGallery = currentViewMode == ViewMode.gallery;

        GroupNoneBtn.Style = currentGroupingMode == GroupingMode.none
            ? (Style)Application.Current.Resources["RailButtonActiveStyle"]
            : (Style)Application.Current.Resources["RailButtonStyle"];

        if (isGallery)
        {
            GroupWorldBtn.Style = (Style)Application.Current.Resources["RailButtonDisabledStyle"];
            GroupWorldBtn.IsEnabled = false;
        }
        else
        {
            GroupWorldBtn.Style = currentGroupingMode == GroupingMode.world
                ? (Style)Application.Current.Resources["RailButtonActiveStyle"]
                : (Style)Application.Current.Resources["RailButtonStyle"];
            GroupWorldBtn.IsEnabled = true;
        }
    }

    private void SyncViewModeButtons()
    {
        ViewGridBtn.Style = currentViewMode == ViewMode.standard
            ? (Style)Application.Current.Resources["RailButtonActiveStyle"]
            : (Style)Application.Current.Resources["RailButtonStyle"];
        ViewGalleryBtn.Style = currentViewMode == ViewMode.gallery
            ? (Style)Application.Current.Resources["RailButtonActiveStyle"]
            : (Style)Application.Current.Resources["RailButtonStyle"];
    }

    private static void SetSectionEnabled(StackPanel section, bool enabled)
    {
        section.Opacity = enabled ? 1.0 : 0.4;
        foreach (var child in section.Children)
        {
            if (child is Microsoft.UI.Xaml.Controls.Control ctrl)
                ctrl.IsEnabled = enabled;
        }
    }

    private void BackToGalleryBtn_Click(object sender, RoutedEventArgs e)
    {
        try { OnShowGallery?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellLeftRail.BackToGalleryBtn_Click: threw: {ex}"); }
    }

    private void TagMasterBtn_Click(object sender, RoutedEventArgs e)
    {
        try { OnShowTagMaster?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellLeftRail.TagMasterBtn_Click: threw: {ex}"); }
    }

    private void TemplateBtn_Click(object sender, RoutedEventArgs e)
    {
        try { OnShowTemplate?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellLeftRail.TemplateBtn_Click: threw: {ex}"); }
    }

    private void MultiSelectBtn_Click(object sender, RoutedEventArgs e)
    {
        try { OnToggleMultiSelect?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellLeftRail.MultiSelectBtn_Click: threw: {ex}"); }
    }

    private void GroupNoneBtn_Click(object sender, RoutedEventArgs e)
    {
        try { OnGroupingChange?.Invoke(GroupingMode.none); }
        catch (Exception ex) { AppLogger.Error($"ShellLeftRail.GroupNoneBtn_Click: threw: {ex}"); }
    }

    private void GroupWorldBtn_Click(object sender, RoutedEventArgs e)
    {
        try { OnGroupingChange?.Invoke(GroupingMode.world); }
        catch (Exception ex) { AppLogger.Error($"ShellLeftRail.GroupWorldBtn_Click: threw: {ex}"); }
    }

    private async void ViewGridBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (OnViewModeChange is not null)
                await OnViewModeChange("standard").ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellLeftRail.ViewGridBtn_Click: threw: {ex}"); }
    }

    private async void ViewGalleryBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (OnViewModeChange is not null)
                await OnViewModeChange("gallery").ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellLeftRail.ViewGalleryBtn_Click: threw: {ex}"); }
    }
}
