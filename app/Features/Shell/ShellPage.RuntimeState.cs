using System.ComponentModel;
using Alpheratz.Core;
using Alpheratz.Features.Bootstrap;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Alpheratz.Features.Shell;

// Tracing convention: every method emits enter/exit traces plus one trace per
// meaningful branch so a crash log can be read top-down to the last surviving
// call. Hot read-only properties are exempt to keep the log usable.
public sealed partial class ShellPage
{
    private bool runtimeStateWired;
    private AppLifecycleService? lifecycleService;

    private void ShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("ShellPage.ShellPage_Loaded: enter");

        if (runtimeStateWired)
        {
            AppLogger.Trace("ShellPage.ShellPage_Loaded: skip (already wired)");
            return;
        }
        runtimeStateWired = true;

        lifecycleService = App.Services?.GetService<AppLifecycleService>();
        if (lifecycleService is null)
        {
            AppLogger.Warn("ShellPage.ShellPage_Loaded: AppLifecycleService not resolved");
        }

        // Apply the persisted theme before the first frame so the user does
        // not see a Light->Dark flash on cold start.
        ApplyTheme(viewModel.ThemeMode);

        // Wait one dispatcher tick after Loaded before flipping uiReady so the
        // first layout/render pass actually completes. Subscribers gated on
        // uiReady can then safely touch visual tree state.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            AppLogger.Trace("ShellPage.ShellPage_Loaded: dispatcher tick, advancing to uiReady");
            lifecycleService?.advanceTo(AppLifecyclePhase.uiReady);
        });

        AppLogger.Trace("ShellPage.ShellPage_Loaded: exit");
    }

    private void ApplyTheme(ThemeMode mode)
    {
        AppLogger.Trace($"ShellPage.ApplyTheme: enter mode={mode}");
        try
        {
            // Setting RequestedTheme on the page propagates to all descendants
            // and resolves Light/Dark ResourceDictionary lookups for the entire
            // visual tree (header, stage, right rail, modals, dialogs).
            RequestedTheme = mode == ThemeMode.dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }
        catch (System.Exception ex)
        {
            AppLogger.Error($"ShellPage.ApplyTheme: threw: {ex}");
        }
        AppLogger.Trace("ShellPage.ApplyTheme: exit");
    }

    private void ShellPage_Unloaded(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("ShellPage.ShellPage_Unloaded: enter");
        viewModel.galleryViewModel.selectionState.PropertyChanged -= OnSelectionStateChanged;
        viewModel.PropertyChanged -= OnShellViewModelChanged;
        AppLogger.Trace("ShellPage.ShellPage_Unloaded: exit");
    }

    private void UpdateScanningOverlayVisibility()
    {
        AppLogger.Trace($"ShellPage.UpdateScanningOverlayVisibility: enter scanStatus={viewModel.ScanStatus}");
        Stage.ScanningOverlayVisibility = viewModel.ScanStatus == "scanning"
            ? Visibility.Visible
            : Visibility.Collapsed;
        AppLogger.Trace($"ShellPage.UpdateScanningOverlayVisibility: exit visibility={Stage.ScanningOverlayVisibility}");
    }

}
