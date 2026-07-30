using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging;
using Alpheratz.Core.Scanner;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Features.Settings;
using Alpheratz.Features.Shell;
using Alpheratz.Features.TagMaster;
using Alpheratz.Features.Template;
using Alpheratz.Messages;
using Alpheratz.Models;
using Alpheratz.Models.Events;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;

namespace Alpheratz.Tests;

/// <summary>
/// ShellViewModel がアプリ全体の状態を束ねる境界を検証するテスト。
///
/// ShellViewModel は設定、ギャラリー、タグ、テンプレート、スキャン進捗、PDQ 進捗をまとめて扱う。
/// 画面や WinUI Dispatcher を使わず、一時 DB と一時設定ディレクトリだけを使って、
/// 起動時初期化、設定保存、イベント購読、フォルダ状態、写真モーダル生成の現在仕様を固定する。
/// </summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class ShellViewModelBehaviorTests
{
    /// <summary>
    /// initialize が保存済み設定を Shell と子 ViewModel へ反映し、
    /// ScanProgress / PhashProgress / PhashComplete / PhashError の購読も登録することを確認する。
    ///
    /// 写真フォルダが未設定の場合、initialize は自動スキャンを開始せず info toast を出して終了する。
    /// この状態ならバックグラウンドスキャンを走らせずに、起動直後の設定反映とイベントバス経由の
    /// ステータス更新を安全に検証できる。
    /// </summary>
    [Fact]
    public async Task Initialize_LoadsSettingsAndRespondsToProgressEvents()
    {
        await using var harness = ShellTestHarness.Create();
        harness.Config.SaveSetting(new AlpheratzSetting
        {
            PhotoFolderPath = "",
            SecondaryPhotoFolderPath = "",
            ThemeMode = "dark",
            ViewMode = "gallery",
            EnableStartup = true,
            OpenWorldLinkOnPost = true,
            TweetTemplates = ["A", "B"],
            ActiveTweetTemplate = "B",
        });

        await harness.ViewModel.initialize();
        await harness.EventBus.PublishAsync(EventNames.ScanProgress, new ScanProgressDto
        {
            processed = 3,
            total = 7,
            current_world = "World",
            phase = "scan",
        });
        await harness.EventBus.PublishAsync(EventNames.PhashProgress, new PhashProgressEvent
        {
            done = 2,
            total = 5,
            current = "a.jpg",
        });
        await harness.EventBus.PublishAsync(EventNames.PhashComplete, new object());
        await harness.EventBus.PublishAsync(EventNames.PhashError, "broken");

        Assert.Equal(ThemeMode.dark, harness.ViewModel.ThemeMode);
        Assert.Equal(ViewMode.gallery, harness.ViewModel.ViewMode);
        Assert.Equal(ViewMode.gallery, harness.ViewModel.galleryViewModel.displayState.ViewMode);
        Assert.True(harness.ViewModel.StartupEnabled);
        Assert.True(harness.ViewModel.OpenWorldLinkOnPost);
        Assert.Equal(["A", "B"], harness.ViewModel.tweetTemplates.ToArray());
        Assert.Equal(["A", "B"], harness.ViewModel.templatePageViewModel.tweetTemplates.ToArray());
        Assert.Equal("B", harness.ViewModel.ActiveTweetTemplate);
        Assert.Equal("B", harness.ViewModel.templatePageViewModel.ActiveTweetTemplate);
        Assert.True(harness.ViewModel.templatePageViewModel.OpenWorldLinkOnPost);
        Assert.Equal(3, harness.ViewModel.ScanProgress.processed);
        Assert.Equal(7, harness.ViewModel.ScanProgress.total);
        Assert.False(harness.ViewModel.IsPdqRunning);
        Assert.True(harness.ViewModel.CanStartWorldResolve);
        Assert.Equal(5, harness.ViewModel.PdqProgress.done);
        Assert.Contains(
            harness.ToastService.toasts,
            toast => toast.Msg == MessageCatalog.getMsg("PhotoScanner.folderUnconfigured"));
        Assert.Contains(harness.ToastService.toasts, toast => toast.Msg.Contains("類似画像の解析が完了しました"));
        Assert.Contains(
            harness.ToastService.toasts,
            toast => toast.Msg == MessageCatalog.getMsg("PhashService.failed"));
    }

    /// <summary>
    /// 未計算 phash が残っている間は、手動ワールド解決を開始できないことを確認する。
    /// </summary>
    [Fact]
    public async Task WorldResolveAvailability_RequiresCompletedPhashAnalysis()
    {
        await using var harness = ShellTestHarness.Create();
        await harness.Db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = "/photo/pending.jpg",
            PhotoFilename = "pending.jpg",
            Timestamp = "2026-06-05 10:00:00",
            SourceSlot = 1,
        });

        await harness.ViewModel.initialize();
        Assert.False(harness.ViewModel.CanStartWorldResolve);
        Assert.Equal(1, harness.ViewModel.PdqProgress.total);
        Assert.Equal(0, harness.ViewModel.PdqProgress.done);

        await harness.Db.UpdatePhotoPhashAsync("/photo/pending.jpg", new string('0', 64));
        await harness.EventBus.PublishAsync(EventNames.PhashComplete, new object());

        Assert.True(harness.ViewModel.CanStartWorldResolve);
    }

    /// <summary>
    /// buildSettingPayload と表示/テーマ変更が、現在の Shell 状態と上書き値を正しく合成して保存することを確認する。
    ///
    /// ShellViewModel は SettingsViewModel とは別に、現在の写真フォルダ、表示モード、テンプレートを保持している。
    /// 保存時に古い子 ViewModel の値や null 上書きが混ざると設定ファイルが不整合になるため、
    /// payload の合成、viewMode 変更時のグルーピング解除、handleToggleViewMode による往復をまとめて検証する。
    /// </summary>
    [Fact]
    public async Task SettingPayloadAndViewModeHandlers_PersistShellState()
    {
        await using var harness = ShellTestHarness.Create();
        harness.ViewModel.PhotoFolderPath = "F:/photos/primary";
        harness.ViewModel.SecondaryPhotoFolderPath = "F:/photos/secondary";
        harness.ViewModel.StartupEnabled = true;
        harness.ViewModel.ViewMode = ViewMode.standard;
        harness.ViewModel.OpenWorldLinkOnPost = true;
        harness.ViewModel.templatePageViewModel.tweetTemplates.ReplaceAll(["{world}", "{tags}"]);
        harness.ViewModel.templatePageViewModel.ActiveTweetTemplate = "{tags}";

        var payload = harness.ViewModel.buildSettingPayload(new AlpheratzSettingDto
        {
            themeMode = ThemeMode.dark,
        });
        await harness.ViewModel.handleOpenWorldOnPostPreference(true);
        harness.ViewModel.galleryViewModel.filtersState.GroupingMode = GroupingMode.world;
        await harness.ViewModel.handleSetViewMode(ViewMode.gallery);
        await harness.ViewModel.handleToggleViewMode();
        await harness.ViewModel.handleThemeChange(ThemeMode.dark);
        var saved = harness.Config.LoadSetting();

        Assert.Equal("F:/photos/primary", payload.photoFolderPath);
        Assert.Equal("F:/photos/secondary", payload.secondaryPhotoFolderPath);
        Assert.Equal(ThemeMode.dark, payload.themeMode);
        Assert.Equal(ViewMode.standard, payload.viewMode);
        Assert.True(payload.openWorldLinkOnPost);
        Assert.Equal(["{world}", "{tags}"], payload.tweetTemplates);
        Assert.Equal("{tags}", payload.activeTweetTemplate);
        Assert.Equal(GroupingMode.none, harness.ViewModel.galleryViewModel.filtersState.GroupingMode);
        Assert.Equal(ViewMode.standard, harness.ViewModel.ViewMode);
        Assert.Equal(ViewMode.standard, harness.ViewModel.galleryViewModel.displayState.ViewMode);
        Assert.Equal("dark", saved.ThemeMode);
        Assert.Equal("standard", saved.ViewMode);
        Assert.True(saved.OpenWorldLinkOnPost);
    }

    /// <summary>
    /// フォルダ変更・リセット関連の保留状態と、executeResetFolder の永続化を確認する。
    ///
    /// フォルダ変更はユーザー確認を挟むため、promptFolderChange は「どのスロットをどのパスに変えるか」だけを保持する。
    /// 一方 executeResetFolder は設定ファイル側のパスを空にして整理要求を保存してから、
    /// DB キャッシュを削除して整理要求を解除する。
    /// ここでは secondary が残っていても primary リセットだけで後続スキャンを起動しないことを検証する。
    /// </summary>
    [Fact]
    public async Task FolderPromptAndReset_UpdatePendingStateSettingsAndDatabase()
    {
        await using var harness = ShellTestHarness.Create();
        var secondaryFolder = Path.Combine(harness.TempDir, "secondary");
        Directory.CreateDirectory(secondaryFolder);
        harness.Config.SaveSetting(new AlpheratzSetting
        {
            PhotoFolderPath = "F:/old-primary",
            SecondaryPhotoFolderPath = secondaryFolder,
        });
        await harness.Db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = "/slot1/a.jpg",
            PhotoFilename = "a.jpg",
            Timestamp = "2026-06-05 10:00:00",
            SourceSlot = 1,
        });
        await harness.ViewModel.refreshSettings();

        harness.ViewModel.promptFolderChange(2, "F:/new-secondary");
        Assert.Equal(2, harness.ViewModel.PendingFolderSlot);
        Assert.Equal("F:/new-secondary", harness.ViewModel.PendingFolderPath);

        harness.ViewModel.handleResetFolder(1);
        Assert.Null(harness.ViewModel.PendingFolderPath);
        Assert.Equal(1, harness.ViewModel.PendingResetRequest?.slot);
        Assert.Equal("F:/old-primary", harness.ViewModel.PendingResetRequest?.path);

        await harness.ViewModel.executeResetFolder(1);
        var saved = harness.Config.LoadSetting();
        var deletedPhoto = await harness.Db.GetPhotoRecordAsync("/slot1/a.jpg");

        Assert.False(harness.ViewModel.IsApplyingFolderChange);
        Assert.Null(harness.ViewModel.PendingResetRequest);
        Assert.Equal("", saved.PhotoFolderPath);
        Assert.Equal(secondaryFolder, saved.SecondaryPhotoFolderPath);
        Assert.Null(saved.PendingFolderCleanup);
        Assert.Equal("", harness.ViewModel.PhotoFolderPath);
        Assert.Equal("idle", harness.ViewModel.ScanStatus);
        Assert.Null(deletedPhoto);
        Assert.Contains(harness.ToastService.toasts, toast => toast.Msg.Contains("リセットしました"));
    }

    /// <summary>
    /// createPhotoModalViewModel が、現在の表示写真リストまたは指定リストを PhotoModalState に渡してから
    /// PhotoModalViewModel を返すことを確認する。
    ///
    /// 写真詳細モーダルの前後移動は、開いた瞬間に state へ渡された写真リストに依存する。
    /// null 写真ではモーダルを作らず、通常表示から開いた場合とドリルダウン表示から開いた場合の
    /// どちらも SelectedPhoto と前後移動可否が期待どおりになることを検証する。
    /// </summary>
    [Fact]
    public void CreatePhotoModalViewModel_SeedsModalStateFromGalleryOrProvidedList()
    {
        using var harness = ShellTestHarness.Create();
        var first = Thumb("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00");
        var second = Thumb("/photo/b.jpg", "b.jpg", "2026-06-05 11:00:00");
        harness.ViewModel.galleryViewModel.photosState.setPhotos([first, second], autoGenerateThumbnails: false);
        harness.ViewModel.galleryViewModel.photosState.rebuildDisplayItems(GroupingMode.none);

        var fromGallery = harness.ViewModel.createPhotoModalViewModel(first);
        fromGallery?.state.goNextPhoto();
        var fromList = harness.ViewModel.createPhotoModalViewModelFromList(second, [second, first]);
        var nullViewModel = harness.ViewModel.createPhotoModalViewModel(null);

        Assert.NotNull(fromGallery);
        Assert.Same(second, fromGallery.state.SelectedPhoto);
        Assert.NotNull(fromList);
        Assert.Same(second, fromList.state.SelectedPhoto);
        Assert.True(fromList.state.CanGoNext);
        Assert.Null(nullViewModel);
    }

    /// <summary>
    /// タグ削除後にギャラリー側のタグフィルタとタグ件数が再同期されることを確認する。
    ///
    /// タグマスタでタグを削除した場合、DB のタグ行だけでなく現在適用中のギャラリーフィルタからも
    /// 同じタグを取り除く必要がある。残ったフィルタで loadPhotos すると「削除済みタグで絞り込み中」の
    /// 空表示になってしまうため、deleteTagAndRefreshGallery の成功分岐を固定する。
    /// </summary>
    [Fact]
    public async Task DeleteTagAndRefreshGallery_RemovesDeletedTagFromActiveFilters()
    {
        await using var harness = ShellTestHarness.Create();
        await harness.Db.CreateTagMasterAsync("night");
        await harness.Db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = "/photo/a.jpg",
            PhotoFilename = "a.jpg",
            Timestamp = "2026-06-05 10:00:00",
            SourceSlot = 1,
        });
        await harness.Db.AddPhotoTagAsync("/photo/a.jpg", "night");
        harness.ViewModel.galleryViewModel.filtersState.tagFilters.Add("night");

        await harness.ViewModel.deleteTagAndRefreshGallery("night");
        var tags = await harness.Db.GetAllTagsAsync();
        var photoTags = await harness.Db.GetPhotoTagsAsync("/photo/a.jpg");

        Assert.Empty(tags);
        Assert.Empty(photoTags);
        Assert.Empty(harness.ViewModel.galleryViewModel.filtersState.tagFilters);
        Assert.DoesNotContain("night", harness.ViewModel.galleryViewModel.filtersState.TagFilterCounts.Keys);
        Assert.Contains(
            harness.ToastService.toasts,
            toast => toast.Msg == MessageCatalog.getMsg("TagMasterViewModel.tagDeleted"));
    }

    /// <summary>
    /// scan:completed 受信後に ShellViewModel がスキャン状態を完了へ戻し、後続補完ワークフローを起動することを確認する。
    ///
    /// scan:completed のハンドラは UI 状態更新後、archive 解決、orientation 補完、phash 計算、フィルタ再取得を
    /// 直列ワークフローとして Task.Run で起動する。
    /// テストでは pending の無い一時 DB を使い、最終的に scan:enrich_completed が発行されることを待って確認する。
    /// </summary>
    [Fact]
    public async Task ScanCompletedEvent_UpdatesStatusAndPublishesEnrichCompleted()
    {
        await using var harness = ShellTestHarness.Create();
        var enrichCount = 0;
        await using var enrichSub = harness.EventBus.Subscribe(EventNames.ScanEnrichCompleted, () =>
        {
            enrichCount++;
            return Task.CompletedTask;
        });
        await harness.ViewModel.initialize();

        await harness.EventBus.PublishAsync(EventNames.ScanCompleted, new object());
        for (var i = 0; i < 30 && enrichCount == 0; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal("completed", harness.ViewModel.ScanStatus);
        Assert.True(enrichCount > 0);
    }

    /// <summary>
    /// applyFolderChange が保留中スロットの設定を先に保存し、DB キャッシュ整理・再読込・ギャラリー再取得を行うことを確認する。
    ///
    /// フォルダ変更の確定時は、新しいパスと再実行可能な整理要求を保存してから古いスロットを消す。
    /// 空の一時フォルダを新しい写真フォルダとして使い、既存DB行が削除され、Shell の PhotoFolderPath が
    /// 新パスへ更新されることを検証する。
    /// </summary>
    [Fact]
    public async Task ApplyFolderChange_ResetsSlotSavesSettingsAndReloadsShellState()
    {
        await using var harness = ShellTestHarness.Create();
        var newFolder = Path.Combine(harness.TempDir, "new-photos");
        Directory.CreateDirectory(newFolder);
        harness.Config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = "F:/old-primary" });
        await harness.ViewModel.refreshSettings();
        harness.ViewModel.PendingFolderSlot = 1;
        harness.ViewModel.PendingFolderPath = newFolder;
        await harness.Db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = "/slot1/a.jpg",
            PhotoFilename = "a.jpg",
            Timestamp = "2026-06-05 10:00:00",
            SourceSlot = 1,
        });

        await harness.ViewModel.applyFolderChange(1, newFolder);
        var saved = harness.Config.LoadSetting();
        var deletedPhoto = await harness.Db.GetPhotoRecordAsync("/slot1/a.jpg");

        Assert.False(harness.ViewModel.IsApplyingFolderChange);
        Assert.Null(harness.ViewModel.PendingFolderPath);
        Assert.Equal(newFolder, saved.PhotoFolderPath);
        Assert.Null(saved.PendingFolderCleanup);
        Assert.Equal(newFolder, harness.ViewModel.PhotoFolderPath);
        Assert.Null(deletedPhoto);
        Assert.Contains(
            harness.ToastService.toasts,
            toast => toast.Msg == MessageCatalog.getMsg("ShellViewModel.folderUpdated"));
    }

    /// <summary>同じ実フォルダを別表記で再選択しても、写真メタデータをリセットしないことを確認する。</summary>
    [Fact]
    public async Task ApplyFolderChange_SameFolderIsNoOp()
    {
        await using var harness = ShellTestHarness.Create();
        var photoFolder = Path.Combine(harness.TempDir, "same-photos");
        Directory.CreateDirectory(photoFolder);
        harness.Config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = photoFolder });
        await harness.ViewModel.refreshSettings();
        harness.ViewModel.PendingFolderSlot = 1;
        harness.ViewModel.PendingFolderPath = Path.Combine(photoFolder, ".");
        await harness.Db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = "/slot1/keep.jpg",
            PhotoFilename = "keep.jpg",
            Timestamp = "2026-06-05 10:00:00",
            SourceSlot = 1,
        });

        await harness.ViewModel.applyFolderChange(1, Path.Combine(photoFolder, "."));

        Assert.NotNull(await harness.Db.GetPhotoRecordAsync("/slot1/keep.jpg"));
        Assert.Equal(photoFolder, harness.Config.LoadSetting().PhotoFolderPath);
        Assert.Null(harness.Config.LoadSetting().PendingFolderCleanup);
        Assert.Null(harness.ViewModel.PendingFolderPath);
    }

    /// <summary>前回終了時に残ったフォルダ整理要求を、ギャラリー初期化前に再実行することを確認する。</summary>
    [Fact]
    public async Task Initialize_ResumesPendingFolderCleanupBeforeLoadingGallery()
    {
        await using var harness = ShellTestHarness.Create();
        harness.Config.SaveSetting(new AlpheratzSetting
        {
            PhotoFolderPath = "",
            PendingFolderCleanup = new PendingFolderCleanupSetting
            {
                OperationId = "pending-operation",
                SourceSlot = 1,
                CommittedPath = "",
            },
        });
        await harness.Db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = "/slot1/stale.jpg",
            PhotoFilename = "stale.jpg",
            Timestamp = "2026-06-05 10:00:00",
            SourceSlot = 1,
        });

        await harness.ViewModel.initialize();

        Assert.Null(await harness.Db.GetPhotoRecordAsync("/slot1/stale.jpg"));
        Assert.Null(harness.Config.LoadSetting().PendingFolderCleanup);
        Assert.Empty(harness.ViewModel.galleryViewModel.photosState.photos);
    }

    /// <summary>整理マーカー保存後に設定パスが変わっていても、承認済み slot の旧DB行を残さないことを確認する。</summary>
    [Fact]
    public async Task Initialize_StaleFolderCleanupMarkerStillResetsApprovedSlot()
    {
        await using var harness = ShellTestHarness.Create();
        harness.Config.SaveSetting(new AlpheratzSetting
        {
            PhotoFolderPath = string.Empty,
            PendingFolderCleanup = new PendingFolderCleanupSetting
            {
                OperationId = "stale-operation",
                SourceSlot = 1,
                CommittedPath = "F:/path-saved-before-manual-edit",
            },
        });
        await harness.Db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = "/slot1/stale.jpg",
            PhotoFilename = "stale.jpg",
            Timestamp = "2026-06-05 10:00:00",
            SourceSlot = 1,
        });

        await harness.ViewModel.initialize();

        Assert.Null(await harness.Db.GetPhotoRecordAsync("/slot1/stale.jpg"));
        Assert.Null(harness.Config.LoadSetting().PendingFolderCleanup);
        Assert.Empty(harness.ViewModel.galleryViewModel.photosState.photos);
    }

    /// <summary>
    /// テストで使う ShellViewModel と依存サービス一式を組み立てる補助クラス。
    ///
    /// 本体と同じ具象サービスを使うが、AppConfig と AlpheratzDb は一時ディレクトリへ向ける。
    /// DispatcherService は DispatcherQueue なしで作り、UI スレッドへ送る処理を同期実行させる。
    /// </summary>
    private sealed class ShellTestHarness : IDisposable, IAsyncDisposable
    {
        private readonly string tempDir;

        public AlpheratzDb Db { get; }
        public AppConfig Config { get; }
        public LocalEventBus EventBus { get; } = new();
        public ToastService ToastService { get; } = new();
        public ShellViewModel ViewModel { get; }
        public string TempDir => tempDir;

        /// <summary>一時DB・設定・サービスを作成し、ShellViewModel を構築する。</summary>
        private ShellTestHarness(string tempDir)
        {
            this.tempDir = tempDir;
            Directory.CreateDirectory(tempDir);
            Db = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
            Db.Initialize();
            Config = new AppConfig(Path.Combine(tempDir, "settings"));

            var settingsService = new SettingsService(Config);
            var photoService = new PhotoService(Db);
            var scanner = new PhotoScanner(Config, Db, EventBus);
            var worldService = new WorldService(Db, scanner);
            var phashService = new PhashService(Db, EventBus);
            var orientationService = new OrientationService(Db, EventBus);
            var dispatcherService = new DispatcherService();
            var thumbnailWorker = new ThumbnailWorker(new ThumbnailService());
            var photosState = new GalleryPhotosState(photoService, EventBus, ToastService, dispatcherService, thumbnailWorker);
            var filtersState = new GalleryFiltersState();
            var selectionState = new GallerySelectionState(photoService, ToastService);
            var displayState = new GalleryDisplayState();
            var scrollState = new GalleryScrollState();
            var galleryViewModel = new GalleryViewModel(
                photoService,
                worldService,
                phashService,
                ToastService,
                dispatcherService,
                photosState,
                filtersState,
                selectionState,
                displayState,
                scrollState);
            var settingsViewModel = new SettingsViewModel(settingsService, new DialogService());
            var tagMasterViewModel = new TagMasterViewModel(Db, ToastService);
            var templatePageViewModel = new TemplatePageViewModel(settingsService, worldService, ToastService);
            var photoModalState = new PhotoModalState(photoService, ToastService);

            ViewModel = new ShellViewModel(
                settingsService,
                Db,
                scanner,
                phashService,
                orientationService,
                worldService,
                EventBus,
                ToastService,
                galleryViewModel,
                settingsViewModel,
                tagMasterViewModel,
                templatePageViewModel,
                photoModalState,
                dispatcherService,
                thumbnailWorker);
        }

        /// <summary>一意の一時ディレクトリを持つ Harness を作成する。</summary>
        public static ShellTestHarness Create()
            => new(Path.Combine(Path.GetTempPath(), "Alpheratz.ShellViewModel.Tests", Guid.NewGuid().ToString("N")));

        /// <summary>同期 using 向けに非同期破棄を待ち、選択ステートの購読も解除する。</summary>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <summary>イベント購読を解除し、一時ディレクトリを削除する。</summary>
        public async ValueTask DisposeAsync()
        {
            try { await ViewModel.DisposeAsync(); }
            catch { }
            try { ViewModel.galleryViewModel.selectionState.Dispose(); }
            catch { }
            try { await ViewModel.galleryViewModel.photosState.DisposeAsync(); }
            catch { }
            try { Directory.Delete(tempDir, recursive: true); }
            catch { }
        }
    }

    /// <summary>
    /// PhotoModal と Gallery 表示用の最小 PhotoThumbnailItem を作る。
    /// ここではファイル実体を使わず、ViewModel が参照するパス・ファイル名・日時だけを固定する。
    /// </summary>
    private static PhotoThumbnailItem Thumb(string path, string filename, string timestamp)
        => new()
        {
            PhotoPath = path,
            PhotoFilename = filename,
            Timestamp = timestamp,
            SourceSlot = 1,
            Tags = [],
        };
}
