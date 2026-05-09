using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Scanner;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Features.Settings;
using Alpheratz.Features.TagMaster;
using Alpheratz.Features.Template;
using Alpheratz.Models;
using Alpheratz.Models.Events;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.Shell;

#pragma warning disable CS0162 // Unreachable code (DETACH flags are compile-time constants for debug)
public partial class ShellViewModel : UiThreadSafeObservableObject, IAsyncDisposable
{
    private const bool DETACH_RUNTIME_DATA = false;
    // Legacy debug switch — must stay false. true blocks the phash-progress
    // event subscription so the header progress chip never updates while the
    // pdq worker is actually running.
    private const bool DETACH_AUXILIARY_RUNTIME_DATA = false;

    private readonly NavigationService navigationService;
    private readonly SettingsService settingsService;
    private readonly AlpheratzDb db;
    private readonly PhotoScanner scanner;
    private readonly PhashService phashService;
    private readonly OrientationService orientationService;
    private readonly WorldService worldService;
    private readonly LocalEventBus eventBus;
    private readonly ToastService toastService;
    private readonly PhotoModalState photoModalState;
    private readonly DispatcherService dispatcherService;

    private bool isScanningRef;
    private readonly List<IAsyncDisposable> scanUnlistenFns = [];
    private readonly List<IAsyncDisposable> phashUnlistenFns = [];

    // PDQ progress events fire at ~50/sec while the worker is hot. Throttle
    // the UI write to 1 Hz so the header chip stops flickering.
    private const long PhashUiUpdateMinIntervalMs = 1000;
    private long lastPhashUiUpdateTicks;
    private PhashProgressEvent latestPhashProgress = PhashProgressEvent.Empty;

    public GalleryViewModel galleryViewModel { get; }
    public SettingsViewModel settingsViewModel { get; }
    public TagMasterViewModel tagMasterViewModel { get; }
    public TemplatePageViewModel templatePageViewModel { get; }

    public MainScreen activeMainScreen
    {
        get => navigationService.ActiveMainScreen;
        set
        {
            navigationService.ActiveMainScreen = value;
            OnPropertyChanged();
        }
    }

    private string scanStatus = "idle";
    private ScanProgressDto scanProgress = new() { processed = 0, total = 0, current_world = "", phase = "scan" };
    private string photoFolderPath = "";
    private string secondaryPhotoFolderPath = "";

    private PhashProgressEvent pdqProgress = PhashProgressEvent.Empty;
    private bool isPdqRunning;

    private string? pendingFolderPath;
    private int pendingFolderSlot = 1;
    private BackupCandidateDto? pendingRestoreCandidate;
    private PendingResetRequest? pendingResetRequest;
    private bool isApplyingFolderChange;
    private bool startupEnabled;
    private ThemeMode themeMode = ThemeMode.light;
    private ViewMode viewMode = ViewMode.standard;
    private bool isMasonryEnabled;
    public UiObservableCollection<string> tweetTemplates { get; } = [];
    private string activeTweetTemplate = "";

    public string ScanStatus
    {
        get => scanStatus;
        set => SetProperty(ref scanStatus, value);
    }

    public ScanProgressDto ScanProgress
    {
        get => scanProgress;
        set => SetProperty(ref scanProgress, value);
    }

    public string PhotoFolderPath
    {
        get => photoFolderPath;
        set => SetProperty(ref photoFolderPath, value);
    }

    public string SecondaryPhotoFolderPath
    {
        get => secondaryPhotoFolderPath;
        set => SetProperty(ref secondaryPhotoFolderPath, value);
    }

    public PhashProgressEvent PdqProgress
    {
        get => pdqProgress;
        set => SetProperty(ref pdqProgress, value);
    }

    public bool IsPdqRunning
    {
        get => isPdqRunning;
        set => SetProperty(ref isPdqRunning, value);
    }

    public string? PendingFolderPath
    {
        get => pendingFolderPath;
        set => SetProperty(ref pendingFolderPath, value);
    }

    public int PendingFolderSlot
    {
        get => pendingFolderSlot;
        set => SetProperty(ref pendingFolderSlot, value);
    }

