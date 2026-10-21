using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging;
using Alpheratz.Features.Gallery;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Microsoft.Data.Sqlite;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Alpheratz.Tests;

/// <summary>所有する写真だけを使い、削除結果・DB・表示・サムネイルの再開をShellから照合する。</summary>
public sealed partial class ShellViewModelBehaviorTests
{
    [Fact]
    public async Task DuplicateDeletion_EmptyTargetsKeepSelectionDisplayAndDurableState()
    {
        await using var fixture = await ShellDuplicateFixture.Create(); var harness = fixture.Harness;
        var selected = await fixture.SelectTarget(); var before = fixture.Snapshot(); var version = harness.ViewModel.galleryViewModel.selectionState.SelectionVersion;
        ExecuteShellSql(harness, "ALTER TABLE photos RENAME TO owned_unavailable_photos;");
        var result = await harness.ViewModel.deleteDuplicatePhotos([]);
        ExecuteShellSql(harness, "ALTER TABLE owned_unavailable_photos RENAME TO photos;");
        Assert.True(result.DatabaseUpdated); Assert.Empty(result.DeletedPhotos); Assert.Empty(result.FailedPhotos);
        Assert.Same(selected, harness.ViewModel.galleryViewModel.photosState.photos.Single(photo => photo.PhotoPath == fixture.TargetPath));
        Assert.True(selected.IsSelected); Assert.Equal([fixture.TargetPath], harness.ViewModel.galleryViewModel.selectionState.selectedPhotoPaths);
        Assert.Equal(version, harness.ViewModel.galleryViewModel.selectionState.SelectionVersion); Assert.Equal(before, fixture.Snapshot());
        fixture.AssertAllSourceBytes(); fixture.AssertAllCacheBytes(); Assert.Empty(harness.ToastService.toasts);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DuplicateDeletion_RemovesOnlyTheSelectedFileAndRefreshesGalleryAndFilters(bool repeatedTarget, bool alreadyMissing)
    {
        await using var fixture = await ShellDuplicateFixture.Create(); var harness = fixture.Harness;
        var selected = await fixture.SelectTarget(); var otherRows = fixture.Snapshot(excludeTarget: true);
        if (alreadyMissing) File.Delete(fixture.TargetPath);
        var result = await harness.ViewModel.deleteDuplicatePhotos(repeatedTarget ? [fixture.Target, fixture.Target] : [fixture.Target]);
        fixture.AssertDeletionResult(result); Assert.False(selected.IsSelected); fixture.AssertSelectionCleared(); fixture.AssertUpdatedGallery();
        Assert.Equal(otherRows, fixture.Snapshot()); fixture.AssertRetainedSourceBytes(); Assert.Empty(harness.ToastService.toasts);
        await fixture.AssertThumbnailGenerationResumed();
    }

    [Fact]
    public async Task DuplicateDeletion_LockedTargetReportsFailureAndReleasesGatesForTheSameTargetsRetry()
    {
        await using var fixture = await ShellDuplicateFixture.Create(); var harness = fixture.Harness;
        var selected = await fixture.SelectTarget(); var before = fixture.Snapshot();
        using (var locked = new FileStream(fixture.TargetPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var blocked = await harness.ViewModel.deleteDuplicatePhotos([fixture.Target]);
            Assert.True(blocked.DatabaseUpdated); Assert.Empty(blocked.DeletedPhotos);
            var failure = Assert.Single(blocked.FailedPhotos); Assert.Equal(fixture.Target.Photo, failure.Photo); Assert.Equal(DuplicatePhotoDeleteFailureKind.ComparisonFailed, failure.Kind);
        }
        Assert.Equal(before, fixture.Snapshot()); fixture.AssertAllSourceBytes(); fixture.AssertAllCacheBytes(); fixture.AssertSelectionCleared(); Assert.False(selected.IsSelected);
        Assert.Equal(3, harness.ViewModel.galleryViewModel.photosState.photos.Count); Assert.Empty(harness.ToastService.toasts);
        var retried = await harness.ViewModel.deleteDuplicatePhotos([fixture.Target]); fixture.AssertDeletionResult(retried); fixture.AssertUpdatedGallery();
        fixture.AssertRetainedSourceBytes(); await fixture.AssertThumbnailGenerationResumed();
    }

    /// <summary>元fileの削除後にDBがrollbackする現行契約を、元file保持の失敗と区別する。</summary>
    [Fact]
    public async Task DuplicateDeletion_DatabaseFailureKeepsRowsAndSameMissingTargetRetryReconcilesTheGallery()
    {
        await using var fixture = await ShellDuplicateFixture.Create(); var harness = fixture.Harness;
        await fixture.SelectTarget(); var before = fixture.Snapshot(); var otherRows = fixture.Snapshot(excludeTarget: true);
        ExecuteShellSql(harness, "CREATE TRIGGER owned_reject_duplicate_delete BEFORE DELETE ON photos BEGIN SELECT RAISE(ABORT, 'owned deletion fault'); END;");
        var failed = await harness.ViewModel.deleteDuplicatePhotos([fixture.Target]);
        Assert.False(failed.DatabaseUpdated); Assert.Equal(fixture.Target.Photo, Assert.Single(failed.DeletedPhotos)); Assert.Empty(failed.FailedPhotos);
        Assert.False(File.Exists(fixture.TargetPath)); fixture.AssertTargetCachesRemoved(); Assert.Equal(before, fixture.Snapshot()); fixture.AssertRetainedSourceBytes(); fixture.AssertSelectionCleared();
        Assert.Equal(3, harness.ViewModel.galleryViewModel.photosState.photos.Count);
        Assert.Equal(1L, harness.ViewModel.galleryViewModel.filtersState.TagFilterCounts["削除対象"]); Assert.Empty(harness.ToastService.toasts);
        ExecuteShellSql(harness, "DROP TRIGGER owned_reject_duplicate_delete;");
        var retried = await harness.ViewModel.deleteDuplicatePhotos([fixture.Target]); fixture.AssertDeletionResult(retried); fixture.AssertUpdatedGallery();
        Assert.Equal(otherRows, fixture.Snapshot()); fixture.AssertRetainedSourceBytes(); Assert.Empty(harness.ToastService.toasts);
        await fixture.AssertThumbnailGenerationResumed();
    }

    [Fact]
    public async Task DuplicateDeletion_ReadFailurePreservesSelectionAndSourcesAndResumesBothThumbnailGatesBeforeRetry()
    {
        await using var fixture = await ShellDuplicateFixture.Create(); var harness = fixture.Harness;
        var selected = await fixture.SelectTarget(); var before = fixture.Snapshot();
        ExecuteShellSql(harness, "ALTER TABLE photos RENAME TO owned_unavailable_photos;");
        var error = await Assert.ThrowsAsync<SqliteException>(() => harness.ViewModel.deleteDuplicatePhotos([fixture.Target]));
        Assert.Contains("no such table: photos", error.Message); Assert.Equal(1, error.SqliteErrorCode);
        ExecuteShellSql(harness, "ALTER TABLE owned_unavailable_photos RENAME TO photos;");
        Assert.Equal(before, fixture.Snapshot()); fixture.AssertAllSourceBytes(); fixture.AssertAllCacheBytes(); Assert.True(selected.IsSelected);
        Assert.Equal([fixture.TargetPath], harness.ViewModel.galleryViewModel.selectionState.selectedPhotoPaths); Assert.Empty(harness.ToastService.toasts);
        await fixture.AssertThumbnailGenerationResumed();
        var retried = await harness.ViewModel.deleteDuplicatePhotos([fixture.Target]); fixture.AssertDeletionResult(retried); fixture.AssertUpdatedGallery(); fixture.AssertSelectionCleared();
        fixture.AssertRetainedSourceBytes(); Assert.Empty(harness.ToastService.toasts);
    }

    /// <summary>複数選択の部分成功を、元fileの削除可否とDBの確定対象に分けて確認する。</summary>
    [Fact]
    public async Task DuplicateDeletion_PartialFileFailureCommitsOnlyDeletedPathsAndSameBatchRetryPreservesTheLastCopy()
    {
        await using var fixture = await ShellDuplicateFixture.Create(); var harness = fixture.Harness;
        await fixture.SelectTarget();
        var secondPath = Path.Combine(harness.TempDir, "対象外の写真.png");
        var second = fixture.Target with { Photo = new() { photo_path = secondPath, source_slot = 2 } };
        var retainedPath = fixture.Target.RetainedPhotoPaths[0]; var retainedBytes = File.ReadAllBytes(retainedPath);
        await harness.Db.AddPhotoTagAsync(secondPath, "一部失敗の保持タグ");
        var gallery = harness.ViewModel.galleryViewModel;
        gallery.selectionState.toggleSelectedPhoto(gallery.photosState.displayItems[2], false, gallery.photosState.displayItems);
        await gallery.selectionState.loadSelectedPhotoRefs();
        Assert.Equal(new[] { fixture.TargetPath, secondPath }.Order(), gallery.selectionState.selectedPhotoRefs.Select(photo => photo.photo_path).Order());
        var unaffected = fixture.Snapshot(excludeTarget: true);

        using (var locked = new FileStream(secondPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // 読込みは許可し、rename／削除だけを実OS handleで拒否する。
            Assert.Equal(retainedBytes, File.ReadAllBytes(secondPath));
            var sharing = Assert.Throws<IOException>(() => File.Move(secondPath, secondPath + ".owned-probe"));
            Assert.Equal(32, sharing.HResult & 0xffff); Assert.False(File.Exists(secondPath + ".owned-probe"));
            var partial = await harness.ViewModel.deleteDuplicatePhotos([fixture.Target, second]);
            Assert.True(partial.DatabaseUpdated); Assert.Equal(fixture.Target.Photo, Assert.Single(partial.DeletedPhotos));
            var failure = Assert.Single(partial.FailedPhotos); Assert.Equal(second.Photo, failure.Photo); Assert.Equal(DuplicatePhotoDeleteFailureKind.DeleteFailed, failure.Kind);
        }
        Assert.False(File.Exists(fixture.TargetPath)); fixture.AssertTargetCachesRemoved(); fixture.AssertRetainedSourceBytes(); fixture.AssertSelectionCleared();
        Assert.Equal(unaffected, fixture.Snapshot());
        Assert.Null(await harness.Db.GetPhotoRecordAsync(fixture.TargetPath));
        var remainingSecond = await harness.Db.GetPhotoRecordAsync(secondPath); Assert.NotNull(remainingSecond); Assert.True(remainingSecond.is_favorite);
        Assert.Equal(new[] { secondPath, retainedPath }.Order(), gallery.photosState.photos.Select(photo => photo.PhotoPath).Order());
        Assert.Equal(1L, gallery.filtersState.TagFilterCounts["一部失敗の保持タグ"]); Assert.False(gallery.filtersState.TagFilterCounts.ContainsKey("削除対象"));
        Assert.Equal(1L, gallery.filtersState.TagFilterCounts["保持"]); Assert.Empty(harness.ToastService.toasts);

        var retried = await harness.ViewModel.deleteDuplicatePhotos([fixture.Target, second]);
        Assert.True(retried.DatabaseUpdated); Assert.Equal(second.Photo, Assert.Single(retried.DeletedPhotos));
        var alreadyRemoved = Assert.Single(retried.FailedPhotos); Assert.Equal(fixture.Target.Photo, alreadyRemoved.Photo); Assert.Equal(DuplicatePhotoDeleteFailureKind.NoLongerRegistered, alreadyRemoved.Kind);
        Assert.False(File.Exists(fixture.TargetPath)); Assert.False(File.Exists(secondPath)); Assert.Equal(retainedBytes, File.ReadAllBytes(retainedPath));
        Assert.Null(await harness.Db.GetPhotoRecordAsync(secondPath));
        Assert.Equal(retainedPath, Assert.Single(await harness.Db.GetPhotosForDuplicateDetectionAsync()).photo_path);
        Assert.Equal(retainedPath, Assert.Single(gallery.photosState.photos).PhotoPath);
        Assert.Equal(new KeyValuePair<string, long>("保持", 1), Assert.Single(gallery.filtersState.TagFilterCounts));
        fixture.AssertSelectionCleared(); Assert.Empty(harness.ToastService.toasts); await fixture.AssertThumbnailGenerationResumed();
    }

    /// <summary>互いを保持写真として指定しても、全写真が削除対象なら元fileとDBを保持する。</summary>
    [Fact]
    public async Task DuplicateDeletion_AllCopiesSelectedRejectsTheBatchWithoutDeletingAnOriginalOrChangingStorage()
    {
        await using var fixture = await ShellDuplicateFixture.Create(); var harness = fixture.Harness;
        await fixture.SelectTarget();
        var retainedPath = fixture.Target.RetainedPhotoPaths[0]; var otherPath = Path.Combine(harness.TempDir, "対象外の写真.png");
        var targets = new[]
        {
            fixture.Target,
            fixture.Target with { Photo = new() { photo_path = retainedPath, source_slot = 2 }, RetainedPhotoPaths = [fixture.TargetPath] },
            fixture.Target with { Photo = new() { photo_path = otherPath, source_slot = 2 }, RetainedPhotoPaths = [retainedPath] },
        };
        var gallery = harness.ViewModel.galleryViewModel;
        foreach (var index in new[] { 1, 2 }) gallery.selectionState.toggleSelectedPhoto(gallery.photosState.displayItems[index], false, gallery.photosState.displayItems);
        await gallery.selectionState.loadSelectedPhotoRefs(); Assert.Equal(3, gallery.selectionState.selectedPhotoRefs.Count);
        var before = fixture.Snapshot();
        var rejected = await harness.ViewModel.deleteDuplicatePhotos(targets);
        Assert.True(rejected.DatabaseUpdated); Assert.Empty(rejected.DeletedPhotos); Assert.Equal(3, rejected.FailedPhotos.Count);
        Assert.Equal(targets.Select(target => target.Photo.photo_path).Order(), rejected.FailedPhotos.Select(failure => failure.Photo.photo_path).Order());
        Assert.All(rejected.FailedPhotos, failure => Assert.Equal(DuplicatePhotoDeleteFailureKind.RetainedPhotoUnavailable, failure.Kind));
        Assert.Equal(before, fixture.Snapshot()); fixture.AssertAllSourceBytes(); fixture.AssertAllCacheBytes(); fixture.AssertSelectionCleared();
        Assert.Equal(targets.Select(target => target.Photo.photo_path).Order(), gallery.photosState.photos.Select(photo => photo.PhotoPath).Order());
        Assert.Equal(1L, gallery.filtersState.TagFilterCounts["削除対象"]); Assert.Equal(1L, gallery.filtersState.TagFilterCounts["保持"]);
        Assert.Empty(harness.ToastService.toasts);

        var retried = await harness.ViewModel.deleteDuplicatePhotos([fixture.Target]); fixture.AssertDeletionResult(retried); fixture.AssertUpdatedGallery();
        fixture.AssertRetainedSourceBytes(); fixture.AssertSelectionCleared(); await fixture.AssertThumbnailGenerationResumed();
    }

    private sealed class ShellDuplicateFixture : IAsyncDisposable
    {
        private static readonly byte[] Pixels = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
        private readonly string? previousDataRoot;
        private readonly Dictionary<string, byte[]> targetCaches = [];
        private readonly Dictionary<string, byte[]> retainedCaches = [];
        public ShellTestHarness Harness { get; } = ShellTestHarness.Create();
        public string TargetPath => Path.Combine(Harness.TempDir, "削除対象.png");
        private string RetainedPath => Path.Combine(Harness.TempDir, "保持する写真.png");
        private string OtherPath => Path.Combine(Harness.TempDir, "対象外の写真.png");
        public DuplicatePhotoDeleteTarget Target => new(new() { photo_path = TargetPath, source_slot = 1 }, Convert.ToHexString(SHA256.HashData(Pixels)), Pixels.Length, [RetainedPath]);

        private ShellDuplicateFixture()
        {
            previousDataRoot = AppPaths.DataDirOverrideForTests; AppPaths.DataDirOverrideForTests = Path.Combine(Harness.TempDir, "Data");
        }

        public static async Task<ShellDuplicateFixture> Create()
        {
            var fixture = new ShellDuplicateFixture();
            try
            {
                foreach (var (path, slot, world) in new[] { (fixture.TargetPath, 1L, "削除対象world"), (fixture.RetainedPath, 2L, "保持world"), (fixture.OtherPath, 2L, "対象外world") })
                {
                    File.WriteAllBytes(path, Pixels); await fixture.Harness.Db.UpsertPhotoAsync(new PhotoUpsertData
                    {
                        PhotoPath = path, PhotoFilename = Path.GetFileName(path), SourceSlot = slot, Timestamp = "2026-10-08 10:00:00",
                        LastModifiedUtc = "2026-10-08T01:00:00Z", WorldName = world, WorldId = "wrld_owned", Orientation = "landscape", ImageWidth = 1, ImageHeight = 1, MatchSource = "metadata",
                    });
                }
                await fixture.Harness.Db.SetPhotoFavoriteAsync(fixture.OtherPath, true); await fixture.Harness.Db.UpdatePhotoPhashAsync(fixture.OtherPath, new string('a', 64));
                await fixture.Harness.Db.AddPhotoTagAsync(fixture.TargetPath, "削除対象"); await fixture.Harness.Db.AddPhotoTagAsync(fixture.RetainedPath, "保持");
                ExecuteShellSql(fixture.Harness, "INSERT INTO photo_user_data_backup VALUES('保持.png',1,'[\"保持\"]'); INSERT INTO archive_world_visits(source_log_name,world_name,join_time,leave_time) VALUES('保持.txt','保持world','2026-10-08 09:00:00',NULL);");
                var thumbnails = new ThumbnailService();
                foreach (var (path, slot) in new[] { (fixture.TargetPath, 1L), (fixture.RetainedPath, 2L), (fixture.OtherPath, 2L) })
                {
                    foreach (var cached in new[] { await thumbnails.EnsureGridThumbAsync(path, slot), await thumbnails.EnsureDisplayThumbAsync(path, slot) })
                    {
                        Assert.StartsWith(Path.GetFullPath(Path.Combine(fixture.Harness.TempDir, "Data")) + Path.DirectorySeparatorChar, Path.GetFullPath(cached));
                        (path == fixture.TargetPath ? fixture.targetCaches : fixture.retainedCaches).Add(cached, File.ReadAllBytes(cached));
                    }
                }
                Assert.Equal(2, fixture.targetCaches.Count); Assert.Equal(4, fixture.retainedCaches.Count);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async Task<PhotoThumbnailItem> SelectTarget()
        {
            var photos = new[] { Thumb(TargetPath, Path.GetFileName(TargetPath), "2026-10-08 10:00:00"), Thumb(RetainedPath, Path.GetFileName(RetainedPath), "2026-10-08 10:00:00"), Thumb(OtherPath, Path.GetFileName(OtherPath), "2026-10-08 10:00:00") };
            photos[1].SourceSlot = 2; photos[2].SourceSlot = 2;
            var gallery = Harness.ViewModel.galleryViewModel; gallery.photosState.setPhotos(photos, autoGenerateThumbnails: false); gallery.photosState.rebuildDisplayItems(GroupingMode.none);
            gallery.selectionState.IsMultiSelectMode = true; gallery.selectionState.toggleSelectedPhoto(gallery.photosState.displayItems[0], false, gallery.photosState.displayItems);
            await gallery.selectionState.loadSelectedPhotoRefs(); Assert.True(photos[0].IsSelected); Assert.Equal(TargetPath, Assert.Single(gallery.selectionState.selectedPhotoRefs).photo_path);
            return photos[0];
        }

        public void AssertDeletionResult(DuplicatePhotoDeleteResult result)
        {
            Assert.True(result.DatabaseUpdated); var photo = Assert.Single(result.DeletedPhotos); Assert.Equal(TargetPath, photo.photo_path); Assert.Equal(1L, photo.source_slot);
            Assert.Empty(result.FailedPhotos); Assert.False(File.Exists(TargetPath)); AssertTargetCachesRemoved();
        }

        public void AssertSelectionCleared()
        {
            var selection = Harness.ViewModel.galleryViewModel.selectionState;
            Assert.Empty(selection.selectedPhotoPaths); Assert.Empty(selection.selectedPhotoRefs); Assert.Null(selection.SelectionAnchorPhotoPath);
        }

        public void AssertUpdatedGallery()
        {
            var gallery = Harness.ViewModel.galleryViewModel;
            Assert.Equal(new[] { OtherPath, RetainedPath }.Order(), gallery.photosState.photos.Select(photo => photo.PhotoPath).Order());
            Assert.Equal(2, gallery.photosState.TotalCount); Assert.False(gallery.photosState.IsLoading);
            Assert.Equal(new[] { "対象外world", "保持world" }.Order(), gallery.filtersState.worldFilterOptions.Select(option => option.world_name).Order());
            Assert.Equal(new KeyValuePair<string, long>("保持", 1), Assert.Single(gallery.filtersState.TagFilterCounts));
        }

        public void AssertAllSourceBytes() { Assert.Equal(Pixels, File.ReadAllBytes(TargetPath)); AssertRetainedSourceBytes(); }
        public void AssertRetainedSourceBytes()
        {
            Assert.Equal(Pixels, File.ReadAllBytes(RetainedPath)); Assert.Equal(Pixels, File.ReadAllBytes(OtherPath));
            foreach (var (path, bytes) in retainedCaches) Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        public void AssertAllCacheBytes() { foreach (var (path, bytes) in targetCaches) Assert.Equal(bytes, File.ReadAllBytes(path)); }
        public void AssertTargetCachesRemoved() { foreach (var path in targetCaches.Keys) Assert.False(File.Exists(path)); }

        /// <summary>private flagを読まず、同じGalleryと共有workerから実JPEGを生成・展開する。</summary>
        public async Task AssertThumbnailGenerationResumed()
        {
            var state = Harness.ViewModel.galleryViewModel.photosState; await state.loadPhotos();
            // 既存cacheの返却だけで再開を済扱いにせず、未生成の所有画像を同じworkerへ渡す。
            var freshSource = Path.Combine(Harness.TempDir, $"生成再開の確認-{Guid.NewGuid():N}.png"); File.WriteAllBytes(freshSource, Pixels);
            var cacheDirectory = AppPaths.GetImgCacheDir(2)!;
            Assert.Empty(Directory.EnumerateFiles(cacheDirectory).Where(path => Path.GetFileName(path).StartsWith(Path.GetFileName(freshSource) + ".", StringComparison.Ordinal)));
            var photo = Thumb(freshSource, Path.GetFileName(freshSource), "2026-10-08 10:00:00"); photo.SourceSlot = 2;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(PhotoThumbnailItem.GridThumbPath) && !string.IsNullOrWhiteSpace(photo.GridThumbPath)) ready.TrySetResult(); }
            photo.PropertyChanged += Changed;
            try
            {
                Assert.True(string.IsNullOrWhiteSpace(photo.GridThumbPath)); state.kickThumbnailsForExternal([photo]); await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.StartsWith(Path.GetFullPath(Path.Combine(Harness.TempDir, "Data")) + Path.DirectorySeparatorChar, Path.GetFullPath(photo.GridThumbPath!));
                using var stream = new InMemoryRandomAccessStream(); using (var writer = new DataWriter(stream)) { writer.WriteBytes(File.ReadAllBytes(photo.GridThumbPath!)); await writer.StoreAsync(); writer.DetachStream(); }
                stream.Seek(0); var decoded = await BitmapDecoder.CreateAsync(stream); Assert.Equal(1U, decoded.PixelWidth); Assert.Equal(1U, decoded.PixelHeight);
                Assert.Equal(Pixels, File.ReadAllBytes(freshSource));
            }
            finally { photo.PropertyChanged -= Changed; }
        }

        public string Snapshot(bool excludeTarget = false)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Harness.DbPath }.ToString()); connection.Open();
            var content = new Dictionary<string, List<object?[]>>();
            foreach (var table in new[] { "photos", "tags", "photo_tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence" })
            {
                using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table}" + (excludeTarget && table is "photos" or "photo_tags" ? " WHERE photo_path<>@target" : "") + " ORDER BY 1,2";
                command.Parameters.AddWithValue("@target", TargetPath); using var reader = command.ExecuteReader(); var rows = new List<object?[]>();
                while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray()); content.Add(table, rows);
            }
            return JsonSerializer.Serialize(content);
        }

        public async ValueTask DisposeAsync()
        {
            try { await Harness.DisposeAsync(); Assert.False(Directory.Exists(Harness.TempDir)); }
            finally { AppPaths.DataDirOverrideForTests = previousDataRoot; }
        }
    }
}
