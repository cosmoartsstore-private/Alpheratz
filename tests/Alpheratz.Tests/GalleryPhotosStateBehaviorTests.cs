using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging;
using Alpheratz.Core.Scanner;
using Alpheratz.Features.Gallery;
using Alpheratz.Models.Events;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;

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
    /// 一時DBと WAL/SHM ファイルを削除する。
    /// 削除失敗はテスト対象ではないため、テスト結果を優先して握りつぶす。
    /// </summary>
    public void Dispose()
    {
        try { state.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch { }
        try { Directory.Delete(tempDir, recursive: true); }
        catch { }
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
        Assert.Equal(GalleryPhotosStateLogic.UnknownWorldGroupKey,
            GalleryPhotosStateLogic.BuildWorldGroupKey(null));
        Assert.Equal(GalleryPhotosStateLogic.UnknownWorldGroupKey,
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
