using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Shared.Controls;

public sealed partial class ScanningOverlay : UserControl
{
    public Func<Task>? OnCancelScan { get; set; }

    public ScanningOverlay()
    {
        AppLogger.Trace("ScanningOverlay.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ScanningOverlay.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("ScanningOverlay.ctor: exit");
    }

    private async void CancelScan_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("ScanningOverlay.CancelScan_Click: enter");
        try
        {
            if (OnCancelScan is not null)
            {
                await OnCancelScan().ConfigureAwait(false);
            }
        }
        catch (Exception ex) { AppLogger.Error($"ScanningOverlay.CancelScan_Click: threw: {ex}"); }
        AppLogger.Trace("ScanningOverlay.CancelScan_Click: exit");
    }
}
