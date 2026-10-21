using System.Text.Json;
using Alpheratz.Core.Database;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>訪問履歴の置換と写真へのワールド補完を、実SQLiteの保存内容・失敗・再試行で確認する。</summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class ArchiveWorldResolutionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Alpheratz.ArchiveWorld.Tests", Guid.NewGuid().ToString("N"));
    private readonly AlpheratzDb db;
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "archive.db") }.ToString();

    public ArchiveWorldResolutionTests()
    {
        Directory.CreateDirectory(directory); db = new AlpheratzDb(Path.Combine(directory, "archive.db")); db.Initialize();
    }

    public void Dispose()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Alpheratz.ArchiveWorld.Tests")) + Path.DirectorySeparatorChar, Path.GetFullPath(directory));
        using var connection = new SqliteConnection(ConnectionString); SqliteConnection.ClearPool(connection); Directory.Delete(directory, recursive: true);
    }

    [Theory]
    [InlineData("09:59:59", false, false)]
    [InlineData("10:00:00", false, true)]
    [InlineData("10:30:00", false, true)]
    [InlineData("11:00:00", false, true)]
    [InlineData("11:00:01", false, false)]
    [InlineData("23:59:59", true, true)]
    public async Task ResolveUsesInclusiveVisitBoundariesAndOpenEndedVisits(string time, bool openEnded, bool matches)
    {
        await SeedPhoto("/photo/a.jpg", At(time));
        await db.UpsertArchiveWorldVisitsAsync([Visit("日本語のワールド", "10:00:00", openEnded ? null : "11:00:00")]);
        var archive = Snapshot("archive_world_visits"); Assert.Equal(matches ? 1 : 0, await db.ResolveUnknownWorldsFromArchiveAsync());
        var photo = (await db.GetPhotoRecordAsync("/photo/a.jpg"))!;
        Assert.Equal(matches ? "日本語のワールド" : null, photo.world_name); Assert.Equal(matches ? "polaris_archive" : null, photo.match_source);
        Assert.Null(photo.world_id); Assert.Equal(archive, Snapshot("archive_world_visits"));
        Assert.Equal(0, await db.ResolveUnknownWorldsFromArchiveAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveIncludesUnknownNamesAcrossBothSlots(string? previousName)
    {
        await SeedPhoto("/slot1/a.jpg", At("10:30:00"), worldName: previousName); await SeedPhoto("/slot2/a.jpg", At("10:30:00"), worldName: previousName, slot: 2);
        await db.UpsertArchiveWorldVisitsAsync([Visit("補完先", "10:00:00", "11:00:00")]);
        Assert.Equal(2, await db.ResolveUnknownWorldsFromArchiveAsync());
        foreach (var path in new[] { "/slot1/a.jpg", "/slot2/a.jpg" })
        {
            var photo = (await db.GetPhotoRecordAsync(path))!; Assert.Equal("補完先", photo.world_name); Assert.Equal("polaris_archive", photo.match_source);
        }
    }

    [Fact]
    public async Task ResolveChoosesTheLatestContainingVisitAndFallsBackAfterAShorterVisitEnds()
    {
        await db.UpsertArchiveWorldVisitsAsync([Visit("長期", "10:00:00", null), Visit("中期", "11:00:00", "12:00:00"), Visit("短期", "11:30:00", "11:40:00")]);
        var cases = new[] { ("11:20:00", "中期"), ("11:35:00", "短期"), ("11:41:00", "中期"), ("12:00:01", "長期") };
        for (var index = 0; index < cases.Length; index++) await SeedPhoto($"/photo/{index}.jpg", At(cases[index].Item1));
        Assert.Equal(4, await db.ResolveUnknownWorldsFromArchiveAsync());
        for (var index = 0; index < cases.Length; index++)
            Assert.Equal(cases[index].Item2, (await db.GetPhotoRecordAsync($"/photo/{index}.jpg"))!.world_name);
    }

    [Fact]
    public async Task ResolvePreservesKnownWorldsIdsMissingPhotosAndUnmatchedPhotos()
    {
        await SeedPhoto("/known/name.jpg", At("10:30:00"), worldName: "既知", matchSource: "manual");
        await SeedPhoto("/known/id.jpg", At("10:30:00"), worldId: "wrld_known", matchSource: "metadata");
        await SeedPhoto("/missing/a.jpg", At("10:30:00")); Execute("UPDATE photos SET is_missing=1 WHERE photo_path='/missing/a.jpg';");
        await SeedPhoto("/outside/a.jpg", At("09:59:59")); await SeedPhoto("/unknown/a.jpg", At("10:30:00"), slot: 2);
        await db.SetPhotoFavoriteAsync("/unknown/a.jpg", true); await db.AddPhotoTagAsync("/unknown/a.jpg", "保持");
        await db.UpdatePhotoPhashAsync("/unknown/a.jpg", new string('a', 64));
        await db.UpsertArchiveWorldVisitsAsync([Visit("補完先", "10:00:00", "11:00:00")]);
        const string stablePhotoQuery = """
            SELECT photo_path, photo_filename, world_id, timestamp, last_modified_utc, phash, phash_version,
                   orientation, image_width, image_height, source_slot, is_favorite, is_missing
            FROM photos ORDER BY photo_path
            """;
        var stableMetadata = ReadRows(stablePhotoQuery);
        var stableTags = Snapshot("tags", "photo_tags", "archive_world_visits");

        Assert.Equal(1, await db.ResolveUnknownWorldsFromArchiveAsync());
        var known = (await db.GetPhotoRecordAsync("/known/name.jpg"))!; Assert.Equal("既知", known.world_name); Assert.Equal("manual", known.match_source);
        var idOnly = (await db.GetPhotoRecordAsync("/known/id.jpg"))!; Assert.Null(idOnly.world_name); Assert.Equal("metadata", idOnly.match_source);
        foreach (var path in new[] { "/missing/a.jpg", "/outside/a.jpg" })
        {
            var photo = (await db.GetPhotoRecordAsync(path))!; Assert.Null(photo.world_name); Assert.Null(photo.match_source);
        }
        var resolved = (await db.GetPhotoRecordAsync("/unknown/a.jpg", includePhash: true))!;
        Assert.Equal("補完先", resolved.world_name); Assert.Equal("polaris_archive", resolved.match_source); Assert.True(resolved.is_favorite);
        Assert.Equal(["保持"], resolved.tags); Assert.Equal(new string('a', 64), resolved.phash);
        Assert.Equal(stableMetadata, ReadRows(stablePhotoQuery));
        Assert.Equal(stableTags, Snapshot("tags", "photo_tags", "archive_world_visits"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveWithNoPhotosOrNoVisitsLeavesExistingStateUntouched(bool noPhotos)
    {
        if (noPhotos) await db.UpsertArchiveWorldVisitsAsync([Visit("保持", "10:00:00", null)]);
        else await SeedPhoto("/photo/a.jpg", At("10:30:00"));
        var original = Snapshot(); Assert.Equal(0, await db.ResolveUnknownWorldsFromArchiveAsync()); Assert.Equal(original, Snapshot());
    }

    /// <summary>1枚目の更新後、2枚目で失敗させ、同じSQL内の先行更新もrollbackする。</summary>
    [Fact]
    public async Task ResolveUpdateFailureRollsBackAllPhotoChangesAndAllowsRetry()
    {
        await SeedPhoto("/photo/a.jpg", At("10:30:00")); await SeedPhoto("/photo/b.jpg", At("10:30:00"));
        await db.AddPhotoTagAsync("/photo/a.jpg", "保持"); await db.UpsertArchiveWorldVisitsAsync([Visit("補完先", "10:00:00", "11:00:00")]);
        var original = Snapshot();
        Execute("""
            CREATE TRIGGER reject_world AFTER UPDATE OF world_name ON photos
            WHEN (SELECT COUNT(*) FROM photos WHERE match_source='polaris_archive')=2
            BEGIN SELECT RAISE(ABORT, '検証用の補完失敗'); END;
            """);
        var error = await Assert.ThrowsAsync<SqliteException>(() => db.ResolveUnknownWorldsFromArchiveAsync());
        Assert.Equal(19, error.SqliteErrorCode); Assert.Equal(original, Snapshot());
        Execute("DROP TRIGGER reject_world;"); Assert.Equal(2, await db.ResolveUnknownWorldsFromArchiveAsync());
        Assert.Equal("補完先", (await db.GetPhotoRecordAsync("/photo/a.jpg"))!.world_name); Assert.Equal("補完先", (await db.GetPhotoRecordAsync("/photo/b.jpg"))!.world_name);
    }

    [Fact]
    public async Task ResolveDatabaseOpenFailureKeepsStateAndCanBeRetriedAfterUnlock()
    {
        await SeedPhoto("/photo/a.jpg", At("10:30:00")); await db.UpsertArchiveWorldVisitsAsync([Visit("補完先", "10:00:00", "11:00:00")]); var original = Snapshot();
        using (var ownPool = new SqliteConnection(ConnectionString)) SqliteConnection.ClearPool(ownPool);
        using (var locked = new FileStream(Path.Combine(directory, "archive.db"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(14, (await Assert.ThrowsAsync<SqliteException>(() => db.ResolveUnknownWorldsFromArchiveAsync())).SqliteErrorCode);
        Assert.Equal(original, Snapshot()); Assert.Equal(1, await db.ResolveUnknownWorldsFromArchiveAsync());
    }

    [Theory]
    [InlineData("resolve")]
    [InlineData("replace")]
    [InlineData("lookup")]
    public async Task CancelledArchiveOperationPreservesStateAndCanBeCalledAgain(string operation)
    {
        await SeedPhoto("/photo/a.jpg", At("10:30:00")); var visits = new[] { Visit("保持", "10:00:00", "11:00:00") };
        await db.UpsertArchiveWorldVisitsAsync(visits); var original = Snapshot(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (operation == "resolve") await db.ResolveUnknownWorldsFromArchiveAsync(cancellation.Token);
            else if (operation == "replace") await db.UpsertArchiveWorldVisitsAsync(visits, cancellation.Token);
            else await db.LookupWorldNameFromArchiveAsync(At("10:30:00"), cancellation.Token);
        });
        Assert.Equal(cancellation.Token, error.CancellationToken); Assert.Equal(original, Snapshot());
        if (operation == "resolve") Assert.Equal(1, await db.ResolveUnknownWorldsFromArchiveAsync());
        else if (operation == "replace") await db.UpsertArchiveWorldVisitsAsync(visits);
        else Assert.Equal("保持", await db.LookupWorldNameFromArchiveAsync(At("10:30:00")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(199)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(401)]
    public async Task ReplaceArchivePersistsEveryVisitAcrossChunkBoundariesAndClearsOldVisits(int count)
    {
        await db.UpsertArchiveWorldVisitsAsync([Visit("旧履歴", "08:00:00", "09:00:00")]); await SeedPhoto("/photo/a.jpg", At("10:30:00"));
        await db.AddPhotoTagAsync("/photo/a.jpg", "保持"); var photos = Snapshot("photos", "tags", "photo_tags", "photo_user_data_backup");
        var visits = Enumerable.Range(0, count).Select(index => new ArchiveWorldVisitData
        {
            SourceLogName = $"output_log_{index:D4}.txt", WorldName = $"日本語\"{index}", JoinTime = At("10:00:00"), LeaveTime = index % 2 == 0 ? null : At("11:00:00"),
        }).ToArray();
        await db.UpsertArchiveWorldVisitsAsync(visits);
        var expected = JsonSerializer.Serialize(visits.Select(visit => new object?[] { visit.SourceLogName, visit.WorldName, visit.JoinTime, visit.LeaveTime }).ToArray());
        Assert.Equal(expected, ReadRows("SELECT source_log_name, world_name, join_time, leave_time FROM archive_world_visits ORDER BY source_log_name"));
        Assert.Equal(photos, Snapshot("photos", "tags", "photo_tags", "photo_user_data_backup"));
    }

    /// <summary>第2・第3chunkの途中で失敗させ、先に保存したchunkとDELETEも戻す。</summary>
    [Theory]
    [InlineData(201)]
    [InlineData(401)]
    public async Task ArchiveInsertFailureInALaterChunkRestoresThePreviousArchiveAndSequence(int failedRow)
    {
        await db.UpsertArchiveWorldVisitsAsync([Visit("旧履歴", "08:00:00", "09:00:00")]); await SeedPhoto("/photo/a.jpg", At("10:30:00")); var original = Snapshot();
        Execute($"CREATE TRIGGER reject_archive AFTER INSERT ON archive_world_visits WHEN (SELECT COUNT(*) FROM archive_world_visits)={failedRow} BEGIN SELECT RAISE(ABORT, '検証用の履歴保存失敗'); END;");
        var visits = Enumerable.Range(0, 402).Select(index => Visit($"履歴{index}", "10:00:00", "11:00:00")).ToArray();
        var error = await Assert.ThrowsAsync<SqliteException>(() => db.UpsertArchiveWorldVisitsAsync(visits));
        Assert.Equal(19, error.SqliteErrorCode); Assert.Equal(original, Snapshot());
        Execute("DROP TRIGGER reject_archive;"); await db.UpsertArchiveWorldVisitsAsync(visits);
        var expected = JsonSerializer.Serialize(visits.Select(visit => new object?[] { visit.SourceLogName, visit.WorldName, visit.JoinTime, visit.LeaveTime }).ToArray());
        Assert.Equal(expected, ReadRows("SELECT source_log_name, world_name, join_time, leave_time FROM archive_world_visits ORDER BY id"));
    }

    [Fact]
    public async Task ThrowingVisitEnumeratorIsDisposedAndDoesNotReplaceTheStoredArchive()
    {
        await db.UpsertArchiveWorldVisitsAsync([Visit("旧履歴", "08:00:00", "09:00:00")]); var original = Snapshot(); var disposed = false;
        IEnumerable<ArchiveWorldVisitData> BrokenVisits()
        {
            try { yield return Visit("未保存", "10:00:00", null); throw new IOException("検証用の列挙失敗"); }
            finally { disposed = true; }
        }
        var error = await Assert.ThrowsAsync<IOException>(() => db.UpsertArchiveWorldVisitsAsync(BrokenVisits()));
        Assert.Equal("検証用の列挙失敗", error.Message); Assert.True(disposed); Assert.Equal(original, Snapshot());
        await db.UpsertArchiveWorldVisitsAsync([Visit("再試行", "10:00:00", null)]); Assert.Equal("再試行", await db.LookupWorldNameFromArchiveAsync(At("10:30:00")));
    }

    /// <summary>input列挙中に取消しを要求し、DELETE後の取消し確認でも旧履歴を保持する。</summary>
    [Fact]
    public async Task CancellationDuringVisitEnumerationRollsBackArchiveReplacement()
    {
        await db.UpsertArchiveWorldVisitsAsync([Visit("旧履歴", "08:00:00", "09:00:00")]); var original = Snapshot();
        using var cancellation = new CancellationTokenSource(); var disposed = false;
        IEnumerable<ArchiveWorldVisitData> CancelledVisits()
        {
            try { yield return Visit("未保存", "10:00:00", null); cancellation.Cancel(); yield return Visit("未保存2", "11:00:00", null); }
            finally { disposed = true; }
        }
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.UpsertArchiveWorldVisitsAsync(CancelledVisits(), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken); Assert.True(disposed); Assert.Equal(original, Snapshot());
        await db.UpsertArchiveWorldVisitsAsync([Visit("再試行", "10:00:00", null)]); Assert.Equal("再試行", await db.LookupWorldNameFromArchiveAsync(At("10:30:00")));
    }

    private static string At(string time) => "2026-10-08 " + time;
    private static ArchiveWorldVisitData Visit(string world, string join, string? leave) => new()
    {
        SourceLogName = "output_log_fixture.txt", WorldName = world, JoinTime = At(join), LeaveTime = leave is null ? null : At(leave),
    };

    private Task SeedPhoto(string path, string timestamp, string? worldName = null, string? worldId = null, string? matchSource = null, int slot = 1)
        => db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = path, PhotoFilename = Path.GetFileName(path), Timestamp = timestamp, WorldName = worldName, WorldId = worldId,
            MatchSource = matchSource, SourceSlot = slot, LastModifiedUtc = "2026-10-08T00:00:00Z", Orientation = "landscape", ImageWidth = 1920, ImageHeight = 1080,
        });

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString); connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = sql; command.ExecuteNonQuery();
    }

    private string ReadRows(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql;
        using var reader = command.ExecuteReader(); var rows = new List<object?[]>();
        while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
        return JsonSerializer.Serialize(rows);
    }

    private string Snapshot(params string[] tables)
    {
        if (tables.Length == 0) tables = ["photos", "tags", "photo_tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence"];
        var snapshots = new Dictionary<string, string>();
        foreach (var table in tables)
        {
            Assert.Contains(table, new[] { "photos", "tags", "photo_tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence" });
            snapshots.Add(table, ReadRows($"SELECT * FROM {table} ORDER BY 1, 2"));
        }
        return JsonSerializer.Serialize(snapshots);
    }
}
