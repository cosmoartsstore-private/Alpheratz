using System.Runtime.CompilerServices;
using System.Text.Json;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Alpheratz.Tests;

/// <summary>実UI queueとSQLiteで写真詳細の選択、タグ再取得、読込故障後の復旧を検証する。</summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class NativePhotoModalStateTests
{
    private const string ChildMarker = "ALPHERATZ_NATIVE_PHOTO_MODAL_TEST_CHILD";

    [Fact]
    public Task NativeModal_ThrottledNavigationUsesTheSameSelectedRowsAndDirectionReversalRemainsAvailable() => RunIsolated(async fixture =>
    {
        var before = fixture.Snapshot();
        await fixture.OnUi(() =>
        {
            fixture.State.setPhotoList([fixture.First, fixture.Second, fixture.Third]); fixture.State.onSelectPhoto(fixture.First);
            fixture.State.goNextPhoto(throttleRepeatedInput: true); fixture.AssertSelected(fixture.Second, "別写真");
            // 受理時刻を未来に固定し、実clockの進行に左右されず同方向の即時再入力を再現する。
            typeof(Alpheratz.Features.PhotoModal.PhotoModalState).GetField("lastNavigationAcceptedTicks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(fixture.State, long.MaxValue);
            fixture.State.goNextPhoto(throttleRepeatedInput: true); fixture.AssertSelected(fixture.Second, "別写真");
            fixture.State.goPrevPhoto(throttleRepeatedInput: true); fixture.AssertSelected(fixture.First, "初回");
        });
        Assert.Equal(before, fixture.Snapshot()); fixture.AssertSourceBytes();
    });

    [Fact]
    public Task NativeModal_NavigationAndHistoryReadCurrentTagsOnUiQueueWithoutChangingStorage() => RunIsolated(async fixture =>
    {
        var before = fixture.Snapshot();
        await fixture.OnUi(() =>
        {
            fixture.State.setSelectedPhoto(fixture.First);
            fixture.AssertSelected(fixture.First, "初回");
            Assert.False(fixture.State.CanGoPrev); Assert.True(fixture.State.CanGoNext);
            fixture.State.goPrevPhoto(); fixture.AssertSelected(fixture.First, "初回");
            fixture.State.goNextPhoto(); fixture.AssertSelected(fixture.Second, "別写真");
            fixture.State.goNextPhoto(); fixture.AssertSelected(fixture.Third, "保持");
            Assert.False(fixture.State.CanGoNext);
            fixture.State.goNextPhoto(); fixture.AssertSelected(fixture.Third, "保持");
            fixture.State.goPrevPhoto(); fixture.AssertSelected(fixture.Second, "別写真");
            fixture.State.onSelectPhoto(fixture.First, isSimilarSearch: true);
            fixture.AssertSelected(fixture.First, "初回"); Assert.Same(fixture.Second, Assert.Single(fixture.State.photoHistory));
        });
        Assert.Equal(before, fixture.Snapshot());

        await fixture.Db.AddPhotoTagAsync(fixture.Second.PhotoPath, "最新");
        var updated = fixture.Snapshot();
        await fixture.OnUi(() =>
        {
            fixture.State.goBackPhoto(); fixture.AssertSelected(fixture.Second, "別写真", "最新");
            Assert.Empty(fixture.State.photoHistory); Assert.False(fixture.State.CanGoBack);
            fixture.State.goBackPhoto(); fixture.AssertSelected(fixture.Second, "別写真", "最新");
            Assert.Equal(["初回"], fixture.First.Tags); Assert.Equal(["保持"], fixture.Third.Tags);
            Assert.Empty(fixture.Toasts.toasts); fixture.AssertTagNotificationsUseQueue();
        });
        Assert.Equal(updated, fixture.Snapshot()); fixture.AssertSourceBytes();
    });

    [Fact]
    public Task NativeModal_AllSelectionRoutesKeepDisplayedTagsOnReadFailureAndReselectAfterRecovery() => RunIsolated(async fixture =>
    {
        foreach (var route in new[] { "direct", "select", "previous", "next", "back" })
        {
            await fixture.OnUi(() =>
            {
                fixture.State.closePhotoModal(); fixture.Toasts.toasts.Clear();
                if (route == "previous") fixture.State.onSelectPhoto(fixture.Third);
                else if (route == "next") fixture.State.onSelectPhoto(fixture.First);
                else
                {
                    fixture.State.onSelectPhoto(fixture.Second);
                    if (route == "back") fixture.State.onSelectPhoto(fixture.Third, isSimilarSearch: true);
                }
                fixture.Second.Tags = ["表示済み"];
            });
            var before = fixture.Snapshot();
            // 読込先の関連tableだけを一時的に不可用にし、実serviceの例外処理を通す。
            fixture.Execute("ALTER TABLE photo_tags RENAME TO owned_unavailable_photo_tags");
            try
            {
                await fixture.OnUi(() =>
                {
                    switch (route)
                    {
                        case "direct": fixture.State.setSelectedPhoto(fixture.Second); break;
                        case "select": fixture.State.onSelectPhoto(fixture.Second); break;
                        case "previous": fixture.State.goPrevPhoto(); break;
                        case "next": fixture.State.goNextPhoto(); break;
                        case "back": fixture.State.goBackPhoto(); break;
                    }
                    fixture.AssertSelected(fixture.Second, "表示済み");
                    Assert.True(fixture.State.CanGoPrev); Assert.True(fixture.State.CanGoNext);
                    Assert.Empty(fixture.State.photoHistory);
                    var failure = Assert.Single(fixture.Toasts.toasts);
                    Assert.Equal(ToastType.error, failure.Type);
                    Assert.Equal("写真のタグを読み込めませんでした。写真詳細を閉じて、写真を開き直してください。", failure.Msg);
                });
            }
            finally { fixture.Execute("ALTER TABLE owned_unavailable_photo_tags RENAME TO photo_tags"); }
            Assert.Equal(before, fixture.Snapshot()); fixture.AssertSourceBytes();

            await fixture.OnUi(() =>
            {
                // 故障後に保持した同じ写真instanceを再選択し、表示だけを最新DB値へ戻す。
                fixture.State.onSelectPhoto(fixture.Second); fixture.AssertSelected(fixture.Second, "別写真");
                Assert.Single(fixture.Toasts.toasts); fixture.AssertTagNotificationsUseQueue();
            });
            Assert.Equal(before, fixture.Snapshot());
        }
    });

    [Fact]
    public Task NativeModal_QueuedCloseAndReopenClearHistoryAndLoadFreshTagsForTheSamePath() => RunIsolated(async fixture =>
    {
        var before = fixture.Snapshot();
        var select = fixture.OnUi(() =>
        {
            fixture.State.onSelectPhoto(fixture.First);
            fixture.State.onSelectPhoto(fixture.Second, isSimilarSearch: true);
            fixture.AssertSelected(fixture.Second, "別写真"); Assert.True(fixture.State.CanGoBack);
        });
        var close = fixture.OnUi(() =>
        {
            fixture.State.closePhotoModal(); Assert.Null(fixture.State.SelectedPhoto);
            Assert.Empty(fixture.State.photoHistory);
            Assert.False(fixture.State.CanGoBack); Assert.False(fixture.State.CanGoPrev); Assert.False(fixture.State.CanGoNext);
        });
        await Task.WhenAll(select, close); Assert.Equal(before, fixture.Snapshot());

        await fixture.Db.AddPhotoTagAsync(fixture.Second.PhotoPath, "最新");
        var updated = fixture.Snapshot();
        await fixture.OnUi(() =>
        {
            var reopened = PhotoThumbnailItem.FromDto(fixture.Second.ToDto()); fixture.Observe(reopened);
            fixture.State.setPhotoList([fixture.First, reopened, fixture.Third]);
            fixture.State.onSelectPhoto(reopened); fixture.AssertSelected(reopened, "別写真", "最新");
            Assert.Equal(fixture.Second.PhotoPath, reopened.PhotoPath); Assert.NotSame(fixture.Second, reopened);
            Assert.Equal(["別写真"], fixture.Second.Tags); Assert.Empty(fixture.State.photoHistory);
            Assert.False(fixture.State.CanGoBack); Assert.True(fixture.State.CanGoPrev); Assert.True(fixture.State.CanGoNext);
            fixture.State.setSelectedPhoto(null); fixture.State.goPrevPhoto(); fixture.State.goNextPhoto(); fixture.State.goBackPhoto();
            Assert.Null(fixture.State.SelectedPhoto); Assert.Equal(["別写真", "最新"], reopened.Tags);
            fixture.State.closePhotoModal(); Assert.Empty(fixture.State.photoHistory);
            Assert.Empty(fixture.Toasts.toasts); fixture.AssertTagNotificationsUseQueue();
        });
        Assert.Equal(updated, fixture.Snapshot()); fixture.AssertSourceBytes();
    });

    // Bootstrapとglobal UiThread.Queueを専用processへ分離する。WindowやXAML bindingの検証は含めない。
    private static async Task RunIsolated(Func<ModalFixture, Task> verify, [CallerMemberName] string testName = "")
    {
        if (Environment.GetEnvironmentVariable(ChildMarker) != "1")
        {
            await NativeDispatcherQueueTests.RunChildProcess(typeof(NativePhotoModalStateTests), testName, ChildMarker);
            return;
        }
        WinRT.ComWrappersSupport.InitializeComWrappers(); Bootstrap.Initialize(0x00010006);
        var previousQueue = UiThread.Queue;
        try
        {
            var controller = DispatcherQueueController.CreateOnDedicatedThread(); var queue = controller.DispatcherQueue;
            try
            {
                UiThread.Queue = queue;
                await using var fixture = new ModalFixture(queue); await fixture.Initialize(); await verify(fixture);
            }
            finally { UiThread.Queue = previousQueue; await controller.ShutdownQueueAsync(); }
        }
        finally { Bootstrap.Shutdown(); }
        var receipt = Environment.GetEnvironmentVariable(NativeDispatcherQueueTests.ReceiptVariable)
            ?? throw new InvalidOperationException("専用testの完了記録先がありません。");
        await File.WriteAllTextAsync(receipt, "completed");
    }

    private sealed class ModalFixture : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "Alpheratz.NativePhotoModal.Tests", Guid.NewGuid().ToString("N"));
        private readonly DispatcherQueue queue;
        private readonly DispatcherService dispatcher;
        private readonly List<bool> tagNotificationThreads = [];
        private readonly Dictionary<string, byte[]> sources = [];
        private readonly string dbPath;
        public AlpheratzDb Db { get; }
        public PhotoModalState State { get; }
        public ToastService Toasts { get; }
        public PhotoThumbnailItem First { get; private set; } = null!;
        public PhotoThumbnailItem Second { get; private set; } = null!;
        public PhotoThumbnailItem Third { get; private set; } = null!;

        public ModalFixture(DispatcherQueue queue)
        {
            this.queue = queue; dispatcher = new DispatcherService(queue);
            Directory.CreateDirectory(root); dbPath = Path.Combine(root, "owned.db"); Db = new AlpheratzDb(dbPath);
            Toasts = new ToastService(dispatcher); State = new PhotoModalState(new PhotoService(Db), Toasts, dispatcher);
        }

        public async Task Initialize()
        {
            Db.Initialize();
            var entries = new[] { ("first.jpg", 1L, "初回"), ("second.jpg", 2L, "別写真"), ("third.jpg", 1L, "保持") };
            var photos = new List<PhotoThumbnailItem>();
            foreach (var (name, slot, tag) in entries)
            {
                var nativePath = Path.Combine(root, name); var bytes = System.Text.Encoding.UTF8.GetBytes("保持元file:" + name);
                // このfixtureはタグ読込だけを対象にし、所有fileを画像decodeへ渡さない。
                await File.WriteAllBytesAsync(nativePath, bytes); sources.Add(nativePath, bytes);
                var path = AppPaths.NormalizePathForDb(nativePath);
                await Db.UpsertPhotoAsync(new PhotoUpsertData { PhotoPath = path, PhotoFilename = name, SourceSlot = slot,
                    Timestamp = "2026-10-09 00:00:00", WorldId = "wrld-owned", WorldName = "保持ワールド", MatchSource = "manual",
                    Orientation = "landscape", ImageWidth = 200, ImageHeight = 100 });
                await Db.AddPhotoTagAsync(path, tag);
                await OnUi(() =>
                {
                    var photo = new PhotoThumbnailItem { PhotoPath = path, PhotoFilename = name, SourceSlot = slot,
                        Timestamp = "2026-10-09 00:00:00", WorldId = "wrld-owned", WorldName = "保持ワールド" };
                    Observe(photo); photos.Add(photo);
                });
            }
            First = photos[0]; Second = photos[1]; Third = photos[2];
            await OnUi(() => State.setPhotoList(photos));
        }

        public Task OnUi(Action action) => dispatcher.RunOnUiThread(() => { Assert.True(queue.HasThreadAccess); action(); }).WaitAsync(TimeSpan.FromSeconds(5));
        public void Observe(PhotoThumbnailItem photo) => photo.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(PhotoThumbnailItem.Tags)) tagNotificationThreads.Add(queue.HasThreadAccess);
        };
        public void AssertTagNotificationsUseQueue() { Assert.NotEmpty(tagNotificationThreads); Assert.All(tagNotificationThreads, flag => Assert.True(flag)); }
        public void AssertSelected(PhotoThumbnailItem photo, params string[] tags)
        {
            Assert.Same(photo, State.SelectedPhoto); Assert.Equal(tags, photo.Tags);
            Assert.Equal("wrld-owned", photo.WorldId); Assert.Equal("保持ワールド", photo.WorldName);
            Assert.Equal(photo.PhotoFilename == "second.jpg" ? 2L : 1L, photo.SourceSlot);
        }
        public void Execute(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
        }
        public string Snapshot()
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()); connection.Open();
            var tables = new[] { "photos", "tags", "photo_tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence" };
            var snapshot = new List<List<object?[]>>();
            foreach (var table in tables)
            {
                using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY rowid";
                using var reader = command.ExecuteReader(); var rows = new List<object?[]>();
                while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
                snapshot.Add(rows);
            }
            return JsonSerializer.Serialize(snapshot);
        }
        public void AssertSourceBytes() { foreach (var (path, bytes) in sources) Assert.Equal(bytes, File.ReadAllBytes(path)); }
        public async ValueTask DisposeAsync()
        {
            await OnUi(State.closePhotoModal);
            using var pool = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()); SqliteConnection.ClearPool(pool);
            Assert.Equal(Path.Combine(Path.GetTempPath(), "Alpheratz.NativePhotoModal.Tests"), Path.GetDirectoryName(root));
            Directory.Delete(root, recursive: true); Assert.False(Directory.Exists(root));
        }
    }
}