    public BackupCandidateDto? PendingRestoreCandidate
    {
        get => pendingRestoreCandidate;
        set => SetProperty(ref pendingRestoreCandidate, value);
    }

    public PendingResetRequest? PendingResetRequest
    {
        get => pendingResetRequest;
        set => SetProperty(ref pendingResetRequest, value);
    }

    public bool IsApplyingFolderChange
    {
        get => isApplyingFolderChange;
        set => SetProperty(ref isApplyingFolderChange, value);
    }

    public bool StartupEnabled
    {
        get => startupEnabled;
        set => SetProperty(ref startupEnabled, value);
    }

    public ThemeMode ThemeMode
    {
        get => themeMode;
        set => SetProperty(ref themeMode, value);
    }

    public ViewMode ViewMode
    {
        get => viewMode;
        set => SetProperty(ref viewMode, value);
    }

    public bool IsMasonryEnabled
    {
        get => isMasonryEnabled;
        set => SetProperty(ref isMasonryEnabled, value);
    }

    public string ActiveTweetTemplate
    {
        get => activeTweetTemplate;
        set => SetProperty(ref activeTweetTemplate, value);
    }

    public ShellViewModel(
        NavigationService navigationService,
        SettingsService settingsService,
        AlpheratzDb db,
        PhotoScanner scanner,
        PhashService phashService,
        OrientationService orientationService,
        WorldService worldService,
        LocalEventBus eventBus,
        ToastService toastService,
        GalleryViewModel galleryViewModel,
        SettingsViewModel settingsViewModel,
        TagMasterViewModel tagMasterViewModel,
        TemplatePageViewModel templatePageViewModel,
        PhotoModalState photoModalState,
        DispatcherService dispatcherService)
    {
        AppLogger.Trace("ShellViewModel.ctor: enter");
        this.navigationService = navigationService;
        this.settingsService = settingsService;
        this.db = db;
        this.scanner = scanner;
        this.phashService = phashService;
        this.orientationService = orientationService;
        this.worldService = worldService;
        this.eventBus = eventBus;
        this.toastService = toastService;
        this.galleryViewModel = galleryViewModel;
        this.settingsViewModel = settingsViewModel;
        this.tagMasterViewModel = tagMasterViewModel;
        this.templatePageViewModel = templatePageViewModel;
        this.photoModalState = photoModalState;
        this.dispatcherService = dispatcherService;
        AppLogger.Trace("ShellViewModel.ctor: exit");
    }

    public PhotoModalViewModel? createPhotoModalViewModel(PhotoThumbnailItem? photo)
    {
        AppLogger.Trace($"ShellViewModel.createPhotoModalViewModel: enter photo={photo?.PhotoPath ?? "(null)"}");
        if (photo is null) return null;
        var list = galleryViewModel.photosState.displayItems
            .Select(item => item.Photo)
            .ToList();
        photoModalState.setPhotoList(list);
        photoModalState.onSelectPhoto(photo);
        var vm = new PhotoModalViewModel(photoModalState, worldService, toastService);
        AppLogger.Trace("ShellViewModel.createPhotoModalViewModel: exit");
        return vm;
    }

    public PhotoModalViewModel? createPhotoModalViewModelFromList(PhotoThumbnailItem? photo, IReadOnlyList<PhotoThumbnailItem> photos)
    {
        AppLogger.Trace($"ShellViewModel.createPhotoModalViewModelFromList: enter photo={photo?.PhotoPath ?? "(null)"} count={photos.Count}");
        if (photo is null) return null;
        photoModalState.setPhotoList(photos.ToList());
        photoModalState.onSelectPhoto(photo);
        var vm = new PhotoModalViewModel(photoModalState, worldService, toastService);
        AppLogger.Trace("ShellViewModel.createPhotoModalViewModelFromList: exit");
        return vm;
    }

