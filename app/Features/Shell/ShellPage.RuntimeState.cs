using System.ComponentModel;
using Alpheratz.Core;
using Alpheratz.Features.Bootstrap;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Alpheratz.Features.Shell;

/// <summary>
/// ShellPage のロード後に必要な実行時状態を配線する部分クラス。
/// 起動フェーズ通知とテーマ反映を、画面構築後の安全なタイミングで行う。
/// </summary>
public sealed partial class ShellPage
{
    private bool runtimeStateWired;
    private AppLifecycleService? lifecycleService;

    // ShellPage 表示後にライフサイクル通知とテーマ反映を一度だけ配線する。
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

        // 初回フレーム前に保存済みテーマを適用し、起動時の明暗切替のちらつきを防ぐ。
        ApplyTheme(viewModel.ThemeMode);

        // Loaded 直後は初回レイアウトが終わっていないため、1 tick 待ってから uiReady に進める。
        // uiReady を待つ側は、この後なら visual tree の状態を安全に参照できる。
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            AppLogger.Trace("ShellPage.ShellPage_Loaded: dispatcher tick, advancing to uiReady");
            lifecycleService?.advanceTo(AppLifecyclePhase.uiReady);
        });

        AppLogger.Trace("ShellPage.ShellPage_Loaded: exit");
    }

    // 保存済みテーマを Shell と各キャッシュ済みページへ明示的に反映する。
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

            // code-behind で動的生成/直接代入するブラシは、この値を基準に再解決する。
            Shared.Services.ThemeHelper.NotifySelectedThemeChanged(theme);

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
            // FilterPanel は FilterOverlay.Visibility=Collapsed 配下に常駐しており、
            // 親が Collapsed のときは ActualThemeChanged が確実に伝播しないことがあるため
            // 明示的に RequestedTheme を直接セットして、Open 時に正しい theme で
            // 解決されるよう保険をかける。
            FilterPanel.ApplyThemeNow(theme);
        }
        catch (System.Exception ex)
        {
            AppLogger.Error($"ShellPage.ApplyTheme: threw: {ex}");
        }
        AppLogger.Trace("ShellPage.ApplyTheme: exit");
    }

    // Shell 破棄時に ViewModel 購読とドリルダウン関連コールバックを解除する。
    private void ShellPage_Unloaded(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("ShellPage.ShellPage_Unloaded: enter");
        try
        {
            viewModel.galleryViewModel.drillDownPhotosProvider = null;
            viewModel.galleryViewModel.selectionState.PropertyChanged -= OnSelectionStateChanged;
            viewModel.galleryViewModel.filtersState.PropertyChanged -= OnShellFiltersStateChanged;
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

    // スキャン状態文字列に合わせて全画面スキャンオーバーレイの表示を切り替える。
    private void UpdateScanningOverlayVisibility()
    {
        AppLogger.Trace($"ShellPage.UpdateScanningOverlayVisibility: enter scanStatus={viewModel.ScanStatus}");
        Stage.ScanningOverlayVisibility = viewModel.ScanStatus == "scanning"
            ? Visibility.Visible
            : Visibility.Collapsed;
        AppLogger.Trace($"ShellPage.UpdateScanningOverlayVisibility: exit visibility={Stage.ScanningOverlayVisibility}");
    }

}
