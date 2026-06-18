using Alpheratz.Core;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging;
using Alpheratz.Core.Scanner;
using Alpheratz.Features.Bootstrap;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Features.Settings;
using Alpheratz.Features.Shell;
using Alpheratz.Features.TagMaster;
using Alpheratz.Features.Template;
using Alpheratz.Services;
using Alpheratz.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz;

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public partial class App : Application
{
    private Window? mainWindow;
    private ServiceProvider? serviceProvider;
    private bool winUiResourcesInitialized;

    public static Window? MainWindowInstance => (Current as App)?.mainWindow;
    public static IServiceProvider? Services => (Current as App)?.serviceProvider;

    // アプリケーションリソースを初期化する前に、保存済みテーマを可能な範囲で適用する。
    public App()
    {
        AppLogger.Trace("App.ctor: enter");

        // Application.RequestedTheme は InitializeComponent より前に設定する必要がある。
        // この時点では DI が未構築なので、setting.json を直接読んで保存済みテーマを反映する。
        // ここを遅らせると Application レベルの ThemeResource が OS テーマで解決される。
        try
        {
            var settingDir = AppPaths.GetSettingDir();
            if (settingDir is not null)
            {
                var path = System.IO.Path.Combine(settingDir, "setting.json");
                if (System.IO.File.Exists(path))
                {
                    var json = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
                    var setting = System.Text.Json.JsonSerializer.Deserialize<AlpheratzSetting>(json);
                    RequestedTheme = setting?.ThemeMode == "dark"
                        ? ApplicationTheme.Dark
                        : ApplicationTheme.Light;
                }
                else
                {
                    RequestedTheme = ApplicationTheme.Light;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"App.ctor: theme pre-set failed: {ex.Message}");
        }

        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Fatal($"App.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("App.ctor: exit");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLogger.Trace("App.OnLaunched: enter");

        UnhandledException += (_, e) =>
        {
            AppLogger.Fatal($"App.UnhandledException: {e.Exception}");
            e.Handled = true;
        };

        try
        {
            OnLaunchedCore(args);
        }
        catch (Exception ex)
        {
            // ここまで来るとアプリは部分初期化済みなので、最上位ではログだけ残す。
            AppLogger.Fatal($"App.OnLaunched: fatal: {ex}");
        }

        AppLogger.Trace("App.OnLaunched: exit");
    }

    // DI、DB、Window、スプラッシュを順に初期化し、Shell への遷移を開始する。
    private void OnLaunchedCore(LaunchActivatedEventArgs args)
    {
        AppLogger.Trace($"App.OnLaunchedCore: enter (build {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version})");
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UiThread.Queue = dispatcherQueue;
        AppLogger.Trace("App.OnLaunchedCore: dispatcherQueue acquired");
        EnsureWinUiResources();

        var services = new ServiceCollection();
        services.AddSingleton<AppLifecycleService>();
        services.AddSingleton<LocalEventBus>();
        services.AddSingleton<AppConfig>();
        services.AddSingleton<AlpheratzDb>();
        services.AddSingleton<ThumbnailService>();
        services.AddSingleton<ThumbnailWorker>();
        services.AddSingleton(_ => new DispatcherService(dispatcherQueue));
        services.AddSingleton<PhotoScanner>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<PhotoService>();
        services.AddSingleton<WorldService>();
        services.AddSingleton<PhashService>();
        services.AddSingleton<OrientationService>();
        services.AddSingleton<GalleryPhotosState>();
        services.AddSingleton<GalleryFiltersState>();
        services.AddSingleton<GallerySelectionState>();
        services.AddSingleton<GalleryDisplayState>();
        services.AddSingleton<GalleryScrollState>();
        services.AddSingleton<PhotoModalState>();
        services.AddSingleton<GalleryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<TagMasterViewModel>();
        services.AddSingleton<TemplatePageViewModel>();
        services.AddSingleton<ShellViewModel>();
        AppLogger.Trace("App.OnLaunchedCore: services registered");

        serviceProvider = services.BuildServiceProvider();
        AppLogger.Trace("App.OnLaunchedCore: ServiceProvider built");

        var lifecycle = serviceProvider.GetRequiredService<AppLifecycleService>();
        lifecycle.advanceTo(AppLifecyclePhase.sdkReady);

        try
        {
            serviceProvider.GetRequiredService<AlpheratzDb>().Initialize();
            AppLogger.Trace("App.OnLaunchedCore: DB initialized");
        }
        catch (Exception ex)
        {
            // DB が初期化できない状態では画面のデータ操作を安全に開始できない。
            AppLogger.Fatal($"App.OnLaunchedCore: DB initialize failed: {ex}");
            throw;
        }
        lifecycle.advanceTo(AppLifecyclePhase.servicesReady);

        var shellViewModel = serviceProvider.GetRequiredService<ShellViewModel>();
        AppLogger.Trace("App.OnLaunchedCore: ShellViewModel resolved");

        // WinUI 3 の compositor は Window 作成まで初期化されないため、
        // Page.InitializeComponent より先に Window を作成・Activate する。
        // 逆順にすると Application.LoadComponent 内で停止することがある。
        MainWindow win;
        try
        {
            AppLogger.Trace("App.OnLaunchedCore: creating MainWindow");
            win = new MainWindow();
            mainWindow = win;
            AppLogger.Trace("App.OnLaunchedCore: MainWindow created");
        }
        catch (Exception ex)
        {
            AppLogger.Fatal($"App.OnLaunchedCore: MainWindow creation failed: {ex}");
            throw;
        }

        try
        {
            AppLogger.Trace("App.OnLaunchedCore: activating MainWindow");
            mainWindow.Activate();
            AppLogger.Trace("App.OnLaunchedCore: MainWindow activated");
        }
        catch (Exception ex)
        {
            AppLogger.Fatal($"App.OnLaunchedCore: MainWindow.Activate failed: {ex}");
            throw;
        }

        // ShellViewModel.initialize() は時間がかかるため、先にスプラッシュを表示する。
        // ShellPage は dataReady 到達後に遅延生成し、起動直後の空白を避ける。
        BootstrapPage bootstrapPage;
        try
        {
            AppLogger.Trace("App.OnLaunchedCore: creating BootstrapPage");
            bootstrapPage = new BootstrapPage();
            bootstrapPage.SetPhase(lifecycle.CurrentPhase);
            win.SetRoot(bootstrapPage);
            splashShownAt = DateTimeOffset.UtcNow;
            AppLogger.Trace("App.OnLaunchedCore: BootstrapPage shown");
        }
        catch (Exception ex)
        {
            AppLogger.Fatal($"App.OnLaunchedCore: BootstrapPage setup failed: {ex}");
            throw;
        }

        // ライフサイクルフェーズをスプラッシュの表示へ反映する。
        // PhaseAdvanced は initialize() 継続などバックグラウンドから発火し得るため、
        // UI 更新は DispatcherQueue へ戻す。
        lifecycle.PhaseAdvanced += (_, phase) =>
        {
            try
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    try { bootstrapPage.SetPhase(phase); }
                    catch (Exception ex) { AppLogger.Fatal($"App.bootstrap.PhaseAdvanced ui: threw: {ex}"); }

                    if (phase >= AppLifecyclePhase.dataReady)
                    {
                        var elapsed = DateTimeOffset.UtcNow - splashShownAt;
                        var minSplash = TimeSpan.FromSeconds(1.5);
                        if (elapsed >= minSplash)
                        {
                            SwapToShell(win, shellViewModel, bootstrapPage);
                        }
                        else
                        {
                            var remaining = minSplash - elapsed;
                            _ = Task.Delay(remaining).ContinueWith(_ =>
                                dispatcherQueue.TryEnqueue(() => SwapToShell(win, shellViewModel, bootstrapPage)),
                                TaskContinuationOptions.ExecuteSynchronously);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                AppLogger.Fatal($"App.bootstrap.PhaseAdvanced: threw: {ex}");
            }
        };

        _ = BeginSplashSequenceAsync(bootstrapPage, shellViewModel, lifecycle);
        AppLogger.Trace("App.OnLaunchedCore: splash sequence dispatched");

        AppLogger.Trace("App.OnLaunchedCore: exit");
    }

    private void EnsureWinUiResources()
    {
        if (winUiResourcesInitialized) return;

        try
        {
            // XamlControlsResources can fail-fast during App.InitializeComponent
            // in this self-contained unpackaged build. OnLaunched runs after the
            // WinUI desktop host is ready, while still before any page/control XAML
            // is loaded.
            Resources.MergedDictionaries.Insert(0, new XamlControlsResources());
            winUiResourcesInitialized = true;
            AppLogger.Trace("App.EnsureWinUiResources: XamlControlsResources installed");
        }
        catch (Exception ex)
        {
            AppLogger.Fatal($"App.EnsureWinUiResources: failed: {ex}");
            throw;
        }
    }

    private bool shellSwapped;
    private DateTimeOffset splashShownAt;

    // スプラッシュの導入アニメーション後に ShellViewModel の初期化をバックグラウンドで開始する。
    private async Task BeginSplashSequenceAsync(BootstrapPage bootstrapPage, ShellViewModel shellViewModel, AppLifecycleService lifecycle)
    {
        AppLogger.Trace("App.BeginSplashSequence: intro blank start");
        await Task.Delay(TimeSpan.FromSeconds(1));
        await bootstrapPage.FadeInAsync();
        splashShownAt = DateTimeOffset.UtcNow;
        AppLogger.Trace("App.BeginSplashSequence: fade-in done, starting init");

        _ = shellViewModel.initialize().ContinueWith(t =>
        {
            AppLogger.Trace($"App.initContinuation: enter status={t.Status}");
            try
            {
                if (t.IsCompletedSuccessfully)
                    lifecycle.advanceTo(AppLifecyclePhase.dataReady);
                else if (t.Exception is not null)
                    AppLogger.Fatal($"App.initContinuation: initialize() failed: {t.Exception}");
            }
            catch (Exception ex)
            {
                AppLogger.Fatal($"App.initContinuation: threw: {ex}");
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    // スプラッシュをフェードアウトし、メインの ShellPage を Window へ差し替える。
    private async void SwapToShell(MainWindow win, ShellViewModel shellViewModel, BootstrapPage bootstrapPage)
    {
        if (shellSwapped) return;
        shellSwapped = true;
        AppLogger.Trace("App.SwapToShell: enter");
        try
        {
            await bootstrapPage.FadeOutAsync();
            var shellPage = new ShellPage(shellViewModel);
            win.SetRoot(shellPage);
            AppLogger.Trace("App.SwapToShell: ShellPage mounted");
        }
        catch (Exception ex)
        {
            AppLogger.Fatal($"App.SwapToShell: failed: {ex}");
        }
        AppLogger.Trace("App.SwapToShell: exit");
    }
}
