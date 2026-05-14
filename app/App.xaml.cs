using Alpheratz.Core;
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

namespace Alpheratz;

public partial class App : Application
{
    private Window? mainWindow;
    private ServiceProvider? serviceProvider;

    public static Window? MainWindowInstance => (Current as App)?.mainWindow;
    public static IServiceProvider? Services => (Current as App)?.serviceProvider;

    public App()
    {
        AppLogger.Trace("App.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"App.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("App.ctor: exit");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLogger.Trace("App.OnLaunched: enter");

        UnhandledException += (_, e) =>
        {
            AppLogger.Error($"App.UnhandledException: {e.Exception}");
            e.Handled = true;
        };

        try
        {
            OnLaunchedCore(args);
        }
        catch (Exception ex)
        {
            // Continue: the app is already in a partially-initialized state
            // and there is nothing meaningful we can do beyond logging. The
            // legacy alpheratz frontend likewise swallowed top-level errors
            // so the runtime stayed alive long enough to show a toast.
            AppLogger.Error($"App.OnLaunched: fatal: {ex}");
        }

        AppLogger.Trace("App.OnLaunched: exit");
    }

    private void OnLaunchedCore(LaunchActivatedEventArgs args)
    {
        AppLogger.Trace($"App.OnLaunchedCore: enter (build {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version})");
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UiThread.Queue = dispatcherQueue;
        AppLogger.Trace("App.OnLaunchedCore: dispatcherQueue acquired");

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
            // Rethrow: the app cannot run without its database; this matches
            // the legacy startup contract that bailed loudly on init failure.
            AppLogger.Error($"App.OnLaunchedCore: DB initialize failed: {ex}");
            throw;
        }
        lifecycle.advanceTo(AppLifecyclePhase.servicesReady);

        var shellViewModel = serviceProvider.GetRequiredService<ShellViewModel>();
        AppLogger.Trace("App.OnLaunchedCore: ShellViewModel resolved");

        // Window must be created and activated BEFORE any Page's InitializeComponent()
        // because WinUI3's compositor is not initialized until a Window exists.
        // Creating a Page before this point deadlocks inside Application.LoadComponent().
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
            AppLogger.Error($"App.OnLaunchedCore: MainWindow creation failed: {ex}");
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
            AppLogger.Error($"App.OnLaunchedCore: MainWindow.Activate failed: {ex}");
            throw;
        }

        // Show the splash first so the user sees the brand mark + phase
        // status while ShellViewModel.initialize() runs in the background.
        // ShellPage is constructed lazily once dataReady fires below.
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
            AppLogger.Error($"App.OnLaunchedCore: BootstrapPage setup failed: {ex}");
            throw;
        }

        // Mirror lifecycle phase changes onto the splash status text. The
        // dispatcher hop is required because PhaseAdvanced may fire from a
        // background thread (e.g. the initialize() continuation).
        lifecycle.PhaseAdvanced += (_, phase) =>
        {
            try
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    try { bootstrapPage.SetPhase(phase); }
                    catch (Exception ex) { AppLogger.Error($"App.bootstrap.PhaseAdvanced ui: threw: {ex}"); }

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
                AppLogger.Error($"App.bootstrap.PhaseAdvanced: threw: {ex}");
            }
        };

        _ = BeginSplashSequenceAsync(bootstrapPage, shellViewModel, lifecycle);
        AppLogger.Trace("App.OnLaunchedCore: splash sequence dispatched");

        AppLogger.Trace("App.OnLaunchedCore: exit");
    }

    private bool shellSwapped;
    private DateTimeOffset splashShownAt;

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
                    AppLogger.Error($"App.initContinuation: initialize() failed: {t.Exception}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"App.initContinuation: threw: {ex}");
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

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
            AppLogger.Error($"App.SwapToShell: failed: {ex}");
        }
        AppLogger.Trace("App.SwapToShell: exit");
    }
}
