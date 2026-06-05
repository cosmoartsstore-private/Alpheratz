using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Shared.Controls;

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class ScanningOverlay : UserControl
{
    public Func<Task>? OnCancelScan { get; set; }

    // スキャン中オーバーレイを初期化する。
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

    // キャンセルボタン押下時に上位のスキャン停止処理を呼び出す。
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
