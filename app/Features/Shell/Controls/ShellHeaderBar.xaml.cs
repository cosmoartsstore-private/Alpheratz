using System;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Features.Shell.Controls;

/// <summary>
/// 画面上部のヘッダーバー。検索ボックス、フィルタ表示ボタン、設定ボタンを束ねる。
/// ViewModel と DataContext 経由で SearchQuery 等を双方向バインドする。
/// </summary>
public sealed partial class ShellHeaderBar : UserControl
{
    /// <summary>フィルタオーバーレイ開閉ボタンが押されたとき発火。</summary>
    public Action? OnToggleFilter { get; set; }
    /// <summary>設定ボタンが押されたとき発火。</summary>
    public Action? OnShowSettings { get; set; }

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
        AppLogger.Trace("ShellHeaderBar.ctor: exit");
    }

    public void SetGalleryControlsEnabled(bool enabled)
    {
        try
        {
            FilterPillBtn.IsEnabled = enabled;
            FilterPillBtn.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            SearchBoxBorder.IsHitTestVisible = enabled;
            SearchBoxBorder.Opacity = enabled ? 1.0 : 0.4;
        }
        catch (Exception ex) { AppLogger.Error($"ShellHeaderBar.SetGalleryControlsEnabled: threw: {ex}"); }
    }

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

    private Brush? ResolveThemeBrush(string key)
    {
        try
        {
            var themeKey = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
            if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var raw)
                && raw is ResourceDictionary dict
                && dict.TryGetValue(key, out var value)
                && value is Brush brush)
                return brush;
        }
        catch { }
        return null;
    }
}
