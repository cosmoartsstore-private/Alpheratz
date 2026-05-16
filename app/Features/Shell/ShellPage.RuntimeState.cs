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
            // ShellPage と MainWindow.RootHost の RequestedTheme をセットすることで、
            // RootHost.Resources にマージされた Brushes.xaml の ThemeDictionaries が
            // 要素の ActualTheme で解決される (構造的に theme 切替に追従する経路)。
            // Application.RequestedTheme は WinUI 3 では InitializeComponent 後に
            // 変更できないためここでは触らない。
            var theme = mode == ThemeMode.dark
                ? ElementTheme.Dark
                : ElementTheme.Light;

            RequestedTheme = theme;

            if (App.MainWindowInstance is MainWindow mw)
                mw.SetTheme(theme);

            // 他のキャッシュページ (Settings / Gallery / GroupDrillDown) は閉じている間
            // visual tree から外れていて親の RequestedTheme 変更を受け取れないため、
            // 直接 RequestedTheme をセットして再表示時に正しいテーマで解決されるようにする。
            if (settingsPage is not null) settingsPage.RequestedTheme = theme;
            if (galleryPage is not null) galleryPage.RequestedTheme = theme;
            if (drillDownPage is not null) drillDownPage.RequestedTheme = theme;
            // PhotoModal は毎回 new で生成するためキャッシュは不要だが、表示中に切替
            // された場合は Page が ContentControl 経由でテーマを自動継承しないので
            // 直接セットして即時反映する。
            if (Stage.TopModalContent is Microsoft.UI.Xaml.FrameworkElement activeTopModal)
                activeTopModal.RequestedTheme = theme;
            if (Stage.ModalContent is Microsoft.UI.Xaml.FrameworkElement activeMidModal)
                activeMidModal.RequestedTheme = theme;

            // IValueConverter のように element context を渡せない経路向けに通知する。
            Shared.Services.ThemeHelper.NotifySelectedThemeChanged(theme);
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
        try
        {
            viewModel.galleryViewModel.drillDownPhotosProvider = null;
            viewModel.galleryViewModel.selectionState.PropertyChanged -= OnSelectionStateChanged;
            viewModel.PropertyChanged -= OnShellViewModelChanged;
            drillDownPhotos = null;
            if (drillDownPage is not null)
            {
                drillDownPage.OnBack = null;
                drillDownPage.OnPhotoActivated = null;
                drillDownPage.OnFavoriteClicked = null;
                drillDownPage.OnThumbnailsNeeded = null;
                drillDownPage = null;
            }
        }
        catch (System.Exception ex) { AppLogger.Error($"ShellPage.ShellPage_Unloaded: threw: {ex}"); }
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
