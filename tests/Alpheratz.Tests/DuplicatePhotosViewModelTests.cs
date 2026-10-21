using Alpheratz.Core.Database;
using Alpheratz.Features.Settings;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>実際のSQLiteと写真ファイルを使い、重複検出から保持制約・削除結果表示までを確認する。</summary>
public sealed class DuplicatePhotosViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Alpheratz.DuplicatePhotos.Tests", Guid.NewGuid().ToString("N"));
    private readonly AlpheratzDb db;
    private readonly ToastService toasts = new();
    private readonly PhotoService photoService;
    private readonly DuplicatePhotosViewModel model;

    public DuplicatePhotosViewModelTests()
    {
        Directory.CreateDirectory(directory);
        db = new AlpheratzDb(Path.Combine(directory, "photos.db"));
        db.Initialize();
        photoService = new PhotoService(db);
        model = new DuplicatePhotosViewModel(photoService, new DispatcherService(), toasts);
    }

    public void Dispose()
    {
        var ownedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Alpheratz.DuplicatePhotos.Tests")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(ownedRoot, Path.GetFullPath(directory));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "photos.db") }.ToString());
        SqliteConnection.ClearPool(connection);
        Directory.Delete(directory, recursive: true);
    }

    private async Task<string> AddPhoto(string name, string contents, long slot = 1, string timestamp = "2026-10-07 12:00:00")
    {
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, contents);
        var stored = path.Replace('\\', '/');
        await db.UpsertPhotoAsync(new PhotoUpsertData { PhotoPath = stored, PhotoFilename = name, Timestamp = timestamp, SourceSlot = slot });
        return stored;
    }

    [Fact]
    public async Task Detection_SelectsByActualContentsAndMaintainsOneRetainedPhoto()
    {
        var first = await AddPhoto("a.jpg", "same");
        var second = await AddPhoto("b.jpg", "same", slot: 2);
        await AddPhoto("c.jpg", "same");
        await AddPhoto("different.jpg", "diff");
        Assert.True(model.ShowInitialGuidance);
        await model.DetectAsync();
        var group = Assert.Single(model.Groups);
        Assert.Equal(3, group.Photos.Count);
        Assert.True(model.HasSearched);
        Assert.False(model.IsBusy);
        Assert.False(model.CanDeleteSelected);
        Assert.False(model.HasProgress);
        Assert.NotEmpty(model.DetectionSummary);
        Assert.Equal("もう一度検出", model.DetectButtonText);
        group.Photos[0].IsSelected = true;
        group.Photos[1].IsSelected = true;
        Assert.Equal(2, model.SelectedCount);
        Assert.Equal(8, model.SelectedBytes);
        Assert.True(model.CanDeleteSelected);
        Assert.True(group.Photos[2].IsRequiredRetention);
        Assert.False(group.Photos[2].IsSelectionEnabled);
        Assert.True(group.Photos[0].IsSelectionEnabled);
        var targets = model.BuildDeleteTargets();
        Assert.Equal(2, targets.Count);
        Assert.All(targets, target => { Assert.Equal(group.ContentHash, target.ExpectedContentHash); Assert.Equal(4, target.ExpectedFileSize); Assert.Equal([group.Photos[2].PhotoPath], target.RetainedPhotoPaths); });
        Assert.Contains(targets, target => target.Photo.photo_path == first && target.Photo.source_slot == 1);
        Assert.Contains(targets, target => target.Photo.photo_path == second && target.Photo.source_slot == 2);
        group.Photos[0].IsSelected = false;
        Assert.True(group.Photos[2].CanSelect);
        Assert.False(group.Photos[2].IsRequiredRetention);
        Assert.Equal(1, model.SelectedCount);
        await model.DetectAsync();
        Assert.Equal(0, model.SelectedCount);
        Assert.Empty(model.BuildDeleteTargets());
    }

    [Fact]
    public async Task Deletion_PassesSnapshotBlocksReentryAndRemovesOnlySucceededPhotos()
    {
        foreach (var name in new[] { "a.jpg", "b.jpg", "c.jpg" }) await AddPhoto(name, "same");
        await model.DetectAsync();
        var group = Assert.Single(model.Groups);
        group.Photos[0].IsSelected = true;
        group.Photos[1].IsSelected = true;
        var completed = new TaskCompletionSource<DuplicatePhotoDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyList<DuplicatePhotoDeleteTarget>? requested = null;
        var pending = model.DeleteSelectedAsync(targets => { requested = targets; return completed.Task; });
        Assert.True(model.IsDeleting);
        Assert.True(model.HasProgress);
        Assert.False(model.CanDetect);
        Assert.All(group.Photos, photo => Assert.False(photo.IsSelectionEnabled));
        var calledAgain = false;
        await model.DeleteSelectedAsync(_ => { calledAgain = true; throw new InvalidOperationException(); });
        await model.DetectAsync();
        Assert.False(calledAgain);
        Assert.Same(group, Assert.Single(model.Groups));
        Assert.NotNull(requested);
        var succeeded = requested[0].Photo;
        var failed = requested[1].Photo;
        completed.SetResult(new DuplicatePhotoDeleteResult([succeeded], [new(failed, DuplicatePhotoDeleteFailureKind.FileChanged)], true));
        await pending;
        Assert.Equal(2, group.Photos.Count);
        Assert.DoesNotContain(group.Photos, photo => photo.PhotoPath == succeeded.photo_path);
        Assert.Contains(group.Photos, photo => photo.PhotoPath == failed.photo_path && photo.IsSelected);
        Assert.Equal(1, model.SelectedCount);
        Assert.Equal("1枚を削除しました。1枚は現在の写真一覧から外れた、内容が変わった、使用中、または削除権限がないため削除していません。", model.StatusText);
        Assert.Equal(ToastType.error, Assert.Single(toasts.toasts).Type);
        Assert.False(model.IsBusy);
        Assert.False(model.HasProgress);
        Assert.True(model.CanDeleteSelected);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Deletion_DistinguishesSuccessFailureAndDatabaseFailure(bool databaseUpdated, bool deleted)
    {
        await AddPhoto("a.jpg", "same");
        await AddPhoto("b.jpg", "same");
        await model.DetectAsync();
        model.Groups[0].Photos[0].IsSelected = true;
        await model.DeleteSelectedAsync(targets => Task.FromResult(new DuplicatePhotoDeleteResult(deleted ? [targets[0].Photo] : [], deleted ? [] : [new(targets[0].Photo, DuplicatePhotoDeleteFailureKind.DeleteFailed)], databaseUpdated)));
        var expected = !databaseUpdated ? "1枚の元ファイルは削除しましたが、管理情報を更新できませんでした。次回の写真スキャンで管理情報が整理されます。" : deleted ? "1枚の写真を削除しました。" : "1枚は現在の写真一覧から外れた、内容が変わった、使用中、または削除権限がないため削除していません。";
        Assert.Equal(expected, model.StatusText);
        Assert.Equal(databaseUpdated && deleted ? ToastType.info : ToastType.error, Assert.Single(toasts.toasts).Type);
        Assert.Equal(deleted ? 0 : 1, model.Groups.Count);
        Assert.False(model.IsDeleting);
    }

    [Fact]
    public async Task CancellationAndDeletionException_RestoreSelectionAndOperationState()
    {
        await AddPhoto("a.jpg", "same");
        await AddPhoto("b.jpg", "same");
        await model.DetectAsync();
        model.Groups[0].Photos[0].IsSelected = true;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var called = false;
        await model.DeleteSelectedAsync(_ => { called = true; throw new InvalidOperationException(); }, canceled.Token);
        Assert.False(called);
        Assert.Equal("重複写真の削除を中止しました。", model.StatusText);
        Assert.True(model.CanDeleteSelected);
        Assert.Empty(toasts.toasts);
        await model.DeleteSelectedAsync(_ => Task.FromException<DuplicatePhotoDeleteResult>(new IOException("テスト用失敗")));
        Assert.Equal("重複写真の削除処理を完了できませんでした。選択内容は画面に残っています。", model.StatusText);
        Assert.Equal(ToastType.error, Assert.Single(toasts.toasts).Type);
        Assert.True(model.CanDetect);
        await model.DetectAsync(canceled.Token);
        Assert.Equal("重複写真の検出を中止しました。", model.StatusText);
        Assert.False(model.IsBusy);
    }

    [Theory]
    [InlineData(0, false, "DuplicatePhotosViewModel.noPhotos")]
    [InlineData(1, false, "DuplicatePhotosViewModel.noDuplicates")]
    [InlineData(1, true, "DuplicatePhotosViewModel.noDuplicatesWithUnreadable")]
    public async Task Detection_ReportsEmptyDistinctAndUnreadableInputs(int count, bool missing, string key)
    {
        if (count > 0)
        {
            var path = await AddPhoto("single.jpg", "unique");
            if (missing) File.Delete(path);
        }
        await model.DetectAsync();
        Assert.Empty(model.Groups);
        var expected = key switch
        {
            "DuplicatePhotosViewModel.noPhotos" => "比較できる写真がありません。写真フォルダを設定し、写真の読み込みが完了してから検出してください。",
            "DuplicatePhotosViewModel.noDuplicates" => "ファイル内容が完全に一致する写真はありません。",
            "DuplicatePhotosViewModel.noDuplicatesWithUnreadable" => "ファイル内容が完全に一致する写真はありません。1枚は読み取れなかったため比較していません。",
            _ => throw new InvalidOperationException("検証対象のメッセージキーがありません。"),
        };
        Assert.Equal(expected, model.StatusText);
        Assert.True(model.HasStatus);
        Assert.False(model.ShowInitialGuidance);
        Assert.False(model.HasProgress);
    }

    [Fact]
    public void ItemAndGroup_DisplayFallbacksPreservePathAndReleaseRemovedSubscriptions()
    {
        var item = new DuplicatePhotoItemViewModel(new ExactDuplicatePhoto("", "/folder/a.jpg", "", 2, 1024));
        Assert.Equal("a.jpg", item.PhotoFilename);
        Assert.Contains("撮影日時不明", item.DetailsText);
        Assert.Contains("写真フォルダ2", item.DetailsText);
        Assert.Contains("a.jpg", item.SelectionAutomationName);
        Assert.NotEmpty(item.DirectoryPath);
        var group = new DuplicatePhotoGroupViewModel(new("hash", 1, [new("a", "a", "date", 1, 1), new("b", "b", "date", 1, 1)]));
        var removed = group.Photos[0];
        var events = 0;
        group.SelectionChanged += (_, _) => events++;
        group.RemovePhotos(new HashSet<string> { "a" });
        removed.IsSelected = true;
        Assert.Equal(0, events);
        Assert.True(Assert.Single(group.Photos).IsRequiredRetention);
        group.SetBusy(true);
        Assert.False(group.Photos[0].IsSelectionEnabled);
        Assert.NotEmpty(group.SummaryText);
    }

    [Theory]
    [InlineData(-1, "0 B")]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(1099511627776, "1 TB")]
    public void FileSize_ClampsNegativeAndUsesBinaryUnits(long bytes, string expected) => Assert.Equal(expected, DuplicatePhotosViewModel.FormatFileSize(bytes));

    private void ExecuteSql(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "photos.db"), Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private async Task<DuplicatePhotoDeleteTarget> CreateDeleteTarget()
    {
        var first = await AddPhoto("a.jpg", "same");
        var retained = await AddPhoto("b.jpg", "same", slot: 2);
        var detected = await photoService.FindExactDuplicatePhotosAsync();
        var group = Assert.Single(detected.Groups);
        return new(new SelectedPhotoRefDto { photo_path = first, source_slot = 1 }, group.ContentHash, group.FileSize, [retained]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ServiceDeletion_RemovesFileAndDatabaseOnceAndPreservesRetainedPhoto(bool duplicateRequest, bool alreadyMissing)
    {
        var target = await CreateDeleteTarget();
        if (alreadyMissing) File.Delete(target.Photo.photo_path);
        var result = await photoService.DeleteDuplicatePhotosAsync(duplicateRequest ? [target, target] : [target]);
        Assert.Equal(target.Photo, Assert.Single(result.DeletedPhotos));
        Assert.Empty(result.FailedPhotos);
        Assert.True(result.DatabaseUpdated);
        Assert.False(File.Exists(target.Photo.photo_path));
        Assert.Equal("same", await File.ReadAllTextAsync(target.RetainedPhotoPaths[0]));
        var remaining = await db.GetPhotosForDuplicateDetectionAsync();
        Assert.Equal(target.RetainedPhotoPaths[0], Assert.Single(remaining).photo_path);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("self")]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("locked")]
    public async Task ServiceDeletion_UnavailableRetainedContentNeverDeletesSelectedPhoto(string condition)
    {
        var target = await CreateDeleteTarget();
        if (condition == "empty") target = target with { RetainedPhotoPaths = [] };
        if (condition == "self") target = target with { RetainedPhotoPaths = [target.Photo.photo_path] };
        if (condition == "missing") File.Delete(target.RetainedPhotoPaths[0]);
        if (condition == "changed") await File.WriteAllTextAsync(target.RetainedPhotoPaths[0], "diff");
        using var locked = condition == "locked" ? new FileStream(target.RetainedPhotoPaths[0], FileMode.Open, FileAccess.Read, FileShare.None) : null;
        var result = await photoService.DeleteDuplicatePhotosAsync([target]);
        Assert.Empty(result.DeletedPhotos);
        Assert.Equal(DuplicatePhotoDeleteFailureKind.RetainedPhotoUnavailable, Assert.Single(result.FailedPhotos).Kind);
        Assert.Equal("same", await File.ReadAllTextAsync(target.Photo.photo_path));
        Assert.Equal(2, (await db.GetPhotosForDuplicateDetectionAsync()).Count);
    }

    [Theory]
    [InlineData("diff")]
    [InlineData("different length")]
    public async Task ServiceDeletion_ChangedSelectedContentIsPreserved(string contents)
    {
        var target = await CreateDeleteTarget();
        await File.WriteAllTextAsync(target.Photo.photo_path, contents);
        var result = await photoService.DeleteDuplicatePhotosAsync([target]);
        Assert.Empty(result.DeletedPhotos);
        Assert.Equal(DuplicatePhotoDeleteFailureKind.FileChanged, Assert.Single(result.FailedPhotos).Kind);
        Assert.Equal(contents, await File.ReadAllTextAsync(target.Photo.photo_path));
        Assert.Equal(2, (await db.GetPhotosForDuplicateDetectionAsync()).Count);
    }

    [Fact]
    public async Task ServiceDeletion_RejectsUnregisteredPathsAndWrongSlotWithoutFilesystemEffects()
    {
        var target = await CreateDeleteTarget();
        var result = await photoService.DeleteDuplicatePhotosAsync([
            target with { Photo = new() { photo_path = target.Photo.photo_path, source_slot = 2 } },
            target with { Photo = new() { photo_path = " ", source_slot = 1 } },
            target with { Photo = new() { photo_path = Path.Combine(directory, "unregistered.jpg"), source_slot = 1 } },
        ]);
        Assert.Empty(result.DeletedPhotos);
        Assert.Equal(3, result.FailedPhotos.Count);
        Assert.All(result.FailedPhotos, failure => Assert.Equal(DuplicatePhotoDeleteFailureKind.NoLongerRegistered, failure.Kind));
        Assert.True(File.Exists(target.Photo.photo_path));
        Assert.Equal(2, (await db.GetPhotosForDuplicateDetectionAsync()).Count);
        Assert.Empty((await photoService.DeleteDuplicatePhotosAsync([])).DeletedPhotos);
    }

    [Fact]
    public async Task ServiceDeletion_ReportsReadLockAndDatabaseFailureSeparately()
    {
        var target = await CreateDeleteTarget();
        using (var locked = new FileStream(target.Photo.photo_path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var blocked = await photoService.DeleteDuplicatePhotosAsync([target]);
            Assert.Empty(blocked.DeletedPhotos);
            Assert.Equal(DuplicatePhotoDeleteFailureKind.ComparisonFailed, Assert.Single(blocked.FailedPhotos).Kind);
        }
        ExecuteSql("CREATE TRIGGER prevent_photo_delete BEFORE DELETE ON photos BEGIN SELECT RAISE(FAIL, 'test delete failure'); END;");
        var failedDatabase = await photoService.DeleteDuplicatePhotosAsync([target]);
        Assert.Equal(target.Photo, Assert.Single(failedDatabase.DeletedPhotos));
        Assert.False(failedDatabase.DatabaseUpdated);
        Assert.False(File.Exists(target.Photo.photo_path));
        Assert.Equal(2, (await db.GetPhotosForDuplicateDetectionAsync()).Count);
        Assert.True(File.Exists(target.RetainedPhotoPaths[0]));
    }

    [Fact]
    public async Task ServiceDeletion_CancellationDoesNotKeepTheWriteGateOrDeleteFiles()
    {
        var target = await CreateDeleteTarget();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => photoService.DeleteDuplicatePhotosAsync([target], canceled.Token));
        Assert.True(File.Exists(target.Photo.photo_path));
        Assert.Equal(2, (await db.GetPhotosForDuplicateDetectionAsync()).Count);
        Assert.Single((await photoService.DeleteDuplicatePhotosAsync([target])).DeletedPhotos);
    }

    [Fact]
    public async Task Detection_DatabaseFailureRestoresBusyStateAndCanRetry()
    {
        ExecuteSql("DROP TABLE photos;");
        await model.DetectAsync();
        Assert.Equal("重複写真を検出できませんでした。写真フォルダを開けることを確認してから、もう一度検出してください。", model.StatusText);
        Assert.Equal(ToastType.error, Assert.Single(toasts.toasts).Type);
        Assert.False(model.HasSearched);
        Assert.False(model.IsBusy);
        db.Initialize();
        await model.DetectAsync();
        Assert.Equal("比較できる写真がありません。写真フォルダを設定し、写真の読み込みが完了してから検出してください。", model.StatusText);
        Assert.True(model.HasSearched);
    }
}
