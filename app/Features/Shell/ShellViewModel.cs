using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    private const bool DETACH_AUXILIARY_RUNTIME_DATA = false;

    private readonly NavigationService navigationService;
    private readonly SettingsService settingsService;
    private readonly AlpheratzDb db;
    private readonly PhotoScanner scanner;
    private readonly PhashService phashService;
    private readonly OrientationService orientationService;
    private readonly WorldService worldService;
    private readonly LocalEventBus eventBus;
    public readonly ToastService toastService;
    private readonly PhotoModalState photoModalState;
    private readonly DispatcherService dispatcherService;
    private readonly ThumbnailWorker thumbnailWorker;

    private bool isScanningRef;
    private readonly List<IAsyncDisposable> scanUnlistenFns = [];
    private readonly List<IAsyncDisposable> phashUnlistenFns = [];

    // scan:completed の後続タスク (archive/orientation/phash) を直列化するためのセマフォ。
    // 3 タスクを並列で走らせると DB writer が衝突し、SQLITE_BUSY や orientation の
    // 部分書き込みが発生しうるため、明示的に逐次化する。
    private readonly SemaphoreSlim postScanGate = new(1, 1);

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
        set { navigationService.ActiveMainScreen = value; OnPropertyChanged(); }
    }

    private string scanStatus = "idle";
    private ScanProgressDto scanProgress = new() { processed = 0, total = 0, current_world = "", phase = "scan" };
    private string photoFolderPath = "";
    private string secondaryPhotoFolderPath = "";
    private PhashProgressEvent pdqProgress = PhashProgressEvent.Empty;
    private bool isPdqRunning;
    private string? pendingFolderPath;
    private int pendingFolderSlot = 1;
    private PendingResetRequest? pendingResetRequest;
    private bool isApplyingFolderChange;
    private bool startupEnabled;
    private ThemeMode themeMode = ThemeMode.light;
    private ViewMode viewMode = ViewMode.standard;
    private bool isMasonryEnabled;
    public UiObservableCollection<string> tweetTemplates { get; } = [];
    private string activeTweetTemplate = "";

    public string ScanStatus { get => scanStatus; set => SetProperty(ref scanStatus, value); }
    public ScanProgressDto ScanProgress { get => scanProgress; set => SetProperty(ref scanProgress, value); }
    public string PhotoFolderPath { get => photoFolderPath; set => SetProperty(ref photoFolderPath, value); }
    public string SecondaryPhotoFolderPath { get => secondaryPhotoFolderPath; set => SetProperty(ref secondaryPhotoFolderPath, value); }
    public PhashProgressEvent PdqProgress { get => pdqProgress; set => SetProperty(ref pdqProgress, value); }
    public bool IsPdqRunning { get => isPdqRunning; set => SetProperty(ref isPdqRunning, value); }
    public string? PendingFolderPath { get => pendingFolderPath; set => SetProperty(ref pendingFolderPath, value); }
    public int PendingFolderSlot { get => pendingFolderSlot; set => SetProperty(ref pendingFolderSlot, value); }
    public PendingResetRequest? PendingResetRequest { get => pendingResetRequest; set => SetProperty(ref pendingResetRequest, value); }
    public bool IsApplyingFolderChange { get => isApplyingFolderChange; set => SetProperty(ref isApplyingFolderChange, value); }
    public bool StartupEnabled { get => startupEnabled; set => SetProperty(ref startupEnabled, value); }
    public ThemeMode ThemeMode { get => themeMode; set => SetProperty(ref themeMode, value); }
    public ViewMode ViewMode { get => viewMode; set => SetProperty(ref viewMode, value); }
    public bool IsMasonryEnabled { get => isMasonryEnabled; set => SetProperty(ref isMasonryEnabled, value); }
    public string ActiveTweetTemplate { get => activeTweetTemplate; set => SetProperty(ref activeTweetTemplate, value); }

    public ShellViewModel(
        NavigationService navigationService, SettingsService settingsService, AlpheratzDb db, PhotoScanner scanner,
        PhashService phashService, OrientationService orientationService, WorldService worldService,
        LocalEventBus eventBus, ToastService toastService, GalleryViewModel galleryViewModel,
        SettingsViewModel settingsViewModel, TagMasterViewModel tagMasterViewModel,
        TemplatePageViewModel templatePageViewModel, PhotoModalState photoModalState, DispatcherService dispatcherService,
        ThumbnailWorker thumbnailWorker)
    {
        AppLogger.Trace("ShellViewModel.ctor: enter");
        this.navigationService = navigationService; this.settingsService = settingsService; this.db = db;
        this.scanner = scanner; this.phashService = phashService; this.orientationService = orientationService;
        this.worldService = worldService; this.eventBus = eventBus; this.toastService = toastService;
        this.galleryViewModel = galleryViewModel; this.settingsViewModel = settingsViewModel;
        this.tagMasterViewModel = tagMasterViewModel; this.templatePageViewModel = templatePageViewModel;
        this.photoModalState = photoModalState; this.dispatcherService = dispatcherService;
        this.thumbnailWorker = thumbnailWorker;
        AppLogger.Trace("ShellViewModel.ctor: exit");
    }

    public WorldResolve.WorldResolveViewModel CreateWorldResolveViewModel()
        => new(db, thumbnailWorker, toastService);

    public PhotoModalViewModel? createPhotoModalViewModel(PhotoThumbnailItem? photo)
    {
        AppLogger.Trace($"ShellViewModel.createPhotoModalViewModel: enter photo={photo?.PhotoPath ?? "(null)"}");
        if (photo is null) return null;
        var list = galleryViewModel.photosState.displayItems.Select(item => item.Photo).ToList();
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
            await registerPhashWorker().ConfigureAwait(false);
            await refreshSettings().ConfigureAwait(false);
            await galleryViewModel.photosState.InitializeAsync().ConfigureAwait(false);
            await tagMasterViewModel.loadTags().ConfigureAwait(false);
            await galleryViewModel.loadWorldFilterOptions().ConfigureAwait(false);

            if (DETACH_RUNTIME_DATA)
            {
                PhotoFolderPath = ""; SecondaryPhotoFolderPath = ""; ScanStatus = "idle";
                ScanProgress = new ScanProgressDto { processed = 0, total = 0, current_world = "", phase = "scan" };
                return;
            }

            if (string.IsNullOrWhiteSpace(PhotoFolderPath) && string.IsNullOrWhiteSpace(SecondaryPhotoFolderPath))
            {
                toastService.addToast("写真フォルダが未設定です。設定から参照フォルダを選択してください。", ToastType.info);
                return;
            }

            await startScan().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellViewModel.initialize: threw: {ex}"); throw; }
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
        if (DETACH_RUNTIME_DATA || isScanningRef) return Task.CompletedTask;
        isScanningRef = true; ScanStatus = "scanning";
        ScanProgress = new ScanProgressDto { processed = 0, total = 0, current_world = "", phase = "scan" };
        try
        {
            // R2-A-15: 旧実装 `Task.Run(() => scanner.ScanAsync())` は fire-and-forget で、
            //          ScanAsync の枠外で起きた例外（例: スキャナ生成失敗、Task.Run 自体の失敗）が
            //          UnobservedTaskException としてアプリ全体に伝播する恐れがあった。
            //          async ラッパで try/catch し、漏れた例外を scan:error として購読者に通知する。
            _ = Task.Run(async () =>
            {
                try
                {
                    await scanner.ScanAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ShellViewModel.startScan: ScanAsync wrapper threw: {ex}");
                    try { await eventBus.PublishAsync("scan:error", ex.Message).ConfigureAwait(false); }
                    catch (Exception pubEx) { AppLogger.Error($"ShellViewModel.startScan: failed to publish scan:error: {pubEx}"); }
                }
            });
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.startScan: threw: {err}");
            isScanningRef = false; ScanStatus = "error";
            toastService.addToast($"スキャンの開始に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.startScan: exit");
        return Task.CompletedTask;
    }

    public Task cancelScan()
    {
        AppLogger.Trace("ShellViewModel.cancelScan: enter");
        if (DETACH_RUNTIME_DATA) return Task.CompletedTask;
        try { scanner.RequestCancel(); }
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
            PhotoFolderPath = ""; SecondaryPhotoFolderPath = "";
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
                // 永続化された viewMode を優先し、なければ既定値 standard を適用する。
                var nextViewMode = setting.viewMode ?? ViewMode.standard;
                ViewMode = nextViewMode;
                galleryViewModel.displayState.ViewMode = nextViewMode;
                IsMasonryEnabled = setting.enableMasonryLayout ?? false;
                ActiveTweetTemplate = setting.activeTweetTemplate ?? "";
                tweetTemplates.Clear();
                foreach (var template in setting.tweetTemplates ?? []) tweetTemplates.Add(template);
            }).ConfigureAwait(false);
            await settingsViewModel.refreshSettings().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellViewModel.refreshSettings: threw: {ex}"); throw; }
        AppLogger.Trace("ShellViewModel.refreshSettings: exit");
    }

    /// <summary>
    /// scan:completed 後の archive 解決 / orientation 計算 / phash 計算を順番に走らせる。
    /// 旧実装ではこの 3 つを Task.Run で同時に投げていたため、DB writer が競合して
    /// 部分書き込みや SQLITE_BUSY が起きていた。postScanGate で直列化する。
    /// </summary>
    private async Task runPostScanWorkflow()
    {
        await postScanGate.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                var resolved = await worldService.ResolveUnknownWorldsFromArchiveAsync().ConfigureAwait(false);
                if (resolved > 0) await galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);
            }
            catch (Exception ex) { AppLogger.Error($"ShellViewModel.runPostScanWorkflow archive: threw: {ex}"); }

            try { await orientationService.StartOrientationCalculationAsync().ConfigureAwait(false); }
            catch (Exception ex) { AppLogger.Error($"ShellViewModel.runPostScanWorkflow orientation: threw: {ex}"); }

            try { await phashService.StartPdqAnalysisAsync().ConfigureAwait(false); }
            catch (Exception ex) { AppLogger.Error($"ShellViewModel.runPostScanWorkflow phash: threw: {ex}"); }
        }
        finally
        {
            postScanGate.Release();
        }
    }

    private Task registerScanListeners()
    {
        AppLogger.Trace("ShellViewModel.registerScanListeners: enter");
        if (DETACH_RUNTIME_DATA) return Task.CompletedTask;
        try
        {
            scanUnlistenFns.Add(eventBus.Subscribe<ScanProgressDto>("scan:progress", payload =>
            {
                dispatcherService.requestAnimationFrame(() => ScanProgress = payload);
                return Task.CompletedTask;
            }));
            scanUnlistenFns.Add(eventBus.Subscribe("scan:completed", async () =>
            {
                try
                {
                    dispatcherService.requestAnimationFrame(() => { isScanningRef = false; ScanStatus = "completed"; });
                    await galleryViewModel.loadWorldFilterOptions().ConfigureAwait(false);
                    // archive → orientation → phash を直列実行する。
                    _ = Task.Run(runPostScanWorkflow);
                }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.scan:completed: threw: {ex}"); }
            }));
            scanUnlistenFns.Add(eventBus.Subscribe<string>("scan:error", payload =>
            {
                try
                {
                    dispatcherService.requestAnimationFrame(() =>
                    {
                        isScanningRef = false; ScanStatus = "error";
                        toastService.addToast(string.IsNullOrWhiteSpace(payload) ? "スキャンに失敗しました。" : payload, ToastType.error);
                    });
                }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.scan:error: threw: {ex}"); }
                return Task.CompletedTask;
            }));
        }
        catch (Exception ex) { AppLogger.Error($"ShellViewModel.registerScanListeners: threw: {ex}"); throw; }
        return Task.CompletedTask;
    }

    private async Task registerPhashWorker()
    {
        AppLogger.Trace("ShellViewModel.registerPhashWorker: enter");
        if (DETACH_RUNTIME_DATA || DETACH_AUXILIARY_RUNTIME_DATA)
        {
            PdqProgress = PhashProgressEvent.Empty; IsPdqRunning = false;
            return;
        }
        try
        {
            var initial = await phashService.GetPhashProgressAsync().ConfigureAwait(false);
            PdqProgress = initial;
            IsPdqRunning = initial.total > 0 && initial.done < initial.total;
        }
        catch (Exception ex) { AppLogger.Warn($"ShellViewModel.registerPhashWorker: initial probe failed: {ex}"); PdqProgress = PhashProgressEvent.Empty; }
        try
        {
            phashUnlistenFns.Add(eventBus.Subscribe<PhashProgressEvent>("phash_progress", payload =>
            {
                latestPhashProgress = payload; IsPdqRunning = true;
                var nowTicks = Environment.TickCount64;
                if (payload.done >= payload.total || nowTicks - lastPhashUiUpdateTicks >= PhashUiUpdateMinIntervalMs)
                {
                    lastPhashUiUpdateTicks = nowTicks; PdqProgress = payload;
                }
                return Task.CompletedTask;
            }));
            phashUnlistenFns.Add(eventBus.Subscribe("phash_complete", () =>
            {
                IsPdqRunning = false;
                PdqProgress = PdqProgress with { done = PdqProgress.total, current = null };
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var resolved = await worldService.ResolveUnknownWorldsFromSimilarPhotosAsync("all").ConfigureAwait(false);
                        if (resolved > 0) await galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);
                    }
                    catch (Exception ex) { AppLogger.Error($"ShellViewModel.phash_complete resolver: threw: {ex}"); }
                });
                return Task.CompletedTask;
            }));
        }
        catch (Exception ex) { AppLogger.Error($"ShellViewModel.registerPhashWorker: subscription failed: {ex}"); throw; }
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

    public async Task applyFolderChange(string newPath)
    {
        AppLogger.Trace($"ShellViewModel.applyFolderChange: enter newPath={newPath}");
        IsApplyingFolderChange = true;
        try
        {
            await db.ResetPhotoCacheBySlotAsync(PendingFolderSlot).ConfigureAwait(false);
            PendingFolderPath = null;
            await settingsService.SaveSettingAsync(buildSettingPayload(new AlpheratzSettingDto
            {
                photoFolderPath = PendingFolderSlot == 1 ? newPath : PhotoFolderPath,
                secondaryPhotoFolderPath = PendingFolderSlot == 2 ? newPath : SecondaryPhotoFolderPath,
            })).ConfigureAwait(false);
            await refreshSettings().ConfigureAwait(false);
            await galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);
            await startScan().ConfigureAwait(false);
            toastService.addToast("写真フォルダを更新しました");
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.applyFolderChange: threw: {err}");
            toastService.addToast($"写真フォルダの更新に失敗しました: {err}", ToastType.error);
        }
        finally { IsApplyingFolderChange = false; }
        AppLogger.Trace("ShellViewModel.applyFolderChange: exit");
    }

    public async Task executeResetFolder(int slot)
    {
        AppLogger.Trace($"ShellViewModel.executeResetFolder: enter slot={slot}");
        var currentPath = slot == 1 ? PhotoFolderPath : SecondaryPhotoFolderPath;
        if (string.IsNullOrEmpty(currentPath)) return;
        var nextPrimaryPath = slot == 1 ? "" : PhotoFolderPath;
        var nextSecondaryPath = slot == 2 ? "" : SecondaryPhotoFolderPath;
        IsApplyingFolderChange = true;
        try
        {
            await db.ResetPhotoCacheBySlotAsync(slot).ConfigureAwait(false);
            await settingsService.SaveSettingAsync(buildSettingPayload(new AlpheratzSettingDto
            { photoFolderPath = nextPrimaryPath, secondaryPhotoFolderPath = nextSecondaryPath })).ConfigureAwait(false);
            await refreshSettings().ConfigureAwait(false);
            await galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(nextPrimaryPath) || !string.IsNullOrEmpty(nextSecondaryPath)) await startScan().ConfigureAwait(false);
            PendingResetRequest = null;
            toastService.addToast(slot == 1 ? "1st 写真フォルダをリセットしました" : "2nd 写真フォルダをリセットしました");
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.executeResetFolder: threw: {err}");
            toastService.addToast($"写真フォルダのリセットに失敗しました: {err}", ToastType.error);
        }
        finally { IsApplyingFolderChange = false; }
        AppLogger.Trace("ShellViewModel.executeResetFolder: exit");
    }

    public void promptFolderChange(int slot, string newPath)
    {
        AppLogger.Trace($"ShellViewModel.promptFolderChange: enter slot={slot} newPath={newPath}");
        PendingFolderSlot = slot; PendingFolderPath = newPath; PendingResetRequest = null;
        AppLogger.Trace("ShellViewModel.promptFolderChange: exit");
    }

    public void handleResetFolder(int slot)
    {
        AppLogger.Trace($"ShellViewModel.handleResetFolder: enter slot={slot}");
        var currentPath = slot == 1 ? PhotoFolderPath : SecondaryPhotoFolderPath;
        if (string.IsNullOrEmpty(currentPath)) return;
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
                    galleryViewModel.filtersState.GroupingMode = GroupingMode.none;
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

    public async Task handleMasonryPreference(bool enabled)
    {
        AppLogger.Trace($"ShellViewModel.handleMasonryPreference: enter enabled={enabled}");
        try
        {
            await settingsService.SaveSettingAsync(buildSettingPayload(new AlpheratzSettingDto { enableMasonryLayout = enabled })).ConfigureAwait(false);
            IsMasonryEnabled = enabled;
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.handleMasonryPreference: threw: {err}");
            toastService.addToast($"メイソンリー設定の更新に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.handleMasonryPreference: exit");
    }

    public async Task handleStartUnknownWorldAnalysisFromArchive()
    {
        AppLogger.Trace("ShellViewModel.handleStartUnknownWorldAnalysisFromArchive: enter");
        try
        {
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

    /// <summary>紛失写真の救済 UI 用。is_missing=1 の写真を取得する。</summary>
    public Task<IReadOnlyList<PhotoRecordDto>> getMissingPhotosAsync()
    {
        AppLogger.Trace("ShellViewModel.getMissingPhotosAsync: enter");
        return db.GetMissingPhotosAsync();
    }

    /// <summary>紛失写真の救済 UI 用。指定パスを DB から完全削除する。</summary>
    public async Task deleteMissingPhotosAsync(IReadOnlyList<string> photoPaths)
    {
        AppLogger.Trace($"ShellViewModel.deleteMissingPhotosAsync: enter count={photoPaths.Count}");
        try
        {
            await db.DeletePhotosByPathsAsync(photoPaths).ConfigureAwait(false);
            await galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);
            toastService.addToast($"{photoPaths.Count} 件の写真情報を DB から削除しました");
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.deleteMissingPhotosAsync: threw: {err}");
            toastService.addToast($"写真情報の削除に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.deleteMissingPhotosAsync: exit");
    }

    public async ValueTask DisposeAsync()
    {
        AppLogger.Trace($"ShellViewModel.DisposeAsync: enter scanCount={scanUnlistenFns.Count} phashCount={phashUnlistenFns.Count}");
        foreach (var unlisten in scanUnlistenFns)
        {
            try { await unlisten.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { AppLogger.Error($"ShellViewModel.DisposeAsync: scan unlisten threw: {ex}"); }
        }
        foreach (var unlisten in phashUnlistenFns)
        {
            try { await unlisten.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { AppLogger.Error($"ShellViewModel.DisposeAsync: phash unlisten threw: {ex}"); }
        }
        galleryViewModel.Cleanup();
        AppLogger.Trace("ShellViewModel.DisposeAsync: exit");
    }
}