    public async Task initialize()
    {
        AppLogger.Trace("ShellViewModel.initialize: enter");
        try
        {
            await registerScanListeners().ConfigureAwait(false);
            AppLogger.Trace("ShellViewModel.initialize: scan listeners registered");
            await registerPhashWorker().ConfigureAwait(false);
            AppLogger.Trace("ShellViewModel.initialize: phash worker registered");
            await refreshSettings().ConfigureAwait(false);
            AppLogger.Trace("ShellViewModel.initialize: settings refreshed");
            await galleryViewModel.photosState.InitializeAsync().ConfigureAwait(false);
            AppLogger.Trace("ShellViewModel.initialize: photosState initialized");
            await tagMasterViewModel.loadTags().ConfigureAwait(false);
            AppLogger.Trace("ShellViewModel.initialize: tags loaded");
            await galleryViewModel.loadWorldFilterOptions().ConfigureAwait(false);
            AppLogger.Trace("ShellViewModel.initialize: world filter options loaded");

            if (DETACH_RUNTIME_DATA)
            {
                AppLogger.Trace("ShellViewModel.initialize: branch=DETACH_RUNTIME_DATA");
                PhotoFolderPath = "";
                SecondaryPhotoFolderPath = "";
                ScanStatus = "idle";
                ScanProgress = new ScanProgressDto { processed = 0, total = 0, current_world = "", phase = "scan" };
                AppLogger.Trace("ShellViewModel.initialize: exit (detached)");
                return;
            }

            if (string.IsNullOrWhiteSpace(PhotoFolderPath) && string.IsNullOrWhiteSpace(SecondaryPhotoFolderPath))
            {
                AppLogger.Trace("ShellViewModel.initialize: branch=no-folder");
                toastService.addToast("写真フォルダが未設定です。設定から参照フォルダを選択してください。", ToastType.info);
                AppLogger.Trace("ShellViewModel.initialize: exit (no folder)");
                return;
            }

            await startScan().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Rethrow: caller (App.OnLaunchedCore.initContinuation) logs and
            // skips advancing to dataReady so consumers stay gated.
            AppLogger.Error($"ShellViewModel.initialize: threw: {ex}");
            throw;
        }
        AppLogger.Trace("ShellViewModel.initialize: exit");
    }

    public void setActiveMainScreen(MainScreen screen)
    {
        AppLogger.Trace($"ShellViewModel.setActiveMainScreen: enter screen={screen}");
        activeMainScreen = screen;
        AppLogger.Trace("ShellViewModel.setActiveMainScreen: exit");
    }

