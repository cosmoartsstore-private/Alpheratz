using Alpheratz.Core.Database;
using Alpheratz.Models;
using Alpheratz.Services;

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
            "unknown",
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

        await service.BulkSetPhotoFavoriteAsync(refs, isFavorite: true);
        await service.BulkAddPhotoTagAsync(refs, "bulk");
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
        Assert.Empty(tagsA);
        Assert.Equal(["bulk"], tagsB);
        Assert.Equal(2, page.total);
        Assert.All(page.items, photo => Assert.True(photo.is_favorite));
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
