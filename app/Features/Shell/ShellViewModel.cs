using System;
using System.Collections.Generic;
using System.IO;
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
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Features.Shell;

#pragma warning disable CS0162 // Unreachable code (DETACH flags are compile-time constants for debug)
public partial class ShellViewModel : UiThreadSafeObservableObject, IAsyncDisposable
{
    private const bool DETACH_RUNTIME_DATA = false;
    private const bool DETACH_AUXILIARY_RUNTIME_DATA = false;

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
    private readonly PhotoService photoService;

    private bool isScanningRef;
    private readonly object activeScanGate = new();
    private Task? activeScanTask;
    private CancellationTokenSource? activeScanCancellation;
    private readonly SemaphoreSlim folderMutationGate = new(1, 1);
    private readonly List<IAsyncDisposable> scanUnlistenFns = [];
    private readonly List<IAsyncDisposable> phashUnlistenFns = [];

    // scan:completed の後続タスク (archive 解析 / orientation 補完 / phash 計算) を
    // 直列化するためのセマフォ。3 タスクを並列で走らせると同じ photos テーブルへの
    // UPDATE が衝突して SQLITE_BUSY が返り、結果として orientation や phash が一部行だけ
    // 反映されず欠落するパーシャル書き込みが発生する。WAL でも複数 writer は禁止のため、
    // ここで明示的に 1 つずつ走らせる。トレードオフ：スキャン後の補完が逐次なので終了が
    // やや遅いが、ユーザは UI 操作可能なので体感問題にはなりにくい。
    private readonly SemaphoreSlim postScanGate = new(1, 1);
    private readonly object postScanOperationsGate = new();
    private readonly Dictionary<Task, CancellationTokenSource> postScanOperations = [];

    // phash 計算進捗イベントは数十 ms ごとに発火するため、状態更新の最小間隔を 1 秒に絞る。
    private const long PhashProgressUpdateMinIntervalMs = 1000;
    private long lastPhashProgressUpdateTicks;

    public GalleryViewModel galleryViewModel { get; }
    public SettingsViewModel settingsViewModel { get; }
    public TagMasterViewModel tagMasterViewModel { get; }
    public TemplatePageViewModel templatePageViewModel { get; }
    public DuplicatePhotosViewModel duplicatePhotosViewModel { get; }

    private string scanStatus = "idle";
    private ScanProgressDto scanProgress = new() { processed = 0, total = 0, current_world = "", phase = "scan" };
    private string photoFolderPath = "";
    private string secondaryPhotoFolderPath = "";
    private PhashProgressEvent pdqProgress = PhashProgressEvent.Empty;
    private bool isPdqRunning;
    private bool canStartWorldResolve;
    private string? pendingFolderPath;
    private int pendingFolderSlot = 1;
    private PendingResetRequest? pendingResetRequest;
    private bool isApplyingFolderChange;
    private bool startupEnabled;
    private ThemeMode themeMode = ThemeMode.light;
    private ViewMode viewMode = ViewMode.standard;
    private bool openWorldLinkOnPost;
    public UiObservableCollection<string> tweetTemplates { get; } = [];
    private string activeTweetTemplate = "";

    public string ScanStatus { get => scanStatus; set => SetProperty(ref scanStatus, value); }
    public ScanProgressDto ScanProgress { get => scanProgress; set => SetProperty(ref scanProgress, value); }
    public string PhotoFolderPath { get => photoFolderPath; set => SetProperty(ref photoFolderPath, value); }
    public string SecondaryPhotoFolderPath { get => secondaryPhotoFolderPath; set => SetProperty(ref secondaryPhotoFolderPath, value); }
    public PhashProgressEvent PdqProgress { get => pdqProgress; set => SetProperty(ref pdqProgress, value); }
    public bool IsPdqRunning { get => isPdqRunning; set => SetProperty(ref isPdqRunning, value); }
    public bool CanStartWorldResolve { get => canStartWorldResolve; private set => SetProperty(ref canStartWorldResolve, value); }
    public string? PendingFolderPath { get => pendingFolderPath; set => SetProperty(ref pendingFolderPath, value); }
    public int PendingFolderSlot { get => pendingFolderSlot; set => SetProperty(ref pendingFolderSlot, value); }
    public PendingResetRequest? PendingResetRequest { get => pendingResetRequest; set => SetProperty(ref pendingResetRequest, value); }
    public bool IsApplyingFolderChange { get => isApplyingFolderChange; set => SetProperty(ref isApplyingFolderChange, value); }
    public bool StartupEnabled { get => startupEnabled; set => SetProperty(ref startupEnabled, value); }
    public ThemeMode ThemeMode { get => themeMode; set => SetProperty(ref themeMode, value); }
    public ViewMode ViewMode { get => viewMode; set => SetProperty(ref viewMode, value); }
    public bool OpenWorldLinkOnPost { get => openWorldLinkOnPost; set => SetProperty(ref openWorldLinkOnPost, value); }
    public string ActiveTweetTemplate { get => activeTweetTemplate; set => SetProperty(ref activeTweetTemplate, value); }

    public ShellViewModel(
        SettingsService settingsService, AlpheratzDb db, PhotoScanner scanner,
        PhashService phashService, OrientationService orientationService, WorldService worldService,
        LocalEventBus eventBus, ToastService toastService, GalleryViewModel galleryViewModel,
        SettingsViewModel settingsViewModel, TagMasterViewModel tagMasterViewModel,
        TemplatePageViewModel templatePageViewModel, PhotoModalState photoModalState, DispatcherService dispatcherService,
        ThumbnailWorker thumbnailWorker, PhotoService? photoService = null)
    {
        AppLogger.Trace("ShellViewModel.ctor: enter");
        this.settingsService = settingsService; this.db = db;
        this.scanner = scanner; this.phashService = phashService; this.orientationService = orientationService;
        this.worldService = worldService; this.eventBus = eventBus; this.toastService = toastService;
        this.galleryViewModel = galleryViewModel; this.settingsViewModel = settingsViewModel;
        this.tagMasterViewModel = tagMasterViewModel; this.templatePageViewModel = templatePageViewModel;
        this.photoModalState = photoModalState; this.dispatcherService = dispatcherService;
        this.thumbnailWorker = thumbnailWorker;
        this.photoService = photoService ?? new PhotoService(db);
        duplicatePhotosViewModel = new DuplicatePhotosViewModel(this.photoService, dispatcherService, toastService);
        AppLogger.Trace("ShellViewModel.ctor: exit");
    }