    public Task startScan()
    {
        AppLogger.Trace($"ShellViewModel.startScan: enter isScanningRef={isScanningRef}");

        if (DETACH_RUNTIME_DATA || isScanningRef)
        {
            AppLogger.Trace("ShellViewModel.startScan: skip (detached or already scanning)");
            return Task.CompletedTask;
        }

        isScanningRef = true;
        ScanStatus = "scanning";
        ScanProgress = new ScanProgressDto { processed = 0, total = 0, current_world = "", phase = "scan" };

        try
        {
            _ = Task.Run(() => scanner.ScanAsync());
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.startScan: threw: {err}");
            isScanningRef = false;
            ScanStatus = "error";
            toastService.addToast($"スキャンの開始に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.startScan: exit");
        return Task.CompletedTask;
    }

    public Task cancelScan()
    {
        AppLogger.Trace("ShellViewModel.cancelScan: enter");

        if (DETACH_RUNTIME_DATA)
        {
            AppLogger.Trace("ShellViewModel.cancelScan: skip (detached)");
            return Task.CompletedTask;
        }

        try
        {
            scanner.RequestCancel();
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.cancelScan: threw: {err}");
            toastService.addToast($"スキャンの中断に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.cancelScan: exit");
        return Task.CompletedTask;
    }

    public async Task refreshSettings()
    {
        AppLogger.Trace("ShellViewModel.refreshSettings: enter");

        if (DETACH_RUNTIME_DATA)
        {
            AppLogger.Trace("ShellViewModel.refreshSettings: branch=detached");
            PhotoFolderPath = "";
            SecondaryPhotoFolderPath = "";
            AppLogger.Trace("ShellViewModel.refreshSettings: exit (detached)");
            return;
        }

        try
        {
            var setting = await settingsService.GetSettingAsync().ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
            {
                PhotoFolderPath = setting.photoFolderPath ?? "";
                SecondaryPhotoFolderPath = setting.secondaryPhotoFolderPath ?? "";
                StartupEnabled = setting.enableStartup ?? false;
                ThemeMode = setting.themeMode ?? ThemeMode.light;
                ViewMode = ViewMode.standard;
                galleryViewModel.displayState.ViewMode = ViewMode.standard;
                IsMasonryEnabled = setting.enableMasonryLayout ?? false;
                ActiveTweetTemplate = setting.activeTweetTemplate ?? "";
                tweetTemplates.Clear();
                foreach (var template in setting.tweetTemplates ?? [])
                    tweetTemplates.Add(template);
            }).ConfigureAwait(false);
            await settingsViewModel.refreshSettings().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Rethrow: caller (initialize) treats this as fatal so dataReady
            // never flips and gated work stays paused.
            AppLogger.Error($"ShellViewModel.refreshSettings: threw: {ex}");
            throw;
        }
        AppLogger.Trace("ShellViewModel.refreshSettings: exit");
    }

    private Task registerScanListeners()
    {
        AppLogger.Trace("ShellViewModel.registerScanListeners: enter");

        if (DETACH_RUNTIME_DATA)
        {
            AppLogger.Trace("ShellViewModel.registerScanListeners: skip (detached)");
            return Task.CompletedTask;
        }

        try
        {
            scanUnlistenFns.Add(eventBus.Subscribe<ScanProgressDto>("scan:progress", payload =>
            {
                dispatcherService.requestAnimationFrame(() => ScanProgress = payload);
                return Task.CompletedTask;
            }));

            scanUnlistenFns.Add(eventBus.Subscribe("scan:completed", async () =>
            {
                AppLogger.Trace("ShellViewModel.scan:completed: enter");
                try
                {
                    dispatcherService.requestAnimationFrame(() =>
                    {
                        isScanningRef = false;
                        ScanStatus = "completed";
                    });
                    await galleryViewModel.loadWorldFilterOptions().ConfigureAwait(false);

                    // Kick the auxiliary workers in the background. All three
                    // services are single-flight and short-circuit when there
                    // is nothing pending, so concurrent scan completions stay
                    // cheap. We deliberately do NOT await them: the user
                    // should see "scan completed" the moment metadata lands,
                    // not after thousands of pdq hashes finish.
                    //
                    // Order matters for the user-visible result though:
                    // archive resolution is fast (log-file scan) and fills in
                    // worlds from VRChat logs, so run it first. PDQ then only
                    // needs to handle whatever the archive could not resolve.
                    AppLogger.Trace("ShellViewModel.scan:completed: kicking archive world resolver");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var resolved = await worldService.ResolveUnknownWorldsFromArchiveAsync().ConfigureAwait(false);
                            AppLogger.Trace($"ShellViewModel.scan:completed archive: resolved={resolved}");
                            if (resolved > 0)
                            {
                                await galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex) { AppLogger.Error($"ShellViewModel.scan:completed archive: threw: {ex}"); }
                    });

                    AppLogger.Trace("ShellViewModel.scan:completed: kicking orientation worker");
                    _ = Task.Run(async () =>
                    {
                        try { await orientationService.StartOrientationCalculationAsync().ConfigureAwait(false); }
                        catch (Exception ex) { AppLogger.Error($"ShellViewModel.scan:completed orientation: threw: {ex}"); }
                    });

                    AppLogger.Trace("ShellViewModel.scan:completed: kicking phash worker");
                    _ = Task.Run(async () =>
                    {
                        try { await phashService.StartPdqAnalysisAsync().ConfigureAwait(false); }
                        catch (Exception ex) { AppLogger.Error($"ShellViewModel.scan:completed phash: threw: {ex}"); }
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ShellViewModel.scan:completed: threw: {ex}");
                }
                AppLogger.Trace("ShellViewModel.scan:completed: exit");
            }));

            scanUnlistenFns.Add(eventBus.Subscribe<string>("scan:error", payload =>
            {
                AppLogger.Trace($"ShellViewModel.scan:error: enter payload={payload}");
                try
                {
                    dispatcherService.requestAnimationFrame(() =>
                    {
                        isScanningRef = false;
                        ScanStatus = "error";
                        toastService.addToast(string.IsNullOrWhiteSpace(payload) ? "スキャンに失敗しました。" : payload, ToastType.error);
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ShellViewModel.scan:error: threw: {ex}");
                }
                AppLogger.Trace("ShellViewModel.scan:error: exit");
                return Task.CompletedTask;
            }));
        }
        catch (Exception ex)
        {
            // Rethrow: subscription failure means we will not react to
            // scanner events at all; better to fail init than silently miss.
            AppLogger.Error($"ShellViewModel.registerScanListeners: threw: {ex}");
            throw;
        }
        AppLogger.Trace("ShellViewModel.registerScanListeners: exit");
        return Task.CompletedTask;
    }

    private async Task registerPhashWorker()
    {
        AppLogger.Trace("ShellViewModel.registerPhashWorker: enter");

        if (DETACH_RUNTIME_DATA || DETACH_AUXILIARY_RUNTIME_DATA)
        {
            AppLogger.Trace("ShellViewModel.registerPhashWorker: skip (detached)");
            PdqProgress = PhashProgressEvent.Empty;
            IsPdqRunning = false;
            AppLogger.Trace("ShellViewModel.registerPhashWorker: exit (detached)");
            return;
        }

        try
        {
            var initial = await phashService.GetPhashProgressAsync().ConfigureAwait(false);
            PdqProgress = initial;
            IsPdqRunning = initial.total > 0 && initial.done < initial.total;
        }
        catch (Exception ex)
        {
            // Continue: legacy alpheratz tolerates a missing initial phash
            // snapshot and falls back to the empty state.
            AppLogger.Warn($"ShellViewModel.registerPhashWorker: initial probe failed: {ex}");
            PdqProgress = PhashProgressEvent.Empty;
        }

        try
        {
            phashUnlistenFns.Add(eventBus.Subscribe<PhashProgressEvent>("phash_progress", payload =>
            {
                // Throttle UI updates to ~1Hz. PhashService publishes per-photo
                // (effectively per ~50-photo batch boundary) so without this
                // the chip flickers wildly. Always let the final tick through
                // (done == total) so the chip lands on the right number.
                latestPhashProgress = payload;
                IsPdqRunning = true;
                var nowTicks = Environment.TickCount64;
                if (payload.done >= payload.total
                    || nowTicks - lastPhashUiUpdateTicks >= PhashUiUpdateMinIntervalMs)
                {
                    lastPhashUiUpdateTicks = nowTicks;
                    PdqProgress = payload;
                }
                return Task.CompletedTask;
            }));

            phashUnlistenFns.Add(eventBus.Subscribe("phash_complete", () =>
            {
                AppLogger.Trace("ShellViewModel.phash_complete: enter");
                IsPdqRunning = false;
                PdqProgress = PdqProgress with { done = PdqProgress.total, current = null };

                // Now that every photo has a PDQ hash, backfill the world
                // names of "unknown world" photos by matching them against
                // the known-world phash corpus. Fire-and-forget; the next
                // gallery refresh will pick up the resolved entries.
                AppLogger.Trace("ShellViewModel.phash_complete: kicking similar-world resolver");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var resolved = await worldService
                            .ResolveUnknownWorldsFromSimilarPhotosAsync("all")
                            .ConfigureAwait(false);
                        AppLogger.Trace($"ShellViewModel.phash_complete resolver: resolved={resolved}");
                        if (resolved > 0)
                        {
                            await galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"ShellViewModel.phash_complete resolver: threw: {ex}");
                    }
                });

                AppLogger.Trace("ShellViewModel.phash_complete: exit");
                return Task.CompletedTask;
            }));
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ShellViewModel.registerPhashWorker: subscription failed: {ex}");
            throw;
        }
        AppLogger.Trace("ShellViewModel.registerPhashWorker: exit");
    }

