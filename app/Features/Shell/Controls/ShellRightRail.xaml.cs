using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Features.Shell.Controls;

public sealed partial class ShellRightRail : UserControl
{
    public Action? OnToggleMultiSelect { get; set; }
    public Action? OnToggleViewMode { get; set; }
    public Func<Task>? OnRefresh { get; set; }

    public ShellRightRail()
    {
        AppLogger.Trace("ShellRightRail.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ShellRightRail.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("ShellRightRail.ctor: exit");
    }

    public void SetMultiSelectActive(bool active)
    {
        try
        {
            MultiSelectBtn.Background = active
                ? (Brush)Application.Current.Resources["APrimarySoft"]
                : new SolidColorBrush(Colors.Transparent);
        }
        catch (Exception ex) { AppLogger.Error($"ShellRightRail.SetMultiSelectActive: threw: {ex}"); }
    }

    public void SetViewModeGallery(bool isGallery)
    {
        try
        {
            ViewModeIcon.IconName = isGallery ? "grid" : "gallery";
            ViewModeBtn.Background = isGallery
                ? (Brush)Application.Current.Resources["APrimarySoft"]
                : new SolidColorBrush(Colors.Transparent);
        }
        catch (Exception ex) { AppLogger.Error($"ShellRightRail.SetViewModeGallery: threw: {ex}"); }
    }

    private void MultiSelectButton_Click(object sender, RoutedEventArgs e)
    {
        try { OnToggleMultiSelect?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellRightRail.MultiSelectButton_Click: threw: {ex}"); }
    }

    private void ViewModeButton_Click(object sender, RoutedEventArgs e)
    {
        try { OnToggleViewMode?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellRightRail.ViewModeButton_Click: threw: {ex}"); }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (OnRefresh is not null) await OnRefresh().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ShellRightRail.RefreshButton_Click: threw: {ex}");
        }
    }
}
