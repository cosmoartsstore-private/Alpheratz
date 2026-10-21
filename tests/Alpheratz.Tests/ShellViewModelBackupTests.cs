using Alpheratz.Core.Database;
using Alpheratz.Shared.Models;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>実際のserviceとDBを接続し、バックアップ・復元の保存結果と公開state・通知を照合する。</summary>
public sealed partial class ShellViewModelBehaviorTests
{
    [Fact]
    public async Task BackupReportsSourcePhotosAndConsolidatedFilenamesWithoutRefreshingTheGallery()
    {
        await using var harness = ShellTestHarness.Create(); await SeedShellPhoto(harness, 1); await SeedShellPhoto(harness, 2);
        await harness.Db.SetPhotoFavoriteAsync("/slot2/retain.jpg", true); await harness.Db.AddPhotoTagAsync("/slot1/retain.jpg", "保存済み");
        var displayed = Thumb("/display/old.jpg", "old.jpg", "2026-10-08 10:00:00"); harness.ViewModel.galleryViewModel.photosState.setPhotos([displayed]);
        await harness.ViewModel.createPhotoUserDataBackup();
        AssertShellToast(harness, "写真2枚を、ファイル名1件としてバックアップしました。", ToastType.info);
        Assert.Equal(("retain.jpg", true, "[\"保存済み\"]"), Assert.Single(ReadShellBackups(harness)));
        Assert.Same(displayed, Assert.Single(harness.ViewModel.galleryViewModel.photosState.photos));
        Assert.False((await harness.Db.GetPhotoRecordAsync("/slot1/retain.jpg"))!.is_favorite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackupWithNoActivePhotosReportsThatThePreviousBackupWasPreserved(bool missingOnly)
    {
        await using var harness = ShellTestHarness.Create(); SeedShellBackup(harness, "saved.jpg", true, "[\"保持\"]");
        if (missingOnly) { await SeedShellPhoto(harness, 1); ExecuteShellSql(harness, "UPDATE photos SET is_missing=1;"); }
        var saved = ReadShellBackups(harness); await harness.ViewModel.createPhotoUserDataBackup();
        AssertShellToast(harness, "バックアップできる写真がありません。以前のバックアップは変更していません。", ToastType.info);
        Assert.Equal(saved, ReadShellBackups(harness));
    }

    [Fact]
    public async Task BackupFailureReportsFailureKeepsThePreviousBackupAndSupportsRetry()
    {
        await using var harness = ShellTestHarness.Create(); await SeedShellPhoto(harness, 1); SeedShellBackup(harness, "saved.jpg", true, "[\"保持\"]");
        var saved = ReadShellBackups(harness);
        ExecuteShellSql(harness, "CREATE TRIGGER reject_shell_backup AFTER DELETE ON photo_user_data_backup BEGIN SELECT RAISE(ABORT, '検証用の保存失敗'); END;");
        await harness.ViewModel.createPhotoUserDataBackup();
        AssertShellToast(harness, "お気に入りとタグをバックアップできませんでした。もう一度実行してください。", ToastType.error);
        Assert.Equal(saved, ReadShellBackups(harness)); Assert.NotNull(await harness.Db.GetPhotoRecordAsync("/slot1/retain.jpg"));
        ExecuteShellSql(harness, "DROP TRIGGER reject_shell_backup;"); harness.ToastService.toasts.Clear(); await harness.ViewModel.createPhotoUserDataBackup();
        AssertShellToast(harness, "写真1枚を、ファイル名1件としてバックアップしました。", ToastType.info);
        Assert.Equal(("retain.jpg", false, "[]"), Assert.Single(ReadShellBackups(harness)));
    }

    [Fact]
    public async Task RestoreWithoutBackupReportsMissingBackupAndKeepsCurrentStorageAndDisplay()
    {
        await using var harness = ShellTestHarness.Create(); await SeedShellPhoto(harness, 1); await harness.Db.SetPhotoFavoriteAsync("/slot1/retain.jpg", true);
        await harness.Db.AddPhotoTagAsync("/slot1/retain.jpg", "現在"); var displayed = Thumb("/slot1/retain.jpg", "retain.jpg", "2026-06-05 10:00:00");
        harness.ViewModel.galleryViewModel.photosState.setPhotos([displayed]); harness.ViewModel.tagMasterViewModel.masterTags.Add("表示保持");
        await harness.ViewModel.restorePhotoUserDataBackup();
        AssertShellToast(harness, "復元に使えるバックアップがありません。先にバックアップを作成してください。", ToastType.info);
        Assert.True((await harness.Db.GetPhotoRecordAsync("/slot1/retain.jpg"))!.is_favorite); Assert.Equal(["現在"], await harness.Db.GetPhotoTagsAsync("/slot1/retain.jpg"));
        Assert.Same(displayed, Assert.Single(harness.ViewModel.galleryViewModel.photosState.photos)); Assert.Equal(["表示保持"], harness.ViewModel.tagMasterViewModel.masterTags);
    }

    [Fact]
    public async Task RestoreWithOnlyMissingMatchesReportsNoMatchesWithoutRefreshingOrCreatingTags()
    {
        await using var harness = ShellTestHarness.Create(); await SeedShellPhoto(harness, 1); ExecuteShellSql(harness, "UPDATE photos SET is_missing=1;");
        SeedShellBackup(harness, "RETAIN.JPG", true, "[\"作成しない\"]"); harness.ViewModel.tagMasterViewModel.masterTags.Add("表示保持");
        await harness.ViewModel.restorePhotoUserDataBackup();
        AssertShellToast(harness, "バックアップとファイル名が一致する写真はありませんでした。", ToastType.info);
        Assert.False((await harness.Db.GetPhotoRecordAsync("/slot1/retain.jpg"))!.is_favorite); Assert.Empty(await harness.Db.GetAllTagsAsync());
        Assert.Equal(["表示保持"], harness.ViewModel.tagMasterViewModel.masterTags); Assert.Single(ReadShellBackups(harness));
    }

    [Fact]
    public async Task RestoreRefreshesGalleryFavoritesTagMastersAndFilterCountsAndCanBeRepeated()
    {
        await using var harness = ShellTestHarness.Create(); await SeedShellPhoto(harness, 1); await SeedShellPhoto(harness, 2);
        await harness.Db.AddPhotoTagAsync("/slot1/retain.jpg", "現在"); SeedShellBackup(harness, "RETAIN.JPG", true, "[\"保存済み\"]");
        await harness.ViewModel.restorePhotoUserDataBackup();
        AssertShellToast(harness, "写真2枚へお気に入りとタグを復元しました。タグマスタへ1件を追加しました。", ToastType.info);
        var photos = harness.ViewModel.galleryViewModel.photosState.photos; Assert.Equal(2, photos.Count); Assert.All(photos, photo => Assert.True(photo.IsFavorite));
        Assert.Equal(["保存済み", "現在"], photos.Single(photo => photo.SourceSlot == 1).Tags.OrderBy(tag => tag, StringComparer.Ordinal));
        Assert.Equal(["保存済み"], photos.Single(photo => photo.SourceSlot == 2).Tags);
        Assert.Equal(["保存済み", "現在"], harness.ViewModel.tagMasterViewModel.masterTags.OrderBy(tag => tag, StringComparer.Ordinal));
        var counts = harness.ViewModel.galleryViewModel.filtersState.TagFilterCounts; Assert.Equal(2L, counts["保存済み"]); Assert.Equal(1L, counts["現在"]);
        Assert.False(harness.ViewModel.galleryViewModel.photosState.IsLoading); Assert.Single(ReadShellBackups(harness));

        harness.ToastService.toasts.Clear(); await harness.ViewModel.restorePhotoUserDataBackup();
        AssertShellToast(harness, "写真2枚へお気に入りとタグを復元しました。タグマスタへ0件を追加しました。", ToastType.info);
        Assert.Equal(["保存済み", "現在"], await harness.Db.GetPhotoTagsAsync("/slot1/retain.jpg"));
    }

    [Theory]
    [InlineData("AFTER UPDATE OF is_favorite ON photos")]
    [InlineData("AFTER INSERT ON tags")]
    [InlineData("AFTER INSERT ON photo_tags")]
    public async Task RestoreFailureReportsFailurePreservesStorageAndKeepsDisplayedDataUntilRetry(string triggerPoint)
    {
        await using var harness = ShellTestHarness.Create(); await SeedShellPhoto(harness, 1); await harness.Db.AddPhotoTagAsync("/slot1/retain.jpg", "現在");
        SeedShellBackup(harness, "retain.jpg", true, "[\"保存済み\"]"); var saved = ReadShellBackups(harness);
        var displayed = Thumb("/slot1/retain.jpg", "retain.jpg", "2026-06-05 10:00:00"); harness.ViewModel.galleryViewModel.photosState.setPhotos([displayed]);
        ExecuteShellSql(harness, $"CREATE TRIGGER reject_shell_restore {triggerPoint} BEGIN SELECT RAISE(ABORT, '検証用の復元失敗'); END;");
        await harness.ViewModel.restorePhotoUserDataBackup();
        AssertShellToast(harness, "お気に入りとタグを復元できませんでした。もう一度実行してください。", ToastType.error);
        Assert.False((await harness.Db.GetPhotoRecordAsync("/slot1/retain.jpg"))!.is_favorite); Assert.Equal(["現在"], await harness.Db.GetPhotoTagsAsync("/slot1/retain.jpg"));
        Assert.Equal(["現在"], await harness.Db.GetAllTagsAsync()); Assert.Equal(saved, ReadShellBackups(harness));
        Assert.Same(displayed, Assert.Single(harness.ViewModel.galleryViewModel.photosState.photos));
        ExecuteShellSql(harness, "DROP TRIGGER reject_shell_restore;"); harness.ToastService.toasts.Clear(); await harness.ViewModel.restorePhotoUserDataBackup();
        AssertShellToast(harness, "写真1枚へお気に入りとタグを復元しました。タグマスタへ1件を追加しました。", ToastType.info);
        Assert.True(Assert.Single(harness.ViewModel.galleryViewModel.photosState.photos).IsFavorite);
    }

    /// <summary>専用DBへ破損行を注入し、復元のcommit成功と、その後のタグ一覧読込失敗を分ける。</summary>
    [Fact]
    public async Task RestoreCommittedBeforeTagRefreshFailureReportsRestartAndKeepsCommittedDataForRetry()
    {
        await using var harness = ShellTestHarness.Create(); await SeedShellPhoto(harness, 1); SeedShellBackup(harness, "retain.jpg", true, "[\"保存済み\"]");
        ExecuteShellSql(harness, "PRAGMA foreign_keys=OFF; DROP TABLE tags; CREATE TABLE tags (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE); INSERT INTO tags (name) VALUES (NULL);");
        harness.ViewModel.tagMasterViewModel.masterTags.Add("旧表示"); await harness.ViewModel.restorePhotoUserDataBackup();
        AssertShellToast(harness, "復元は完了しましたが、画面を更新できませんでした。アプリを再起動すると反映されます。", ToastType.error);
        Assert.True((await harness.Db.GetPhotoRecordAsync("/slot1/retain.jpg"))!.is_favorite); Assert.Equal(["保存済み"], await harness.Db.GetPhotoTagsAsync("/slot1/retain.jpg"));
        Assert.Equal(["旧表示"], harness.ViewModel.tagMasterViewModel.masterTags); Assert.Single(ReadShellBackups(harness));
        ExecuteShellSql(harness, "DELETE FROM tags WHERE name IS NULL;"); harness.ToastService.toasts.Clear(); await harness.ViewModel.restorePhotoUserDataBackup();
        AssertShellToast(harness, "写真1枚へお気に入りとタグを復元しました。タグマスタへ0件を追加しました。", ToastType.info);
        Assert.Equal(["保存済み"], harness.ViewModel.tagMasterViewModel.masterTags); Assert.True(Assert.Single(harness.ViewModel.galleryViewModel.photosState.photos).IsFavorite);
    }

    private static void SeedShellBackup(ShellTestHarness harness, string filename, bool favorite, string tags)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = harness.DbPath }.ToString()); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO photo_user_data_backup (photo_filename, is_favorite, tags) VALUES (@filename, @favorite, @tags)";
        command.Parameters.AddWithValue("@filename", filename); command.Parameters.AddWithValue("@favorite", favorite ? 1 : 0); command.Parameters.AddWithValue("@tags", tags);
        command.ExecuteNonQuery();
    }

    private static (string Filename, bool Favorite, string Tags)[] ReadShellBackups(ShellTestHarness harness)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = harness.DbPath }.ToString()); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT photo_filename, is_favorite, tags FROM photo_user_data_backup ORDER BY photo_filename";
        using var reader = command.ExecuteReader(); var rows = new List<(string, bool, string)>();
        while (reader.Read()) rows.Add((reader.GetString(0), reader.GetInt64(1) != 0, reader.GetString(2)));
        return rows.ToArray();
    }
}