    public AlpheratzSettingDto buildSettingPayload(AlpheratzSettingDto? overrides = null)
    {
        AppLogger.Trace("ShellViewModel.buildSettingPayload: enter");
        var payload = new AlpheratzSettingDto
        {
            photoFolderPath = overrides?.photoFolderPath ?? PhotoFolderPath,
            secondaryPhotoFolderPath = overrides?.secondaryPhotoFolderPath ?? SecondaryPhotoFolderPath,
            enableStartup = overrides?.enableStartup ?? StartupEnabled,
            themeMode = overrides?.themeMode ?? ThemeMode,
            viewMode = overrides?.viewMode ?? ViewMode,
            enableMasonryLayout = overrides?.enableMasonryLayout ?? IsMasonryEnabled,
            tweetTemplates = overrides?.tweetTemplates ?? tweetTemplates,
            activeTweetTemplate = overrides?.activeTweetTemplate ?? ActiveTweetTemplate,
        };
        AppLogger.Trace("ShellViewModel.buildSettingPayload: exit");
        return payload;
    }

    public async Task finalizeFolderSelection(string newPath, bool restoreBackup)
    {
        AppLogger.Trace($"ShellViewModel.finalizeFolderSelection: enter newPath={newPath} restoreBackup={restoreBackup}");
        try
        {
            await settingsService.SaveSettingAsync(buildSettingPayload(new AlpheratzSettingDto
            {
                photoFolderPath = PendingFolderSlot == 1 ? newPath : PhotoFolderPath,
                secondaryPhotoFolderPath = PendingFolderSlot == 2 ? newPath : SecondaryPhotoFolderPath,
            })).ConfigureAwait(false);

            if (restoreBackup)
            {
                AppLogger.Trace("ShellViewModel.finalizeFolderSelection: restoring backup");
                await db.RestoreCacheBackupAsync(newPath).ConfigureAwait(false);
            }

            await refreshSettings().ConfigureAwait(false);
            await galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);
            await startScan().ConfigureAwait(false);
            PendingRestoreCandidate = null;
            PendingFolderPath = null;
            toastService.addToast(restoreBackup ? "バックアップデータを反映して再スキャンを開始します" : "写真フォルダを更新しました");
        }
        catch (Exception err)
        {
            // Continue: legacy alpheratz surfaces the failure as a toast and
            // leaves the user in the previous folder state.
            AppLogger.Error($"ShellViewModel.finalizeFolderSelection: threw: {err}");
            toastService.addToast($"写真フォルダの切替に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.finalizeFolderSelection: exit");
    }

