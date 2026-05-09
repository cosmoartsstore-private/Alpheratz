using System;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Shell.Controls;

public sealed partial class ShellHeaderBar : UserControl
{
    public Action? OnToggleFilter { get; set; }
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
}
