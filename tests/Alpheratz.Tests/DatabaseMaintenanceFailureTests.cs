using System.Collections;
using System.Text.Json;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>再スキャンの一括更新・欠落削除・スロット初期化が失敗した後の永続状態と再試行を確認する。</summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class DatabaseMaintenanceFailureTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Alpheratz.DatabaseMaintenanceFailure.Tests", Guid.NewGuid().ToString("N"));
    private readonly string? previousDataDirectory;
    private readonly AlpheratzDb db;
    private string DbPath => Path.Combine(directory, "maintenance.db");
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString();

    public DatabaseMaintenanceFailureTests()
    {
        Directory.CreateDirectory(directory); previousDataDirectory = AppPaths.DataDirOverrideForTests;
        AppPaths.DataDirOverrideForTests = Path.Combine(directory, "Data"); db = new AlpheratzDb(DbPath); db.Initialize();
    }

    public void Dispose()
    {
        AppPaths.DataDirOverrideForTests = previousDataDirectory;
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString());
        SqliteConnection.ClearPool(connection);
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Alpheratz.DatabaseMaintenanceFailure.Tests")) + Path.DirectorySeparatorChar, Path.GetFullPath(directory));
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task UpsertBatch_DatabaseFailureRollsBackUpdatedAndInsertedPhotosAndAllowsRetry()
    {
        await SeedAsync(); var before = Snapshot(); var unrelated = Snapshot("tags", "photo_tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence");
        var input = new[] { Photo("/primary/keep.jpg", filename: "renamed.jpg"), Photo("/primary/new.jpg"), Photo("/primary/fault.jpg") };
        Execute("CREATE TRIGGER reject_rescan BEFORE INSERT ON photos WHEN NEW.photo_path='/primary/fault.jpg' BEGIN SELECT RAISE(ABORT, '検証用の再スキャン保存失敗'); END;");

        var error = await Assert.ThrowsAsync<SqliteException>(() => db.UpsertPhotosAsync(input));

        Assert.Equal(19, error.SqliteErrorCode); Assert.Contains("検証用の再スキャン保存失敗", error.Message); Assert.Equal(before, Snapshot());
        Execute("DROP TRIGGER reject_rescan;"); Assert.Equal(3, await db.UpsertPhotosAsync(input));
        Assert.Equal("renamed.jpg", (await db.GetPhotoRecordAsync("/primary/keep.jpg"))!.photo_filename);
        Assert.NotNull(await db.GetPhotoRecordAsync("/primary/new.jpg")); Assert.NotNull(await db.GetPhotoRecordAsync("/primary/fault.jpg"));
        Assert.Equal(unrelated, Snapshot("tags", "photo_tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence"));
    }

    [Fact]
    public async Task UpsertBatch_CancellationAfterPartialEnumerationRollsBackRowsAndAllowsRetry()
    {
        await SeedAsync(); var before = Snapshot(); using var cancellation = new CancellationTokenSource();
        var input = Enumerable.Range(0, 140).Select(index => Photo(index == 0 ? "/primary/keep.jpg" : $"/rescan/{index:D3}.jpg", filename: "rescan.jpg")).ToArray();
        var interrupted = new InterruptedPhotoBatch(input, cancellation);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.UpsertPhotosAsync(interrupted, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken); Assert.True(interrupted.Disposed); Assert.Equal(129, interrupted.EnumeratedCount);
        Assert.Equal(before, Snapshot()); Assert.Equal(140, await db.UpsertPhotosAsync(input));
        Assert.Equal("rescan.jpg", (await db.GetPhotoRecordAsync("/primary/keep.jpg"))!.photo_filename);
        Assert.Equal(142, (await db.GetExistingPhotosAsync()).Count);
        Assert.Equal(new string('a', 64), (await db.GetPhotoRecordAsync("/primary/keep.jpg", includePhash: true))!.phash);
        Assert.Equal(["保持"], await db.GetPhotoTagsAsync("/secondary/keep.jpg"));
    }

    [Fact]
    public async Task MissingPhotos_LaterChunkFailureRestoresPhotosAndAllTagLinksThenRetries()
    {
        await SeedAsync(); var input = Enumerable.Range(0, 1001).Select(index => Photo($"/missing/{index:D4}.jpg")).ToArray();
        await db.UpsertPhotosAsync(input);
        // 実際の500件単位の削除を複数回通し、先行するタグ削除も同じtransactionで保持する。
        Execute("INSERT INTO photo_tags(photo_path, tag_id) SELECT photo_path, (SELECT id FROM tags WHERE name='保持') FROM photos WHERE photo_path LIKE '/missing/%';");
        var before = Snapshot(); var masters = Snapshot("tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence");
        Execute("CREATE TRIGGER reject_missing_photo BEFORE DELETE ON photos WHEN OLD.photo_path='/missing/1000.jpg' BEGIN SELECT RAISE(ABORT, '検証用の欠落写真削除失敗'); END;");

        var error = await Assert.ThrowsAsync<SqliteException>(() => db.DeleteMissingPhotosAsync(["/PRIMARY/KEEP.JPG", "/primary/delete.jpg"], [1]));

        Assert.Equal(19, error.SqliteErrorCode); Assert.Contains("検証用の欠落写真削除失敗", error.Message); Assert.Equal(before, Snapshot());
        Execute("DROP TRIGGER reject_missing_photo;");
        Assert.Equal(1001, await db.DeleteMissingPhotosAsync(["/PRIMARY/KEEP.JPG", "/primary/delete.jpg"], [1]));
        Assert.Equal(new[] { "/primary/delete.jpg", "/primary/keep.jpg", "/secondary/keep.jpg" }, (await db.GetExistingPhotosAsync()).Keys.Order().ToArray());
        Assert.Equal(["保持"], await db.GetPhotoTagsAsync("/secondary/keep.jpg")); Assert.Equal(["保持"], await db.GetPhotoTagsAsync("/primary/delete.jpg"));
        Assert.Equal(masters, Snapshot("tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence"));
        Assert.Equal(0, await db.DeleteMissingPhotosAsync(["/primary/keep.jpg", "/primary/delete.jpg"], [1]));
    }

    [Fact]
    public async Task SlotReset_DatabaseFailurePreservesTagLinksAndCacheThenSameSlotRetries()
    {
        await SeedAsync(); var (primary, secondary) = CreateCaches(); var before = Snapshot();
        var masters = Snapshot("tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence");
        Execute("CREATE TRIGGER reject_slot_reset BEFORE DELETE ON photos WHEN OLD.source_slot=1 BEGIN SELECT RAISE(ABORT, '検証用のスロット削除失敗'); END;");

        var error = await Assert.ThrowsAsync<SqliteException>(() => db.ResetPhotoCacheBySlotAsync(1));

        Assert.Equal(19, error.SqliteErrorCode); Assert.Contains("検証用のスロット削除失敗", error.Message); Assert.Equal(before, Snapshot());
        Assert.Equal("slot 1 cache"u8.ToArray(), File.ReadAllBytes(primary)); Assert.Equal("slot 2 cache"u8.ToArray(), File.ReadAllBytes(secondary));
        Execute("DROP TRIGGER reject_slot_reset;"); await db.ResetPhotoCacheBySlotAsync(1);
        Assert.Equal(["/secondary/keep.jpg"], (await db.GetExistingPhotosAsync()).Keys); Assert.Equal(["保持"], await db.GetPhotoTagsAsync("/secondary/keep.jpg"));
        Assert.True(Directory.Exists(Path.GetDirectoryName(primary))); Assert.Empty(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(primary)!));
        Assert.Equal("slot 2 cache"u8.ToArray(), File.ReadAllBytes(secondary)); Assert.Equal(masters, Snapshot("tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence"));
    }

    [Fact]
    public async Task SlotReset_LockedCacheKeepsCommittedDatabaseAndRetriesCleanupAfterRelease()
    {
        await SeedAsync(); var (primary, secondary) = CreateCaches(); var secondaryRows = PhotoSnapshot("/secondary/keep.jpg");
        using (var locked = new FileStream(primary, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await db.ResetPhotoCacheBySlotAsync(1);
            Assert.Equal(["/secondary/keep.jpg"], (await db.GetExistingPhotosAsync()).Keys);
            var bytes = new byte[locked.Length]; Assert.Equal(bytes.Length, locked.Read(bytes)); Assert.Equal("slot 1 cache"u8.ToArray(), bytes);
            Assert.Equal(secondaryRows, PhotoSnapshot("/secondary/keep.jpg")); Assert.Equal("slot 2 cache"u8.ToArray(), File.ReadAllBytes(secondary));
        }
        await db.ResetPhotoCacheBySlotAsync(1);
        Assert.False(File.Exists(primary)); Assert.Equal(secondaryRows, PhotoSnapshot("/secondary/keep.jpg"));
        Assert.Equal("slot 2 cache"u8.ToArray(), File.ReadAllBytes(secondary)); Assert.Equal(["保持"], await db.GetPhotoTagsAsync("/secondary/keep.jpg"));
    }

    [Theory]
    [InlineData("upsert")]
    [InlineData("delete-missing")]
    [InlineData("reset-slot")]
    public async Task PreCancelledMaintenance_PreservesAllRowsAndCachesAndAllowsRetry(string operation)
    {
        await SeedAsync(); var (primary, secondary) = CreateCaches(); var before = Snapshot(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunMaintenanceAsync(operation, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken); Assert.Equal(before, Snapshot());
        Assert.Equal("slot 1 cache"u8.ToArray(), File.ReadAllBytes(primary)); Assert.Equal("slot 2 cache"u8.ToArray(), File.ReadAllBytes(secondary));
        await RunMaintenanceAsync(operation, CancellationToken.None);
        Assert.NotNull(await db.GetPhotoRecordAsync("/secondary/keep.jpg")); Assert.Equal(["保持"], await db.GetPhotoTagsAsync("/secondary/keep.jpg"));
        Assert.Equal("slot 2 cache"u8.ToArray(), File.ReadAllBytes(secondary));
        if (operation == "upsert") Assert.NotNull(await db.GetPhotoRecordAsync("/primary/new.jpg"));
        if (operation == "delete-missing") { Assert.Null(await db.GetPhotoRecordAsync("/primary/delete.jpg")); Assert.NotNull(await db.GetPhotoRecordAsync("/primary/keep.jpg")); }
        if (operation == "reset-slot") { Assert.Equal(["/secondary/keep.jpg"], (await db.GetExistingPhotosAsync()).Keys); Assert.False(File.Exists(primary)); }
    }

    private async Task RunMaintenanceAsync(string operation, CancellationToken token)
    {
        switch (operation)
        {
            case "upsert": await db.UpsertPhotosAsync([Photo("/primary/new.jpg")], token); break;
            case "delete-missing": Assert.Equal(1, await db.DeleteMissingPhotosAsync(["/primary/keep.jpg"], [1], token)); break;
            case "reset-slot": await db.ResetPhotoCacheBySlotAsync(1, token); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private async Task SeedAsync()
    {
        await db.UpsertPhotosAsync([Photo("/primary/keep.jpg"), Photo("/primary/delete.jpg"), Photo("/secondary/keep.jpg", 2)]);
        await db.UpdatePhotoPhashAsync("/primary/keep.jpg", new string('a', 64)); await db.SetPhotoFavoriteAsync("/primary/keep.jpg", true);
        await db.AddPhotoTagAsync("/primary/delete.jpg", "保持"); await db.AddPhotoTagAsync("/secondary/keep.jpg", "保持");
        Execute("INSERT INTO photo_user_data_backup VALUES('saved.jpg',1,'[\"保持\"]'); INSERT INTO archive_world_visits(source_log_name,world_name,join_time,leave_time) VALUES('保持.txt','保持したworld','2026-10-08 09:00:00',NULL);");
    }

    private (string Primary, string Secondary) CreateCaches()
    {
        Assert.Equal(Path.Combine(directory, "Data"), AppPaths.GetDataDir());
        var primary = Path.Combine(AppPaths.GetImgCacheDir(1)!, "primary.bin"); var secondary = Path.Combine(AppPaths.GetImgCacheDir(2)!, "secondary.bin");
        File.WriteAllBytes(primary, "slot 1 cache"u8.ToArray()); File.WriteAllBytes(secondary, "slot 2 cache"u8.ToArray()); return (primary, secondary);
    }

    private static PhotoUpsertData Photo(string path, long slot = 1, string? filename = null) => new()
    {
        PhotoPath = path, PhotoFilename = filename ?? Path.GetFileName(path), Timestamp = "2026-10-08 10:00:00", LastModifiedUtc = "2026-10-08T01:00:00Z",
        WorldId = "wrld_preserved", WorldName = "保持したworld", Orientation = "landscape", ImageWidth = 1920, ImageHeight = 1080, SourceSlot = slot, MatchSource = "metadata",
    };

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString); connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON;" + sql; command.ExecuteNonQuery();
    }

    /// <summary>productionの読込DTOを使わず、全columnと採番stateを独立して照合する。</summary>
    private string Snapshot(params string[] tables)
    {
        if (tables.Length == 0) tables = ["photos", "tags", "photo_tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence"];
        using var connection = new SqliteConnection(ConnectionString); connection.Open(); var content = new Dictionary<string, List<object?[]>>();
        foreach (var table in tables)
        {
            Assert.Contains(table, new[] { "photos", "tags", "photo_tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence" });
            using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY 1,2"; using var reader = command.ExecuteReader();
            var rows = new List<object?[]>(); while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
            content.Add(table, rows);
        }
        return JsonSerializer.Serialize(content);
    }

    private string PhotoSnapshot(string path)
    {
        using var connection = new SqliteConnection(ConnectionString); connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM photos WHERE photo_path=@path"; command.Parameters.AddWithValue("@path", path); using var reader = command.ExecuteReader(); Assert.True(reader.Read());
        return JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
    }

    private sealed class InterruptedPhotoBatch(PhotoUpsertData[] rows, CancellationTokenSource cancellation) : IReadOnlyList<PhotoUpsertData>
    {
        public int Count => rows.Length;
        public PhotoUpsertData this[int index] => rows[index];
        public int EnumeratedCount { get; private set; }
        public bool Disposed { get; private set; }
        public IEnumerator<PhotoUpsertData> GetEnumerator()
        {
            try
            {
                for (var index = 0; index < rows.Length; index++)
                {
                    if (index == 128) cancellation.Cancel(); EnumeratedCount++; yield return rows[index];
                }
            }
            finally { Disposed = true; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