    public async Task handleFinalizeFolderSelection(string newPath, bool restoreBackup)
    {
        AppLogger.Trace($"ShellViewModel.handleFinalizeFolderSelection: enter newPath={newPath} restoreBackup={restoreBackup}");
        IsApplyingFolderChange = true;
        try
        {
            await finalizeFolderSelection(newPath, restoreBackup).ConfigureAwait(false);
        }
        finally
        {
            IsApplyingFolderChange = false;
        }
        AppLogger.Trace("ShellViewModel.handleFinalizeFolderSelection: exit");
    }

    public async Task applyFolderChange(string newPath, bool createBackup)
    {
        AppLogger.Trace($"ShellViewModel.applyFolderChange: enter newPath={newPath} createBackup={createBackup}");
        IsApplyingFolderChange = true;
        try
        {
            var currentPath = PendingFolderSlot == 1 ? PhotoFolderPath : SecondaryPhotoFolderPath;

            if (createBackup && !string.IsNullOrEmpty(currentPath))
            {
                AppLogger.Trace("ShellViewModel.applyFolderChange: creating backup");
                await db.CreateCacheBackupAsync(currentPath).ConfigureAwait(false);
            }

            AppLogger.Trace($"ShellViewModel.applyFolderChange: resetting slot={PendingFolderSlot}");
            await db.ResetPhotoCacheBySlotAsync(PendingFolderSlot).ConfigureAwait(false);

            var backupCandidate = await db.GetBackupCandidateAsync(newPath).ConfigureAwait(false);
            PendingFolderPath = null;
            if (backupCandidate is not null)
            {
                AppLogger.Trace("ShellViewModel.applyFolderChange: backup candidate found");
                PendingRestoreCandidate = backupCandidate;
                toastService.addToast("関連するバックアップデータを検出しました。");
                return;
            }

            await finalizeFolderSelection(newPath, false).ConfigureAwait(false);
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.applyFolderChange: threw: {err}");
            toastService.addToast($"写真フォルダの更新に失敗しました: {err}", ToastType.error);
        }
        finally
        {
            IsApplyingFolderChange = false;
        }
        AppLogger.Trace("ShellViewModel.applyFolderChange: exit");
    }

