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

    // scan:completed の後続タスク (archive 解析 / orientation 補完 / phash 計算) を
    // 直列化するためのセマフォ。3 タスクを並列で走らせると同じ photos テーブルへの
    // UPDATE が衝突して SQLITE_BUSY が返り、結果として orientation や phash が一部行だけ
    // 反映されず欠落するパーシャル書き込みが発生する。WAL でも複数 writer は禁止のため、
    // ここで明示的に 1 つずつ走らせる。トレードオフ：スキャン後の補完が逐次なので終了が
    // やや遅いが、ユーザは UI 操作可能なので体感問題にはなりにくい。
    private readonly SemaphoreSlim postScanGate = new(1, 1);

    // phash 計算進捗の UI 更新スロットリング。生ハンドラは数十 ms ごとに発火するが、
    // UI の TextBlock 更新を毎回マーシャリングすると CPU が無駄になるため、最小間隔を 1 秒に絞る。
    private const long PhashUiUpdateMinIntervalMs = 1000;
    private long lastPhashUiUpdateTicks;

    public GalleryViewModel galleryViewModel { get; }
    public SettingsViewModel settingsViewModel { get; }
    public TagMasterViewModel tagMasterViewModel { get; }
    public TemplatePageViewModel templatePageViewModel { get; }

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
    public string ActiveTweetTemplate { get => activeTweetTemplate; set => SetProperty(ref activeTweetTemplate, value); }

    public ShellViewModel(
        SettingsService settingsService, AlpheratzDb db, PhotoScanner scanner,
        PhashService phashService, OrientationService orientationService, WorldService worldService,
        LocalEventBus eventBus, ToastService toastService, GalleryViewModel galleryViewModel,
        SettingsViewModel settingsViewModel, TagMasterViewModel tagMasterViewModel,
        TemplatePageViewModel templatePageViewModel, PhotoModalState photoModalState, DispatcherService dispatcherService,
        ThumbnailWorker thumbnailWorker)
    {
        AppLogger.Trace("ShellViewModel.ctor: enter");
        this.settingsService = settingsService; this.db = db;
        this.scanner = scanner; this.phashService = phashService; this.orientationService = orientationService;
        this.worldService = worldService; this.eventBus = eventBus; this.toastService = toastService;
        this.galleryViewModel = galleryViewModel; this.settingsViewModel = settingsViewModel;
        this.tagMasterViewModel = tagMasterViewModel; this.templatePageViewModel = templatePageViewModel;
        this.photoModalState = photoModalState; this.dispatcherService = dispatcherService;
        this.thumbnailWorker = thumbnailWorker;
        AppLogger.Trace("ShellViewModel.ctor: exit");
    }

    /// <summary>ワールド解決モーダル用の ViewModel を現在のサービス構成から作成する。</summary>
    public WorldResolve.WorldResolveViewModel CreateWorldResolveViewModel()
        => new(db, thumbnailWorker, toastService);

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
                toastService.addToast("写真フォルダが未設定です。設定から参照フォルダを選択してください。", ToastType.info);
                return;
            }

            await startScan().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellViewModel.initialize: threw: {ex}"); throw; }
        AppLogger.Trace("ShellViewModel.initialize: exit");
    }

    /// <summary>写真スキャンをバックグラウンドで開始する。すでに実行中なら何もしない。</summary>
    public Task startScan()
    {
        AppLogger.Trace($"ShellViewModel.startScan: enter isScanningRef={isScanningRef}");
        if (DETACH_RUNTIME_DATA || isScanningRef) return Task.CompletedTask;
        isScanningRef = true; ScanStatus = "scanning";
        ScanProgress = new ScanProgressDto { processed = 0, total = 0, current_world = "", phase = "scan" };
        try
        {
            // バックグラウンド起動時の例外も scan:error として通知できるよう、
            // Task.Run の内側で ScanAsync 全体を try/catch する。
            _ = Task.Run(async () =>
            {
                try
                {
                    await scanner.ScanAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ShellViewModel.startScan: ScanAsync wrapper threw: {ex}");
                    try { await eventBus.PublishAsync(EventNames.ScanError, ex.Message).ConfigureAwait(false); }
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

    /// <summary>実行中スキャンへキャンセルを要求する。</summary>
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
                ActiveTweetTemplate = setting.activeTweetTemplate ?? "";
                tweetTemplates.ReplaceAll(setting.tweetTemplates ?? Array.Empty<string>());
                templatePageViewModel.applySettings(setting.tweetTemplates, setting.activeTweetTemplate);
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
            toastService.addToast($"タグ削除後のギャラリー更新に失敗しました: {ex}", ToastType.error);
        }
        AppLogger.Trace("ShellViewModel.deleteTagAndRefreshGallery: exit");
    }

    /// <summary>
    /// scan:completed 後の archive 解決 / orientation 計算 / phash 計算を順番に走らせる。
    /// この 3 つはすべて DB 書き込みを伴うため、同時実行せず postScanGate で直列化する。
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

            try
            {
                await refreshGalleryFilterMetadata().ConfigureAwait(false);
                await eventBus.PublishAsync(EventNames.ScanEnrichCompleted, null).ConfigureAwait(false);
            }
            catch (Exception ex) { AppLogger.Error($"ShellViewModel.runPostScanWorkflow refresh: threw: {ex}"); }
        }
        finally
        {
            postScanGate.Release();
        }
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
            scanUnlistenFns.Add(eventBus.Subscribe(EventNames.ScanCompleted, async () =>
            {
                try
                {
                    dispatcherService.requestAnimationFrame(() => { isScanningRef = false; ScanStatus = "completed"; });
                    await refreshGalleryFilterMetadata().ConfigureAwait(false);
                    // archive → orientation → phash を直列実行する。
                    _ = Task.Run(runPostScanWorkflow);
                }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.scan:completed: threw: {ex}"); }
            }));
            scanUnlistenFns.Add(eventBus.Subscribe<string>(EventNames.ScanError, payload =>
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

    /// <summary>PDQ と orientation 補完の進捗イベントを購読し、画面状態へ反映する。</summary>
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
            phashUnlistenFns.Add(eventBus.Subscribe<PhashProgressEvent>(EventNames.PhashProgress, payload =>
            {
                IsPdqRunning = true;
                var nowTicks = Environment.TickCount64;
                if (payload.done >= payload.total || nowTicks - lastPhashUiUpdateTicks >= PhashUiUpdateMinIntervalMs)
                {
                    lastPhashUiUpdateTicks = nowTicks; PdqProgress = payload;
                }
                return Task.CompletedTask;
            }));
            phashUnlistenFns.Add(eventBus.Subscribe(EventNames.PhashComplete, () =>
            {
                IsPdqRunning = false;
                // 完了トーストは「実際に処理対象があったとき」だけ出す。total==0 (保留ゼロで即完了)
                // のときは無音にして無駄な通知を避ける。PdqProgress を done=total へ更新する前に
                // total を退避しておく (更新後でも値は同じだが、判定意図を明示するため先に読む)。
                var hadWork = PdqProgress.total > 0;
                PdqProgress = PdqProgress with { done = PdqProgress.total, current = null };
                if (hadWork)
                    toastService.addToast("類似画像の解析が完了しました", ToastType.success);
                // PDQ ハッシュ計算完了の通知のみを行う。ワールドの自動確定 (緩い閾値での最近接
                // 1 件の勝手採用) は誤割り当ての発生源になるため撤去した。PDQ 解決は手動の
                // WorldResolve ランキング UI (phash_confirmed) を唯一の経路とする。
                return Task.CompletedTask;
            }));
            // phash_error は従来購読者が無く silent だった。中断 (payload="中断されました") は info、
            // それ以外の失敗は error でトースト化する。購読解除は phashUnlistenFns 経由で
            // DisposeAsync が確実に行う。
            phashUnlistenFns.Add(eventBus.Subscribe<string>(EventNames.PhashError, payload =>
            {
                try
                {
                    IsPdqRunning = false;
                    var isCancelled = payload == "中断されました";
                    var message = isCancelled
                        ? "類似画像の解析を中断しました"
                        : (string.IsNullOrWhiteSpace(payload) ? "類似画像の解析に失敗しました。" : $"類似画像の解析に失敗しました: {payload}");
                    toastService.addToast(message, isCancelled ? ToastType.info : ToastType.error);
                }
                catch (Exception ex) { AppLogger.Error($"ShellViewModel.phash_error: threw: {ex}"); }
                return Task.CompletedTask;
            }));
        }
        catch (Exception ex) { AppLogger.Error($"ShellViewModel.registerPhashWorker: subscription failed: {ex}"); throw; }
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
            tweetTemplates = overrides?.tweetTemplates ?? currentTweetTemplates,
            activeTweetTemplate = overrides?.activeTweetTemplate ?? templatePageViewModel.ActiveTweetTemplate,
        };
        AppLogger.Trace("ShellViewModel.buildSettingPayload: exit");
        return payload;
    }

    /// <summary>保留中スロットの写真フォルダを変更し、設定保存と DB キャッシュリセットを行う。</summary>
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
            await refreshGalleryFilterMetadata().ConfigureAwait(false);
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

    /// <summary>指定スロットの写真キャッシュを削除し、設定上のフォルダパスも空にする。</summary>
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
            await refreshGalleryFilterMetadata().ConfigureAwait(false);
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

    /// <summary>フォルダ変更の確認ダイアログに必要な保留状態を設定する。</summary>
    public void promptFolderChange(int slot, string newPath)
    {
        AppLogger.Trace($"ShellViewModel.promptFolderChange: enter slot={slot} newPath={newPath}");
        PendingFolderSlot = slot; PendingFolderPath = newPath; PendingResetRequest = null;
        AppLogger.Trace("ShellViewModel.promptFolderChange: exit");
    }

    /// <summary>フォルダリセットの確認ダイアログに必要な保留状態を設定する。</summary>
    public void handleResetFolder(int slot)
    {
        AppLogger.Trace($"ShellViewModel.handleResetFolder: enter slot={slot}");
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

    /// <summary>テーマ設定を更新し、設定ファイルへ保存する。</summary>
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

    /// <summary>自動起動の希望値を更新し、設定ファイルと OS 側へ保存する。</summary>
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

    /// <summary>イベント購読と子ステートを解放する。</summary>
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
