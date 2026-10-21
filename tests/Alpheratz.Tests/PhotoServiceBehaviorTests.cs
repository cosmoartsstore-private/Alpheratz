using Alpheratz.Core.Database;
using Alpheratz.Messages;
using Alpheratz.Models;
using Alpheratz.Services;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Alpheratz.Tests;

/// <summary>
/// PhotoService のDBラッパーとしての振る舞いを確認するテスト。
///
/// PhotoService はViewModelから見た写真操作の入口であり、DBの戻り値をDTOへ変換したり、
/// 一括お気に入り・一括タグ追加・ファイルコピーのようなユースケース単位の処理をまとめる。
/// AlpheratzDb単体のテストでは見えないサービス境界の仕様をここで固定する。
/// </summary>
public sealed class PhotoServiceBehaviorTests : IDisposable
{
    private readonly string tempDir;
    private readonly AlpheratzDb db;
    private readonly PhotoService service;

    /// <summary>
    /// テストごとに独立したDBとPhotoServiceを作成する。
    ///
    /// PhotoServiceは内部状態として一括書き込み用Semaphoreを持つ。
    /// テスト間でサービスを共有すると、失敗時にSemaphore状態が次テストへ漏れる可能性があるため、
    /// 各テストで新しいインスタンスを作る。
    /// </summary>
    public PhotoServiceBehaviorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.PhotoService.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        db = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
        db.Initialize();
        service = new PhotoService(db);
    }

    /// <summary>
    /// 一時DB、WAL/SHM、コピー元/コピー先ファイルをまとめて削除する。
    /// クリーンアップに失敗しても、テスト本体の成否を優先する。
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Cleanup failure should not hide the assertion result.
        }
    }

    /// <summary>
    /// GetPhotosAsync と GetMonthSummaryAsync がDB結果をDTOとして返すことを確認する。
    ///
    /// ViewModelはDB型ではなくPhotoPageDtoとMonthSummaryItemを受け取る。
    /// ここでは、検索語・件数・月別集計がサービス境界で欠落せず渡ることを検証する。
    /// </summary>
    [Fact]
    public async Task QueryMethods_ReturnPhotoPageAndMonthSummary()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00", worldName: "Alpha"));
        await db.UpsertPhotoAsync(Photo("/photo/b.jpg", "b.jpg", "2026-06-15 10:00:00", worldName: "Beta"));
        await db.UpsertPhotoAsync(Photo("/photo/c.jpg", "c.jpg", "2026-07-01 10:00:00", worldName: "Gamma"));

        var page = await service.GetPhotosAsync(new PhotoQueryPayload(
            startDate: "2026-06-01",
            endDate: "2026-06-30",
            worldQuery: null,
            worldExacts: null,
            orientation: null,
            favoritesOnly: null,
            tagFilters: null,
            sourceSlot: null,
            limit: 10,
            offset: 0));
        var months = await service.GetMonthSummaryAsync(new PhotoQueryPayload(
            startDate: null,
            endDate: null,
            worldQuery: null,
            worldExacts: null,
            orientation: null,
            favoritesOnly: null,
            tagFilters: null,
            sourceSlot: null,
            limit: null,
            offset: null));

        Assert.Equal(2, page.total);
        Assert.All(page.items, item => Assert.StartsWith("2026-06", item.timestamp, StringComparison.Ordinal));
        Assert.Contains(months, month => month.Year == 2026 && month.Month == 6 && month.Count == 2);
        Assert.Contains(months, month => month.Year == 2026 && month.Month == 7 && month.Count == 1);
    }

    /// <summary>
    /// ワールド候補、タグ件数、ドリルダウン用写真取得がサービス経由で整合することを確認する。
    ///
    /// ギャラリーのフィルタ候補とグループドリルダウンは同じDBを別クエリで参照する。
    /// このテストは、タグ・ワールドの集計と、world groupKey から写真一覧を取得する経路をまとめて固定する。
    /// </summary>
    [Fact]
    public async Task FilterAndGroupMethods_ReturnConsistentMetadata()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00", worldName: "Alpha"));
        await db.UpsertPhotoAsync(Photo("/photo/b.jpg", "b.jpg", "2026-06-05 11:00:00", worldName: "Alpha"));
        await db.AddPhotoTagAsync("/photo/a.jpg", "tag-a");
        await db.AddPhotoTagAsync("/photo/b.jpg", "tag-a");

        var worlds = await service.GetWorldFilterOptionsAsync();
        var tagCounts = await service.GetTagFilterCountsAsync();
        var groupPhotos = await service.GetWorldGroupPhotosAsync(
            "Alpha",
            startDate: null,
            endDate: null,
            sourceSlot: null,
            orientation: null,
            favoritesOnly: null,
            tagFilters: null);

        var alpha = Assert.Single(worlds, world => world.world_name == "Alpha");
        Assert.Equal(2, alpha.count);
        Assert.Equal(2, tagCounts["tag-a"]);
        Assert.Equal(2, groupPhotos.Count);
        Assert.All(groupPhotos, photo => Assert.Equal("Alpha", photo.world_name));
    }

    [Fact]
    public async Task GetWorldGroupPhotosAsync_TreatsSlashContainingGroupKeyAsWorldName()
    {
        await db.UpsertPhotoAsync(Photo("/photo/slash.jpg", "slash.jpg", "2026-06-05 10:00:00", worldName: "VR/Chat"));
        await db.UpsertPhotoAsync(Photo("/photo/other.jpg", "other.jpg", "2026-06-05 11:00:00", worldName: "Other"));

        var groupPhotos = await service.GetWorldGroupPhotosAsync(
            "VR/Chat",
            startDate: null,
            endDate: null,
            sourceSlot: null,
            orientation: null,
            favoritesOnly: null,
            tagFilters: null);

        var photo = Assert.Single(groupPhotos);
        Assert.Equal("/photo/slash.jpg", photo.photo_path);
        Assert.Equal("VR/Chat", photo.world_name);
    }

    /// <summary>
    /// 未解決ワールドの groupKey が null・空文字・空白だけの world_name を同じ一覧として返すことを確認する。
    ///
    /// 画面上のグループ表示では、これらをすべて「ワールド不明」として同じカードへまとめる。
    /// DB の取り直し経路だけ null しか拾わないと、ドリルダウン時に写真が欠けるため、
    /// unknown クエリも同じ定義で扱う。
    /// </summary>
    [Fact]
    public async Task GetWorldGroupPhotosAsync_TreatsNullEmptyAndBlankWorldNamesAsUnknown()
    {
        await db.UpsertPhotoAsync(Photo("/photo/null.jpg", "null.jpg", "2026-06-05 10:00:00", worldName: null));
        await db.UpsertPhotoAsync(Photo("/photo/empty.jpg", "empty.jpg", "2026-06-05 11:00:00", worldName: ""));
        await db.UpsertPhotoAsync(Photo("/photo/blank.jpg", "blank.jpg", "2026-06-05 12:00:00", worldName: "   "));
        await db.UpsertPhotoAsync(Photo("/photo/world.jpg", "world.jpg", "2026-06-05 13:00:00", worldName: "Alpha"));

        var groupPhotos = await service.GetWorldGroupPhotosAsync(
            WorldFilterValues.Unknown,
            startDate: null,
            endDate: null,
            sourceSlot: null,
            orientation: null,
            favoritesOnly: null,
            tagFilters: null);

        Assert.Equal(3, groupPhotos.Count);
        Assert.DoesNotContain(groupPhotos, photo => photo.world_name == "Alpha");
    }

    /// <summary>
    /// お気に入りとタグの単体/一括更新がDBへ反映されることを確認する。
    ///
    /// ViewModelは一括操作後に画面側のPhotoThumbnailItemを更新するが、正となる状態はDBにある。
    /// ここではPhotoServiceの一括処理が順番にDB更新を行い、選択写真参照も取得できることを検証する。
    /// </summary>
    [Fact]
    public async Task MutationMethods_UpdateFavoritesTagsAndSelectedRefs()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00"));
        await db.UpsertPhotoAsync(Photo("/photo/b.jpg", "b.jpg", "2026-06-05 11:00:00"));
        var refs = new[]
        {
            new SelectedPhotoRefDto { photo_path = "/photo/a.jpg", source_slot = 1 },
            new SelectedPhotoRefDto { photo_path = "/photo/b.jpg", source_slot = 1 },
        };

        var favoriteResult = await service.BulkSetPhotoFavoriteAsync(refs, isFavorite: true);
        var tagResult = await service.BulkAddPhotoTagAsync(refs, "bulk");
        await service.RemovePhotoTagAsync("/photo/a.jpg", "bulk", sourceSlot: 1);
        var selectedRefs = await service.GetSelectedPhotoRefsAsync(["/photo/a.jpg", "/photo/b.jpg"]);
        var tagsA = await service.GetPhotoTagsAsync("/photo/a.jpg", sourceSlot: 1);
        var tagsB = await service.GetPhotoTagsAsync("/photo/b.jpg", sourceSlot: 1);
        var page = await service.GetPhotosAsync(new PhotoQueryPayload(
            startDate: null,
            endDate: null,
            worldQuery: null,
            worldExacts: null,
            orientation: null,
            favoritesOnly: true,
            tagFilters: null,
            sourceSlot: null,
            limit: null,
            offset: null));

        Assert.Equal(2, selectedRefs.Count);
        Assert.Equal(2, favoriteResult.SucceededCount);
        Assert.Empty(favoriteResult.FailedPhotos);
        Assert.Equal(2, tagResult.SucceededCount);
        Assert.Empty(tagResult.FailedPhotos);
        Assert.Empty(tagsA);
        Assert.Equal(["bulk"], tagsB);
        Assert.Equal(2, page.total);
        Assert.All(page.items, photo => Assert.True(photo.is_favorite));
    }

    /// <summary>
    /// 一括更新の途中で 1 写真だけ失敗しても残りを続行し、完了・失敗を写真単位で返すことを確認する。
    /// </summary>
    [Fact]
    public async Task RunBulkPhotoUpdatesAsync_ContinuesAfterIndividualFailure()
    {
        var refs = new[]
        {
            new SelectedPhotoRefDto { photo_path = "/photo/a.jpg", source_slot = 1 },
            new SelectedPhotoRefDto { photo_path = "/photo/b.jpg", source_slot = 1 },
            new SelectedPhotoRefDto { photo_path = "/photo/c.jpg", source_slot = 1 },
        };
        var visited = new List<string>();

        var result = await service.RunBulkPhotoUpdatesAsync(
            refs,
            "test",
            photo =>
            {
                visited.Add(photo.photo_path);
                return photo.photo_path == "/photo/b.jpg"
                    ? Task.FromException(new IOException("書き込み失敗"))
                    : Task.CompletedTask;
            });

        Assert.Equal(["/photo/a.jpg", "/photo/b.jpg", "/photo/c.jpg"], visited);
        Assert.Equal(["/photo/a.jpg", "/photo/c.jpg"], result.SucceededPhotos.Select(photo => photo.photo_path));
        var failure = Assert.Single(result.FailedPhotos);
        Assert.Equal("/photo/b.jpg", failure.Photo.photo_path);
        Assert.Equal("操作を完了できませんでした。", failure.Reason);
        Assert.False(failure.IsCanceled);
    }

    /// <summary>
    /// 写真更新中にキャンセルされた場合、完了済み写真を成功として残し、後続写真を未更新として返すことを確認する。
    /// </summary>
    [Fact]
    public async Task RunBulkPhotoUpdatesAsync_CancellationReturnsCompletedPrefixAndPendingPhotos()
    {
        var refs = new[]
        {
            new SelectedPhotoRefDto { photo_path = "/photo/a.jpg", source_slot = 1 },
            new SelectedPhotoRefDto { photo_path = "/photo/b.jpg", source_slot = 1 },
            new SelectedPhotoRefDto { photo_path = "/photo/c.jpg", source_slot = 1 },
        };
        using var cts = new CancellationTokenSource();

        var result = await service.RunBulkPhotoUpdatesAsync(
            refs,
            "test",
            photo =>
            {
                if (photo.photo_path == "/photo/a.jpg")
                    cts.Cancel();
                return Task.CompletedTask;
            },
            cts.Token);

        Assert.Equal("/photo/a.jpg", Assert.Single(result.SucceededPhotos).photo_path);
        Assert.Equal(["/photo/b.jpg", "/photo/c.jpg"], result.FailedPhotos.Select(failure => failure.Photo.photo_path));
        Assert.All(result.FailedPhotos, failure => Assert.True(failure.IsCanceled));
        Assert.True(result.WasCanceled);
    }

    /// <summary>
    /// 共通一括処理の取消しでは処理中の写真を実DBへ確定し、後続・非選択写真を保持する。
    /// DB書込み前と確定後で停止位置を分け、公開APIによる同対象の再試行も確認する。
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RunBulkPhotoUpdatesAsync_CancellationPreservesDurablePhotoBoundaryAndAllowsPublicRetry(
        bool addTags, bool cancelAfterCommit)
    {
        var paths = Enumerable.Range(0, 4).Select(index => Path.Combine(tempDir, $"photo-{index}.jpg")).ToArray();
        var sourceBytes = new[] { (byte)42, (byte)83, (byte)255 };
        for (var index = 0; index < paths.Length; index++)
        {
            File.WriteAllBytes(paths[index], sourceBytes);
            await db.UpsertPhotoAsync(Photo(paths[index], Path.GetFileName(paths[index]),
                $"2026-06-05 1{index}:00:00", $"world-{index}"));
            await db.AddPhotoTagsAsync(paths[index], [index == 3 ? "untouched" : "existing"]);
        }
        await db.SetPhotoFavoriteAsync(paths[3], true);
        var refs = paths.Take(3).Select(path => new SelectedPhotoRefDto { photo_path = path, source_slot = 1 }).ToArray();
        var initialRows = SnapshotPhotoRows();
        var initialMetadata = ReadPhotoMetadata();
        using var cts = new CancellationTokenSource();
        var boundaryReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumePhoto = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // 公開メソッドと同じ実DB操作を共通処理へ渡す。処理中の写真には取消しを渡さない。
        var pending = service.RunBulkPhotoUpdatesAsync(refs, "durable-cancellation", async photo =>
        {
            if (photo.photo_path == paths[0] && !cancelAfterCommit)
            {
                boundaryReached.TrySetResult();
                await resumePhoto.Task;
            }
            if (addTags)
                await db.AddPhotoTagsAsync(photo.photo_path, ["night", "city"], CancellationToken.None);
            else
                Assert.True(await db.TrySetPhotoFavoriteAsync(photo.photo_path, true, CancellationToken.None));
            if (photo.photo_path == paths[0] && cancelAfterCommit)
            {
                boundaryReached.TrySetResult();
                await resumePhoto.Task;
            }
        }, cts.Token);

        try
        {
            await boundaryReached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(pending.IsCompleted);
            await AssertPersistedStateAsync(cancelAfterCommit ? 1 : 0);
            if (!cancelAfterCommit)
                Assert.Equal(initialRows, SnapshotPhotoRows());
            cts.Cancel();
        }
        finally
        {
            resumePhoto.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(refs[0], Assert.Single(result.SucceededPhotos));
        Assert.Equal(refs.Skip(1), result.FailedPhotos.Select(failure => failure.Photo));
        Assert.All(result.FailedPhotos, failure => Assert.True(failure.IsCanceled));
        Assert.True(result.WasCanceled);
        await AssertPersistedStateAsync(1);

        var retry = addTags
            ? await service.BulkAddPhotoTagsAsync(refs, ["night", "city"]).WaitAsync(TimeSpan.FromSeconds(10))
            : await service.BulkSetPhotoFavoriteAsync(refs, true).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(refs, retry.SucceededPhotos);
        Assert.Empty(retry.FailedPhotos);
        Assert.False(retry.WasCanceled);
        await AssertPersistedStateAsync(3);

        async Task AssertPersistedStateAsync(int completedPhotos)
        {
            var reopened = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
            for (var index = 0; index < paths.Length; index++)
            {
                var saved = await reopened.GetPhotoRecordAsync(paths[index]);
                Assert.NotNull(saved);
                Assert.Equal(index == 3 || (!addTags && index < completedPhotos), saved.is_favorite);
                string[] expectedTags = index == 3 ? ["untouched"]
                    : addTags && index < completedPhotos ? ["city", "existing", "night"] : ["existing"];
                Assert.Equal(expectedTags, await reopened.GetPhotoTagsAsync(paths[index]));
                Assert.Equal(sourceBytes, File.ReadAllBytes(paths[index]));
            }
            Assert.Equal(initialMetadata, ReadPhotoMetadata());
        }

        string ReadPhotoMetadata()
        {
            using var connection = new SqliteConnection($"Data Source={Path.Combine(tempDir, "Alpheratz.db")};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM photos ORDER BY photo_path";
            using var reader = command.ExecuteReader();
            var favoriteColumn = reader.GetOrdinal("is_favorite");
            var rows = new List<object[]>();
            while (reader.Read())
            {
                var row = new object[reader.FieldCount];
                for (var column = 0; column < row.Length; column++)
                    row[column] = column == favoriteColumn ? 0L : reader.GetValue(column);
                rows.Add(row);
            }
            return JsonSerializer.Serialize(rows);
        }
    }

    /// <summary>
    /// 公開一括APIが書込みgate待ちで取消された場合、選択写真を保存せず返し、同対象を再試行できる。
    /// 先行操作を保持したまま取消しを完了させ、取消しが別操作へ伝播しないことも確認する。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicBulkUpdates_CancellationWhileWaitingPreservesDatabaseAndAllowsRetry(bool addTags)
    {
        var paths = new[] { "/photo/control.jpg", "/photo/a.jpg", "/photo/b.jpg" };
        foreach (var path in paths)
        {
            await db.UpsertPhotoAsync(Photo(path, Path.GetFileName(path), "2026-06-05 10:00:00", "retained-world"));
            await db.AddPhotoTagsAsync(path, [path == paths[0] ? "control" : "existing"]);
        }
        var controlRef = new SelectedPhotoRefDto { photo_path = paths[0], source_slot = 1 };
        var refs = paths.Skip(1).Select(path => new SelectedPhotoRefDto { photo_path = path, source_slot = 1 }).ToArray();
        var initialRows = SnapshotPhotoRows();
        var activeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeActive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        // 同じserviceの先行書込みを保持し、公開APIを実際の一括gateで待機させる。
        var active = service.RunBulkPhotoUpdatesAsync([controlRef], "active-update", async photo =>
        {
            activeEntered.TrySetResult();
            await resumeActive.Task;
            Assert.True(await db.TrySetPhotoFavoriteAsync(photo.photo_path, true));
        });
        try
        {
            await activeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var queued = addTags
                ? service.BulkAddPhotoTagsAsync(refs, ["night", "city"], cts.Token)
                : service.BulkSetPhotoFavoriteAsync(refs, true, cts.Token);
            Assert.False(queued.IsCompleted);
            cts.Cancel();
            var canceled = await queued.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(canceled.SucceededPhotos);
            Assert.Equal(refs, canceled.FailedPhotos.Select(failure => failure.Photo));
            Assert.All(canceled.FailedPhotos, failure => Assert.True(failure.IsCanceled));
            Assert.True(canceled.WasCanceled);
            Assert.False(active.IsCompleted);
            Assert.Equal(initialRows, SnapshotPhotoRows());
        }
        finally
        {
            resumeActive.TrySetResult();
            await active.WaitAsync(TimeSpan.FromSeconds(10));
        }

        var completed = await active;
        Assert.Equal(controlRef, Assert.Single(completed.SucceededPhotos));
        Assert.Empty(completed.FailedPhotos);
        Assert.False(completed.WasCanceled);
        await AssertSavedAsync(retried: false);
        var retry = addTags
            ? await service.BulkAddPhotoTagsAsync(refs, ["night", "city"]).WaitAsync(TimeSpan.FromSeconds(10))
            : await service.BulkSetPhotoFavoriteAsync(refs, true).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(refs, retry.SucceededPhotos);
        Assert.Empty(retry.FailedPhotos);
        Assert.False(retry.WasCanceled);
        await AssertSavedAsync(retried: true);

        async Task AssertSavedAsync(bool retried)
        {
            var reopened = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
            foreach (var path in paths)
            {
                var saved = await reopened.GetPhotoRecordAsync(path);
                Assert.NotNull(saved);
                var control = path == paths[0];
                Assert.Equal(control || (!addTags && retried), saved.is_favorite);
                string[] expectedTags = control ? ["control"]
                    : addTags && retried ? ["city", "existing", "night"] : ["existing"];
                Assert.Equal(expectedTags, await reopened.GetPhotoTagsAsync(path));
            }
        }
    }

    /// <summary>
    /// 一括お気に入りで失効した写真参照を成功扱いせず、更新できた写真と分けて返すことを確認する。
    /// </summary>
    [Fact]
    public async Task BulkSetPhotoFavoriteAsync_ReturnsFailureForStaleReference()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00"));
        var refs = new[]
        {
            new SelectedPhotoRefDto { photo_path = "/photo/a.jpg", source_slot = 1 },
            new SelectedPhotoRefDto { photo_path = "/photo/deleted.jpg", source_slot = 1 },
        };

        var result = await service.BulkSetPhotoFavoriteAsync(refs, isFavorite: true);
        var saved = await db.GetPhotoRecordAsync("/photo/a.jpg");

        Assert.NotNull(saved);
        Assert.True(saved.is_favorite);
        Assert.Equal("/photo/a.jpg", Assert.Single(result.SucceededPhotos).photo_path);
        Assert.Equal("/photo/deleted.jpg", Assert.Single(result.FailedPhotos).Photo.photo_path);
    }

    /// <summary>
    /// 一括タグ追加で失効した写真参照だけが失敗となり、他写真のDB更新が維持されることを確認する。
    /// </summary>
    [Fact]
    public async Task BulkAddPhotoTagsAsync_ReturnsPerPhotoResultForStaleReference()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00"));
        var refs = new[]
        {
            new SelectedPhotoRefDto { photo_path = "/photo/a.jpg", source_slot = 1 },
            new SelectedPhotoRefDto { photo_path = "/photo/deleted.jpg", source_slot = 1 },
        };

        var result = await service.BulkAddPhotoTagsAsync(refs, ["night", "city"]);
        var tags = await db.GetPhotoTagsAsync("/photo/a.jpg");

        Assert.Equal(["city", "night"], tags);
        Assert.Equal("/photo/a.jpg", Assert.Single(result.SucceededPhotos).photo_path);
        Assert.Equal("/photo/deleted.jpg", Assert.Single(result.FailedPhotos).Photo.photo_path);
    }

    /// <summary>
    /// 一括複数タグで 1 写真が途中失敗しても、その写真だけをロールバックし、他写真の完了結果を維持する。
    /// </summary>
    [Fact]
    public async Task BulkAddPhotoTagsAsync_RollsBackFailedPhotoAndContinues()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00"));
        await db.UpsertPhotoAsync(Photo("/photo/b.jpg", "b.jpg", "2026-06-05 11:00:00"));
        var dbPath = Path.Combine(tempDir, "Alpheratz.db");
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
CREATE TRIGGER reject_blocked_photo_tag
BEFORE INSERT ON photo_tags
WHEN NEW.photo_path = '/photo/b.jpg'
 AND NEW.tag_id = (SELECT id FROM tags WHERE name = 'blocked')
