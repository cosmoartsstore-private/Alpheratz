using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Shared.Controls;

public sealed partial class PhotoGridPager : UserControl
{
    public Func<Task>? OnGoToPrevPage { get; set; }
    public Func<Task>? OnGoToNextPage { get; set; }

    public PhotoGridPager()
    {
        AppLogger.Trace("PhotoGridPager.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoGridPager.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("PhotoGridPager.ctor: exit");
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("PhotoGridPager.PrevPage_Click: enter");
        try
        {
            if (OnGoToPrevPage is not null)
            {
                await OnGoToPrevPage().ConfigureAwait(false);
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridPager.PrevPage_Click: threw: {ex}"); }
        AppLogger.Trace("PhotoGridPager.PrevPage_Click: exit");
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("PhotoGridPager.NextPage_Click: enter");
        try
        {
            if (OnGoToNextPage is not null)
            {
                await OnGoToNextPage().ConfigureAwait(false);
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoGridPager.NextPage_Click: threw: {ex}"); }
        AppLogger.Trace("PhotoGridPager.NextPage_Click: exit");
    }
}
