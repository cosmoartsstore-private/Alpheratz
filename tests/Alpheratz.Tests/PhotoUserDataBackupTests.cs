using System.Text.Json;
using Alpheratz.Core.Database;
using Alpheratz.Services;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>現行schemaの専用DBで、写真情報の保存・再読込・復元と失敗時の永続状態を確認する。</summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class PhotoUserDataBackupTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Alpheratz.PhotoUserData.Tests", Guid.NewGuid().ToString("N"));
    private readonly AlpheratzDb db;
    private readonly PhotoService service;
    private string DbPath => Path.Combine(directory, "photos.db");
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString();

    public PhotoUserDataBackupTests()
    {
        Directory.CreateDirectory(directory); db = new AlpheratzDb(DbPath); db.Initialize(); service = new PhotoService(db);
    }

    public void Dispose()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Alpheratz.PhotoUserData.Tests")) + Path.DirectorySeparatorChar, Path.GetFullPath(directory));
        using var connection = new SqliteConnection(ConnectionString); SqliteConnection.ClearPool(connection);
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task BackupMergesActiveFilenamesAcrossSlotsWithoutChangingPhotosOrTags()
    {
        await SeedPhoto("/slot1/shared.jpg", "Shared.JPG"); await SeedPhoto("/slot2/shared.jpg", "shared.jpg", slot: 2, favorite: true);
        await SeedPhoto("/slot1/plain.jpg", "plain.jpg"); await SeedPhoto("/missing/shared.jpg", "SHARED.jpg", missing: true);
        await SeedPhoto("/missing/other.jpg", "other.jpg", missing: true, favorite: true);
        await db.AddPhotoTagsAsync("/slot1/shared.jpg", ["Alpha", "Zeta", "昼 \"夜\"\n雪"]);
        await db.AddPhotoTagsAsync("/slot2/shared.jpg", ["alpha", "Zeta"]);
        await db.AddPhotoTagAsync("/missing/shared.jpg", "欠損写真のみ");
        Execute("UPDATE photos SET is_favorite = NULL WHERE photo_filename = 'plain.jpg';");
        var original = Snapshot("photos", "tags", "photo_tags");

        var result = await db.CreatePhotoUserDataBackupAsync();

        Assert.Equal(new PhotoUserDataBackupResult(3, 2), result); Assert.Equal(1, result.ConsolidatedPhotoCount);
        var backups = ReadBackups(); Assert.Equal(["plain.jpg", "Shared.JPG"], backups.Select(row => row.Filename).ToArray());
        Assert.False(backups[0].Favorite); Assert.Empty(backups[0].Tags);
        Assert.True(backups[1].Favorite); Assert.Equal(["Alpha", "Zeta", "昼 \"夜\"\n雪"], backups[1].Tags);
        Assert.Equal(original, Snapshot("photos", "tags", "photo_tags"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackupWithNoActivePhotosPreservesThePreviousBackup(bool missingOnly)
    {
        SeedBackup("saved.jpg", true, "[\"保持\"]");
        if (missingOnly) await SeedPhoto("/missing/current.jpg", "current.jpg", missing: true);
        var original = Snapshot();
        var result = await db.CreatePhotoUserDataBackupAsync();
        Assert.Equal(new PhotoUserDataBackupResult(0, 0), result); Assert.Equal(original, Snapshot());
    }

    [Fact]
    public async Task BackupReplacesOldEntriesAndRepeatedBackupKeepsTheSameStoredContent()
    {
        SeedBackup("obsolete.jpg", true, "[\"旧タグ\"]"); await SeedPhoto("/photo/current.jpg", "current.jpg");
        await db.AddPhotoTagAsync("/photo/current.jpg", "現在"); await db.CreatePhotoUserDataBackupAsync();
        var first = Snapshot(); var only = Assert.Single(ReadBackups()); Assert.Equal("current.jpg", only.Filename);
        Assert.False(only.Favorite); Assert.Equal(["現在"], only.Tags);
        Assert.Equal(new PhotoUserDataBackupResult(1, 1), await db.CreatePhotoUserDataBackupAsync()); Assert.Equal(first, Snapshot());

        await db.SetPhotoFavoriteAsync("/photo/current.jpg", true); await db.RemovePhotoTagAsync("/photo/current.jpg", "現在");
        await db.CreatePhotoUserDataBackupAsync(); only = Assert.Single(ReadBackups());
        Assert.True(only.Favorite); Assert.Empty(only.Tags);
    }

    [Fact]
    public async Task SavedUserDataSurvivesPhotoRemovalAndRestoresRescannedPathsAndSlots()
    {
        await SeedPhoto("/old/original.jpg", "original.jpg", favorite: true); await db.AddPhotoTagAsync("/old/original.jpg", "保存済み");
        await db.CreatePhotoUserDataBackupAsync(); var saved = Snapshot("photo_user_data_backup");
        await db.DeletePhotosByPathsAsync(["/old/original.jpg"]); Assert.Null(await db.GetPhotoRecordAsync("/old/original.jpg"));
        Assert.Equal(saved, Snapshot("photo_user_data_backup"));
        await SeedPhoto("/new/ORIGINAL.jpg", "ORIGINAL.jpg", slot: 2);

        Assert.Equal(new PhotoUserDataRestoreResult(1, 1, 0, 1), await db.RestorePhotoUserDataBackupAsync());
        await AssertPhoto("/new/ORIGINAL.jpg", true, ["保存済み"]); Assert.Equal(saved, Snapshot("photo_user_data_backup"));
    }

    /// <summary>DELETE後と複数行INSERTの途中で失敗させ、旧バックアップの消失を検出する。</summary>
    [Theory]
    [InlineData("AFTER DELETE ON photo_user_data_backup")]
    [InlineData("AFTER INSERT ON photo_user_data_backup WHEN (SELECT COUNT(*) FROM photo_user_data_backup) = 2")]
    public async Task BackupWriteFailureRollsBackTheWholeReplacementAndAllowsRetry(string triggerPoint)
    {
        await SeedPhoto("/photo/a.jpg", "a.jpg", favorite: true); await SeedPhoto("/photo/b.jpg", "b.jpg");
        await db.AddPhotoTagAsync("/photo/a.jpg", "保持"); SeedBackup("previous.jpg", false, "[\"以前\"]");
        var original = Snapshot(); Execute($"CREATE TRIGGER reject_backup {triggerPoint} BEGIN SELECT RAISE(ABORT, '検証用のバックアップ失敗'); END;");

        var error = await Assert.ThrowsAsync<SqliteException>(() => db.CreatePhotoUserDataBackupAsync());
        Assert.Equal(19, error.SqliteErrorCode); Assert.Equal(original, Snapshot());
        Execute("DROP TRIGGER reject_backup;"); Assert.Equal(new PhotoUserDataBackupResult(2, 2), await db.CreatePhotoUserDataBackupAsync());
        Assert.Equal(["a.jpg", "b.jpg"], ReadBackups().Select(row => row.Filename).ToArray());
    }

    [Fact]
    public async Task RestoreWithoutBackupLeavesAllCurrentUserDataUntouched()
    {
        await SeedPhoto("/photo/a.jpg", "a.jpg", favorite: true); await db.AddPhotoTagAsync("/photo/a.jpg", "現在");
        var original = Snapshot(); Assert.Equal(new PhotoUserDataRestoreResult(0, 0, 0, 0), await db.RestorePhotoUserDataBackupAsync());
        Assert.Equal(original, Snapshot());
    }

    [Fact]
    public async Task RestoreWithoutActiveMatchesDoesNotCreateTagsOrChangeMissingPhotos()
    {
        SeedBackup("same.jpg", false, "[\"復元対象外\"]"); SeedBackup("absent.jpg", true, "[\"未登録\"]");
        await SeedPhoto("/missing/same.jpg", "SAME.jpg", favorite: true, missing: true);
        await SeedPhoto("/current/different.jpg", "different.jpg", favorite: true); await db.AddPhotoTagAsync("/missing/same.jpg", "保持");
        var original = Snapshot(); Assert.Equal(new PhotoUserDataRestoreResult(2, 0, 0, 0), await db.RestorePhotoUserDataBackupAsync());
        Assert.Equal(original, Snapshot());
    }

    [Fact]
    public async Task RestoreOverwritesBothFavoriteValuesAndAddsTagsToAllActiveMatchesOnly()
    {
        await SeedPhoto("/slot1/shared.jpg", "SHARED.jpg"); await SeedPhoto("/slot2/shared.jpg", "shared.JPG", slot: 2);
        await SeedPhoto("/slot1/reset.jpg", "reset.jpg", favorite: true); await SeedPhoto("/other.jpg", "other.jpg", favorite: true);
        await SeedPhoto("/missing/shared.jpg", "shared.jpg", missing: true);
        await db.AddPhotoTagsAsync("/slot1/shared.jpg", ["Alpha", "現在"]); await db.AddPhotoTagAsync("/other.jpg", "対象外");
        SeedBackup("shared.jpg", true, "[\"alpha\",\"Alpha\",\"新規\",\"alpha\"]");
        SeedBackup("RESET.JPG", false, "[\"新規\",\"Beta\"]"); SeedBackup("absent.jpg", true, "[\"作成しない\"]");
        var saved = Snapshot("photo_user_data_backup");

        Assert.Equal(new PhotoUserDataRestoreResult(3, 3, 2, 5), await db.RestorePhotoUserDataBackupAsync());
        await AssertPhoto("/slot1/shared.jpg", true, ["Alpha", "新規", "現在"]);
        await AssertPhoto("/slot2/shared.jpg", true, ["Alpha", "新規"]); await AssertPhoto("/slot1/reset.jpg", false, ["Beta", "新規"]);
        await AssertPhoto("/other.jpg", true, ["対象外"]); await AssertPhoto("/missing/shared.jpg", false, []);
        Assert.DoesNotContain("作成しない", await db.GetAllTagsAsync()); Assert.DoesNotContain("alpha", await db.GetAllTagsAsync());
        Assert.Equal(saved, Snapshot("photo_user_data_backup")); var restored = Snapshot();
        Assert.Equal(new PhotoUserDataRestoreResult(3, 3, 0, 0), await db.RestorePhotoUserDataBackupAsync()); Assert.Equal(restored, Snapshot());
    }

    [Fact]
    public async Task RestoreIgnoresNonTextAndBlankEntriesAndPreservesStoredTextExactly()
    {
        await SeedPhoto("/photo/a.jpg", "a.jpg");
        SeedBackup("a.jpg", true, "[null,42,true,{},[],\"\",\"   \",\"quoted \\\"tag\\\"\",\"日本語\",\" spaced \"]");
        Assert.Equal(new PhotoUserDataRestoreResult(1, 1, 3, 3), await db.RestorePhotoUserDataBackupAsync());
        await AssertPhoto("/photo/a.jpg", true, [" spaced ", "quoted \"tag\"", "日本語"]);
    }

    /// <summary>お気に入り・タグマスタ・関連行の各書込後に失敗し、先行する変更も戻す。</summary>
    [Theory]
    [InlineData("AFTER UPDATE OF is_favorite ON photos")]
    [InlineData("AFTER INSERT ON tags")]
    [InlineData("AFTER INSERT ON photo_tags")]
    public async Task RestoreStageFailureRollsBackFavoritesTagsAndLinksBeforeRetry(string triggerPoint)
    {
        await SeedPhoto("/photo/a.jpg", "a.jpg"); await SeedPhoto("/photo/b.jpg", "b.jpg", favorite: true);
        await db.AddPhotoTagAsync("/photo/a.jpg", "現在"); SeedBackup("a.jpg", true, "[\"追加\"]"); SeedBackup("b.jpg", false, "[\"追加\"]");
        var original = Snapshot(); Execute($"CREATE TRIGGER reject_restore {triggerPoint} BEGIN SELECT RAISE(ABORT, '検証用の復元失敗'); END;");
        var error = await Assert.ThrowsAsync<SqliteException>(() => db.RestorePhotoUserDataBackupAsync());
        Assert.Equal(19, error.SqliteErrorCode); Assert.Equal(original, Snapshot());
        Execute("DROP TRIGGER reject_restore;"); Assert.Equal(new PhotoUserDataRestoreResult(2, 2, 1, 2), await db.RestorePhotoUserDataBackupAsync());
        await AssertPhoto("/photo/a.jpg", true, ["現在", "追加"]); await AssertPhoto("/photo/b.jpg", false, ["追加"]);
    }

    /// <summary>CHECKを専用接続だけで無効にし、外部破損によるJSON読込失敗を注入する。</summary>
    [Fact]
    public async Task CorruptBackupJsonFailsWithoutCommittingTheEarlierFavoriteUpdate()
    {
        await SeedPhoto("/photo/a.jpg", "a.jpg"); await db.AddPhotoTagAsync("/photo/a.jpg", "保持");
        Execute("PRAGMA ignore_check_constraints=ON; INSERT INTO photo_user_data_backup VALUES ('a.jpg', 1, 'invalid-json'); PRAGMA ignore_check_constraints=OFF;");
        var original = Snapshot(); var error = await Assert.ThrowsAsync<SqliteException>(() => db.RestorePhotoUserDataBackupAsync());
        Assert.Equal(1, error.SqliteErrorCode); Assert.Equal(original, Snapshot());
        Execute("UPDATE photo_user_data_backup SET tags='[\"復旧\"]';");
        Assert.Equal(new PhotoUserDataRestoreResult(1, 1, 1, 1), await db.RestorePhotoUserDataBackupAsync());
        await AssertPhoto("/photo/a.jpg", true, ["保持", "復旧"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledDatabaseOperationPreservesStorageAndAllowsANewOperation(bool restore)
    {
        await SeedPhoto("/photo/a.jpg", "a.jpg"); SeedBackup("a.jpg", true, "[\"保存済み\"]"); var original = Snapshot();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (restore) await db.RestorePhotoUserDataBackupAsync(cancellation.Token); else await db.CreatePhotoUserDataBackupAsync(cancellation.Token);
        });
        Assert.Equal(original, Snapshot());
        if (restore)
        {
            Assert.Equal(new PhotoUserDataRestoreResult(1, 1, 1, 1), await db.RestorePhotoUserDataBackupAsync());
            await AssertPhoto("/photo/a.jpg", true, ["保存済み"]);
        }
        else
            Assert.Equal(new PhotoUserDataBackupResult(1, 1), await db.CreatePhotoUserDataBackupAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServiceCancellationDoesNotRetainTheWriteGate(bool restore)
    {
        await SeedPhoto("/photo/a.jpg", "a.jpg"); SeedBackup("a.jpg", true, "[\"保存済み\"]"); var original = Snapshot();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (restore) await service.RestorePhotoUserDataBackupAsync(cancellation.Token); else await service.CreatePhotoUserDataBackupAsync(cancellation.Token);
        });
        Assert.Equal(original, Snapshot());
        if (restore)
        {
            Assert.Equal(new PhotoUserDataRestoreResult(1, 1, 1, 1), await service.RestorePhotoUserDataBackupAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            await AssertPhoto("/photo/a.jpg", true, ["保存済み"]);
        }
        else
            Assert.Equal(new PhotoUserDataBackupResult(1, 1), await service.CreatePhotoUserDataBackupAsync().WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServicePropagatesDatabaseFailureAndReleasesTheWriteGateForRetry(bool restore)
    {
        await SeedPhoto("/photo/a.jpg", "a.jpg"); SeedBackup("a.jpg", true, "[\"保存済み\"]"); var original = Snapshot();
        var table = restore ? "tags" : "photo_user_data_backup";
        Execute($"CREATE TRIGGER reject_service AFTER INSERT ON {table} BEGIN SELECT RAISE(ABORT, '検証用のservice失敗'); END;");
        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            if (restore) await service.RestorePhotoUserDataBackupAsync(); else await service.CreatePhotoUserDataBackupAsync();
        });
        Assert.Equal(original, Snapshot()); Execute("DROP TRIGGER reject_service;");
        if (restore)
        {
            Assert.Equal(new PhotoUserDataRestoreResult(1, 1, 1, 1), await service.RestorePhotoUserDataBackupAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            await AssertPhoto("/photo/a.jpg", true, ["保存済み"]);
        }
        else
        {
            Assert.Equal(new PhotoUserDataBackupResult(1, 1), await service.CreatePhotoUserDataBackupAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            var saved = Assert.Single(ReadBackups()); Assert.False(saved.Favorite); Assert.Empty(saved.Tags);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseOpenFailurePreservesStorageAndAllowsRetryAfterTheFileIsUnlocked(bool restore)
    {
        await SeedPhoto("/photo/a.jpg", "a.jpg"); SeedBackup("a.jpg", true, "[\"保存済み\"]"); var original = Snapshot();
        using (var ownPool = new SqliteConnection(ConnectionString)) SqliteConnection.ClearPool(ownPool);
        using (var locked = new FileStream(DbPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = await Assert.ThrowsAsync<SqliteException>(async () =>
            {
                if (restore) await db.RestorePhotoUserDataBackupAsync(); else await db.CreatePhotoUserDataBackupAsync();
            });
            Assert.Equal(14, error.SqliteErrorCode);
        }
        Assert.Equal(original, Snapshot());
        if (restore)
        {
            Assert.Equal(new PhotoUserDataRestoreResult(1, 1, 1, 1), await db.RestorePhotoUserDataBackupAsync());
            await AssertPhoto("/photo/a.jpg", true, ["保存済み"]);
        }
        else
            Assert.Equal(new PhotoUserDataBackupResult(1, 1), await db.CreatePhotoUserDataBackupAsync());
    }

    private async Task SeedPhoto(string path, string filename, int slot = 1, bool favorite = false, bool missing = false)
    {
        await db.UpsertPhotoAsync(new PhotoUpsertData { PhotoPath = path, PhotoFilename = filename, Timestamp = "2026-10-08 10:00:00", SourceSlot = slot });
        await db.SetPhotoFavoriteAsync(path, favorite);
        if (missing) Execute("UPDATE photos SET is_missing=1 WHERE photo_path=@path", ("@path", path));
    }

    private void SeedBackup(string filename, bool favorite, string tags)
        => Execute("INSERT INTO photo_user_data_backup (photo_filename, is_favorite, tags) VALUES (@filename, @favorite, @tags)",
            ("@filename", filename), ("@favorite", favorite ? 1 : 0), ("@tags", tags));

    private async Task AssertPhoto(string path, bool favorite, string[] tags)
    {
        var photo = Assert.IsType<Alpheratz.Models.PhotoRecordDto>(await db.GetPhotoRecordAsync(path));
        Assert.Equal(favorite, photo.is_favorite);
        Assert.Equal(tags.OrderBy(tag => tag, StringComparer.Ordinal), (await db.GetPhotoTagsAsync(path)).OrderBy(tag => tag, StringComparer.Ordinal));
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new SqliteConnection(ConnectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        command.ExecuteNonQuery();
    }

    private sealed record BackupRow(string Filename, bool Favorite, string[] Tags);

    private BackupRow[] ReadBackups()
    {
        using var connection = new SqliteConnection(ConnectionString); connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT photo_filename, is_favorite, tags FROM photo_user_data_backup ORDER BY photo_filename COLLATE NOCASE";
        using var reader = command.ExecuteReader(); var rows = new List<BackupRow>();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetInt64(1) != 0, JsonSerializer.Deserialize<string[]>(reader.GetString(2))!));
        return rows.ToArray();
    }

    /// <summary>結果DTOやproductionの変換処理を使わず、永続行と採番stateを直接比較する。</summary>
    private string Snapshot(params string[] tables)
    {
        if (tables.Length == 0) tables = ["photos", "tags", "photo_tags", "photo_user_data_backup", "sqlite_sequence"];
        using var connection = new SqliteConnection(ConnectionString); connection.Open(); var content = new Dictionary<string, List<object?[]>>();
        foreach (var table in tables)
        {
            Assert.Contains(table, new[] { "photos", "tags", "photo_tags", "photo_user_data_backup", "sqlite_sequence" });
            using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY 1, 2";
            using var reader = command.ExecuteReader(); var rows = new List<object?[]>();
            while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
            content.Add(table, rows);
        }
        return JsonSerializer.Serialize(content);
    }
}
