using System.Text;
using System.Text.Json;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging;
using Alpheratz.Core.Scanner;
using Alpheratz.Features.Gallery;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>公開Gallery操作から実SQLiteの故障・状態保持・同じ対象の再試行を検証する。</summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class GalleryMutationFailureTests
{
    [Theory]
    [InlineData("favorite")]
    [InlineData("tag")]
    [InlineData("tags")]
    [InlineData("remove")]
    public async Task SingleMutation_DatabaseFailureKeepsAllRowsAndVisibleCopiesAndSameInputRetrySucceeds(string operation)
    {
        await using var fixture = await MutationFixture.Create();
        var before = fixture.Snapshot(); var retained = fixture.RetainedRows(fixture.First.PhotoPath);
        var metadata = fixture.PhotoMetadata(); var filters = fixture.ViewModel.filtersState.TagFilterCounts.ToArray();
        fixture.FailMutation(operation, fixture.First.PhotoPath);

        await fixture.SingleMutation(operation);

        Assert.Equal(before, fixture.Snapshot()); Assert.Equal(filters, fixture.ViewModel.filtersState.TagFilterCounts.ToArray());
        Assert.False(fixture.First.IsFavorite); Assert.Equal(["保持"], fixture.First.Tags);
        Assert.False(fixture.DrillFirst.IsFavorite); Assert.Equal(["保持"], fixture.DrillFirst.Tags);
        var failure = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.error, failure.Type);
        Assert.Equal(operation switch
        {
            "favorite" => "お気に入りを更新できませんでした。お気に入りをもう一度変更してください。",
            "remove" => "写真からタグを削除できませんでした。タグをもう一度削除してください。",
            _ => "写真にタグを追加できませんでした。タグをもう一度追加してください。",
        }, failure.Msg);
        fixture.AssertSourceBytes(); fixture.Execute("DROP TRIGGER mutation_failure"); fixture.Toasts.toasts.Clear();

        await fixture.SingleMutation(operation);

        var saved = await fixture.Db.GetPhotoRecordAsync(fixture.First.PhotoPath); Assert.NotNull(saved);
        var expectedTags = operation switch { "tag" => new[] { "Alpha", "保持" }, "tags" => ["Alpha", "Zeta", "保持"], "remove" => [], _ => ["保持"] };
        Assert.Equal(operation == "favorite", saved.is_favorite); Assert.Equal(saved.is_favorite, fixture.First.IsFavorite);
        Assert.Equal(saved.is_favorite, fixture.DrillFirst.IsFavorite);
        Assert.Equal(expectedTags.Order(), (await fixture.Db.GetPhotoTagsAsync(fixture.First.PhotoPath)).Order());
        Assert.Equal(expectedTags.Order(), fixture.First.Tags.Order()); Assert.Equal(expectedTags.Order(), fixture.DrillFirst.Tags.Order());
        Assert.Same(fixture.First, fixture.ViewModel.photosState.displayItems.Single(item => item.Photo.PhotoPath == fixture.First.PhotoPath).Photo);
        Assert.Equal(retained, fixture.RetainedRows(fixture.First.PhotoPath)); Assert.Equal(metadata, fixture.PhotoMetadata());
        if (operation != "favorite")
        {
            var success = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.info, success.Type);
            Assert.Equal(operation switch { "remove" => "写真からタグを削除しました。", "tags" => "写真に2件のタグを追加しました。", _ => "写真にタグを追加しました。" }, success.Msg);
            Assert.Equal(operation == "remove" ? 2L : 3L, fixture.ViewModel.filtersState.TagFilterCounts["保持"]);
        }
        else Assert.Empty(fixture.Toasts.toasts);
        fixture.AssertSourceBytes();
    }

    [Theory]
    [InlineData("favorite")]
    [InlineData("tags")]
    public async Task BulkMutation_ActualPartialFailureKeepsOnlyFailedSelectionAndRetriesWithoutChangingCompletedRows(string operation)
    {
        await using var fixture = await MutationFixture.Create();
        await fixture.Select(fixture.First, fixture.Second);
        var preserved = fixture.RetainedRows(fixture.First.PhotoPath, fixture.Second.PhotoPath); var metadata = fixture.PhotoMetadata();
        fixture.FailMutation(operation, fixture.Second.PhotoPath);

        await fixture.BulkMutation(operation); await fixture.ViewModel.selectionState.loadSelectedPhotoRefs();

        Assert.Equal(operation == "favorite", (await fixture.Db.GetPhotoRecordAsync(fixture.First.PhotoPath))!.is_favorite);
        Assert.False((await fixture.Db.GetPhotoRecordAsync(fixture.Second.PhotoPath))!.is_favorite);
        Assert.Equal(operation == "tags" ? new[] { "Alpha", "Zeta", "保持" } : ["保持"], (await fixture.Db.GetPhotoTagsAsync(fixture.First.PhotoPath)).Order().ToArray());
        Assert.Equal(["保持"], await fixture.Db.GetPhotoTagsAsync(fixture.Second.PhotoPath));
        Assert.Equal(operation == "favorite", fixture.First.IsFavorite); Assert.False(fixture.Second.IsFavorite);
        Assert.Equal(fixture.First.Tags.Order(), fixture.DrillFirst.Tags.Order()); Assert.Equal(["保持"], fixture.Second.Tags);
        Assert.False(fixture.First.IsSelected); Assert.True(fixture.Second.IsSelected);
        Assert.Equal([fixture.Second.PhotoPath], fixture.ViewModel.selectionState.selectedPhotoPaths);
        var failedRef = Assert.Single(fixture.ViewModel.selectionState.selectedPhotoRefs); Assert.Equal(fixture.Second.PhotoPath, failedRef.photo_path); Assert.Equal(2L, failedRef.source_slot);
        var notification = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.error, notification.Type);
        Assert.Equal((operation == "favorite" ? "お気に入りの更新" : "タグの追加") + "に失敗した写真があります。1枚は更新済みです。未完了の1枚を選択したままにしています：2.jpg。同じ操作を実行できます。", notification.Msg);
        if (operation == "tags") { Assert.Equal(1L, fixture.ViewModel.filtersState.TagFilterCounts["Alpha"]); Assert.Equal(1L, fixture.ViewModel.filtersState.TagFilterCounts["Zeta"]); }
        var completed = fixture.PhotoRows(fixture.First.PhotoPath); fixture.AssertSourceBytes();
        fixture.Execute("DROP TRIGGER mutation_failure"); fixture.Toasts.toasts.Clear();

        await fixture.BulkMutation(operation);

        Assert.Equal(completed, fixture.PhotoRows(fixture.First.PhotoPath)); Assert.Equal(preserved, fixture.RetainedRows(fixture.First.PhotoPath, fixture.Second.PhotoPath));
        Assert.Equal(metadata, fixture.PhotoMetadata()); Assert.Equal(operation == "favorite", fixture.Second.IsFavorite);
        Assert.Equal(operation == "favorite", (await fixture.Db.GetPhotoRecordAsync(fixture.Second.PhotoPath))!.is_favorite);
        Assert.Equal(operation == "tags" ? new[] { "Alpha", "Zeta", "保持" } : ["保持"], (await fixture.Db.GetPhotoTagsAsync(fixture.Second.PhotoPath)).Order().ToArray());
        Assert.Equal(fixture.First.Tags.Order(), fixture.Second.Tags.Order()); Assert.True(fixture.Second.IsSelected);
        Assert.Equal([fixture.Second.PhotoPath], fixture.ViewModel.selectionState.selectedPhotoPaths);
        var success = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.info, success.Type);
        Assert.Equal(operation == "favorite" ? "1枚をお気に入りに追加しました。" : "2件のタグを1枚の写真に追加しました。", success.Msg);
        if (operation == "tags") { Assert.Equal(2L, fixture.ViewModel.filtersState.TagFilterCounts["Alpha"]); Assert.Equal(2L, fixture.ViewModel.filtersState.TagFilterCounts["Zeta"]); }
        fixture.AssertSourceBytes();
    }

    [Theory]
    [InlineData("favorite")]
    [InlineData("tags")]
    public async Task BulkMutation_SelectedPhotoReadFailurePreservesRowsAndSelectionAndSameSelectionCanRetry(string operation)
    {
        await using var fixture = await MutationFixture.Create(); await fixture.Select(fixture.First, fixture.Second);
        var before = fixture.Snapshot(); var selected = fixture.ViewModel.selectionState.selectedPhotoRefs.ToArray();
        var metadata = fixture.PhotoMetadata(); var preserved = fixture.RetainedRows(fixture.First.PhotoPath, fixture.Second.PhotoPath);
        fixture.Execute("ALTER TABLE photos RENAME TO unavailable_photos");
        try { await fixture.BulkMutation(operation); }
        finally { fixture.Execute("ALTER TABLE unavailable_photos RENAME TO photos"); }

        Assert.Equal(before, fixture.Snapshot()); Assert.Equal(selected, fixture.ViewModel.selectionState.selectedPhotoRefs);
        Assert.Equal([fixture.First.PhotoPath, fixture.Second.PhotoPath], fixture.ViewModel.selectionState.selectedPhotoPaths);
        Assert.False(fixture.First.IsFavorite); Assert.False(fixture.Second.IsFavorite); Assert.Equal(["保持"], fixture.First.Tags); Assert.Equal(["保持"], fixture.Second.Tags);
        var failure = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.error, failure.Type);
        Assert.Equal("選択した写真の情報を取得できないため、一括操作を実行できません。", failure.Msg);
        fixture.AssertSourceBytes(); fixture.Toasts.toasts.Clear();

        await fixture.BulkMutation(operation);

        Assert.Equal(operation == "favorite", fixture.First.IsFavorite); Assert.Equal(operation == "favorite", fixture.Second.IsFavorite);
        foreach (var photo in new[] { fixture.First, fixture.Second })
        {
            Assert.Equal(operation == "favorite", (await fixture.Db.GetPhotoRecordAsync(photo.PhotoPath))!.is_favorite);
            Assert.Equal(operation == "tags" ? new[] { "Alpha", "Zeta", "保持" } : ["保持"], (await fixture.Db.GetPhotoTagsAsync(photo.PhotoPath)).Order().ToArray());
        }
        Assert.Equal(metadata, fixture.PhotoMetadata()); Assert.Equal(preserved, fixture.RetainedRows(fixture.First.PhotoPath, fixture.Second.PhotoPath));
        Assert.Equal(fixture.First.Tags.Order(), fixture.Second.Tags.Order());
        Assert.Equal(operation == "tags" ? new[] { "Alpha", "Zeta", "保持" } : ["保持"], fixture.First.Tags.Order().ToArray());
        var success = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.info, success.Type);
        Assert.Equal(operation == "favorite" ? "2枚をお気に入りに追加しました。" : "2件のタグを2枚の写真に追加しました。", success.Msg);
        fixture.AssertSourceBytes();
    }

    [Theory]
    [InlineData("favorite")]
    [InlineData("tags")]
    public async Task BulkMutation_AllSixFailuresPreserveRowsAndSummarizeFiveNamesAndRetryAllSelectedPhotos(string operation)
    {
        await using var fixture = await MutationFixture.Create(); var photos = await fixture.IncludeSixPhotos(); await fixture.Select(photos);
        var before = fixture.Snapshot(); var metadata = fixture.PhotoMetadata(); fixture.FailMutation(operation, path: null);
        var preserved = fixture.RetainedRows(photos.Select(photo => photo.PhotoPath).ToArray());

        await fixture.BulkMutation(operation); await fixture.ViewModel.selectionState.loadSelectedPhotoRefs();

        Assert.Equal(before, fixture.Snapshot()); Assert.All(photos, photo => { Assert.True(photo.IsSelected); Assert.False(photo.IsFavorite); Assert.Equal(["保持"], photo.Tags); });
        Assert.Equal(6, fixture.ViewModel.selectionState.selectedPhotoPaths.Count); Assert.Equal(6, fixture.ViewModel.selectionState.selectedPhotoRefs.Count);
        Assert.Equal(photos.Select(photo => photo.PhotoPath).Order(), fixture.ViewModel.selectionState.selectedPhotoRefs.Select(photo => photo.photo_path).Order());
        var failure = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.error, failure.Type);
        Assert.StartsWith((operation == "favorite" ? "お気に入りの更新" : "タグの追加") + "に失敗した写真があります。未完了の6枚を選択したままにしています：", failure.Msg);
        Assert.Contains("、ほか1枚。同じ操作を実行できます。", failure.Msg);
        Assert.Equal(5, photos.Count(photo => failure.Msg.Contains(photo.PhotoFilename, StringComparison.Ordinal)));
        Assert.Equal(6L, fixture.ViewModel.filtersState.TagFilterCounts["保持"]); fixture.AssertSourceBytes();
        fixture.Execute("DROP TRIGGER mutation_failure"); fixture.Toasts.toasts.Clear();

        await fixture.BulkMutation(operation);

        foreach (var photo in photos)
        {
            Assert.Equal(operation == "favorite", (await fixture.Db.GetPhotoRecordAsync(photo.PhotoPath))!.is_favorite);
            Assert.Equal(operation == "tags" ? new[] { "Alpha", "Zeta", "保持" } : ["保持"], (await fixture.Db.GetPhotoTagsAsync(photo.PhotoPath)).Order().ToArray());
            Assert.Equal(operation == "favorite", photo.IsFavorite); Assert.True(photo.IsSelected);
        }
        Assert.Equal(metadata, fixture.PhotoMetadata()); Assert.Equal(6, fixture.ViewModel.selectionState.selectedPhotoPaths.Count);
        Assert.Equal(preserved, fixture.RetainedRows(photos.Select(photo => photo.PhotoPath).ToArray()));
        var success = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.info, success.Type);
        Assert.Equal(operation == "favorite" ? "6枚をお気に入りに追加しました。" : "2件のタグを6枚の写真に追加しました。", success.Msg);
        if (operation == "tags") { Assert.Equal(6L, fixture.ViewModel.filtersState.TagFilterCounts["Alpha"]); Assert.Equal(6L, fixture.ViewModel.filtersState.TagFilterCounts["Zeta"]); }
        fixture.AssertSourceBytes();
    }

    [Theory]
    [InlineData("favorite", false)]
    [InlineData("tags", false)]
    [InlineData("favorite", true)]
    [InlineData("tags", true)]
    public async Task BulkMutation_SelectionChangedBeforeResultPreservesCurrentSelectionAndExplainsHowToRetry(string operation, bool clearSelection)
    {
        await using var fixture = await MutationFixture.Create();
        await fixture.Select(fixture.First, fixture.Second);
        var next = fixture.ViewModel.photosState.photos.Single(photo => photo.PhotoFilename == "keep.jpg");
        var untouched = fixture.PhotoRows(next.PhotoPath);
        fixture.FailMutation(operation, fixture.Second.PhotoPath);
        var changed = false;
        // 成功写真の通知時に選択を変更し、同じ結果内の失敗通知が届く前の操作を再現する。
        fixture.First.PropertyChanged += (_, args) =>
        {
            if (changed || args.PropertyName != (operation == "favorite" ? nameof(PhotoThumbnailItem.IsFavorite) : nameof(PhotoThumbnailItem.Tags))) return;
            changed = true;
            fixture.ViewModel.selectionState.clearSelectedPhotos();
            if (!clearSelection)
                fixture.ViewModel.selectionState.toggleSelectedPhoto(
                    new PhotoGridItem { Photo = next }, shiftKey: false, fixture.ViewModel.photosState.displayItems.ToArray());
        };

        await fixture.BulkMutation(operation);
        await fixture.ViewModel.selectionState.loadSelectedPhotoRefs();

        Assert.True(changed);
        var expectedSelection = clearSelection ? Array.Empty<string>() : [next.PhotoPath];
        Assert.Equal(expectedSelection, fixture.ViewModel.selectionState.selectedPhotoPaths);
        Assert.Equal(expectedSelection, fixture.ViewModel.selectionState.selectedPhotoRefs.Select(photo => photo.photo_path));
        Assert.False(fixture.First.IsSelected); Assert.False(fixture.Second.IsSelected); Assert.Equal(!clearSelection, next.IsSelected);
        Assert.Equal(untouched, fixture.PhotoRows(next.PhotoPath));
        Assert.Equal(operation == "favorite", (await fixture.Db.GetPhotoRecordAsync(fixture.First.PhotoPath))!.is_favorite);
        Assert.False((await fixture.Db.GetPhotoRecordAsync(fixture.Second.PhotoPath))!.is_favorite);
        Assert.Equal(operation == "tags" ? new[] { "Alpha", "Zeta", "保持" } : ["保持"], (await fixture.Db.GetPhotoTagsAsync(fixture.First.PhotoPath)).Order().ToArray());
        Assert.Equal(["保持"], await fixture.Db.GetPhotoTagsAsync(fixture.Second.PhotoPath));
        var failure = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.error, failure.Type);
        Assert.Equal((operation == "favorite" ? "お気に入りの更新" : "タグの追加") +
            "に失敗した写真があります。1枚は更新済みです。未完了の1枚：2.jpg。現在の選択は維持しています。再試行する場合は、未完了の写真を選択してください。", failure.Msg);
        fixture.AssertSourceBytes();
        fixture.Execute("DROP TRIGGER mutation_failure"); fixture.Toasts.toasts.Clear();
        fixture.ViewModel.selectionState.clearSelectedPhotos(); await fixture.Select(fixture.Second);
        var completed = fixture.PhotoRows(fixture.First.PhotoPath);

        await fixture.BulkMutation(operation);

        Assert.Equal(completed, fixture.PhotoRows(fixture.First.PhotoPath)); Assert.Equal(untouched, fixture.PhotoRows(next.PhotoPath));
        Assert.Equal(operation == "favorite", (await fixture.Db.GetPhotoRecordAsync(fixture.Second.PhotoPath))!.is_favorite);
        Assert.Equal(operation == "tags" ? new[] { "Alpha", "Zeta", "保持" } : ["保持"], (await fixture.Db.GetPhotoTagsAsync(fixture.Second.PhotoPath)).Order().ToArray());
        var success = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.info, success.Type);
        Assert.Equal(operation == "favorite" ? "1枚をお気に入りに追加しました。" : "2件のタグを1枚の写真に追加しました。", success.Msg);
        fixture.AssertSourceBytes();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkCopy_UnavailableDestinationPreservesSelectionDatabaseAndSourcesAndSameDestinationCanRetry(bool destinationIsFile)
    {
        await using var fixture = await MutationFixture.Create(); await fixture.Select(fixture.First, fixture.Second);
        var before = fixture.Snapshot(); var destination = fixture.CopyDestination;
        if (destinationIsFile) await File.WriteAllTextAsync(destination, "保持する衝突ファイル");

        await fixture.ViewModel.bulkCopyPhotos(destination);

        Assert.Equal(before, fixture.Snapshot()); fixture.AssertSourceBytes();
        Assert.Equal([fixture.First.PhotoPath, fixture.Second.PhotoPath], fixture.ViewModel.selectionState.selectedPhotoPaths);
        Assert.True(fixture.First.IsSelected); Assert.True(fixture.Second.IsSelected);
        Assert.False(Directory.Exists(destination));
        if (destinationIsFile) Assert.Equal("保持する衝突ファイル", await File.ReadAllTextAsync(destination));
        var failure = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.error, failure.Type);
        Assert.Equal("写真をコピーできませんでした。コピー元とコピー先のフォルダを確認してください。", failure.Msg);
        if (destinationIsFile) File.Delete(destination);
        Directory.CreateDirectory(destination); fixture.Toasts.toasts.Clear();

        await fixture.ViewModel.bulkCopyPhotos(destination);

        Assert.Equal(before, fixture.Snapshot()); fixture.AssertSourceBytes();
        Assert.Equal(new[] { "1.jpg", "2.jpg" }, Directory.GetFiles(destination).Select(Path.GetFileName).Order().ToArray());
        foreach (var photo in new[] { fixture.First, fixture.Second })
            Assert.Equal(await File.ReadAllBytesAsync(photo.PhotoPath), await File.ReadAllBytesAsync(Path.Combine(destination, photo.PhotoFilename)));
        var success = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.info, success.Type); Assert.Equal("2枚の写真をコピーしました。", success.Msg);
        Assert.Equal([fixture.First.PhotoPath, fixture.Second.PhotoPath], fixture.ViewModel.selectionState.selectedPhotoPaths);
    }

    [Fact]
    public async Task BulkCopy_NameCollisionPreservesExistingFileCopiesOtherPhotoAndReselectedFailureCanRetryElsewhere()
    {
        await using var fixture = await MutationFixture.Create(); await fixture.Select(fixture.First, fixture.Second);
        var before = fixture.Snapshot(); var destination = fixture.CopyDestination; Directory.CreateDirectory(destination);
        var conflictPath = Path.Combine(destination, fixture.First.PhotoFilename); var conflictBytes = Encoding.UTF8.GetBytes("別の写真として保持する出力先の内容");
        await File.WriteAllBytesAsync(conflictPath, conflictBytes);

        await fixture.ViewModel.bulkCopyPhotos(destination);

        Assert.Equal(conflictBytes, await File.ReadAllBytesAsync(conflictPath));
        var completedPath = Path.Combine(destination, fixture.Second.PhotoFilename); var completedBytes = await File.ReadAllBytesAsync(completedPath);
        Assert.Equal(await File.ReadAllBytesAsync(fixture.Second.PhotoPath), completedBytes);
        Assert.Equal(before, fixture.Snapshot()); fixture.AssertSourceBytes();
        var partial = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.info, partial.Type);
        Assert.Equal("1枚をコピーし、1枚はコピーできませんでした。", partial.Msg);
        Assert.Equal([fixture.First.PhotoPath, fixture.Second.PhotoPath], fixture.ViewModel.selectionState.selectedPhotoPaths);
        fixture.ViewModel.selectionState.clearSelectedPhotos(); await fixture.Select(fixture.First); fixture.Toasts.toasts.Clear();
        var retryDestination = Path.Combine(destination, "retry"); Directory.CreateDirectory(retryDestination);

        await fixture.ViewModel.bulkCopyPhotos(retryDestination);

        Assert.Equal([fixture.First.PhotoFilename], Directory.GetFiles(retryDestination).Select(Path.GetFileName));
        Assert.Equal(await File.ReadAllBytesAsync(fixture.First.PhotoPath), await File.ReadAllBytesAsync(Path.Combine(retryDestination, fixture.First.PhotoFilename)));
        Assert.Equal(conflictBytes, await File.ReadAllBytesAsync(conflictPath)); Assert.Equal(completedBytes, await File.ReadAllBytesAsync(completedPath));
        Assert.Equal(before, fixture.Snapshot()); fixture.AssertSourceBytes();
        var success = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.info, success.Type); Assert.Equal("1枚の写真をコピーしました。", success.Msg);
    }

    private sealed class MutationFixture : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "Alpheratz.GalleryMutation.Tests", Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]> sources = [];
        private string DbPath => Path.Combine(root, "gallery.db");
        public string CopyDestination => Path.Combine(root, "copy-destination");
        private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString();
        public AlpheratzDb Db { get; }
        public GalleryViewModel ViewModel { get; }
        public ToastService Toasts { get; } = new();
        public PhotoThumbnailItem First { get; private set; } = null!;
        public PhotoThumbnailItem Second { get; private set; } = null!;
        public PhotoThumbnailItem DrillFirst { get; private set; } = null!;

        private MutationFixture()
        {
            Directory.CreateDirectory(root); Db = new AlpheratzDb(DbPath); Db.Initialize();
            var events = new LocalEventBus(); var config = new AppConfig(Path.Combine(root, "settings")); var photos = new PhotoService(Db);
            var scanner = new PhotoScanner(config, Db, events);
            var state = new GalleryPhotosState(photos, events, Toasts, new DispatcherService(), new ThumbnailWorker(new ThumbnailService()));
            ViewModel = new GalleryViewModel(photos, new WorldService(Db, scanner), new PhashService(Db, events), Toasts, new DispatcherService(),
                state, new GalleryFiltersState(), new GallerySelectionState(photos, Toasts), new GalleryDisplayState(), new GalleryScrollState());
        }

        public static async Task<MutationFixture> Create()
        {
            var fixture = new MutationFixture();
            try
            {
                fixture.First = await fixture.Seed("1.jpg", 1); fixture.Second = await fixture.Seed("2.jpg", 2); var third = await fixture.Seed("keep.jpg", 1);
                fixture.DrillFirst = PhotoThumbnailItem.FromDto((await fixture.Db.GetPhotoRecordAsync(fixture.First.PhotoPath))!);
                fixture.ViewModel.drillDownPhotosProvider = () => [fixture.DrillFirst];
                fixture.ViewModel.photosState.setPhotos([fixture.First, fixture.Second, third], autoGenerateThumbnails: false);
                fixture.ViewModel.photosState.rebuildDisplayItems(GroupingMode.none);
                fixture.Execute("INSERT INTO photo_user_data_backup VALUES ('保存済み.jpg', 1, '[\"保持\"]'); INSERT INTO archive_world_visits (source_log_name,world_name,join_time,leave_time) VALUES ('所有log.txt','保持ワールド','2026-10-08 09:00:00','2026-10-08 10:00:00')");
                await fixture.ViewModel.loadTagFilterCounts(); Assert.Equal(3L, fixture.ViewModel.filtersState.TagFilterCounts["保持"]);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private async Task<PhotoThumbnailItem> Seed(string name, int slot)
        {
            var source = Path.Combine(root, name); var bytes = Encoding.UTF8.GetBytes(name + "\n保持する元のbyte列\n"); File.WriteAllBytes(source, bytes); sources.Add(source, bytes);
            var path = AppPaths.NormalizePathForDb(source);
            await Db.UpsertPhotoAsync(new PhotoUpsertData { PhotoPath = path, PhotoFilename = name, SourceSlot = slot, Timestamp = "2026-10-08 10:00:00",
                WorldId = "wrld-owned", WorldName = "保持ワールド", MatchSource = "manual", Orientation = "landscape", ImageWidth = 200, ImageHeight = 100, LastModifiedUtc = "2026-10-08T10:00:00Z" });
            await Db.AddPhotoTagAsync(path, "保持");
            return PhotoThumbnailItem.FromDto((await Db.GetPhotoRecordAsync(path))!);
        }

        public async Task Select(params PhotoThumbnailItem[] photos)
        {
            foreach (var photo in photos) ViewModel.selectionState.selectedPhotoPaths.Add(photo.PhotoPath);
            ViewModel.selectionState.syncSelectionWithVisiblePhotos(ViewModel.photosState.photos); await ViewModel.selectionState.loadSelectedPhotoRefs();
        }

        public async Task<PhotoThumbnailItem[]> IncludeSixPhotos()
        {
            var original = ViewModel.photosState.photos.ToArray(); var added = new[] { await Seed("3.jpg", 2), await Seed("4.jpg", 1), await Seed("5.jpg", 2) };
            var all = original.Concat(added).ToArray(); ViewModel.photosState.setPhotos(all, autoGenerateThumbnails: false);
            ViewModel.photosState.rebuildDisplayItems(GroupingMode.none); await ViewModel.loadTagFilterCounts(); return all;
        }

        public Task SingleMutation(string operation) => operation switch
        {
            "favorite" => ViewModel.toggleFavorite(First.PhotoPath, currentIsFavorite: false),
            "tag" => ViewModel.addTag(First.PhotoPath, " Alpha "),
            "tags" => ViewModel.addTags(First.PhotoPath, [" Alpha ", "Zeta", "ALPHA"]),
            "remove" => ViewModel.removeTag(First.PhotoPath, "保持"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        public Task BulkMutation(string operation) => operation == "favorite" ? ViewModel.bulkSetFavorite(true) : ViewModel.bulkAddTags([" Alpha ", "Zeta", "ALPHA"]);

        // 複数タグは2件目のlinkで故障させ、最初のタグ・link・採番値もrollbackする契約を確認する。
        public void FailMutation(string operation, string? path)
        {
            var quoted = path?.Replace("'", "''"); var target = path is null ? "1=1" : $"NEW.photo_path = '{quoted}'";
            var clause = operation switch
            {
                "favorite" => $"AFTER UPDATE OF is_favorite ON photos WHEN {target}",
                "remove" => $"BEFORE DELETE ON photo_tags WHEN OLD.photo_path = '{quoted}'",
                _ => $"AFTER INSERT ON photo_tags WHEN {target} AND (SELECT name FROM tags WHERE id = NEW.tag_id) = '{(operation == "tag" ? "Alpha" : "Zeta")}'",
            };
            Execute($"CREATE TRIGGER mutation_failure {clause} BEGIN SELECT RAISE(ABORT, 'owned gallery mutation fault'); END");
        }

        public void Execute(string sql) { using var connection = new SqliteConnection(ConnectionString); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
        public string Snapshot() => QuerySnapshot(["SELECT * FROM photos ORDER BY photo_path", "SELECT * FROM photo_tags ORDER BY photo_path,tag_id", "SELECT * FROM tags ORDER BY id",
            "SELECT * FROM photo_user_data_backup ORDER BY photo_filename", "SELECT * FROM archive_world_visits ORDER BY id", "SELECT * FROM sqlite_sequence ORDER BY name"]);
        public string RetainedRows(params string[] changed)
        {
            var paths = string.Join(',', changed.Select(path => "'" + path.Replace("'", "''") + "'"));
            return QuerySnapshot([$"SELECT * FROM photos WHERE photo_path NOT IN ({paths}) ORDER BY photo_path", $"SELECT * FROM photo_tags WHERE photo_path NOT IN ({paths}) ORDER BY photo_path,tag_id",
                "SELECT * FROM photo_user_data_backup ORDER BY photo_filename", "SELECT * FROM archive_world_visits ORDER BY id"]);
        }
        public string PhotoRows(string path)
        {
            var quoted = path.Replace("'", "''"); return QuerySnapshot([$"SELECT * FROM photos WHERE photo_path = '{quoted}'", $"SELECT * FROM photo_tags WHERE photo_path = '{quoted}' ORDER BY tag_id"]);
        }
        public string PhotoMetadata() => QuerySnapshot(["SELECT photo_path,photo_filename,world_id,world_name,timestamp,last_modified_utc,orientation,image_width,image_height,source_slot,match_source,phash,phash_version,is_missing FROM photos ORDER BY photo_path"]);
        private string QuerySnapshot(string[] queries)
        {
            using var connection = new SqliteConnection(ConnectionString); connection.Open(); var content = new List<List<object?[]>>();
            foreach (var query in queries)
            {
                using var command = connection.CreateCommand(); command.CommandText = query; using var reader = command.ExecuteReader(); var rows = new List<object?[]>();
                while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray()); content.Add(rows);
            }
            return JsonSerializer.Serialize(content);
        }
        public void AssertSourceBytes() { foreach (var (path, bytes) in sources) Assert.Equal(bytes, File.ReadAllBytes(path)); }
        public async ValueTask DisposeAsync()
        {
            ViewModel.Cleanup(); ViewModel.selectionState.Dispose(); await ViewModel.photosState.DisposeAsync();
            using var pool = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString()); SqliteConnection.ClearPool(pool);
            Assert.Equal(Path.Combine(Path.GetTempPath(), "Alpheratz.GalleryMutation.Tests"), Path.GetDirectoryName(root));
            Directory.Delete(root, recursive: true); Assert.False(Directory.Exists(root));
        }
    }
}
