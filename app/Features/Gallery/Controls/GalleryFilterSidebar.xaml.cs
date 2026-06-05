using System;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Gallery.Controls;

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class GalleryFilterSidebar : UserControl
{
    public Action? OnResetFilters { get; set; }
    public Action<string>? OnDatePresetSelect { get; set; }

    // フィルタサイドバーの XAML を初期化する。
    public GalleryFilterSidebar()
    {
        AppLogger.Trace("GalleryFilterSidebar.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryFilterSidebar.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("GalleryFilterSidebar.ctor: exit");
    }

    // リセットボタンのクリックを上位のフィルタ初期化処理へ渡す。
    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.ResetFilters_Click: enter");
        try { OnResetFilters?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.ResetFilters_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.ResetFilters_Click: exit");
    }

    // 今日プリセットの選択を上位へ通知する。
    private void PresetToday_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.PresetToday_Click: enter");
        try { OnDatePresetSelect?.Invoke("today"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.PresetToday_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.PresetToday_Click: exit");
    }

    // 直近 7 日プリセットの選択を上位へ通知する。
    private void PresetLast7Days_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.PresetLast7Days_Click: enter");
        try { OnDatePresetSelect?.Invoke("last7days"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.PresetLast7Days_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.PresetLast7Days_Click: exit");
    }

    // 今月プリセットの選択を上位へ通知する。
    private void PresetThisMonth_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.PresetThisMonth_Click: enter");
        try { OnDatePresetSelect?.Invoke("thisMonth"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.PresetThisMonth_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.PresetThisMonth_Click: exit");
    }

    // 先月プリセットの選択を上位へ通知する。
    private void PresetLastMonth_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.PresetLastMonth_Click: enter");
        try { OnDatePresetSelect?.Invoke("lastMonth"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.PresetLastMonth_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.PresetLastMonth_Click: exit");
    }

    // 半年プリセットの選択を上位へ通知する。
    private void PresetHalfYear_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.PresetHalfYear_Click: enter");
        try { OnDatePresetSelect?.Invoke("halfYear"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.PresetHalfYear_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.PresetHalfYear_Click: exit");
    }

    // 1 年プリセットの選択を上位へ通知する。
    private void PresetOneYear_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.PresetOneYear_Click: enter");
        try { OnDatePresetSelect?.Invoke("oneYear"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.PresetOneYear_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.PresetOneYear_Click: exit");
    }
}