BEGIN
    SELECT RAISE(ABORT, 'blocked tag');
END;
""";
            command.ExecuteNonQuery();
        }
        var refs = new[]
        {
            new SelectedPhotoRefDto { photo_path = "/photo/a.jpg", source_slot = 1 },
            new SelectedPhotoRefDto { photo_path = "/photo/b.jpg", source_slot = 1 },
        };

        var result = await service.BulkAddPhotoTagsAsync(refs, ["first", "blocked"]);
        var firstTags = await db.GetPhotoTagsAsync("/photo/a.jpg");
        var secondTags = await db.GetPhotoTagsAsync("/photo/b.jpg");

        Assert.Equal(["blocked", "first"], firstTags);
        Assert.Empty(secondTags);
        Assert.Equal("/photo/a.jpg", Assert.Single(result.SucceededPhotos).photo_path);
        Assert.Equal("/photo/b.jpg", Assert.Single(result.FailedPhotos).Photo.photo_path);
    }

    /// <summary>
    /// 単体写真へ複数タグを追加したとき、全タグが同じ書き込みゲート内で保存されることを確認する。
    /// </summary>
    [Fact]
    public async Task AddPhotoTagsAsync_AddsMultipleTagsToSinglePhoto()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00"));

        await service.AddPhotoTagsAsync("/photo/a.jpg", ["night", "city"], sourceSlot: 1);
        var tags = await service.GetPhotoTagsAsync("/photo/a.jpg", sourceSlot: 1);

        Assert.Equal(["city", "night"], tags);
    }

    /// <summary>
    /// タグ追加が短いSQLite書き込み競合を待ってから成功することを確認する。
    ///
    /// スキャンや解析の書き込みとユーザーのタグ追加が重なると、以前は SQLITE_BUSY で即失敗し得た。
    /// 別接続で書き込みトランザクションを保持し、解除後にタグが保存されることを固定する。
    /// </summary>
    [Fact]
    public async Task AddPhotoTagAsync_WaitsForShortWriteLockAndSucceeds()
    {
        var dbPath = Path.Combine(tempDir, "Alpheratz.db");
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00"));

        using var lockConnection = new SqliteConnection($"Data Source={dbPath}");
        lockConnection.Open();
        using (var pragma = lockConnection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }
        using var tx = lockConnection.BeginTransaction();
        using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.Transaction = tx;
            lockCommand.CommandText = "INSERT INTO tags (name) VALUES ('lock-holder')";
            lockCommand.ExecuteNonQuery();
        }

        var addTask = service.AddPhotoTagAsync("/photo/a.jpg", "delayed", sourceSlot: 1);
        await Task.Delay(150);

        Assert.False(addTask.IsCompleted);

        tx.Commit();
        await addTask.WaitAsync(TimeSpan.FromSeconds(5));
        var tags = await service.GetPhotoTagsAsync("/photo/a.jpg", sourceSlot: 1);

        Assert.Equal(["delayed"], tags);
    }

    /// <summary>
    /// BulkCopyPhotosAsync がコピー済み件数とスキップ件数を返すことを確認する。
    ///
    /// コピー先に同名ファイルが存在する場合、現在の仕様では例外で全体を止めず、そのファイルだけをスキップする。
    /// 大量選択時に一部重複があっても残りをコピーできることが重要なため、コピー成功と重複スキップを同時に検証する。
    /// </summary>
    [Fact]
    public async Task BulkCopyPhotosAsync_CopiesFilesAndSkipsExistingNames()
    {
        var sourceDir = Path.Combine(tempDir, "source");
        var destinationDir = Path.Combine(tempDir, "dest");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destinationDir);
        var sourceA = Path.Combine(sourceDir, "a.jpg");
        var sourceB = Path.Combine(sourceDir, "b.jpg");
        await File.WriteAllTextAsync(sourceA, "a");
        await File.WriteAllTextAsync(sourceB, "b");
        await File.WriteAllTextAsync(Path.Combine(destinationDir, "b.jpg"), "existing");

        var result = await service.BulkCopyPhotosAsync(
            [
                new SelectedPhotoRefDto { photo_path = sourceA.Replace('\\', '/'), source_slot = 1 },
                new SelectedPhotoRefDto { photo_path = sourceB.Replace('\\', '/'), source_slot = 1 },
            ],
            destinationDir);

        Assert.Equal(1, result.copied);
        Assert.Equal(1, result.skipped);
        Assert.True(File.Exists(Path.Combine(destinationDir, "a.jpg")));
        Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(destinationDir, "b.jpg")));
    }

    [Theory]
    [InlineData("photos", "photos", "2|/photo/b.jpg,/photo/a.jpg")]
    [InlineData("months", "photos", "2026/6/2")]
    [InlineData("group", "photos", "/photo/b.jpg,/photo/a.jpg")]
    [InlineData("worlds", "photos", "日本語ワールド/2")]
    [InlineData("tag-counts", "photos", "保存済み/1")]
    [InlineData("references", "photos", "/photo/a.jpg/1,/photo/b.jpg/1")]
    [InlineData("tags", "photo_tags", "保存済み")]
    public async Task ReadMethods_PropagateDatabaseFailurePreserveRowsAndRecover(string operation, string table, string expected)
    {
        await SeedReadPhotosAsync();
        Assert.Equal(expected, await ReadServiceValueAsync(operation));
        var before = SnapshotPhotoRows();
        ExecuteSql($"ALTER TABLE {table} RENAME TO owned_unavailable_table;");
        try
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() => ReadServiceValueAsync(operation));
            Assert.Equal(1, error.SqliteErrorCode);
            Assert.Contains($"no such table: {table}", error.Message);
        }
        finally
        {
            ExecuteSql($"ALTER TABLE owned_unavailable_table RENAME TO {table};");
        }

        Assert.Equal(before, SnapshotPhotoRows());
        Assert.Equal(expected, await ReadServiceValueAsync(operation).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(before, SnapshotPhotoRows());
    }

    [Theory]
    [InlineData("photos", "2|/photo/b.jpg,/photo/a.jpg")]
    [InlineData("months", "2026/6/2")]
    [InlineData("group", "/photo/b.jpg,/photo/a.jpg")]
    [InlineData("worlds", "日本語ワールド/2")]
    [InlineData("tag-counts", "保存済み/1")]
    [InlineData("references", "/photo/a.jpg/1,/photo/b.jpg/1")]
    [InlineData("tags", "保存済み")]
    public async Task ReadMethods_PreCancelledTokenPreservesRowsAndAllowsSameServiceRetry(string operation, string expected)
    {
        await SeedReadPhotosAsync();
        var before = SnapshotPhotoRows();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadServiceValueAsync(operation, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(before, SnapshotPhotoRows());
        Assert.Equal(expected, await ReadServiceValueAsync(operation).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(before, SnapshotPhotoRows());
    }

    [Fact]
    public async Task BulkCopyPhotosAsync_SourceLockMissingFileAndDirectoryCollisionDoNotStopOtherCopies()
    {
        var sourceDirectory = Path.Combine(tempDir, "copy-source");
        var destinationDirectory = Path.Combine(tempDir, "copy-destination");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(destinationDirectory);
        var lockedPath = Path.Combine(sourceDirectory, "locked.jpg");
        var collisionPath = Path.Combine(sourceDirectory, "collision.jpg");
        var goodPath = Path.Combine(sourceDirectory, "good.jpg");
        var expected = new byte[] { 0, 255, 10, 30, 128 };
        await File.WriteAllTextAsync(lockedPath, "排他中の元写真");
        await File.WriteAllTextAsync(collisionPath, "衝突する元写真");
        await File.WriteAllBytesAsync(goodPath, expected);
        var collisionDirectory = Path.Combine(destinationDirectory, "collision.jpg");
        Directory.CreateDirectory(collisionDirectory);
        await File.WriteAllTextAsync(Path.Combine(collisionDirectory, "retained.txt"), "保持する既存ファイル");
        var before = SnapshotPhotoRows();
        var photos = new[] { lockedPath, Path.Combine(sourceDirectory, "missing.jpg"), collisionPath, goodPath }
            .Select(path => new SelectedPhotoRefDto { photo_path = path.Replace('\\', '/'), source_slot = 1 }).ToArray();
        using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal((1, 3), await service.BulkCopyPhotosAsync(photos, destinationDirectory));
            Assert.Equal(expected, await File.ReadAllBytesAsync(Path.Combine(destinationDirectory, "good.jpg")));
            Assert.False(File.Exists(Path.Combine(destinationDirectory, "locked.jpg")));
            Assert.False(File.Exists(Path.Combine(destinationDirectory, "missing.jpg")));
        }

        Assert.Equal("排他中の元写真", await File.ReadAllTextAsync(lockedPath));
        Assert.Equal("衝突する元写真", await File.ReadAllTextAsync(collisionPath));
        Assert.Equal(expected, await File.ReadAllBytesAsync(goodPath));
        Assert.Equal("保持する既存ファイル", await File.ReadAllTextAsync(Path.Combine(collisionDirectory, "retained.txt")));
        Assert.Equal(before, SnapshotPhotoRows());
        Assert.Equal((1, 0), await service.BulkCopyPhotosAsync([photos[0]], destinationDirectory));
        Assert.Equal("排他中の元写真", await File.ReadAllTextAsync(Path.Combine(destinationDirectory, "locked.jpg")));
        Assert.Equal(before, SnapshotPhotoRows());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkCopyPhotosAsync_UnavailableDestinationPreservesSourceAndAllowsRetry(bool destinationIsFile)
    {
        var sourcePath = Path.Combine(tempDir, "source.jpg");
        var destination = Path.Combine(tempDir, "unavailable-destination");
        await File.WriteAllTextAsync(sourcePath, "コピー元の日本語データ");
        if (destinationIsFile)
            await File.WriteAllTextAsync(destination, "保持する既存ファイル");
        var photo = new SelectedPhotoRefDto { photo_path = sourcePath.Replace('\\', '/'), source_slot = 1 };
        var before = SnapshotPhotoRows();

        var error = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.BulkCopyPhotosAsync([photo], destination));
        Assert.Equal($"コピー先フォルダが見つかりません: {destination}", error.Message);
        Assert.Equal("コピー元の日本語データ", await File.ReadAllTextAsync(sourcePath));
        Assert.Equal(before, SnapshotPhotoRows());
        if (destinationIsFile)
            Assert.Equal("保持する既存ファイル", await File.ReadAllTextAsync(destination));
        else
            Assert.False(Path.Exists(destination));

        var retryDestination = Path.Combine(tempDir, "valid-destination");
        Directory.CreateDirectory(retryDestination);
        Assert.Equal((1, 0), await service.BulkCopyPhotosAsync([photo], retryDestination));
        Assert.Equal("コピー元の日本語データ", await File.ReadAllTextAsync(Path.Combine(retryDestination, "source.jpg")));
        Assert.Equal(before, SnapshotPhotoRows());
    }

    [Fact]
    public async Task BulkCopyPhotosAsync_PreCancelledTokenDoesNotCreateFilesAndAllowsRetry()
    {
        var sourcePath = Path.Combine(tempDir, "cancel-source.jpg");
        var destination = Path.Combine(tempDir, "cancel-destination");
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(sourcePath, "取消し後も保持する元写真");
        var photo = new SelectedPhotoRefDto { photo_path = sourcePath.Replace('\\', '/'), source_slot = 1 };
        var before = SnapshotPhotoRows();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BulkCopyPhotosAsync([photo], destination, cancellation.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
        Assert.Equal("取消し後も保持する元写真", await File.ReadAllTextAsync(sourcePath));
        Assert.Equal(before, SnapshotPhotoRows());
        Assert.Equal((1, 0), await service.BulkCopyPhotosAsync([photo], destination));
        Assert.Equal("取消し後も保持する元写真", await File.ReadAllTextAsync(Path.Combine(destination, "cancel-source.jpg")));
        Assert.Equal(before, SnapshotPhotoRows());
    }

    private async Task SeedReadPhotosAsync()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00", "日本語ワールド"));
        await db.UpsertPhotoAsync(Photo("/photo/b.jpg", "b.jpg", "2026-06-05 11:00:00", "日本語ワールド"));
        await db.AddPhotoTagAsync("/photo/a.jpg", "保存済み");
        await db.SetPhotoFavoriteAsync("/photo/a.jpg", true);
    }

    // 各画面が必要とする検索結果を、独立したliteralと比較できる形へ限定する。
    private async Task<string> ReadServiceValueAsync(string operation, CancellationToken ct = default)
    {
        var query = new PhotoQueryPayload(null, null, null, null, null, null, null, null, null, null);
        switch (operation)
        {
            case "photos":
                var page = await service.GetPhotosAsync(query, ct);
                return $"{page.total}|{string.Join(',', page.items.Select(photo => photo.photo_path))}";
            case "months":
                return string.Join(',', (await service.GetMonthSummaryAsync(query, ct)).Select(month => $"{month.Year}/{month.Month}/{month.Count}"));
            case "group":
                return string.Join(',', (await service.GetWorldGroupPhotosAsync("日本語ワールド", null, null, null, null, null, null, ct)).Select(photo => photo.photo_path));
            case "worlds":
                return string.Join(',', (await service.GetWorldFilterOptionsAsync(ct)).Select(world => $"{world.world_name}/{world.count}"));
            case "tag-counts":
                return string.Join(',', (await service.GetTagFilterCountsAsync(ct)).OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}/{entry.Value}"));
            case "references":
                return string.Join(',', (await service.GetSelectedPhotoRefsAsync(["/photo/a.jpg", "/photo/missing.jpg", "/photo/b.jpg"], ct)).Select(photo => $"{photo.photo_path}/{photo.source_slot}"));
            case "tags":
                return string.Join(',', await service.GetPhotoTagsAsync("/photo/a.jpg", 1, ct));
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private void ExecuteSql(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(tempDir, "Alpheratz.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private string SnapshotPhotoRows()
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(tempDir, "Alpheratz.db")};Pooling=False");
        connection.Open();
        var snapshot = new List<object[]>();
        foreach (var (table, order) in new[] { ("photos", "photo_path"), ("tags", "id"), ("photo_tags", "photo_path, tag_id") })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY {order}";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount + 1];
                values[0] = table;
                for (var index = 0; index < reader.FieldCount; index++)
                    values[index + 1] = reader.GetValue(index);
                snapshot.Add(values);
            }
        }
        return JsonSerializer.Serialize(snapshot);
    }

    /// <summary>
    /// PhotoUpsertData を短く生成するためのテスト専用ファクトリ。
    ///
    /// PhotoServiceのテストではDB登録そのものが主目的ではないため、
    /// 各ケースで必要な timestamp と world だけを引数に出し、その他は既定値で統一する。
    /// </summary>
    private static PhotoUpsertData Photo(
        string path,
        string filename,
        string timestamp,
        string? worldName = null)
        => new()
        {
            PhotoPath = path,
            PhotoFilename = filename,
            Timestamp = timestamp,
            WorldName = worldName,
            SourceSlot = 1,
        };
}