    public async Task executeResetFolder(int slot, bool createBackup)
    {
        AppLogger.Trace($"ShellViewModel.executeResetFolder: enter slot={slot} createBackup={createBackup}");
        var currentPath = slot == 1 ? PhotoFolderPath : SecondaryPhotoFolderPath;
        if (string.IsNullOrEmpty(currentPath))
        {
            AppLogger.Trace("ShellViewModel.executeResetFolder: skip (no current path)");
            return;
        }

        var nextPrimaryPath = slot == 1 ? "" : PhotoFolderPath;
        var nextSecondaryPath = slot == 2 ? "" : SecondaryPhotoFolderPath;

        IsApplyingFolderChange = true;
        try
        {
            if (createBackup)
            {
                AppLogger.Trace("ShellViewModel.executeResetFolder: creating backup");
                await db.CreateCacheBackupAsync(currentPath).ConfigureAwait(false);
            }

            await db.ResetPhotoCacheBySlotAsync(slot).ConfigureAwait(false);
            await settingsService.SaveSettingAsync(buildSettingPayload(new AlpheratzSettingDto
            {
                photoFolderPath = nextPrimaryPath,
                secondaryPhotoFolderPath = nextSecondaryPath,
            })).ConfigureAwait(false);

            await refreshSettings().ConfigureAwait(false);
            await galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);

            if (!string.IsNullOrEmpty(nextPrimaryPath) || !string.IsNullOrEmpty(nextSecondaryPath))
            {
                AppLogger.Trace("ShellViewModel.executeResetFolder: starting scan for remaining slot");
                await startScan().ConfigureAwait(false);
            }

            PendingResetRequest = null;
            toastService.addToast(slot == 1 ? "1st 写真フォルダをリセットしました" : "2nd 写真フォルダをリセットしました");
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.executeResetFolder: threw: {err}");
            toastService.addToast($"写真フォルダのリセットに失敗しました: {err}", ToastType.error);
        }
        finally
        {
            IsApplyingFolderChange = false;
        }
        AppLogger.Trace("ShellViewModel.executeResetFolder: exit");
    }

    public void promptFolderChange(int slot, string newPath)
    {
        AppLogger.Trace($"ShellViewModel.promptFolderChange: enter slot={slot} newPath={newPath}");
        PendingFolderSlot = slot;
        PendingFolderPath = newPath;
        PendingResetRequest = null;
        AppLogger.Trace("ShellViewModel.promptFolderChange: exit");
    }

    public async Task handleFolderChangeBackupDecision(bool createBackup)
    {
        AppLogger.Trace($"ShellViewModel.handleFolderChangeBackupDecision: enter createBackup={createBackup}");
        if (PendingFolderPath is null)
        {
            AppLogger.Trace("ShellViewModel.handleFolderChangeBackupDecision: skip (no pending path)");
            return;
        }

        await applyFolderChange(PendingFolderPath, createBackup).ConfigureAwait(false);
        AppLogger.Trace("ShellViewModel.handleFolderChangeBackupDecision: exit");
    }

    public async Task handleResetBackupDecision(bool createBackup)
    {
        AppLogger.Trace($"ShellViewModel.handleResetBackupDecision: enter createBackup={createBackup}");
        if (PendingResetRequest is null)
        {
            AppLogger.Trace("ShellViewModel.handleResetBackupDecision: skip (no pending request)");
            return;
        }

        await executeResetFolder(PendingResetRequest.slot, createBackup).ConfigureAwait(false);
        AppLogger.Trace("ShellViewModel.handleResetBackupDecision: exit");
    }

    public void handleResetFolder(int slot)
    {
        AppLogger.Trace($"ShellViewModel.handleResetFolder: enter slot={slot}");
        var currentPath = slot == 1 ? PhotoFolderPath : SecondaryPhotoFolderPath;
        if (string.IsNullOrEmpty(currentPath))
        {
            AppLogger.Trace("ShellViewModel.handleResetFolder: skip (no current path)");
            return;
        }

        PendingFolderPath = null;
        PendingResetRequest = new PendingResetRequest(slot, currentPath);
        AppLogger.Trace("ShellViewModel.handleResetFolder: exit");
    }

    public async Task handleToggleViewMode()
    {
        var nextMode = ViewMode == ViewMode.standard ? ViewMode.gallery : ViewMode.standard;
        await handleSetViewMode(nextMode).ConfigureAwait(false);
    }

    public async Task handleSetViewMode(ViewMode nextMode)
    {
        AppLogger.Trace($"ShellViewModel.handleSetViewMode: enter ViewMode={ViewMode} next={nextMode}");
        if (ViewMode == nextMode) return;
        try
        {
            await settingsService.SaveSettingAsync(buildSettingPayload(new AlpheratzSettingDto { viewMode = nextMode })).ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
            {
                if (nextMode == ViewMode.gallery && galleryViewModel.filtersState.GroupingMode != GroupingMode.none)
                {
                    galleryViewModel.filtersState.GroupingMode = GroupingMode.none;
                }
                ViewMode = nextMode;
                galleryViewModel.displayState.ViewMode = nextMode;
            }).ConfigureAwait(false);
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.handleSetViewMode: threw: {err}");
            toastService.addToast($"ビューモードの変更に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace($"ShellViewModel.handleSetViewMode: exit ViewMode={ViewMode}");
    }

    public async Task handleThemeChange(ThemeMode mode)
    {
        AppLogger.Trace($"ShellViewModel.handleThemeChange: enter mode={mode}");
        try
        {
            await settingsService.SaveSettingAsync(buildSettingPayload(new AlpheratzSettingDto { themeMode = mode })).ConfigureAwait(false);
            ThemeMode = mode;
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.handleThemeChange: threw: {err}");
            toastService.addToast($"テーマの変更に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.handleThemeChange: exit");
    }

    public async Task handleStartUnknownWorldAnalysisFromArchive()
    {
        AppLogger.Trace("ShellViewModel.handleStartUnknownWorldAnalysisFromArchive: enter");
        try
        {
            // Run the fast log-based archive resolver inline so the user sees
            // a meaningful "resolved N" toast immediately. Then chain into
            // PDQ analysis (fire-and-forget) for whatever the archive could
            // not match.
            var resolved = await worldService.ResolveUnknownWorldsFromArchiveAsync().ConfigureAwait(false);
            if (resolved > 0)
            {
                await galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);
                toastService.addToast($"アーカイブから {resolved} 件のワールドを解決しました");
            }
            else
            {
                toastService.addToast("アーカイブで解決できるワールドはありませんでした");
            }

            _ = Task.Run(async () =>
            {
                try { await phashService.StartPdqAnalysisAsync().ConfigureAwait(false); }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.handleStartUnknownWorldAnalysisFromArchive phash: threw: {ex}"); }
            });
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.handleStartUnknownWorldAnalysisFromArchive: threw: {err}");
            toastService.addToast($"分析の開始に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.handleStartUnknownWorldAnalysisFromArchive: exit");
    }

    public async Task handleStartupPreference(bool enabled)
    {
        AppLogger.Trace($"ShellViewModel.handleStartupPreference: enter enabled={enabled}");
        try
        {
            await settingsService.SaveStartupPreferenceAsync(enabled).ConfigureAwait(false);
            StartupEnabled = enabled;
            toastService.addToast(enabled ? "Alpheratz をログイン時に起動する設定にしました。" : "Alpheratz のログイン時起動を無効にしました。");
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.handleStartupPreference: threw: {err}");
            toastService.addToast($"自動起動設定の更新に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.handleStartupPreference: exit");
    }

    public async ValueTask DisposeAsync()
    {
        AppLogger.Trace($"ShellViewModel.DisposeAsync: enter scanCount={scanUnlistenFns.Count} phashCount={phashUnlistenFns.Count}");
        foreach (var unlisten in scanUnlistenFns)
        {
            try
            {
                await unlisten.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Continue: best-effort cleanup, do not let one bad subscription
                // block disposing the rest.
                AppLogger.Error($"ShellViewModel.DisposeAsync: scan unlisten threw: {ex}");
            }
        }

        foreach (var unlisten in phashUnlistenFns)
        {
            try
            {
                await unlisten.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"ShellViewModel.DisposeAsync: phash unlisten threw: {ex}");
            }
        }
        AppLogger.Trace("ShellViewModel.DisposeAsync: exit");
    }
}
