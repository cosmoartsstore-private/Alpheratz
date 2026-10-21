using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Models.Events;
using Alpheratz.Services;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>PDQ解析の取消し・保存失敗・通知待機・再試行を、実画像とSQLiteの保存内容で確認する。</summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed partial class BackgroundServiceBehaviorTests
{
    [Fact]
    public async Task PhashCancellationBeforeStartPreservesPhotosAndTheSameServiceCanRetry()
    {
        var path = Path.Combine(tempDir, "cancel-before-start.png"); await WriteGradientPngAsync(path, 96, 96);
        var bytes = File.ReadAllBytes(path); var normalized = path.Replace('\\', '/');
        await db.UpsertPhotoAsync(Photo(path, "cancel-before-start.png", "2026-10-08 10:00:00"));
        await db.SetPhotoFavoriteAsync(normalized, true); await db.AddPhotoTagAsync(normalized, "保持"); var original = PhashSnapshot();
        var errors = new ConcurrentQueue<string>(); var order = new ConcurrentQueue<string>(); var complete = 0;
        await using var progressSub = eventBus.Subscribe<PhashProgressEvent>(EventNames.PhashProgress, _ => { order.Enqueue("progress"); return Task.CompletedTask; });
        await using var errorSub = eventBus.Subscribe<string>(EventNames.PhashError, message => { errors.Enqueue(message); order.Enqueue("error"); return Task.CompletedTask; });
        await using var completeSub = eventBus.Subscribe(EventNames.PhashComplete, () => { Interlocked.Increment(ref complete); return Task.CompletedTask; });
        var service = new PhashService(db, eventBus); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();

        await service.StartPdqAnalysisAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(original, PhashSnapshot()); Assert.Equal(1, await db.GetPendingPhashCountAsync()); Assert.Equal(0, complete);
        Assert.Equal(["類似画像の解析を中断しました。"], errors.ToArray()); Assert.Equal("error", order.Last());
        Assert.All(order.Take(order.Count - 1), item => Assert.Equal("progress", item));

        await service.StartPdqAnalysisAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var saved = (await db.GetPhotoRecordAsync(normalized, includePhash: true))!; AssertPdqHash(saved.phash);
        Assert.True(saved.is_favorite); Assert.Equal(["保持"], saved.tags); Assert.Equal(0, await db.GetPendingPhashCountAsync()); Assert.Equal(1, complete);
        Assert.Equal(["類似画像の解析を中断しました。"], errors.ToArray()); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <summary>保存後の進捗で取消しを要求し、commit済みの写真を保持する。終了通知中は次の解析を開始しない。</summary>
    [Fact]
    public async Task PhashCancellationAfterCommitKeepsSavedHashAndHoldsTheGateUntilErrorDeliveryFinishes()
    {
        var path = Path.Combine(tempDir, "cancel-after-commit.png"); await WriteGradientPngAsync(path, 96, 96);
        var normalized = path.Replace('\\', '/'); await db.UpsertPhotoAsync(Photo(path, "cancel-after-commit.png", "2026-10-08 10:00:00"));
        var service = new PhashService(db, eventBus); using var cancellation = new CancellationTokenSource();
        var errorStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseError = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new ConcurrentQueue<string>(); var progress = new ConcurrentQueue<PhashProgressEvent>(); var complete = 0; var cancelled = 0;
        await using var progressSub = eventBus.Subscribe<PhashProgressEvent>(EventNames.PhashProgress, payload =>
        {
            progress.Enqueue(payload);
            if (payload.done == 1 && payload.total == 1 && Interlocked.Exchange(ref cancelled, 1) == 0) cancellation.Cancel();
            return Task.CompletedTask;
        });
        await using var errorSub = eventBus.Subscribe<string>(EventNames.PhashError, message =>
        {
            errors.Enqueue(message); errorStarted.TrySetResult(); return releaseError.Task;
        });
        await using var completeSub = eventBus.Subscribe(EventNames.PhashComplete, () => { Interlocked.Increment(ref complete); return Task.CompletedTask; });

        var analysis = service.StartPdqAnalysisAsync(cancellation.Token); string? committedHash = null;
        var nextPath = Path.Combine(tempDir, "next-broken.jpg"); var nextNormalized = nextPath.Replace('\\', '/');
        try
        {
            await errorStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(analysis.IsCompleted); Assert.Equal(1, cancelled); Assert.Equal(0, complete);
            committedHash = (await db.GetPhotoRecordAsync(normalized, includePhash: true))!.phash; AssertPdqHash(committedHash);
            Assert.Equal(0, await db.GetPendingPhashCountAsync()); Assert.Equal(["類似画像の解析を中断しました。"], errors.ToArray());
            Assert.Equal(1, progress.Last().done); Assert.Equal(1, progress.Last().total); Assert.Null(progress.Last().current);
            File.WriteAllText(nextPath, "検証用の読込不能画像"); await db.UpsertPhotoAsync(Photo(nextPath, "next-broken.jpg", "2026-10-08 10:00:01"));
            var held = PhashSnapshot(); var publications = progress.Count;
            await service.StartPdqAnalysisAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(analysis.IsCompleted); Assert.Equal(held, PhashSnapshot()); Assert.Equal(publications, progress.Count);
            Assert.Equal(1, await db.GetPendingPhashCountAsync()); Assert.Equal(0, complete); Assert.Single(errors);
        }
        finally
        {
            releaseError.TrySetResult(); await analysis.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await service.StartPdqAnalysisAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(committedHash, (await db.GetPhotoRecordAsync(normalized, includePhash: true))!.phash);
        Assert.Equal("unreadable", (await db.GetPhotoRecordAsync(nextNormalized, includePhash: true))!.phash);
        Assert.Equal(0, await db.GetPendingPhashCountAsync()); Assert.Equal(1, complete); Assert.Equal(["類似画像の解析を中断しました。"], errors.ToArray());
    }

    /// <summary>2枚目のphash書込後に実triggerで失敗させ、同じchunkの先行書込もrollbackする。</summary>
    [Fact]
    public async Task PhashDatabaseFailureRollsBackTheChunkAndRetryComputesAllPendingPhotos()
    {
        var first = Path.Combine(tempDir, "first.png"); var second = Path.Combine(tempDir, "second.png"); await WriteGradientPngAsync(first, 96, 96); File.Copy(first, second);
        await db.UpsertPhotoAsync(Photo(first, "first.png", "2026-10-08 10:00:00")); await db.UpsertPhotoAsync(Photo(second, "second.png", "2026-10-08 10:00:01"));
        await db.SetPhotoFavoriteAsync(first.Replace('\\', '/'), true); await db.AddPhotoTagAsync(first.Replace('\\', '/'), "保持"); var original = PhashSnapshot();
        PhashExecute("""
            CREATE TRIGGER reject_phash AFTER UPDATE OF phash ON photos
            WHEN (SELECT COUNT(*) FROM photos WHERE phash IS NOT NULL)=2
            BEGIN SELECT RAISE(ABORT, '検証用のphash保存失敗'); END;
            """);
        var errors = new ConcurrentQueue<string>(); var progress = new ConcurrentQueue<PhashProgressEvent>(); var complete = 0;
        await using var progressSub = eventBus.Subscribe<PhashProgressEvent>(EventNames.PhashProgress, payload => { progress.Enqueue(payload); return Task.CompletedTask; });
        await using var errorSub = eventBus.Subscribe<string>(EventNames.PhashError, message => { errors.Enqueue(message); return Task.CompletedTask; });
        await using var completeSub = eventBus.Subscribe(EventNames.PhashComplete, () => { Interlocked.Increment(ref complete); return Task.CompletedTask; });
        var service = new PhashService(db, eventBus);

        await service.StartPdqAnalysisAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(original, PhashSnapshot()); Assert.Equal(2, await db.GetPendingPhashCountAsync()); Assert.Equal(0, complete);
        Assert.Equal(["類似画像の解析に失敗しました。アプリを再起動すると、未解析の写真をもう一度解析します。"], errors.ToArray());
        Assert.Equal(0, progress.Last().done); Assert.Equal(2, progress.Last().total); Assert.Null(progress.Last().current);
        PhashExecute("DROP TRIGGER reject_phash;"); await service.StartPdqAnalysisAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var saved = (await db.GetPhotoRecordAsync(first.Replace('\\', '/'), includePhash: true))!; AssertPdqHash(saved.phash);
        Assert.True(saved.is_favorite); Assert.Equal(["保持"], saved.tags);
        AssertPdqHash((await db.GetPhotoRecordAsync(second.Replace('\\', '/'), includePhash: true))!.phash);
        Assert.Equal(0, await db.GetPendingPhashCountAsync()); Assert.Equal(1, complete); Assert.Single(errors);
        Assert.Equal(2, progress.Last().done); Assert.Equal(2, progress.Last().total); Assert.Null(progress.Last().current);
    }

    /// <summary>30枚のcommit後、次のchunkの途中書込を失敗させ、保存済みの保持と残る30枚の再試行を確認する。</summary>
    [Fact]
    public async Task PhashFailureInTheSecondChunkPreservesCommittedPhotosAndRetriesOnlyPendingPhotos()
    {
        var source = Path.Combine(tempDir, "chunk-00.png"); await WriteGradientPngAsync(source, 96, 96); var originalBytes = File.ReadAllBytes(source);
        var paths = new List<string>();
        for (var index = 0; index < 60; index++)
        {
            var filename = $"chunk-{index:00}.png"; var path = Path.Combine(tempDir, filename); if (index != 0) File.Copy(source, path);
            var normalized = path.Replace('\\', '/'); paths.Add(normalized);
            await db.UpsertPhotoAsync(Photo(path, filename, $"2026-10-09 10:00:{index:00}"));
            await db.SetPhotoFavoriteAsync(normalized, index % 2 == 0); await db.AddPhotoTagAsync(normalized, index < 30 ? "前半の保持" : "後半の保持");
        }
        var original = PhashSnapshot(includePhash: false); var beforeHashes = PhashReadHashes();
        Assert.Equal(60, beforeHashes.Count); Assert.All(beforeHashes.Values, Assert.Null);
        // 記録triggerも同じtransactionに属するため、失敗chunkの途中更新は記録に残らない。
        PhashExecute("""
            CREATE TABLE phash_write_trace(photo_path TEXT NOT NULL);
            CREATE TRIGGER record_phash_write AFTER UPDATE OF phash ON photos
            BEGIN INSERT INTO phash_write_trace(photo_path) VALUES(NEW.photo_path); END;
            CREATE TRIGGER reject_second_phash_chunk AFTER UPDATE OF phash ON photos
            WHEN (SELECT COUNT(*) FROM photos WHERE phash IS NOT NULL)=45
            BEGIN SELECT RAISE(ABORT, '検証用の2chunk目のphash保存失敗'); END;
            """);
        var errors = new ConcurrentQueue<string>(); var progress = new ConcurrentQueue<PhashProgressEvent>(); var order = new ConcurrentQueue<string>(); var complete = 0;
        await using var progressSub = eventBus.Subscribe<PhashProgressEvent>(EventNames.PhashProgress, payload => { progress.Enqueue(payload); order.Enqueue("progress"); return Task.CompletedTask; });
        await using var errorSub = eventBus.Subscribe<string>(EventNames.PhashError, message => { errors.Enqueue(message); order.Enqueue("error"); return Task.CompletedTask; });
        await using var completeSub = eventBus.Subscribe(EventNames.PhashComplete, () => { Interlocked.Increment(ref complete); order.Enqueue("complete"); return Task.CompletedTask; });
        var service = new PhashService(db, eventBus);

        await service.StartPdqAnalysisAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var failedHashes = PhashReadHashes(); var committed = failedHashes.Where(row => row.Value is not null).ToDictionary(row => row.Key, row => row.Value);
        var pending = failedHashes.Where(row => row.Value is null).Select(row => row.Key).ToHashSet();
        Assert.Equal(30, committed.Count); Assert.All(committed.Values, AssertPdqHash); Assert.Equal(30, pending.Count);
        var writesAfterFailure = PhashReadWrittenPaths(); Assert.Equal(30, writesAfterFailure.Count); Assert.Equal(committed.Keys.OrderBy(path => path), writesAfterFailure.OrderBy(path => path));
        Assert.Equal(paths.OrderBy(path => path), failedHashes.Keys); Assert.Equal(original, PhashSnapshot(includePhash: false));
        var pendingBatch = await db.GetPendingPhashBatchAsync(60);
        Assert.True(pending.SetEquals(pendingBatch.Select(item => item.PhotoPath))); Assert.Equal(30, await db.GetPendingPhashCountAsync());
        Assert.Equal(["類似画像の解析に失敗しました。アプリを再起動すると、未解析の写真をもう一度解析します。"], errors.ToArray());
        Assert.Equal(0, complete); Assert.Equal("error", order.Last()); Assert.All(order.Take(order.Count - 1), item => Assert.Equal("progress", item));
        var failedProgress = progress.ToArray(); Assert.All(failedProgress, item => { Assert.Equal(60, item.total); Assert.InRange(item.done, 0, 30); });
        Assert.All(failedProgress.Zip(failedProgress.Skip(1)), pair => Assert.True(pair.First.done <= pair.Second.done));
        Assert.Equal(30, failedProgress.Last().done); Assert.Null(failedProgress.Last().current); Assert.Equal(failedProgress.Last(), await service.GetPhashProgressAsync());
        Assert.All(paths, path => Assert.Equal(originalBytes, File.ReadAllBytes(path)));

        PhashExecute("DROP TRIGGER reject_second_phash_chunk;"); await service.StartPdqAnalysisAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var retryHashes = PhashReadHashes(); Assert.Equal(60, retryHashes.Count); Assert.All(retryHashes.Values, AssertPdqHash);
        Assert.All(committed, row => Assert.Equal(row.Value, retryHashes[row.Key])); Assert.All(pending, path => AssertPdqHash(retryHashes[path]));
        var writesAfterRetry = PhashReadWrittenPaths(); Assert.Equal(60, writesAfterRetry.Count); Assert.Equal(paths.OrderBy(path => path), writesAfterRetry.OrderBy(path => path));
        Assert.Equal(original, PhashSnapshot(includePhash: false)); Assert.Equal(0, await db.GetPendingPhashCountAsync());
        Assert.Equal(1, complete); Assert.Single(errors); Assert.Equal("complete", order.Last());
        var retryProgress = progress.ToArray().Skip(failedProgress.Length).ToArray();
        Assert.Equal(0, retryProgress.First().done); Assert.All(retryProgress, item => { Assert.Equal(30, item.total); Assert.InRange(item.done, 0, 30); });
        Assert.All(retryProgress.Zip(retryProgress.Skip(1)), pair => Assert.True(pair.First.done <= pair.Second.done));
        Assert.Equal(30, retryProgress.Last().done); Assert.Null(retryProgress.Last().current); Assert.Equal(retryProgress.Last(), await service.GetPhashProgressAsync());
        Assert.All(paths, path => Assert.Equal(originalBytes, File.ReadAllBytes(path)));

        var evidenceDirectory = Environment.GetEnvironmentVariable("ALPHERATZ_STARTUP_TEST_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidenceDirectory))
        {
            var evidence = new { case_name = nameof(PhashFailureInTheSecondChunkPreservesCommittedPhotosAndRetriesOnlyPendingPhotos), real_winrt_images = true,
                real_sqlite_transactions = true, trigger_failure_after_non_null_count = 45, photos = 60, committed_after_failure = committed.Count, pending_after_failure = pending.Count,
                hashes_before = beforeHashes, hashes_after_failure = failedHashes, hashes_after_retry = retryHashes,
                writes_after_failure = writesAfterFailure, writes_after_retry = writesAfterRetry,
                original_state_except_phash = original, state_except_phash_after_retry = PhashSnapshot(includePhash: false),
                original_png_sha256 = Convert.ToHexString(SHA256.HashData(originalBytes)), source_images_preserved = true,
                progress_after_failure = failedProgress, progress_after_retry = retryProgress, errors = errors.ToArray(), notification_order = order.ToArray(), complete,
                same_service_retry = true, production_seam_added = false, actual_ui = false, in_flight_decoder_cancellation = false, verification_complete = false };
            File.WriteAllText(Path.Combine(evidenceDirectory, "alpheratz-phash-chunk-failure-evidence.json"), JsonSerializer.Serialize(evidence));
        }
    }

    /// <summary>保存入口と最初・2枚目の保存直前で取消し、先行更新も含めて元のDB状態へ戻す。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task PhashBatchCancellationPreservesTheWholeChunkAndCanBeRetried(int cancellationStage)
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-10-08 10:00:00"));
        await db.UpsertPhotoAsync(Photo("/photo/b.jpg", "b.jpg", "2026-10-08 10:00:01"));
        await db.SetPhotoFavoriteAsync("/photo/a.jpg", true); await db.AddPhotoTagAsync("/photo/a.jpg", "保持"); var original = PhashSnapshot();
        var updates = new[] { new AlpheratzDb.PhotoPhashUpdate("/photo/a.jpg", new string('a', 64)), new AlpheratzDb.PhotoPhashUpdate("/photo/b.jpg", new string('b', 64)) };
        using var cancellation = new CancellationTokenSource(); if (cancellationStage == -1) cancellation.Cancel();
        var controlled = new CancellingPhashUpdates(updates, cancellationStage, cancellation);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.UpdatePhotoPhashesAsync(controlled, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken); Assert.Equal(original, PhashSnapshot()); Assert.Equal(2, await db.GetPendingPhashCountAsync());
        await db.UpdatePhotoPhashesAsync(updates);
        var first = (await db.GetPhotoRecordAsync("/photo/a.jpg", includePhash: true))!;
        Assert.Equal(new string('a', 64), first.phash); Assert.True(first.is_favorite); Assert.Equal(["保持"], first.tags);
        Assert.Equal(new string('b', 64), (await db.GetPhotoRecordAsync("/photo/b.jpg", includePhash: true))!.phash); Assert.Equal(0, await db.GetPendingPhashCountAsync());
    }

    /// <summary>公開IReadOnlyListの列挙境界で取消しを要求し、処理途中の時点をsleepなしで固定する。</summary>
    private sealed class CancellingPhashUpdates(AlpheratzDb.PhotoPhashUpdate[] updates, int stage, CancellationTokenSource cancellation)
        : IReadOnlyList<AlpheratzDb.PhotoPhashUpdate>
    {
        public int Count => updates.Length;
        public AlpheratzDb.PhotoPhashUpdate this[int index] => updates[index];
        public IEnumerator<AlpheratzDb.PhotoPhashUpdate> GetEnumerator()
        {
            for (var index = 0; index < updates.Length; index++)
            {
                if (index == stage) cancellation.Cancel();
                yield return updates[index];
            }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void AssertPdqHash(string? hash)
    {
        Assert.NotNull(hash); Assert.Equal(4, hash.Split('|').Length);
        Assert.All(hash.Split('|'), variant => Assert.Matches("^[0-9a-f]{64}$", variant));
    }

    private void PhashExecute(string sql)
    {
        using var connection = PhashConnection(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    private SqliteConnection PhashConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(tempDir, "Alpheratz.db") }.ToString()); connection.Open(); return connection;
    }

    private List<string> PhashReadWrittenPaths()
    {
        using var connection = PhashConnection(); using var command = connection.CreateCommand(); command.CommandText = "SELECT photo_path FROM phash_write_trace ORDER BY rowid";
        using var reader = command.ExecuteReader(); var paths = new List<string>(); while (reader.Read()) paths.Add(reader.GetString(0));
        return paths;
    }

    private SortedDictionary<string, string?> PhashReadHashes()
    {
        using var connection = PhashConnection(); using var command = connection.CreateCommand(); command.CommandText = "SELECT photo_path, phash FROM photos ORDER BY photo_path";
        using var reader = command.ExecuteReader(); var hashes = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        while (reader.Read()) hashes.Add(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
        return hashes;
    }

    private string PhashSnapshot(bool includePhash = true)
    {
        var snapshots = new Dictionary<string, object?[][]>(); using var connection = PhashConnection();
        foreach (var table in new[] { "photos", "tags", "photo_tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence" })
        {
            using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY 1, 2";
            using var reader = command.ExecuteReader(); var rows = new List<object?[]>();
            while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Where(index => includePhash || table != "photos" || reader.GetName(index) != "phash").Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
            snapshots.Add(table, rows.ToArray());
        }
        return JsonSerializer.Serialize(snapshots);
    }
}
