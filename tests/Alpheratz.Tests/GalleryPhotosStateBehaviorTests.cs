using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging;
using Alpheratz.Core.Scanner;
using Alpheratz.Features.Gallery;
using Alpheratz.Models.Events;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Alpheratz.Tests;

/// <summary>
/// GalleryPhotosState の DB ロードと表示用コレクション構築を検証するテスト。
///
/// GalleryPhotosState はギャラリーのデータ面の中心で、PhotoService から取得した DTO を
/// PhotoThumbnailItem と PhotoGridItem に変換し、月ナビゲーションやグループ表示もここで構築する。
/// 画面コントロールを起動せず、実 SQLite DB と即時実行 DispatcherService を使って、
/// ユーザーに見える写真一覧の並びとグルーピングの現在仕様を固定する。
/// </summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class GalleryPhotosStateBehaviorTests : IDisposable
{
    private readonly string tempDir;
    private readonly AlpheratzDb db;
    private readonly GalleryPhotosState state;
    private const string QueueChildMarker = "ALPHERATZ_GALLERY_UI_QUEUE_TEST_CHILD";

    /// <summary>
    /// 一時DBと GalleryPhotosState を作成する。
    /// ThumbnailWorker は loadPhotos から fire-and-forget で呼ばれるが、
    /// テストでは実画像を用意しないため個別生成失敗はワーカー内でスキップされる。
    /// DispatcherService は null Dispatcher で生成し、UI 更新を同期実行させる。
    /// </summary>
    public GalleryPhotosStateBehaviorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.GalleryPhotosState.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        db = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
        db.Initialize();

        var photoService = new PhotoService(db);
        state = new GalleryPhotosState(
            photoService,
            new LocalEventBus(),
            new ToastService(),
            new DispatcherService(),
            new ThumbnailWorker(new ThumbnailService()));
    }

    /// <summary>
    /// 生成処理を終了し、専用DBの接続プールを解放して一時ファイルを削除する。
    /// 清掃失敗もテスト失敗として扱い、次の実行へ資源を残さない。
    /// </summary>
    public void Dispose()
    {
        try { state.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        finally
        {
            using var pool = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(tempDir, "Alpheratz.db") }.ToString());
            SqliteConnection.ClearPool(pool);
            Assert.Equal(Path.Combine(Path.GetTempPath(), "Alpheratz.GalleryPhotosState.Tests"), Path.GetDirectoryName(tempDir));
            Directory.Delete(tempDir, recursive: true);
            Assert.False(Directory.Exists(tempDir));
        }
    }

    /// <summary>
    /// loadPhotos が DB から写真を読み込み、月グループ・TotalCount・displayItems を同期することを確認する。
    ///
    /// ギャラリー初期表示では loadPhotos が一括で写真と月集計を取得し、
    /// UI コレクションを同じ世代のデータで置き換える。
    /// このテストでは 6月2枚・7月1枚のデータを登録し、件数、月グループ通知、
    /// 標準表示用 PhotoGridItem が期待どおり構築されることを検証する。
    /// </summary>
    [Fact]
    public async Task LoadPhotos_PopulatesPhotosDisplayItemsAndMonthGroups()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00", "Alpha"));
        await db.UpsertPhotoAsync(Photo("/photo/b.jpg", "b.jpg", "2026-06-06 10:00:00", "Beta"));
        await db.UpsertPhotoAsync(Photo("/photo/c.jpg", "c.jpg", "2026-07-01 10:00:00", "Gamma"));
        IReadOnlyList<GalleryMonthGroup>? notifiedGroups = null;
        state.OnMonthGroupsChanged = groups => notifiedGroups = groups;

        await state.loadPhotos();

        Assert.Equal(3, state.TotalCount);
        Assert.Equal(3, state.photos.Count);
        Assert.Equal(3, state.displayItems.Count);
        Assert.NotNull(notifiedGroups);
        Assert.Contains(state.monthGroups, group => group.Year == 2026 && group.Month == 6 && group.Count == 2);
        Assert.Contains(state.monthGroups, group => group.Year == 2026 && group.Month == 7 && group.Count == 1);
        Assert.All(state.displayItems, item => Assert.Null(item.GroupCount));
    }

    /// <summary>
    /// 旧サムネイル停止を待つ間に新しい読込が始まった場合、最新世代だけが一覧へ反映されることを確認する。
    /// </summary>
    [Fact]
    public async Task LoadPhotos_WhenSupersededWhileStoppingThumbnails_AppliesLatestGenerationOnly()
    {
        await db.UpsertPhotoAsync(Photo("/photo/alpha.jpg", "alpha.jpg", "2026-06-05 10:00:00", "Alpha"));
        await db.UpsertPhotoAsync(Photo("/photo/beta.jpg", "beta.jpg", "2026-06-05 11:00:00", "Beta"));
        var blockingSource = Path.Combine(tempDir, "blocking-source.png");
        await File.WriteAllBytesAsync(blockingSource, [0x00]);
        var generationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thumbnailService = new ThumbnailService(async (_, _, _, ct) =>
        {
            generationStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                await finishCancellation.Task;
                throw;
            }
        });
        var localState = CreateState(new ThumbnailWorker(thumbnailService));
        var replacedCount = 0;
        localState.OnPhotosReplaced = () => replacedCount++;

        try
        {
            localState.setPhotos(
                [Thumb(AppPaths.NormalizePathForDb(blockingSource), "blocking-source.png", null, "2026-06-05 09:00:00")]);
            await generationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            localState.SetFilters(FiltersForWorld("Alpha"));
            var firstLoad = localState.loadPhotos();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            localState.SetFilters(FiltersForWorld("Beta"));
            var secondLoad = localState.loadPhotos();

            finishCancellation.TrySetResult();
            await Task.WhenAll(firstLoad, secondLoad).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, replacedCount);
            var photo = Assert.Single(localState.photos);
            Assert.Equal("Beta", photo.WorldName);
            Assert.Equal("/photo/beta.jpg", photo.PhotoPath);
        }
        finally
        {
            finishCancellation.TrySetResult();
            await localState.DisposeAsync();
        }
    }

    /// <summary>
    /// フォルダ整理の停止処理が進行中生成の終了を待ち、許可解除後も新しい一覧読込までは生成を再開しないことを確認する。
    /// </summary>
    [Fact]
    public async Task FolderCleanupSuspension_BlocksRequestsUntilNewPhotoLoadResumesGeneration()
    {
        var oldSource = Path.Combine(tempDir, "old-source.png");
        var newSource = Path.Combine(tempDir, "new-source.png");
        await File.WriteAllBytesAsync(oldSource, [0x00]);
        await File.WriteAllBytesAsync(newSource, [0x00]);
        var firstGenerationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumedGenerationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generationCount = 0;
        var thumbnailService = new ThumbnailService(async (_, destinationPath, _, ct) =>
        {
            var generation = Interlocked.Increment(ref generationCount);
            if (generation == 1)
            {
                firstGenerationStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.TrySetResult();
                    await finishCancellation.Task;
                    throw;
                }
            }

            resumedGenerationStarted.TrySetResult();
            await File.WriteAllBytesAsync(destinationPath, [0xFF, 0xD8, 0x00, 0xFF, 0xD9], ct);
        });
        var localState = CreateState(new ThumbnailWorker(thumbnailService));
        var pending = Thumb(
            AppPaths.NormalizePathForDb(newSource),
            "new-source.png",
            "New",
            "2026-06-05 11:00:00");

        try
        {
            localState.setPhotos([
                Thumb(
                    AppPaths.NormalizePathForDb(oldSource),
                    "old-source.png",
                    "Old",
                    "2026-06-05 10:00:00")]);
            await firstGenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var suspension = localState.suspendThumbnailGenerationAndWait();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(suspension.IsCompleted);
            finishCancellation.TrySetResult();
            await suspension.WaitAsync(TimeSpan.FromSeconds(5));

            localState.requestVisibleThumbnails([pending]);
            Assert.Equal(1, generationCount);
            localState.allowThumbnailReloadAfterFolderCleanup();
            localState.requestVisibleThumbnails([pending]);
            Assert.Equal(1, generationCount);

            await db.UpsertPhotoAsync(Photo(
                AppPaths.NormalizePathForDb(newSource),
                "new-source.png",
                "2026-06-05 11:00:00",
                "New"));
            await localState.loadPhotos();
            await resumedGenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(generationCount >= 2);
            Assert.Equal(AppPaths.NormalizePathForDb(newSource), Assert.Single(localState.photos).PhotoPath);
        }
        finally
        {
            finishCancellation.TrySetResult();
            await localState.DisposeAsync();
            foreach (var photo in localState.photos)
            {
                if (!string.IsNullOrWhiteSpace(photo.GridThumbPath))
                {
                    try { File.Delete(photo.GridThumbPath); }
                    catch { }
                }
            }
        }
    }

    /// <summary>DB取得済みの旧UI反映を待機させ、新しい読込だけが実queueから一覧へ反映される。</summary>
    [Fact]
    public Task LoadPhotos_WhenUiApplyIsQueuedBeforeNewerLoad_AppliesLatestGenerationOnly()
        => RunNativeGalleryQueueCase(nameof(LoadPhotos_WhenUiApplyIsQueuedBeforeNewerLoad_AppliesLatestGenerationOnly), disposeBeforeApply: false);

    /// <summary>DB取得済みのUI反映前に破棄した場合、旧結果で一覧と月ナビゲーションを置き換えない。</summary>
    [Fact]
    public Task LoadPhotos_WhenUiApplyIsQueuedBeforeDisposal_PreservesRowsAndDropsQueuedResult()
        => RunNativeGalleryQueueCase(nameof(LoadPhotos_WhenUiApplyIsQueuedBeforeDisposal_PreservesRowsAndDropsQueuedResult), disposeBeforeApply: true);

    private async Task RunNativeGalleryQueueCase(string testName, bool disposeBeforeApply)
    {
        if (Environment.GetEnvironmentVariable(QueueChildMarker) != "1")
        {
            await NativeDispatcherQueueTests.RunChildProcess(typeof(GalleryPhotosStateBehaviorTests), testName, QueueChildMarker);
            var evidenceDir = Environment.GetEnvironmentVariable("ALPHERATZ_STARTUP_TEST_EVIDENCE_DIR");
            if (!string.IsNullOrEmpty(evidenceDir))
            {
                using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidenceDir, $"alpheratz-thumbnail-ui-queue-{(disposeBeforeApply ? "disposal" : "newer-load")}-evidence.json")));
                Assert.False(Directory.Exists(evidence.RootElement.GetProperty("owned_root").GetString()));
                Assert.False(Directory.Exists(evidence.RootElement.GetProperty("owned_cache_root").GetString()));
            }
            return;
        }
        await VerifyQueuedGalleryLoad(disposeBeforeApply);
        await File.WriteAllTextAsync(Environment.GetEnvironmentVariable(NativeDispatcherQueueTests.ReceiptVariable)!, "completed");
    }

    /// <summary>本体の実queueを待機させ、DB取得後のawaitを読み取ってから公開操作で世代を変更する。</summary>
    private async Task VerifyQueuedGalleryLoad(bool disposeBeforeApply)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers(); Bootstrap.Initialize(0x00010006);
        try
        {
            var controller = DispatcherQueueController.CreateOnDedicatedThread(); var queue = controller.DispatcherQueue; var dispatcher = new DispatcherService(queue);
            var previousQueue = UiThread.Queue; Assert.Null(previousQueue); UiThread.Queue = queue;
            using var releaseQueue = new ManualResetEventSlim(); var queueBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var queueTimedOut = false;
            var localState = new GalleryPhotosState(new PhotoService(db), new LocalEventBus(), new ToastService(), dispatcher, new ThumbnailWorker(new ThumbnailService()));
            Task? oldLoad = null; Task? newerLoad = null; var replacements = new List<string[]>(); var appliedMonths = new List<int[]>(); var loadingStates = new List<bool>();
            var initialPath = AppPaths.NormalizePathForDb(Path.Combine(tempDir, "initial.jpg")); var alphaPath = AppPaths.NormalizePathForDb(Path.Combine(tempDir, "alpha.jpg")); var betaPath = AppPaths.NormalizePathForDb(Path.Combine(tempDir, "beta.jpg"));
            var initial = Thumb(initialPath, "initial.jpg", "Initial", "2026-05-05 10:00:00");
            try
            {
                await db.UpsertPhotoAsync(Photo(alphaPath, "alpha.jpg", "2026-06-05 10:00:00", "Alpha")); await db.UpsertPhotoAsync(Photo(betaPath, "beta.jpg", "2026-07-05 10:00:00", "Beta"));
                var before = DatabaseSnapshot(); var cacheBefore = OwnedCacheSnapshot(); var cacheRoot = Assert.IsType<string>(AppPaths.GetDataDir());
                await dispatcher.RunOnUiThread(() =>
                {
                    localState.setPhotos([initial]); localState.rebuildDisplayItems(GroupingMode.none); localState.SetFilters(FiltersForWorld("Alpha"));
                    localState.OnPhotosReplaced = () => { Assert.True(queue.HasThreadAccess); replacements.Add(localState.photos.Select(photo => photo.PhotoPath).ToArray()); };
                    localState.OnMonthGroupsChanged = groups => { Assert.True(queue.HasThreadAccess); appliedMonths.Add(groups.Select(group => group.Month).ToArray()); };
                    localState.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName != nameof(GalleryPhotosState.IsLoading)) return;
                        loadingStates.Add(localState.IsLoading);
                        if (loadingStates.Count != 1) return;
                        Assert.True(queue.TryEnqueue(DispatcherQueuePriority.High, () => { queueBlocked.TrySetResult(); if (!releaseQueue.Wait(TimeSpan.FromSeconds(10))) queueTimedOut = true; }));
                    };
                });
                Assert.False(queue.HasThreadAccess); oldLoad = localState.loadPhotos(); await queueBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // queueが止まっている間にDB結果と未完了のUI awaitを観測し、待機時間だけで窓を推測しない。
                Assert.True(await Task.Run(() => SpinWait.SpinUntil(() => HasPendingGalleryUiApply(oldLoad, alphaPath), TimeSpan.FromSeconds(5))));
                Assert.False(oldLoad.IsCompleted); Assert.Same(initial, Assert.Single(localState.photos)); Assert.Empty(replacements); Assert.Empty(appliedMonths);
                if (disposeBeforeApply) await localState.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                else { localState.SetFilters(FiltersForWorld("Beta")); newerLoad = localState.loadPhotos(); Assert.False(newerLoad.IsCompleted); }
                releaseQueue.Set(); await Task.WhenAll(oldLoad, newerLoad ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
                await dispatcher.RunOnUiThread(() =>
                {
                    Assert.False(queueTimedOut);
                    if (disposeBeforeApply)
                    {
                        Assert.Same(initial, Assert.Single(localState.photos)); Assert.Same(initial, Assert.Single(localState.displayItems).Photo);
                        Assert.Empty(replacements); Assert.Empty(appliedMonths); Assert.Empty(localState.monthGroups); Assert.Equal(new[] { true }, loadingStates);
                    }
                    else
                    {
                        Assert.Equal(new[] { betaPath }, Assert.Single(replacements)); Assert.Equal(new[] { 7 }, Assert.Single(appliedMonths));
                        var photo = Assert.Single(localState.photos); Assert.Equal(betaPath, photo.PhotoPath); Assert.Equal("Beta", photo.WorldName); Assert.Same(photo, Assert.Single(localState.displayItems).Photo);
                        Assert.Equal(1, localState.TotalCount); Assert.False(localState.IsLoading); Assert.Equal(new[] { true, false }, loadingStates);
                    }
                });
                Assert.Equal(before, DatabaseSnapshot()); Assert.Equal(cacheBefore, OwnedCacheSnapshot());
                SaveThumbnailLifecycleEvidence($"ui-queue-{(disposeBeforeApply ? "disposal" : "newer-load")}", new { owned_root = tempDir, owned_cache_root = cacheRoot,
                    actual_dispatcher_queue = true, queued_after_database_fetch = true, asynchronous_frame_read_only = true, private_state_mutated_by_test = false,
                    disposed_before_apply = disposeBeforeApply, replacements = replacements.ToArray(), applied_months = appliedMonths.ToArray(), loading_states = loadingStates.ToArray(),
                    initial_path = initialPath, fetched_old_path = alphaPath, expected_latest_path = betaPath, database_before = before, database_after = DatabaseSnapshot(), cache_before = cacheBefore, cache_after = OwnedCacheSnapshot(), verification_complete = false });
            }
            finally
            {
                releaseQueue.Set();
                try { await Task.WhenAll(oldLoad ?? Task.CompletedTask, newerLoad ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5)); await localState.DisposeAsync(); }
                finally { UiThread.Queue = previousQueue; await controller.ShutdownQueueAsync(); }
            }
        }
        finally { Bootstrap.Shutdown(); }
    }

    /// <summary>DB結果のclosureと未完了のUI awaitを読取り、内部状態を変更せず適用待ちを確認する。</summary>
    private static bool HasPendingGalleryUiApply(Task load, string expectedPath)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var frame = load.GetType().GetField("StateMachine", flags)?.GetValue(load); if (frame is null) return false;
        var fields = frame.GetType().GetFields(flags); var awaiterField = fields.SingleOrDefault(field => field.FieldType == typeof(ConfiguredTaskAwaitable.ConfiguredTaskAwaiter));
        var awaiter = awaiterField?.GetValue(frame); var pending = awaiter?.GetType().GetField("m_task", flags)?.GetValue(awaiter) as Task;
        if (pending is null || pending.IsCompleted) return false;
        foreach (var field in fields.Where(field => field.Name.StartsWith("<>8__", StringComparison.Ordinal)))
        {
            var capture = field.GetValue(frame); var photos = capture?.GetType().GetField("allPhotos", flags)?.GetValue(capture) as IReadOnlyList<PhotoThumbnailItem>;
            if (photos is { Count: 1 } && photos[0].PhotoPath == expectedPath) return true;
        }
        return false;
    }

    /// <summary>読込開始の通知中に破棄しても、旧CTSの停止処理を完了し、写真一覧と保存内容を保持する。</summary>
    [Fact]
    public async Task LoadPhotos_WhenDisposedDuringLoadingNotification_PreservesRowsAndRetiresThumbnailResources()
    {
        var sourcePath = Path.Combine(tempDir, "dispose-during-loading.png"); var sourceBytes = new byte[] { 0x10, 0x20 };
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        await db.UpsertPhotoAsync(Photo(AppPaths.NormalizePathForDb(sourcePath), "dispose-during-loading.png", "2026-06-05 10:00:00", "Before"));
        await db.UpsertPhotoAsync(Photo("/photo/retained.jpg", "retained.jpg", "2026-07-05 10:00:00", "Retained"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var generationCount = 0;
        var service = new ThumbnailService(async (_, destinationPath, _, ct) =>
        {
            Interlocked.Increment(ref generationCount); started.TrySetResult();
            await File.WriteAllBytesAsync(destinationPath, new byte[] { 0xFF, 0xD8, 0x00, 0xFF, 0xD9 }, ct);
        });
        var localState = CreateState(new ThumbnailWorker(service)); var photo = Thumb(AppPaths.NormalizePathForDb(sourcePath), "dispose-during-loading.png", "Before", "2026-06-05 10:00:00");
        var replacedCount = 0; var loadingNotificationCount = 0; var disposedBeforeOuterCancellation = false; ValueTask notificationDisposal = default;
        localState.OnPhotosReplaced = () => replacedCount++;
        try
        {
            Assert.Null(UiThread.Queue); localState.setPhotos([photo]); localState.rebuildDisplayItems(GroupingMode.none);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var gate = typeof(GalleryPhotosState).GetField("thumbnailGenerationGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(localState)!;
            var operations = Assert.IsType<Dictionary<Task, CancellationTokenSource>>(typeof(GalleryPhotosState).GetField("thumbnailOperations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(localState));
            // 生成完了を実際の登録状態から確認する。通知内では完了済みの破棄だけを扱い、Taskを同期待機しない。
            Assert.True(await Task.Run(() => SpinWait.SpinUntil(() => { lock (gate) return operations.Count == 0; }, TimeSpan.FromSeconds(5))));
            var thumbnailPath = Assert.IsType<string>(photo.GridThumbPath); Assert.False(ThumbnailService.IsPathLockTracked(thumbnailPath));
            var before = DatabaseSnapshot(); var cacheBefore = OwnedCacheSnapshot();
            // 公開処理で作成されたCTSだけを読み取り、通知内の破棄完了を実測する。内部状態は書き換えない。
            var source = Assert.IsType<CancellationTokenSource>(typeof(GalleryPhotosState).GetField("thumbnailCts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(localState));
            localState.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(GalleryPhotosState.IsLoading) || !localState.IsLoading) return;
                loadingNotificationCount++; notificationDisposal = localState.DisposeAsync(); Assert.True(notificationDisposal.IsCompletedSuccessfully);
                Assert.Throws<ObjectDisposedException>(() => _ = source.Token); disposedBeforeOuterCancellation = true;
            };
            await localState.loadPhotos().WaitAsync(TimeSpan.FromSeconds(5)); await notificationDisposal;
            Assert.Equal(1, loadingNotificationCount); Assert.True(disposedBeforeOuterCancellation); Assert.Equal(0, replacedCount);
            Assert.Same(photo, Assert.Single(localState.photos)); Assert.Same(photo, Assert.Single(localState.displayItems).Photo); Assert.Equal(thumbnailPath, photo.GridThumbPath);
            localState.requestVisibleThumbnails([Thumb("/photo/rejected.jpg", "rejected.jpg", "After", "2026-08-05 10:00:00")]);
            await localState.loadPhotos().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, generationCount); Assert.Equal(before, DatabaseSnapshot()); Assert.Equal(cacheBefore, OwnedCacheSnapshot()); Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
            SaveThumbnailLifecycleEvidence("loading-disposal", new { owned_root = tempDir, loading_notifications = loadingNotificationCount, generations = generationCount,
                photo_replacements = replacedCount, generation_completed_before_reload = true, cts_disposed_before_outer_cancel = disposedBeforeOuterCancellation, private_state_mutated_by_test = false,
                database_before = before, database_after = DatabaseSnapshot(), cache_before = cacheBefore, cache_after = OwnedCacheSnapshot(), original_bytes_preserved = true, verification_complete = false });
        }
        finally { await localState.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    /// <summary>読込開始通知から進行中生成を停止し、停止完了前の一覧更新と破棄後の新規要求を防ぐ。</summary>
    [Fact]
    public async Task DisposeAsync_FromLoadingNotificationWaitsForActiveGenerationAndSkipsPhotoReplacement()
    {
        var sourcePath = Path.Combine(tempDir, "active-loading-disposal.png"); var sourceBytes = new byte[] { 0x50, 0x60 };
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        await db.UpsertPhotoAsync(Photo(AppPaths.NormalizePathForDb(sourcePath), "active-loading-disposal.png", "2026-06-05 10:00:00", "Retained"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var generationCount = 0;
        var service = new ThumbnailService(async (_, _, _, ct) =>
        {
            Interlocked.Increment(ref generationCount); started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { cancellationObserved.TrySetResult(); await finishCancellation.Task; throw; }
        });
        var localState = CreateState(new ThumbnailWorker(service)); var photo = Thumb(AppPaths.NormalizePathForDb(sourcePath), "active-loading-disposal.png", "Retained", "2026-06-05 10:00:00");
        var before = DatabaseSnapshot(); var cacheBefore = OwnedCacheSnapshot(); var loadingNotificationCount = 0; var replacedCount = 0; Task? disposal = null; Task? load = null;
        localState.OnPhotosReplaced = () => replacedCount++;
        localState.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(GalleryPhotosState.IsLoading) || !localState.IsLoading) return;
            loadingNotificationCount++; disposal = localState.DisposeAsync().AsTask();
        };
        try
        {
            Assert.Null(UiThread.Queue); localState.setPhotos([photo]); localState.rebuildDisplayItems(GroupingMode.none); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            load = localState.loadPhotos(); await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(disposal); Assert.False(disposal.IsCompleted); Assert.False(load.IsCompleted); Assert.Equal(1, loadingNotificationCount); Assert.Equal(0, replacedCount);
            localState.requestVisibleThumbnails([Thumb("/photo/rejected.jpg", "rejected.jpg", "After", "2026-08-05 10:00:00")]); Assert.Equal(1, generationCount);
            finishCancellation.TrySetResult(); await Task.WhenAll(load, disposal).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, replacedCount); Assert.Null(photo.GridThumbPath); Assert.Same(photo, Assert.Single(localState.photos)); Assert.Same(photo, Assert.Single(localState.displayItems).Photo);
            await localState.loadPhotos().WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(1, generationCount); Assert.Equal(before, DatabaseSnapshot()); Assert.Equal(cacheBefore, OwnedCacheSnapshot()); Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
            SaveThumbnailLifecycleEvidence("active-loading-disposal", new { owned_root = tempDir, loading_notifications = loadingNotificationCount, generations = generationCount, photo_replacements = replacedCount,
                cancellation_observed = true, loading_waited_for_active_generation = true, disposal_waited_for_active_generation = true,
                database_before = before, database_after = DatabaseSnapshot(), cache_before = cacheBefore, cache_after = OwnedCacheSnapshot(), original_bytes_preserved = true, verification_complete = false });
        }
        finally
        {
            var cleanup = localState.DisposeAsync().AsTask(); finishCancellation.TrySetResult(); await Task.WhenAll(cleanup, disposal ?? Task.CompletedTask, load ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>取消し後も続く書込みの終了を破棄処理が待ち、一時画像を公開せず、破棄後の要求を拒否する。</summary>
    [Fact]
    public async Task DisposeAsync_WaitsForNonCooperativeWriterAndRejectsLateAndNewRequests()
    {
        var sourcePath = Path.Combine(tempDir, "writer-after-cancel.png"); var sourceBytes = new byte[] { 0x30, 0x40 };
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        await db.UpsertPhotoAsync(Photo(AppPaths.NormalizePathForDb(sourcePath), "writer-after-cancel.png", "2026-06-05 10:00:00", "Retained"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var generationCount = 0; string? temporaryPath = null;
        var service = new ThumbnailService(async (_, destinationPath, _, ct) =>
        {
            Interlocked.Increment(ref generationCount); temporaryPath = destinationPath;
            using var registration = ct.Register(() => cancellationObserved.TrySetResult());
            await using var writer = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await writer.WriteAsync(new byte[] { 0xFF, 0xD8 }); await writer.FlushAsync(); started.TrySetResult();
            // WinRT等で取消しより先に完了し得る書込みを、公開停止処理の前後で制御する。
            await finishWrite.Task; await writer.WriteAsync(new byte[] { 0x00, 0xFF, 0xD9 }); await writer.FlushAsync();
        });
        var localState = CreateState(new ThumbnailWorker(service)); var photo = Thumb(AppPaths.NormalizePathForDb(sourcePath), "writer-after-cancel.png", "Retained", "2026-06-05 10:00:00");
        var before = DatabaseSnapshot(); var cacheBefore = OwnedCacheSnapshot(); var changedThumbnailCount = 0; Task? disposal = null;
        photo.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(PhotoThumbnailItem.GridThumbPath)) Interlocked.Increment(ref changedThumbnailCount); };
        try
        {
            localState.setPhotos([photo]); localState.rebuildDisplayItems(GroupingMode.none); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disposal = localState.DisposeAsync().AsTask(); await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(disposal.IsCompleted); Assert.NotNull(temporaryPath); Assert.True(File.Exists(temporaryPath));
            var temporaryIsLocked = Assert.Throws<IOException>(() => { using var stream = File.OpenRead(temporaryPath); }); Assert.Equal(32, temporaryIsLocked.HResult & 0xFFFF);
            localState.requestVisibleThumbnails([Thumb("/photo/rejected.jpg", "rejected.jpg", "After", "2026-08-05 10:00:00")]); Assert.Equal(1, generationCount);
            finishWrite.TrySetResult(); await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(File.Exists(temporaryPath)); Assert.Null(photo.GridThumbPath); Assert.Equal(0, changedThumbnailCount);
            Assert.Same(photo, Assert.Single(localState.photos)); Assert.Same(photo, Assert.Single(localState.displayItems).Photo);
            localState.requestVisibleThumbnails([photo]); await localState.loadPhotos().WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(1, generationCount);
            Assert.Equal(before, DatabaseSnapshot()); Assert.Equal(cacheBefore, OwnedCacheSnapshot()); Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
            SaveThumbnailLifecycleEvidence("late-writer-disposal", new { owned_root = tempDir, temporary_path = temporaryPath, real_exclusive_writer = true, lock_error_code = 32,
                cancellation_observed = true, disposal_waited_for_writer = true, temporary_removed = true, generations = generationCount, thumbnail_changes = changedThumbnailCount,
                database_before = before, database_after = DatabaseSnapshot(), cache_before = cacheBefore, cache_after = OwnedCacheSnapshot(), original_bytes_preserved = true, verification_complete = false });
        }
        finally
        {
            var cleanup = localState.DisposeAsync().AsTask(); finishWrite.TrySetResult(); await Task.WhenAll(cleanup, disposal ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>初期化を複数回呼んでも購読を重複させず、破棄後はスキャン通知へ反応しないことを確認する。</summary>
    [Fact]
    public async Task InitializeAsync_SubscribesOnceAndDisposeUnsubscribes()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00", "Alpha"));
        var eventBus = new LocalEventBus();
        var thumbnailService = new ThumbnailService((_, destinationPath, _, ct) =>
            File.WriteAllBytesAsync(destinationPath, [0xFF, 0xD8, 0x00, 0xFF, 0xD9], ct));
        var localState = new GalleryPhotosState(
            new PhotoService(db),
            eventBus,
            new ToastService(),
            new DispatcherService(),
            new ThumbnailWorker(thumbnailService));
        var replacedCount = 0;
        var disposed = false;
        localState.OnPhotosReplaced = () => replacedCount++;

        try
        {
            await localState.InitializeAsync(loadInitialData: false);
            await localState.InitializeAsync(loadInitialData: false);
            await eventBus.PublishAsync(EventNames.ScanCompleted, new object());

            Assert.Equal(1, replacedCount);

            await localState.DisposeAsync();
            disposed = true;
            await eventBus.PublishAsync(EventNames.ScanCompleted, new object());

            Assert.Equal(1, replacedCount);
        }
        finally
        {
            if (!disposed)
                await localState.DisposeAsync();
            foreach (var photo in localState.photos)
            {
                if (!string.IsNullOrWhiteSpace(photo.GridThumbPath))
                {
                    try { File.Delete(photo.GridThumbPath); }
                    catch { }
                }
            }
        }
    }

    /// <summary>
    /// グルーピングモード world で displayItems がワールド単位の代表カードへ再構築されることを確認する。
    ///
    /// DB 再取得なしでグループ化だけを切り替える現在仕様では、photos に保持済みのデータから
    /// displayItems を作り直す必要がある。
    /// 同じ WorldName の写真は1つのグループカードになり、GroupCount と GroupKey が設定されることを検証する。
    /// </summary>
    [Fact]
    public void RebuildDisplayItems_GroupsPhotosByWorldName()
    {
        state.setPhotos(
            [
                Thumb("/photo/a.jpg", "a.jpg", "Alpha", "2026-06-05 10:00:00"),
                Thumb("/photo/b.jpg", "b.jpg", "Alpha", "2026-06-05 11:00:00"),
                Thumb("/photo/c.jpg", "c.jpg", null, "2026-06-05 12:00:00"),
                Thumb("/photo/d.jpg", "d.jpg", "   ", "2026-06-05 13:00:00"),
                Thumb("/photo/e.jpg", "e.jpg", "unknown", "2026-06-05 14:00:00"),
            ],
            autoGenerateThumbnails: false);

        state.rebuildDisplayItems(GroupingMode.world);

        Assert.Equal(3, state.displayItems.Count);
        var alpha = Assert.Single(state.displayItems, item => item.GroupKey == "Alpha");
        var unknown = Assert.Single(state.displayItems, item => item.GroupKey == GalleryPhotosStateLogic.UnknownWorldGroupKey);
        var literalUnknown = Assert.Single(state.displayItems, item => item.GroupKey == "unknown");
        Assert.Equal(2, alpha.GroupCount);
        Assert.Equal(2, unknown.GroupCount);
        Assert.Equal(1, literalUnknown.GroupCount);
        Assert.Equal("Alpha", alpha.Photo.WorldName);
        Assert.Equal(["/photo/a.jpg", "/photo/b.jpg"], alpha.GroupPhotos!.Select(photo => photo.PhotoPath));
        Assert.Equal(["/photo/c.jpg", "/photo/d.jpg"], unknown.GroupPhotos!.Select(photo => photo.PhotoPath));
        Assert.Equal(["/photo/e.jpg"], literalUnknown.GroupPhotos!.Select(photo => photo.PhotoPath));
    }

    /// <summary>グループ化変更後に写真を再読込しても、保存した表示方式が元へ戻らないことを確認する。</summary>
    [Fact]
    public async Task SetGroupingMode_PersistsAcrossReload()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00", "Alpha"));
        await db.UpsertPhotoAsync(Photo("/photo/b.jpg", "b.jpg", "2026-06-05 11:00:00", "Alpha"));
        state.SetGroupingMode(GroupingMode.world);

        await state.loadPhotos();

        var group = Assert.Single(state.displayItems);
        Assert.Equal("Alpha", group.GroupKey);
        Assert.Equal(2, group.GroupCount);
    }

    /// <summary>
    /// ワールドグループキーが空白を正規化し、見た目は同じでも前後空白だけ違う値を別グループにしないことを確認する。
    ///
    /// DB 由来の world_name は null、空文字、空白だけの文字列が混在し得る。
    /// これらを別グループにすると「ワールド不明」が複数に分かれ、ドリルダウン対象もずれるため、
    /// helper で同じ unknown キーへ揃える。
    /// </summary>
    [Fact]
    public void GalleryPhotosStateLogic_BuildWorldGroupKey_NormalizesEmptyAndTrimmedNames()
    {
        Assert.Equal("__ALPHERATZ_UNKNOWN_WORLD__",
            GalleryPhotosStateLogic.BuildWorldGroupKey(null));
        Assert.Equal("__ALPHERATZ_UNKNOWN_WORLD__",
            GalleryPhotosStateLogic.BuildWorldGroupKey("   "));
        Assert.Equal("Alpha",
            GalleryPhotosStateLogic.BuildWorldGroupKey(" Alpha "));
        Assert.Equal("unknown",
            GalleryPhotosStateLogic.BuildWorldGroupKey("unknown"));
    }

    /// <summary>
    /// setPhotos が既存 displayItems 内の Photo 参照を、新しい photos コレクションの同一パス要素へ差し替えることを確認する。
    ///
    /// フィルタ変更や再スキャンで PhotoThumbnailItem のインスタンスが入れ替わっても、
    /// displayItems が古い参照を持ち続けると、タグ・お気に入り・選択状態の更新が画面へ届かない。
    /// ここでは同じ photo_path の別インスタンスを渡し、displayItems 側の参照が新しいものへ同期されることを検証する。
    /// </summary>
    [Fact]
    public void SetPhotos_RebindsExistingDisplayItemsToNewPhotoInstances()
    {
        var first = Thumb("/photo/a.jpg", "a.jpg", "Before", "2026-06-05 10:00:00");
        state.setPhotos([first], autoGenerateThumbnails: false);
        state.rebuildDisplayItems(GroupingMode.none);
        var replacement = Thumb("/photo/a.jpg", "a.jpg", "After", "2026-06-05 10:00:00");

        state.setPhotos([replacement], autoGenerateThumbnails: false);

        Assert.Same(replacement, state.photos[0]);
        Assert.Same(replacement, state.displayItems[0].Photo);
        Assert.Equal("After", state.displayItems[0].Photo.WorldName);
    }

    /// <summary>
    /// loadMonthSummary が現在フィルタに基づく月別件数を取得し、通知コールバックへ同じリストを渡すことを確認する。
    ///
    /// MonthNav は GalleryPhotosState.monthGroups と OnMonthGroupsChanged の両方に依存する。
    /// loadPhotos を伴わない月集計だけの更新でも、保存された monthGroups と通知された groups が一致する必要があるため、
    /// DB に2か月分の写真を登録して検証する。
    /// </summary>
    [Fact]
    public async Task LoadMonthSummary_UpdatesMonthGroupsAndNotifiesCallback()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00", "Alpha"));
        await db.UpsertPhotoAsync(Photo("/photo/b.jpg", "b.jpg", "2026-07-01 10:00:00", "Beta"));
        IReadOnlyList<GalleryMonthGroup>? notifiedGroups = null;
        state.OnMonthGroupsChanged = groups => notifiedGroups = groups;

        await state.loadMonthSummary();

        Assert.Equal(2, state.monthGroups.Count);
        Assert.NotNull(notifiedGroups);
        Assert.Equal(state.monthGroups, notifiedGroups);
        Assert.Contains(state.monthGroups, group => group.Key == "2026-06" && group.Count == 1);
        Assert.Contains(state.monthGroups, group => group.Key == "2026-07" && group.Count == 1);
    }

    /// <summary>
    /// 外部リストや可視範囲からのサムネイル要求が、空入力・既存サムネイル済み入力を安全に無視することを確認する。
    ///
    /// ドリルダウンや Masonry の可視範囲通知では、空リストや既に GridThumbPath を持つ写真が頻繁に渡る。
    /// その場合に不要な CancellationTokenSource やバックグラウンド生成を起こさないよう、
    /// 例外なく no-op になる代表入力を固定する。
    /// </summary>
    [Fact]
    public void ThumbnailRequestHelpers_IgnoreEmptyOrAlreadyResolvedItems()
    {
        var item = Thumb("/photo/a.jpg", "a.jpg", "Alpha", "2026-06-05 10:00:00");
        item.GridThumbPath = "C:/cache/a.jpg";

        state.kickThumbnailsForExternal([]);
        state.requestVisibleThumbnails([]);
        state.requestVisibleThumbnails([item]);

        Assert.Equal("C:/cache/a.jpg", item.GridThumbPath);
    }

    /// <summary>
    /// 写真差し替え用マップとサムネイル要求用マップの作成規則を確認する。
    ///
    /// 差し替え用マップは photos の photo_path を一意キーとして扱う。
    /// サムネイル要求用マップは外部/可視範囲から繰り返し呼ばれるため、空パスを無視し、
    /// 可視範囲要求では既に GridThumbPath を持つ写真も除外する。
    /// </summary>
    [Fact]
    public void GalleryPhotosStateLogic_BuildsReplacementAndThumbnailRequestMaps()
    {
        var first = Thumb("/photo/a.jpg", "a.jpg", "Alpha", "2026-06-05 10:00:00");
        var duplicate = Thumb("/photo/a.jpg", "a-duplicate.jpg", "Alpha", "2026-06-05 10:01:00");
        var resolved = Thumb("/photo/b.jpg", "b.jpg", "Beta", "2026-06-05 11:00:00");
        resolved.GridThumbPath = "C:/cache/b.jpg";
        var emptyPath = Thumb("", "empty.jpg", "Empty", "2026-06-05 12:00:00");

        var replacement = GalleryPhotosStateLogic.BuildReplacementMap([first, resolved]);
        var external = GalleryPhotosStateLogic.BuildThumbnailRequestMap([first, duplicate, resolved, emptyPath], requireMissingGridThumb: false);
        var visible = GalleryPhotosStateLogic.BuildThumbnailRequestMap([first, duplicate, resolved, emptyPath], requireMissingGridThumb: true);

        Assert.Same(first, replacement["/photo/a.jpg"]);
        Assert.Same(resolved, replacement["/photo/b.jpg"]);
        Assert.Equal(2, external.Count);
        Assert.Same(first, external["/photo/a.jpg"]);
        Assert.Same(resolved, external["/photo/b.jpg"]);
        Assert.Single(visible);
        Assert.Same(first, visible["/photo/a.jpg"]);
    }

    /// <summary>
    /// サムネイル worker へ渡す対象が、未生成かつ photo_path を持つ写真だけになることを確認する。
    ///
    /// ThumbnailWorker は実ファイル I/O を伴うため、呼び出す前段で不要な対象を落とす必要がある。
    /// SourceSlot はサムネイル保存先の 1st/2nd cache を決める値なので、対象リストにそのまま残す。
    /// </summary>
    [Fact]
    public void GalleryPhotosStateLogic_BuildThumbnailTargetsKeepsOnlyMissingThumbsWithSlots()
    {
        var missing = Thumb("/photo/a.jpg", "a.jpg", "Alpha", "2026-06-05 10:00:00");
        missing.SourceSlot = 2;
        var resolved = Thumb("/photo/b.jpg", "b.jpg", "Beta", "2026-06-05 11:00:00");
        resolved.GridThumbPath = "C:/cache/b.jpg";
        var emptyPath = Thumb("", "empty.jpg", "Empty", "2026-06-05 12:00:00");

        var targets = GalleryPhotosStateLogic.BuildThumbnailTargets([missing, resolved, emptyPath]);

        var target = Assert.Single(targets);
        Assert.Equal("/photo/a.jpg", target.path);
        Assert.Equal(2, target.slot);
    }

    /// <summary>
    /// 既存 PhotoGridItem の代表 Photo と GroupPhotos が、新しい PhotoThumbnailItem インスタンスへ差し替わることを確認する。
    ///
    /// グループ表示では代表写真だけでなく、ドリルダウン用の GroupPhotos も保持する。
    /// 再ロード後に片方だけ古い参照のままだと、選択状態やタグ更新が一部の画面に反映されないため、
    /// 両方を photo_path キーで同期する仕様を固定する。
    /// </summary>
    [Fact]
    public void GalleryPhotosStateLogic_SyncDisplayItemReplacesRepresentativeAndGroupPhotos()
    {
        var oldRepresentative = Thumb("/photo/a.jpg", "a-old.jpg", "Old", "2026-06-05 10:00:00");
        var oldGroup = Thumb("/photo/b.jpg", "b-old.jpg", "Old", "2026-06-05 11:00:00");
        var unchanged = Thumb("/photo/c.jpg", "c.jpg", "Stay", "2026-06-05 12:00:00");
        var newRepresentative = Thumb("/photo/a.jpg", "a-new.jpg", "New", "2026-06-05 10:00:00");
        var newGroup = Thumb("/photo/b.jpg", "b-new.jpg", "New", "2026-06-05 11:00:00");
        var item = new PhotoGridItem
        {
            Photo = oldRepresentative,
            GroupPhotos = [oldRepresentative, oldGroup, unchanged],
        };
        var map = GalleryPhotosStateLogic.BuildReplacementMap([newRepresentative, newGroup]);

        var synced = GalleryPhotosStateLogic.SyncDisplayItem(item, map);

        Assert.Same(item, synced);
        Assert.Same(newRepresentative, synced.Photo);
        Assert.Equal([newRepresentative, newGroup, unchanged], synced.GroupPhotos);
    }

    /// <summary>
    /// DB 登録用の写真データを作る。
    /// GalleryPhotosState のロードテストでは、パス・ファイル名・撮影日時・ワールド名だけが必要である。
    /// </summary>
    private static PhotoUpsertData Photo(string path, string filename, string timestamp, string? worldName)
        => new()
        {
            PhotoPath = path,
            PhotoFilename = filename,
            Timestamp = timestamp,
            WorldName = worldName,
            SourceSlot = 1,
        };

    private GalleryPhotosState CreateState(ThumbnailWorker thumbnailWorker)
        => new(
            new PhotoService(db),
            new LocalEventBus(),
            new ToastService(),
            new DispatcherService(),
            thumbnailWorker);

    /// <summary>専用DBの写真・タグ・backup・訪問履歴・採番値を本体の取得関数を使わず保存する。</summary>
    private string DatabaseSnapshot()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(tempDir, "Alpheratz.db"), Pooling = false }.ToString());
        connection.Open(); var tables = new List<List<object?[]>>();
        foreach (var query in new[] { "SELECT * FROM photos ORDER BY photo_path", "SELECT * FROM photo_tags ORDER BY photo_path,tag_id", "SELECT * FROM tags ORDER BY id",
            "SELECT * FROM photo_user_data_backup ORDER BY photo_filename", "SELECT * FROM archive_world_visits ORDER BY id", "SELECT * FROM sqlite_sequence ORDER BY name" })
        {
            using var command = connection.CreateCommand(); command.CommandText = query; using var reader = command.ExecuteReader(); var rows = new List<object?[]>();
            while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray()); tables.Add(rows);
        }
        return JsonSerializer.Serialize(tables);
    }

    /// <summary>直列実行する専用cacheの全ファイル名とbyte列を、取消し前後で比較する。</summary>
    private static string[] OwnedCacheSnapshot()
        => Directory.EnumerateFiles(Assert.IsType<string>(AppPaths.GetImgCacheDir(1))).Order(StringComparer.Ordinal)
            .Select(path => $"{Path.GetFileName(path)}｜{Convert.ToHexString(File.ReadAllBytes(path))}").ToArray();

    /// <summary>全体実行の入力と対応する実測値を、実行専用の証拠directoryへ保存する。</summary>
    private static void SaveThumbnailLifecycleEvidence(string kind, object observation)
    {
        var directory = Environment.GetEnvironmentVariable("ALPHERATZ_STARTUP_TEST_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(directory)) File.WriteAllText(Path.Combine(directory, $"alpheratz-thumbnail-{kind}-evidence.json"), JsonSerializer.Serialize(observation));
    }

    private static PhotoQueryFilters FiltersForWorld(string worldName)
        => new(
            searchQuery: "",
            worldFilters: [worldName],
            dateFrom: "",
            dateTo: "",
            orientationFilter: "all",
            favoritesOnly: false,
            tagFilters: [],
            includePhash: false,
            pagingEnabled: false,
            viewMode: ViewMode.standard,
            sourceSlot: null,
            groupingMode: GroupingMode.none);

    /// <summary>
    /// 表示コレクション構築用の写真モデルを作る。
    /// DB を介さないテストでは PhotoThumbnailItem を直接用意し、参照の差し替えを観察しやすくする。
    /// </summary>
    private static PhotoThumbnailItem Thumb(string path, string filename, string? worldName, string timestamp)
        => new()
        {
            PhotoPath = path,
            PhotoFilename = filename,
            WorldName = worldName,
            Timestamp = timestamp,
            SourceSlot = 1,
        };
}
