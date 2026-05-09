using System;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Gallery.Controls;

public sealed partial class GalleryFilterSidebar : UserControl
{
    public Action? OnResetFilters { get; set; }
    public Action<string>? OnDatePresetSelect { get; set; }

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

    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.ResetFilters_Click: enter");
        try { OnResetFilters?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.ResetFilters_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.ResetFilters_Click: exit");
    }

    private void PresetToday_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.PresetToday_Click: enter");
        try { OnDatePresetSelect?.Invoke("today"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.PresetToday_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.PresetToday_Click: exit");
    }

    private void PresetLast7Days_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.PresetLast7Days_Click: enter");
        try { OnDatePresetSelect?.Invoke("last7days"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.PresetLast7Days_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.PresetLast7Days_Click: exit");
    }

    private void PresetThisMonth_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.PresetThisMonth_Click: enter");
        try { OnDatePresetSelect?.Invoke("thisMonth"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.PresetThisMonth_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.PresetThisMonth_Click: exit");
    }

    private void PresetLastMonth_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.PresetLastMonth_Click: enter");
        try { OnDatePresetSelect?.Invoke("lastMonth"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.PresetLastMonth_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.PresetLastMonth_Click: exit");
    }

    private void PresetHalfYear_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.PresetHalfYear_Click: enter");
        try { OnDatePresetSelect?.Invoke("halfYear"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.PresetHalfYear_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.PresetHalfYear_Click: exit");
    }

    private void PresetOneYear_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryFilterSidebar.PresetOneYear_Click: enter");
        try { OnDatePresetSelect?.Invoke("oneYear"); }
        catch (Exception ex) { AppLogger.Error($"GalleryFilterSidebar.PresetOneYear_Click: threw: {ex}"); }
        AppLogger.Trace("GalleryFilterSidebar.PresetOneYear_Click: exit");
    }
}
