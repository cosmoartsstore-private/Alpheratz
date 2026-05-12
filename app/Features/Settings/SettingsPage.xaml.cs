using System;
using System.IO;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Settings;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel viewModel;

    public Func<int, Task>? OnChooseFolder { get; set; }
    public Action<int>? OnResetFolder { get; set; }
    public Func<bool, Task>? OnStartupPreferenceChanged { get; set; }
    public Func<bool, Task>? OnThemeChanged { get; set; }
    public Func<Task>? OnStartWorldAnalysis { get; set; }
    public Func<bool, Task>? OnMasonryPreferenceChanged { get; set; }
    public Func<Task>? OnReviewMissingPhotos { get; set; }

    public SettingsPage(SettingsViewModel viewModel)
    {
        AppLogger.Trace("SettingsPage.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SettingsPage.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        this.viewModel = viewModel;
        DataContext = viewModel;
        AppLogger.Trace("SettingsPage.ctor: exit");
    }

    private async void ChoosePrimaryFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ChoosePrimaryFolder_Click: enter");
        try { if (OnChooseFolder is not null) await OnChooseFolder(1).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ChoosePrimaryFolder_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ChoosePrimaryFolder_Click: exit");
    }

    private async void ChooseSecondaryFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ChooseSecondaryFolder_Click: enter");
        try { if (OnChooseFolder is not null) await OnChooseFolder(2).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ChooseSecondaryFolder_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ChooseSecondaryFolder_Click: exit");
    }

    private void ResetPrimaryFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ResetPrimaryFolder_Click: enter");
        try { OnResetFolder?.Invoke(1); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ResetPrimaryFolder_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ResetPrimaryFolder_Click: exit");
    }

    private void ResetSecondaryFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ResetSecondaryFolder_Click: enter");
        try { OnResetFolder?.Invoke(2); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ResetSecondaryFolder_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ResetSecondaryFolder_Click: exit");
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.StartupToggle_Toggled: enter");
        try
        {
            if (sender is ToggleSwitch toggleSwitch && OnStartupPreferenceChanged is not null)
                await OnStartupPreferenceChanged(toggleSwitch.IsOn).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.StartupToggle_Toggled: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.StartupToggle_Toggled: exit");
    }

    private async void MasonryToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.MasonryToggle_Toggled: enter");
        try
        {
            if (sender is ToggleSwitch toggleSwitch && OnMasonryPreferenceChanged is not null)
                await OnMasonryPreferenceChanged(toggleSwitch.IsOn).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.MasonryToggle_Toggled: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.MasonryToggle_Toggled: exit");
    }

    private async void ThemeLight_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ThemeLight_Click: enter");
        try { if (OnThemeChanged is not null) await OnThemeChanged(false).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ThemeLight_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ThemeLight_Click: exit");
    }

    private async void ThemeDark_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ThemeDark_Click: enter");
        try { if (OnThemeChanged is not null) await OnThemeChanged(true).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ThemeDark_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ThemeDark_Click: exit");
    }

    private void RegisterStellaRecord_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.RegisterStellaRecord_Click: enter");
        try
        {
            if (!StellaRecordRegistration.IsStellaRecordAvailable())
            {
                AppLogger.Trace("SettingsPage.RegisterStellaRecord_Click: StellaRecord not available");
                return;
            }
            var exePath = Environment.ProcessPath ?? string.Empty;
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
            StellaRecordRegistration.Register(exePath, iconPath);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.RegisterStellaRecord_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.RegisterStellaRecord_Click: exit");
    }

    private async void StartWorldAnalysis_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.StartWorldAnalysis_Click: enter");
        try { if (OnStartWorldAnalysis is not null) await OnStartWorldAnalysis().ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.StartWorldAnalysis_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.StartWorldAnalysis_Click: exit");
    }

    private async void ReviewMissingPhotos_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ReviewMissingPhotos_Click: enter");
        try { if (OnReviewMissingPhotos is not null) await OnReviewMissingPhotos().ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ReviewMissingPhotos_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ReviewMissingPhotos_Click: exit");
    }
}