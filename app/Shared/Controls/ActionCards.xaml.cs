using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Shared.Controls;

public sealed partial class ActionCards : UserControl
{
    public static readonly DependencyProperty ScanStatusProperty =
        DependencyProperty.Register(nameof(ScanStatus), typeof(string), typeof(ActionCards), new PropertyMetadata("idle", OnScanStatusChanged));

    public string ScanStatus { get => (string)GetValue(ScanStatusProperty); set => SetValue(ScanStatusProperty, value); }

    public Func<Task>? OnStartScan { get; set; }
    public Func<Task>? OnCancelScan { get; set; }
    public Action? OnShowSettings { get; set; }
    public Action? OnOpenFilter { get; set; }

    public ActionCards()
    {
        AppLogger.Trace("ActionCards.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ActionCards.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        ApplyScanStatus();
        AppLogger.Trace("ActionCards.ctor: exit");
    }

    private static void OnScanStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        AppLogger.Trace($"ActionCards.OnScanStatusChanged: enter new={e.NewValue}");
        try
        {
            if (d is ActionCards control) control.ApplyScanStatus();
        }
        catch (Exception ex) { AppLogger.Error($"ActionCards.OnScanStatusChanged: threw: {ex}"); }
        AppLogger.Trace("ActionCards.OnScanStatusChanged: exit");
    }

    private void ApplyScanStatus()
    {
        AppLogger.Trace($"ActionCards.ApplyScanStatus: enter status={ScanStatus}");
        try
        {
            if (ScanStatus == "scanning")
            {
                ScanTitle.Text = "スキャンを中断";
                ScanDescription.Text = "写真の再スキャンを停止します";
                ScanCard.Background = (Brush)Application.Current.Resources["ADanger"];
            }
            else
            {
                ScanTitle.Text = "再スキャン";
                ScanDescription.Text = "写真を最新状態へ更新します";
                ScanCard.Background = (Brush)Application.Current.Resources["ASurfaceSoft"];
            }
        }
        catch (Exception ex) { AppLogger.Error($"ActionCards.ApplyScanStatus: threw: {ex}"); }
        AppLogger.Trace("ActionCards.ApplyScanStatus: exit");
    }

    private async void ScanCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        AppLogger.Trace($"ActionCards.ScanCard_Tapped: enter status={ScanStatus}");
        try
        {
            if (ScanStatus == "scanning")
            {
                if (OnCancelScan is not null) await OnCancelScan().ConfigureAwait(false);
                AppLogger.Trace("ActionCards.ScanCard_Tapped: exit (cancel)");
                return;
            }
            if (OnStartScan is not null) await OnStartScan().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"ActionCards.ScanCard_Tapped: threw: {ex}"); }
        AppLogger.Trace("ActionCards.ScanCard_Tapped: exit");
    }

    private void SettingsCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        AppLogger.Trace("ActionCards.SettingsCard_Tapped: enter");
        try { OnShowSettings?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ActionCards.SettingsCard_Tapped: threw: {ex}"); }
        AppLogger.Trace("ActionCards.SettingsCard_Tapped: exit");
    }

    private void FilterCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        AppLogger.Trace("ActionCards.FilterCard_Tapped: enter");
        try { OnOpenFilter?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ActionCards.FilterCard_Tapped: threw: {ex}"); }
        AppLogger.Trace("ActionCards.FilterCard_Tapped: exit");
    }
}