    /// <summary>ワールド解決モーダル用の ViewModel を現在のサービス構成から作成する。</summary>
    public WorldResolve.WorldResolveViewModel CreateWorldResolveViewModel()
        => new(db, thumbnailWorker, toastService, dispatcherService);

    /// <summary>単一写真を対象にした PhotoModalViewModel を作成する。写真が null なら null。</summary>
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

    /// <summary>前後ナビゲーション用の写真リストを持つ PhotoModalViewModel を作成する。</summary>
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

    /// <summary>起動時に設定、タグ、ギャラリー、各種イベント購読を初期化する。</summary>
    public async Task initialize()
    {
        AppLogger.Trace("ShellViewModel.initialize: enter");
        try
        {
            // リスナ登録と設定読込は副作用が独立しているので並行化する。
            // 写真ロードはこれらが終わった後 (DB セッションが落ち着いた後) に開始する。
            await Task.WhenAll(
                registerScanListeners(),
                registerPhashWorker(),
                refreshSettings()).ConfigureAwait(false);

            // フォルダ設定の保存後にアプリが終了していた場合は、ギャラリーを読む前に
            // 旧スロットの整理を再実行する。整理できない間は新しい走査を開始しない。
            var folderCleanupReady = await ResumePendingFolderCleanupAsync().ConfigureAwait(false);
            if (!folderCleanupReady)
            {
                ScanStatus = "error";
                // 旧 DB は表示しないが、同一セッションで整理を再試行した後の scan 完了を
                // 受け取れるようイベント購読だけは初期化する。
                await galleryViewModel.photosState.InitializeAsync(loadInitialData: false).ConfigureAwait(false);
                return;
            }

            // 写真メタデータ・タグマスタ・ワールド候補は互いに独立した SELECT。
            // 逐次 await すると 3 つの DB ラウンドトリップ分のレイテンシが積み上がる
            // ため、Task.WhenAll で並行発行する。
            var photosInitTask = galleryViewModel.photosState.InitializeAsync();
            var tagsTask = tagMasterViewModel.loadTags();
            var worldsTask = galleryViewModel.loadWorldFilterOptions();
            // M-5c: タグ候補の件数バッジ用ソース。ワールド候補と同様に並行発行する。
            var tagCountsTask = galleryViewModel.loadTagFilterCounts();
            await Task.WhenAll(photosInitTask, tagsTask, worldsTask, tagCountsTask).ConfigureAwait(false);

            if (DETACH_RUNTIME_DATA)
            {
                PhotoFolderPath = ""; SecondaryPhotoFolderPath = ""; ScanStatus = "idle";
                ScanProgress = new ScanProgressDto { processed = 0, total = 0, current_world = "", phase = "scan" };
                return;
            }

            if (string.IsNullOrWhiteSpace(PhotoFolderPath) && string.IsNullOrWhiteSpace(SecondaryPhotoFolderPath))
            {
                toastService.addToast(getMsg("PhotoScanner.folderUnconfigured"), ToastType.info);
                return;
            }

            await startScan().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellViewModel.initialize: threw: {ex}"); throw; }
        AppLogger.Trace("ShellViewModel.initialize: exit");
    }

    /// <summary>写真スキャンをバックグラウンドで開始する。すでに実行中なら何もしない。</summary>
    public async Task startScan()
    {
        AppLogger.Trace($"ShellViewModel.startScan: enter isScanningRef={isScanningRef}");
        if (DETACH_RUNTIME_DATA) return;

        // 設定だけ更新され旧データ整理が未完了の状態では、新旧フォルダを同じ slot として
        // 混在させない。次回起動または次のフォルダ操作で整理を再試行する。
        if (await settingsService.GetPendingFolderCleanupAsync().ConfigureAwait(false) is not null)
        {
            AppLogger.Warn("ShellViewModel.startScan: skip (folder cleanup pending)");
            toastService.addToast(getMsg("ShellViewModel.folderCleanupPending"), ToastType.info);
            return;
        }

        Task task;
        CancellationTokenSource cancellationSource;
        lock (activeScanGate)
        {
            if (isScanningRef || activeScanTask is { IsCompleted: false })
            {
                AppLogger.Trace("ShellViewModel.startScan: skip (already running)");
                return;
            }

            isScanningRef = true;
            ScanStatus = "scanning";
            ScanProgress = new ScanProgressDto { processed = 0, total = 0, current_world = "", phase = "scan" };
            cancellationSource = new CancellationTokenSource();
            task = Task.Run(async () =>
            {
                try
                {
                    await scanner.ScanAsync(cancellationSource.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ShellViewModel.startScan: ScanAsync wrapper threw: {ex}");
                    try { await eventBus.PublishAsync(EventNames.ScanError, getMsg("PhotoScanner.failed")).ConfigureAwait(false); }
                    catch (Exception pubEx) { AppLogger.Error($"ShellViewModel.startScan: failed to publish scan:error: {pubEx}"); }
                }
            });
            activeScanTask = task;
            activeScanCancellation = cancellationSource;
        }

        _ = task.ContinueWith(
            completed =>
            {
                CancellationTokenSource? completedCancellation = null;
                lock (activeScanGate)
                {
                    if (ReferenceEquals(activeScanTask, completed))
                    {
                        activeScanTask = null;
                        completedCancellation = activeScanCancellation;
                        activeScanCancellation = null;
                    }
                }
                completedCancellation?.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        AppLogger.Trace("ShellViewModel.startScan: exit");
    }

    /// <summary>実行中のスキャンと後続解析を中断し、旧フォルダに対する DB 書込みが終了するまで待つ。</summary>
    private async Task StopActiveScanAsync()
    {
        Task? running;
        CancellationTokenSource? scanCancellation;
        lock (activeScanGate)
        {
            running = activeScanTask is { IsCompleted: false } task ? task : null;
            scanCancellation = activeScanCancellation;
        }

        CancelPostScanOperations();
        if (running is not null)
        {
            try { scanCancellation?.Cancel(); }
            catch (ObjectDisposedException) { }
            scanner.RequestCancel();
            try { await running.ConfigureAwait(false); }
            catch (Exception ex) { AppLogger.Warn($"ShellViewModel.StopActiveScanAsync: scan completion failed: {ex.Message}"); }
        }

        // scan:completed の購読処理は activeScanTask の完了前に後続解析を登録する。
        // 主走査を待ってから再度取得することで、直前に登録された処理も漏らさず停止する。
        await StopPostScanOperationsAsync().ConfigureAwait(false);

        await dispatcherService.RunOnUiThread(() =>
        {
            isScanningRef = false;
            if (ScanStatus == "scanning")
                ScanStatus = "idle";
        }).ConfigureAwait(false);
    }

    private void CancelPostScanOperations()
    {
        CancellationTokenSource[] cancellationSources;
        lock (postScanOperationsGate)
            cancellationSources = postScanOperations.Values.ToArray();

        foreach (var cancellationSource in cancellationSources)
        {
            try { cancellationSource.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    private async Task StopPostScanOperationsAsync()
    {
        while (true)
        {
            KeyValuePair<Task, CancellationTokenSource>[] operations;
            lock (postScanOperationsGate)
                operations = postScanOperations.ToArray();
            if (operations.Length == 0)
                return;

            foreach (var operation in operations)
            {
                try { operation.Value.Cancel(); }
                catch (ObjectDisposedException) { }
            }

            try { await Task.WhenAll(operations.Select(operation => operation.Key)).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLogger.Warn($"ShellViewModel.StopPostScanOperationsAsync: completion failed: {ex.Message}"); }
        }
    }

    /// <summary>実行中スキャンへキャンセルを要求する。</summary>
    public Task cancelScan()
    {
        AppLogger.Trace("ShellViewModel.cancelScan: enter");
        if (DETACH_RUNTIME_DATA) return Task.CompletedTask;
        try
        {
            CancellationTokenSource? cancellationSource;
            lock (activeScanGate)
                cancellationSource = activeScanCancellation;
            try { cancellationSource?.Cancel(); }
            catch (ObjectDisposedException) { }
            scanner.RequestCancel();
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.cancelScan: threw: {err}");
            toastService.addToast(getMsg("ShellViewModel.scanCancelFailed"), ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.cancelScan: exit");
        return Task.CompletedTask;
    }

    /// <summary>保存済み設定を再読み込みし、Shell と子 ViewModel へ反映する。</summary>
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
                OpenWorldLinkOnPost = setting.openWorldLinkOnPost ?? false;
                ActiveTweetTemplate = setting.activeTweetTemplate ?? "";
                tweetTemplates.ReplaceAll(setting.tweetTemplates ?? Array.Empty<string>());
                templatePageViewModel.applySettings(setting.tweetTemplates, setting.activeTweetTemplate, setting.openWorldLinkOnPost ?? false);
            }).ConfigureAwait(false);
            await settingsViewModel.refreshSettings().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellViewModel.refreshSettings: threw: {ex}"); throw; }
        AppLogger.Trace("ShellViewModel.refreshSettings: exit");
    }

    /// <summary>ギャラリーのワールド候補とタグ件数を再取得する。</summary>
    private Task refreshGalleryFilterMetadata()
        => Task.WhenAll(
            galleryViewModel.loadWorldFilterOptions(),
            galleryViewModel.loadTagFilterCounts());

    /// <summary>タグを削除し、ギャラリー側のタグフィルタと写真一覧を再同期する。</summary>
    public async Task deleteTagAndRefreshGallery(string tag)
    {
        AppLogger.Trace($"ShellViewModel.deleteTagAndRefreshGallery: enter tag={tag}");
        try
        {
            var deleted = await tagMasterViewModel.tryDeleteTag(tag).ConfigureAwait(false);
            if (!deleted)
            {
                AppLogger.Trace("ShellViewModel.deleteTagAndRefreshGallery: skip refresh");
                return;
            }

            await dispatcherService.RunOnUiThread(() =>
            {
                while (galleryViewModel.filtersState.tagFilters.Remove(tag)) { }
            }).ConfigureAwait(false);
            await Task.WhenAll(
                galleryViewModel.loadTagFilterCounts(),
                galleryViewModel.photosState.loadPhotos()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ShellViewModel.deleteTagAndRefreshGallery: threw: {ex}");
            toastService.addToast(getMsg("ShellViewModel.tagRefreshFailed"), ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.deleteTagAndRefreshGallery: exit");
    }

    /// <summary>現在の全写真から、ファイル名をキーにお気に入りとタグのバックアップを作成する。</summary>
    public async Task createPhotoUserDataBackup()
    {
        AppLogger.Trace("ShellViewModel.createPhotoUserDataBackup: enter");
        try
        {
            var result = await photoService.CreatePhotoUserDataBackupAsync().ConfigureAwait(false);
            if (result.SourcePhotoCount == 0)
            {
                toastService.addToast(getMsg("ShellViewModel.photoUserDataBackupNoPhotos"), ToastType.info);
                AppLogger.Trace("ShellViewModel.createPhotoUserDataBackup: exit (no photos)");
                return;
            }

            toastService.addToast(getMsg(
                "ShellViewModel.photoUserDataBackupCreated",
                ("photoCount", result.SourcePhotoCount),
                ("fileCount", result.BackupEntryCount)));
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ShellViewModel.createPhotoUserDataBackup: threw: {ex}");
            toastService.addToast(getMsg("ShellViewModel.photoUserDataBackupFailed"), ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.createPhotoUserDataBackup: exit");
    }

    /// <summary>
    /// バックアップとファイル名が一致する全写真へお気に入りとタグを復元し、表示中データを再読込する。
    /// DB 更新後の画面再読込だけが失敗した場合は、保存済み変更を失敗扱いにせず再起動を案内する。
    /// </summary>
    public async Task restorePhotoUserDataBackup()
    {
        AppLogger.Trace("ShellViewModel.restorePhotoUserDataBackup: enter");
        try
        {
            var result = await photoService.RestorePhotoUserDataBackupAsync().ConfigureAwait(false);
            if (result.BackupEntryCount == 0)
            {
                toastService.addToast(getMsg("ShellViewModel.photoUserDataBackupMissing"), ToastType.info);
                AppLogger.Trace("ShellViewModel.restorePhotoUserDataBackup: exit (backup missing)");
                return;
            }
            if (result.MatchedPhotoCount == 0)
            {
                toastService.addToast(getMsg("ShellViewModel.photoUserDataRestoreNoMatches"), ToastType.info);
                AppLogger.Trace("ShellViewModel.restorePhotoUserDataBackup: exit (no matches)");
                return;
            }

            try
            {
                await Task.WhenAll(
                    tagMasterViewModel.loadTags(),
                    galleryViewModel.loadTagFilterCounts(),
                    galleryViewModel.photosState.loadPhotos()).ConfigureAwait(false);
            }
            catch (Exception refreshError)
            {
                AppLogger.Error($"ShellViewModel.restorePhotoUserDataBackup: refresh failed after restore: {refreshError}");
                toastService.addToast(getMsg("ShellViewModel.photoUserDataRestoreRefreshFailed"), ToastType.error);
                AppLogger.Trace("ShellViewModel.restorePhotoUserDataBackup: exit (refresh failed)");
                return;
            }

            toastService.addToast(getMsg(
                "ShellViewModel.photoUserDataRestored",
                ("photoCount", result.MatchedPhotoCount),
                ("tagCount", result.CreatedTagCount)));
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ShellViewModel.restorePhotoUserDataBackup: threw: {ex}");
            toastService.addToast(getMsg("ShellViewModel.photoUserDataRestoreFailed"), ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.restorePhotoUserDataBackup: exit");
    }

    /// <summary>
    /// 重複写真の削除中はスキャンとサムネイル生成を停止し、元ファイル・DB・表示一覧の順に同期する。
    /// フォルダ変更と同じ gate を使い、対象フォルダの差し替えと同時に実行されないようにする。
    /// </summary>
    public async Task<DuplicatePhotoDeleteResult> deleteDuplicatePhotos(
        IReadOnlyList<DuplicatePhotoDeleteTarget> targets)
    {
        AppLogger.Trace($"ShellViewModel.deleteDuplicatePhotos: enter count={targets.Count}");
        if (targets.Count == 0)
            return new DuplicatePhotoDeleteResult([], [], DatabaseUpdated: true);

        await folderMutationGate.WaitAsync().ConfigureAwait(false);
        var galleryThumbnailsSuspended = false;
        var sharedThumbnailsSuspended = false;
        try
        {
            await StopActiveScanAsync().ConfigureAwait(false);

            galleryThumbnailsSuspended = true;
            await galleryViewModel.photosState.suspendThumbnailGenerationAndWait().ConfigureAwait(false);
            sharedThumbnailsSuspended = true;
            await thumbnailWorker.SuspendOperationsAndWaitAsync().ConfigureAwait(false);

            var result = await photoService.DeleteDuplicatePhotosAsync(targets).ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
                galleryViewModel.selectionState.clearSelectedPhotos()).ConfigureAwait(false);

            thumbnailWorker.ResumeOperations();
            sharedThumbnailsSuspended = false;
            galleryViewModel.photosState.allowThumbnailReloadAfterFolderCleanup();
            galleryThumbnailsSuspended = false;

            try
            {
                await Task.WhenAll(
                    galleryViewModel.photosState.loadPhotos(),
                    refreshGalleryFilterMetadata()).ConfigureAwait(false);
            }
            catch (Exception refreshError)
            {
                AppLogger.Error($"ShellViewModel.deleteDuplicatePhotos: refresh failed after delete: {refreshError}");
                toastService.addToast(
                    getMsg("DuplicatePhotosViewModel.galleryRefreshFailed"),
                    ToastType.error);
            }

            AppLogger.Trace(
                $"ShellViewModel.deleteDuplicatePhotos: exit deleted={result.DeletedPhotos.Count} failed={result.FailedPhotos.Count}");
            return result;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ShellViewModel.deleteDuplicatePhotos: threw: {ex}");
            throw;
        }
        finally
        {
            if (sharedThumbnailsSuspended)
                thumbnailWorker.ResumeOperations();
            if (galleryThumbnailsSuspended)
                galleryViewModel.photosState.allowThumbnailReloadAfterFolderCleanup();
            folderMutationGate.Release();
        }
    }

    /// <summary>
    /// scan:completed 後の archive 解決 / orientation 計算 / phash 計算を順番に走らせる。
    /// この 3 つはすべて DB 書き込みを伴うため、同時実行せず postScanGate で直列化する。
    /// </summary>
    private async Task runPostScanWorkflow(CancellationToken ct)
    {
        try
        {
            await postScanGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                try
                {
                    var resolved = await worldService.ResolveUnknownWorldsFromArchiveAsync(ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (resolved > 0) await galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.runPostScanWorkflow archive: threw: {ex}"); }

                try { await orientationService.StartOrientationCalculationAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.runPostScanWorkflow orientation: threw: {ex}"); }

                try { await phashService.StartPdqAnalysisAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.runPostScanWorkflow phash: threw: {ex}"); }

                try
                {
                    ct.ThrowIfCancellationRequested();
                    await refreshGalleryFilterMetadata().ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    await eventBus.PublishAsync(EventNames.ScanEnrichCompleted, null).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.runPostScanWorkflow refresh: threw: {ex}"); }
            }
            finally
            {
                postScanGate.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            AppLogger.Trace("ShellViewModel.runPostScanWorkflow: cancelled");
        }
    }

    private void StartPostScanWorkflow()
    {
        var cancellationSource = new CancellationTokenSource();
        var task = Task.Run(() => runPostScanWorkflow(cancellationSource.Token));
        lock (postScanOperationsGate)
            postScanOperations.Add(task, cancellationSource);

        _ = task.ContinueWith(
            completed =>
            {
                CancellationTokenSource? removed = null;
                lock (postScanOperationsGate)
                {
                    if (postScanOperations.Remove(completed, out var operation))
                        removed = operation;
                }
                removed?.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>スキャン進捗、完了、エラーのイベント購読を登録する。</summary>
    private Task registerScanListeners()
    {
        AppLogger.Trace("ShellViewModel.registerScanListeners: enter");
        if (DETACH_RUNTIME_DATA) return Task.CompletedTask;
        try
        {
            scanUnlistenFns.Add(eventBus.Subscribe<ScanProgressDto>(EventNames.ScanProgress, payload =>
            {
                dispatcherService.requestAnimationFrame(() => ScanProgress = payload);
                return Task.CompletedTask;
            }));
            scanUnlistenFns.Add(eventBus.Subscribe<string>(EventNames.ScanWarning, payload =>
            {
                dispatcherService.requestAnimationFrame(() =>
                    toastService.addToast(payload, ToastType.info, duration: 7000));
                return Task.CompletedTask;
            }));
            scanUnlistenFns.Add(eventBus.Subscribe<string>(EventNames.ScanCancelled, payload =>
            {
                try
                {
                    var suppressNotice = IsApplyingFolderChange;
                    dispatcherService.requestAnimationFrame(() =>
                    {
                        isScanningRef = false;
                        ScanStatus = "idle";
                        if (!suppressNotice)
                            toastService.addToast(payload, ToastType.info);
                    });
                }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.scan:cancelled: threw: {ex}"); }
                return Task.CompletedTask;
            }));
            scanUnlistenFns.Add(eventBus.Subscribe(EventNames.ScanCompleted, async () =>
            {
                try
                {
                    dispatcherService.requestAnimationFrame(() => { isScanningRef = false; ScanStatus = "completed"; });
                    await refreshGalleryFilterMetadata().ConfigureAwait(false);
                    // archive → orientation → phash を直列実行する。
                    StartPostScanWorkflow();
                }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.scan:completed: threw: {ex}"); }
            }));
            scanUnlistenFns.Add(eventBus.Subscribe<string>(EventNames.ScanError, payload =>
            {
                try
                {
                    dispatcherService.requestAnimationFrame(() =>
                    {
                        isScanningRef = false;
                        ScanStatus = "error";
                        toastService.addToast(string.IsNullOrWhiteSpace(payload) ? getMsg("PhotoScanner.failed") : payload, ToastType.error);
                    });
                }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.scan:error: threw: {ex}"); }
                return Task.CompletedTask;
            }));
        }
        catch (Exception ex) { AppLogger.Error($"ShellViewModel.registerScanListeners: threw: {ex}"); throw; }
        return Task.CompletedTask;
    }

    /// <summary>PDQ と orientation 補完の進捗イベントを購読し、画面状態へ反映する。</summary>
    private async Task registerPhashWorker()
    {
        AppLogger.Trace("ShellViewModel.registerPhashWorker: enter");
        if (DETACH_RUNTIME_DATA || DETACH_AUXILIARY_RUNTIME_DATA)
        {
            PdqProgress = PhashProgressEvent.Empty; IsPdqRunning = false; CanStartWorldResolve = true;
            return;
        }
        try
        {
            var initial = await phashService.GetPhashProgressAsync().ConfigureAwait(false);
            var pending = await phashService.GetPendingPhashCountAsync().ConfigureAwait(false);
            PdqProgress = initial.total > 0 || pending == 0
                ? initial
                : initial with { done = 0, total = pending, current = null };
            IsPdqRunning = initial.total > 0 && initial.done < initial.total;
            CanStartWorldResolve = !IsPdqRunning && pending == 0;
        }
        catch (Exception ex) { AppLogger.Warn($"ShellViewModel.registerPhashWorker: initial probe failed: {ex}"); PdqProgress = PhashProgressEvent.Empty; CanStartWorldResolve = false; }
        try
        {
            phashUnlistenFns.Add(eventBus.Subscribe<PhashProgressEvent>(EventNames.PhashProgress, payload =>
            {
                IsPdqRunning = true;
                CanStartWorldResolve = false;
                var nowTicks = Environment.TickCount64;
                if (payload.done >= payload.total || nowTicks - lastPhashProgressUpdateTicks >= PhashProgressUpdateMinIntervalMs)
                {
                    lastPhashProgressUpdateTicks = nowTicks; PdqProgress = payload;
                }
                return Task.CompletedTask;
            }));
            phashUnlistenFns.Add(eventBus.Subscribe(EventNames.PhashComplete, async () =>
            {
                IsPdqRunning = false;
                // 完了トーストは「実際に処理対象があったとき」だけ出す。total==0 (保留ゼロで即完了)
                // のときは無音にして無駄な通知を避ける。PdqProgress を done=total へ更新する前に
                // total を退避しておく (更新後でも値は同じだが、判定意図を明示するため先に読む)。
                var hadWork = PdqProgress.total > 0;
                PdqProgress = PdqProgress with { done = PdqProgress.total, current = null };
                if (hadWork)
                    toastService.addToast(getMsg("ShellViewModel.phashComplete"), ToastType.success);
                // PDQ ハッシュ計算完了の通知のみを行う。ワールドの自動確定 (緩い閾値での最近接
                // 1 件の勝手採用) は誤割り当ての発生源になるため撤去した。PDQ 解決は手動の
                // WorldResolve ランキング UI (phash_confirmed) を唯一の経路とする。
                await RefreshWorldResolveAvailabilityAsync().ConfigureAwait(false);
            }));
            // 解析中断は info、それ以外の失敗は error で通知する。
            // 購読解除は phashUnlistenFns 経由で DisposeAsync が確実に行う。
            phashUnlistenFns.Add(eventBus.Subscribe<string>(EventNames.PhashError, async payload =>
            {
                try
                {
                    IsPdqRunning = false;
                    await RefreshWorldResolveAvailabilityAsync().ConfigureAwait(false);
                    var isCancelled = payload == getMsg("PhashService.cancelled");
                    var message = isCancelled ? getMsg("PhashService.cancelled") : getMsg("PhashService.failed");
                    toastService.addToast(message, isCancelled ? ToastType.info : ToastType.error);
                }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.phash_error: threw: {ex}"); }
            }));
        }
        catch (Exception ex) { AppLogger.Error($"ShellViewModel.registerPhashWorker: subscription failed: {ex}"); throw; }
    }

    /// <summary>PDQ 未計算が残っていないときだけ、手動ワールド解決を有効化する。</summary>
    private async Task RefreshWorldResolveAvailabilityAsync(CancellationToken ct = default)
    {
        try
        {
            if (DETACH_RUNTIME_DATA || DETACH_AUXILIARY_RUNTIME_DATA)
            {
                CanStartWorldResolve = true;
                return;
            }

            var pending = await phashService.GetPendingPhashCountAsync(ct).ConfigureAwait(false);
            CanStartWorldResolve = !IsPdqRunning && pending == 0;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"ShellViewModel.RefreshWorldResolveAvailabilityAsync: failed: {ex.Message}");
            CanStartWorldResolve = false;
        }
    }

    /// <summary>Shell が保持する現在設定から保存用 DTO を作る。overrides は指定項目だけ優先する。</summary>
    public AlpheratzSettingDto buildSettingPayload(AlpheratzSettingDto? overrides = null)
    {
        AppLogger.Trace("ShellViewModel.buildSettingPayload: enter");
        var currentTweetTemplates = templatePageViewModel.tweetTemplates.ToArray();
        var payload = new AlpheratzSettingDto
        {
            photoFolderPath = overrides?.photoFolderPath ?? PhotoFolderPath,
            secondaryPhotoFolderPath = overrides?.secondaryPhotoFolderPath ?? SecondaryPhotoFolderPath,
            enableStartup = overrides?.enableStartup ?? StartupEnabled,
            themeMode = overrides?.themeMode ?? ThemeMode,
            viewMode = overrides?.viewMode ?? ViewMode,
            openWorldLinkOnPost = overrides?.openWorldLinkOnPost ?? OpenWorldLinkOnPost,
            tweetTemplates = overrides?.tweetTemplates ?? currentTweetTemplates,
            activeTweetTemplate = overrides?.activeTweetTemplate ?? templatePageViewModel.ActiveTweetTemplate,
        };
        AppLogger.Trace("ShellViewModel.buildSettingPayload: exit");
        return payload;
    }

    /// <summary>
    /// 保存済みのフォルダ整理要求を再実行する。
    /// マーカーはユーザーが対象 slot のリセットを承認した記録なので、保存後に設定ファイルが
    /// 手動変更されていても旧 DB 行を残さず、対象 slot を空にしてから要求を解除する。
    /// </summary>
    private async Task<bool> ResumePendingFolderCleanupAsync()
    {
        var cleanup = await settingsService.GetPendingFolderCleanupAsync().ConfigureAwait(false);
        if (cleanup is null)
            return true;

        try
        {
            if (cleanup.SourceSlot is not (1 or 2) || string.IsNullOrWhiteSpace(cleanup.OperationId))
                throw new InvalidDataException("写真フォルダ整理要求が破損しています");

            var isCurrent = await settingsService.IsPendingFolderCleanupCurrentAsync(cleanup).ConfigureAwait(false);
            if (!isCurrent)
                AppLogger.Warn($"ShellViewModel.ResumePendingFolderCleanupAsync: path changed for operation {cleanup.OperationId}; reset approved slot");

            // 旧フォルダの生成処理が imgCache 削除後に完了すると、削除済みキャッシュを再作成する。
            // Gallery と共有ワーカーの新規要求を止め、WorldResolve を含む全生成の完了を
            // 待ってから DB とキャッシュを整理する。
            await galleryViewModel.photosState.suspendThumbnailGenerationAndWait().ConfigureAwait(false);
            await thumbnailWorker.SuspendOperationsAndWaitAsync().ConfigureAwait(false);
            await db.ResetPhotoCacheBySlotAsync(cleanup.SourceSlot).ConfigureAwait(false);
            await settingsService.ClearPendingFolderCleanupAsync(cleanup.OperationId).ConfigureAwait(false);
            thumbnailWorker.ResumeOperations();
            galleryViewModel.photosState.allowThumbnailReloadAfterFolderCleanup();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ShellViewModel.ResumePendingFolderCleanupAsync: threw: {ex}");
            toastService.addToast(getMsg("ShellViewModel.folderCleanupFailed"), ToastType.error);
            return false;
        }
    }

    /// <summary>保留中スロットの写真フォルダを変更し、設定保存後に DB キャッシュをリセットする。</summary>
    public async Task applyFolderChange(int slot, string newPath)
    {
        AppLogger.Trace($"ShellViewModel.applyFolderChange: enter slot={slot} newPath={newPath}");
        await folderMutationGate.WaitAsync().ConfigureAwait(false);
        IsApplyingFolderChange = true;
        try
        {
            if (slot is not (1 or 2))
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "source_slot は 1 または 2 である必要があります");
            if (string.IsNullOrWhiteSpace(newPath) || !Directory.Exists(newPath))
                throw new DirectoryNotFoundException($"写真フォルダが見つかりません: {newPath}");

            var currentPath = slot == 1 ? PhotoFolderPath : SecondaryPhotoFolderPath;
            var pendingCleanup = await settingsService.GetPendingFolderCleanupAsync().ConfigureAwait(false);
            if (pendingCleanup is null && AreSameFolderPath(currentPath, newPath))
            {
                ClearPendingFolderChangeIfCurrent(slot, newPath);
                AppLogger.Trace("ShellViewModel.applyFolderChange: skip (same folder)");
                return;
            }
            var otherPath = slot == 1 ? SecondaryPhotoFolderPath : PhotoFolderPath;
            if (pendingCleanup is null
                && !string.IsNullOrWhiteSpace(otherPath)
                && AppPaths.AreOverlappingDirectories(newPath, otherPath))
            {
                throw new InvalidOperationException("1st と 2nd の写真フォルダには、同じフォルダや親子関係のフォルダを設定できません");
            }

            await StopActiveScanAsync().ConfigureAwait(false);
            if (!await ResumePendingFolderCleanupAsync().ConfigureAwait(false))
                return;

            if (pendingCleanup is not null)
            {
                await refreshSettings().ConfigureAwait(false);
                currentPath = slot == 1 ? PhotoFolderPath : SecondaryPhotoFolderPath;
                if (AreSameFolderPath(currentPath, newPath))
                {
                    ClearPendingFolderChangeIfCurrent(slot, newPath);
                    await Task.WhenAll(
                        galleryViewModel.photosState.loadPhotos(),
                        tagMasterViewModel.loadTags(),
                        refreshGalleryFilterMetadata()).ConfigureAwait(false);
                    await startScan().ConfigureAwait(false);
                    toastService.addToast(getMsg("ShellViewModel.folderCleanupComplete"));
                    return;
                }
            }

            await settingsService.SaveFolderChangeAsync(slot, currentPath, newPath).ConfigureAwait(false);
            ClearPendingFolderChangeIfCurrent(slot, newPath);
            await refreshSettings().ConfigureAwait(false);

            if (!await ResumePendingFolderCleanupAsync().ConfigureAwait(false))
                return;

            await Task.WhenAll(
                galleryViewModel.photosState.loadPhotos(),
                tagMasterViewModel.loadTags(),
                refreshGalleryFilterMetadata()).ConfigureAwait(false);
            await startScan().ConfigureAwait(false);
            toastService.addToast(getMsg("ShellViewModel.folderUpdated"));
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.applyFolderChange: threw: {err}");
            var messageKey = err switch
            {
                DirectoryNotFoundException => "ShellViewModel.folderMissing",
                InvalidOperationException => "ShellViewModel.foldersOverlap",
                _ => "ShellViewModel.folderUpdateFailed",
            };
            toastService.addToast(getMsg(messageKey), ToastType.error);
        }
        finally
        {
            // 失敗時も同じ保留値を残さない。残すと同一フォルダを再選択しても
            // PropertyChanged が発火せず、確認ダイアログを再表示できなくなる。
            ClearPendingFolderChangeIfCurrent(slot, newPath);
            IsApplyingFolderChange = false;
            folderMutationGate.Release();
        }
        AppLogger.Trace("ShellViewModel.applyFolderChange: exit");
    }

    private void ClearPendingFolderChangeIfCurrent(int slot, string path)
    {
        if (PendingFolderSlot == slot
            && PendingFolderPath is { } pendingPath
            && AreSameFolderPath(pendingPath, path))
        {
            PendingFolderPath = null;
        }
    }

    private static bool AreSameFolderPath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return AppPaths.AreSameDirectory(left, right);
        }
        catch
        {
            return string.Equals(
                left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>指定スロットの写真キャッシュを削除し、設定上のフォルダパスも空にする。</summary>
    public async Task executeResetFolder(int slot, string? expectedPath = null)
    {
        AppLogger.Trace($"ShellViewModel.executeResetFolder: enter slot={slot}");
        await folderMutationGate.WaitAsync().ConfigureAwait(false);
        IsApplyingFolderChange = true;
        var currentPath = string.Empty;
        try
        {
            if (slot is not (1 or 2))
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "source_slot は 1 または 2 である必要があります");

            // 確認後に別のフォルダ操作が先に完了していた場合、古い確認内容で現在の設定を消さない。
            currentPath = slot == 1 ? PhotoFolderPath : SecondaryPhotoFolderPath;
            if (string.IsNullOrEmpty(currentPath))
                return;
            if (!string.IsNullOrWhiteSpace(expectedPath) && !AreSameFolderPath(currentPath, expectedPath))
            {
                AppLogger.Warn("ShellViewModel.executeResetFolder: skip (folder changed after confirmation)");
                return;
            }

            await StopActiveScanAsync().ConfigureAwait(false);
            if (!await ResumePendingFolderCleanupAsync().ConfigureAwait(false))
                return;

            await settingsService.SaveFolderChangeAsync(slot, currentPath, string.Empty).ConfigureAwait(false);
            PendingResetRequest = null;
            await refreshSettings().ConfigureAwait(false);

            if (!await ResumePendingFolderCleanupAsync().ConfigureAwait(false))
                return;

            await Task.WhenAll(
                galleryViewModel.photosState.loadPhotos(),
                tagMasterViewModel.loadTags(),
                refreshGalleryFilterMetadata()).ConfigureAwait(false);
            toastService.addToast(getMsg("ShellViewModel.folderReset", ("slot", slot)));
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.executeResetFolder: threw: {err}");
            toastService.addToast(getMsg("ShellViewModel.folderResetFailed"), ToastType.error);
        }
        finally
        {
            ClearPendingResetRequestIfCurrent(slot, expectedPath ?? currentPath);
            IsApplyingFolderChange = false;
            folderMutationGate.Release();
        }
        AppLogger.Trace("ShellViewModel.executeResetFolder: exit");
    }

    private void ClearPendingResetRequestIfCurrent(int slot, string path)
    {
        if (PendingResetRequest is { } request
            && request.slot == slot
            && AreSameFolderPath(request.path, path))
        {
            PendingResetRequest = null;
        }
    }

    /// <summary>フォルダ変更の確認ダイアログに必要な保留状態を設定する。</summary>
    public void promptFolderChange(int slot, string newPath)
    {
        AppLogger.Trace($"ShellViewModel.promptFolderChange: enter slot={slot} newPath={newPath}");
        if (IsApplyingFolderChange)
        {
            AppLogger.Trace("ShellViewModel.promptFolderChange: skip (folder operation running)");
            return;
        }
        PendingFolderSlot = slot; PendingFolderPath = newPath; PendingResetRequest = null;
        AppLogger.Trace("ShellViewModel.promptFolderChange: exit");
    }

    /// <summary>フォルダリセットの確認ダイアログに必要な保留状態を設定する。</summary>
    public void handleResetFolder(int slot)
    {
        AppLogger.Trace($"ShellViewModel.handleResetFolder: enter slot={slot}");
        if (IsApplyingFolderChange)
        {
            AppLogger.Trace("ShellViewModel.handleResetFolder: skip (folder operation running)");
            return;
        }
        var currentPath = slot == 1 ? PhotoFolderPath : SecondaryPhotoFolderPath;
        if (string.IsNullOrEmpty(currentPath)) return;
        PendingFolderPath = null;
        PendingResetRequest = new PendingResetRequest(slot, currentPath);
        AppLogger.Trace("ShellViewModel.handleResetFolder: exit");
    }

    /// <summary>標準表示とギャラリー表示を切り替え、設定へ保存する。</summary>
    public async Task handleToggleViewMode()
    {
        var nextMode = ViewMode == ViewMode.standard ? ViewMode.gallery : ViewMode.standard;
        await handleSetViewMode(nextMode).ConfigureAwait(false);
    }

    /// <summary>指定された表示モードを Shell とギャラリーへ反映し、設定へ保存する。</summary>
    public async Task handleSetViewMode(ViewMode nextMode)
    {
        AppLogger.Trace($"ShellViewModel.handleSetViewMode: enter ViewMode={ViewMode} next={nextMode}");
        if (ViewMode == nextMode) return;
        try
        {
            await settingsService.SaveSettingAsync(new AlpheratzSettingDto { viewMode = nextMode }).ConfigureAwait(false);
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
            toastService.addToast(getMsg("ShellViewModel.viewModeUpdateFailed"), ToastType.error);
        }
        AppLogger.Trace($"ShellViewModel.handleSetViewMode: exit ViewMode={ViewMode}");
    }

    /// <summary>テーマ設定を更新し、設定ファイルへ保存する。</summary>
    public async Task<bool> handleThemeChange(ThemeMode mode)
    {
        AppLogger.Trace($"ShellViewModel.handleThemeChange: enter mode={mode}");
        try
        {
            await settingsService.SaveSettingAsync(new AlpheratzSettingDto { themeMode = mode }).ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
            {
                ThemeMode = mode;
                settingsViewModel.ThemeMode = mode;
            }).ConfigureAwait(false);
            AppLogger.Trace("ShellViewModel.handleThemeChange: exit success");
            return true;
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.handleThemeChange: threw: {err}");
            toastService.addToast(getMsg("ShellViewModel.themeUpdateFailed"), ToastType.error);
            AppLogger.Trace("ShellViewModel.handleThemeChange: exit failure");
            return false;
        }
    }

    /// <summary>自動起動の希望値を更新し、設定ファイルと OS 側へ保存する。</summary>
    public async Task handleOpenWorldOnPostPreference(bool enabled)
    {
        AppLogger.Trace($"ShellViewModel.handleOpenWorldOnPostPreference: enter enabled={enabled}");
        try
        {
            await settingsService.SaveSettingAsync(new AlpheratzSettingDto { openWorldLinkOnPost = enabled }).ConfigureAwait(false);
            OpenWorldLinkOnPost = enabled;
            templatePageViewModel.OpenWorldLinkOnPost = enabled;
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.handleOpenWorldOnPostPreference: threw: {err}");
            toastService.addToast(getMsg("ShellViewModel.worldLinkSettingUpdateFailed"), ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.handleOpenWorldOnPostPreference: exit");
    }

    public async Task handleStartupPreference(bool enabled)
    {
        AppLogger.Trace($"ShellViewModel.handleStartupPreference: enter enabled={enabled}");
        try
        {
            await settingsService.SaveStartupPreferenceAsync(enabled).ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
            {
                StartupEnabled = enabled;
                settingsViewModel.StartupEnabled = enabled;
            }).ConfigureAwait(false);
            toastService.addToast(getMsg(enabled ? "ShellViewModel.startupEnabled" : "ShellViewModel.startupDisabled"));
        }
        catch (Exception err)
        {
            AppLogger.Error($"ShellViewModel.handleStartupPreference: threw: {err}");
            await dispatcherService.RunOnUiThread(() =>
                settingsViewModel.StartupEnabled = StartupEnabled).ConfigureAwait(false);
            toastService.addToast(getMsg("ShellViewModel.startupUpdateFailed"), ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.handleStartupPreference: exit");
    }

    /// <summary>イベント購読と子ステートを解放する。</summary>
    public async ValueTask DisposeAsync()
    {
        AppLogger.Trace($"ShellViewModel.DisposeAsync: enter scanCount={scanUnlistenFns.Count} phashCount={phashUnlistenFns.Count}");
        try { await StopActiveScanAsync().ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"ShellViewModel.DisposeAsync: stop scan threw: {ex}"); }
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
        try { await galleryViewModel.photosState.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"ShellViewModel.DisposeAsync: gallery photos dispose threw: {ex}"); }
        galleryViewModel.Cleanup();
        AppLogger.Trace("ShellViewModel.DisposeAsync: exit");
    }
}
