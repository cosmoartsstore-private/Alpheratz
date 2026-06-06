using Alpheratz.Core.Database;

namespace Alpheratz.Tests;

/// <summary>
/// AlpheratzDb の保守系・補完系 API を検証するテスト。
///
/// DatabaseBehaviorTests が検索条件やタグ削除などの代表的なユーザー操作を扱うのに対し、
/// このファイルではスキャン後の欠損削除、再スキャン時のメタデータ保持、
/// phash/orientation 補完キュー、アーカイブ照合といったバックグラウンド処理向けの契約を固定する。
/// これらは画面から直接見えにくいが、写真一覧の正しさを支える重要な境界である。
/// </summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class DatabaseMaintenanceTests : IDisposable
{
    private readonly string tempDir;
    private readonly AlpheratzDb db;

    /// <summary>
    /// テストごとに独立した SQLite DB を作成する。
    ///
    /// AlpheratzDb は本番では AppPaths 配下の固定 DB を使うが、テストではコンストラクタで
    /// 一時ファイルを渡し、ユーザーの実データや他テストから完全に分離する。
    /// </summary>
    public DatabaseMaintenanceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.DatabaseMaintenance.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        db = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
        db.Initialize();
    }

    /// <summary>
    /// 一時DBと SQLite の sidecar ファイルを削除する。
    /// ファイル削除の失敗はテスト対象外なので、検証結果を隠さないよう握りつぶす。
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
    /// UpsertPhotoAsync が null 入力で既存の解決済みメタデータを消さないことを確認する。
    ///
    /// 再スキャン時にはファイル名や更新時刻だけが分かり、world_name や orientation は
    /// 別ワーカで補完済みというケースがある。
    /// その状態で null を上書きしてしまうと、解決済みワールドや画像向きが再スキャンで失われるため、
    /// COALESCE による保持仕様を検証する。
    /// </summary>
    [Fact]
    public async Task UpsertPhotoAsync_PreservesResolvedMetadataWhenNextValuesAreNull()
    {
        await db.UpsertPhotoAsync(Photo(
            "/photo/a.jpg",
            "a.jpg",
            "2026-06-05 10:00:00",
            worldName: "Known",
            worldId: "wrld_known",
            orientation: "landscape",
            width: 1920,
            height: 1080,
            matchSource: "metadata"));

        await db.UpsertPhotoAsync(Photo(
            "/photo/a.jpg",
            "renamed.jpg",
            "2026-06-06 10:00:00",
            worldName: null,
            worldId: null,
            orientation: null,
            width: null,
            height: null,
            matchSource: null));

        var record = await db.GetPhotoRecordAsync("/photo/a.jpg");

        Assert.NotNull(record);
        Assert.Equal("renamed.jpg", record.photo_filename);
        Assert.Equal("Known", record.world_name);
        Assert.Equal("wrld_known", record.world_id);
        Assert.Equal("landscape", record.orientation);
        Assert.Equal(1920, record.image_width);
        Assert.Equal(1080, record.image_height);
        Assert.Equal("metadata", record.match_source);
    }

    /// <summary>
    /// DeleteMissingPhotosAsync が対象 source_slot の欠落写真とタグ紐付けだけを削除することを確認する。
    ///
    /// プライマリ/セカンダリの片方だけを再スキャンした場合、もう片方のフォルダを foundPaths に含めない。
    /// このとき sourceSlots フィルタを誤ると、未スキャン側の写真まで消えてしまう。
    /// テストでは slot=1 だけを対象にし、slot=2 の写真とタグが残ることを検証する。
    /// </summary>
    [Fact]
    public async Task DeleteMissingPhotosAsync_DeletesOnlyMissingPhotosInsideRequestedSlots()
    {
        await db.UpsertPhotoAsync(Photo("/slot1/keep.jpg", "keep.jpg", "2026-06-05 10:00:00", sourceSlot: 1));
        await db.UpsertPhotoAsync(Photo("/slot1/delete.jpg", "delete.jpg", "2026-06-05 11:00:00", sourceSlot: 1));
        await db.UpsertPhotoAsync(Photo("/slot2/keep.jpg", "keep.jpg", "2026-06-05 12:00:00", sourceSlot: 2));
        await db.AddPhotoTagAsync("/slot1/delete.jpg", "old");
        await db.AddPhotoTagAsync("/slot2/keep.jpg", "secondary");

        var deleted = await db.DeleteMissingPhotosAsync(["/slot1/keep.jpg"], sourceSlots: [1]);
        var slot1Deleted = await db.GetPhotoRecordAsync("/slot1/delete.jpg");
        var slot2 = await db.GetPhotoRecordAsync("/slot2/keep.jpg");
        var slot2Tags = await db.GetPhotoTagsAsync("/slot2/keep.jpg");
        var tagCounts = await db.GetTagFilterCountsAsync();

        Assert.Equal(1, deleted);
        Assert.Null(slot1Deleted);
        Assert.NotNull(slot2);
        Assert.Equal(["secondary"], slot2Tags);
        Assert.DoesNotContain("old", tagCounts.Keys);
    }

    /// <summary>
    /// ResetPhotoCacheBySlotAsync が指定スロットの写真とタグだけを消すことを確認する。
    ///
    /// 設定画面から片方のフォルダをリセットする操作では、対象 slot のキャッシュだけを破棄する。
    /// DB 上では photos と photo_tags の両方を消す必要があり、別 slot の写真は残さなければならない。
    /// </summary>
    [Fact]
    public async Task ResetPhotoCacheBySlotAsync_RemovesPhotosAndTagsForOnlyOneSlot()
    {
        await db.UpsertPhotoAsync(Photo("/slot1/a.jpg", "a.jpg", "2026-06-05 10:00:00", sourceSlot: 1));
        await db.UpsertPhotoAsync(Photo("/slot2/b.jpg", "b.jpg", "2026-06-05 11:00:00", sourceSlot: 2));
        await db.AddPhotoTagAsync("/slot1/a.jpg", "primary");
        await db.AddPhotoTagAsync("/slot2/b.jpg", "secondary");

        await db.ResetPhotoCacheBySlotAsync(1);
        var slot1 = await db.GetPhotoRecordAsync("/slot1/a.jpg");
        var slot2 = await db.GetPhotoRecordAsync("/slot2/b.jpg");
        var tagCounts = await db.GetTagFilterCountsAsync();

        Assert.Null(slot1);
        Assert.NotNull(slot2);
        Assert.Equal(1, tagCounts["secondary"]);
        Assert.DoesNotContain("primary", tagCounts.Keys);
    }

    /// <summary>
    /// GetExistingPhotosAsync がスキャナ差分判定に必要なメタデータを辞書で返すことを確認する。
    ///
    /// スキャナは既存DBの world / orientation / source_slot / last_modified_utc を見て、
    /// 再スキャン時に再利用できる値を判断する。
    /// ここでは Upsert で保存した代表メタデータが ExistingPhotoInfo に欠落なく写ることを検証する。
    /// </summary>
    [Fact]
    public async Task GetExistingPhotosAsync_ReturnsMetadataForScannerReuse()
    {
        await db.UpsertPhotoAsync(Photo(
            "/photo/a.jpg",
            "a.jpg",
            "2026-06-05 10:00:00",
            worldName: "World",
            worldId: "wrld_world",
            orientation: "portrait",
            width: 1080,
            height: 1920,
            sourceSlot: 2,
            lastModifiedUtc: "2026-06-05 01:00:00",
            matchSource: "title"));

        var existing = await db.GetExistingPhotosAsync();
        var item = existing["/photo/a.jpg"];

        Assert.Equal("a.jpg", item.PhotoFilename);
        Assert.Equal("World", item.WorldName);
        Assert.Equal("wrld_world", item.WorldId);
        Assert.Equal("portrait", item.Orientation);
        Assert.Equal(1080, item.ImageWidth);
        Assert.Equal(1920, item.ImageHeight);
        Assert.Equal(2, item.SourceSlot);
        Assert.Equal("2026-06-05 01:00:00", item.LastModifiedUtc);
        Assert.False(item.IsMissing);
        Assert.Equal("title", item.MatchSource);
    }

    /// <summary>
    /// アーカイブ訪問履歴の全置換と時刻範囲検索を確認する。
    ///
    /// VRChat ログ由来の archive_world_visits は毎回全置換される仕様である。
    /// 重なった区間では join_time が新しいものを優先するため、古い履歴を入れた後に新しい履歴を入れ直し、
    /// 範囲内・範囲外・leave_time=null の検索結果を検証する。
    /// </summary>
    [Fact]
    public async Task ArchiveWorldVisits_AreReplacedAndLookupUsesContainingLatestInterval()
    {
        await db.UpsertArchiveWorldVisitsAsync(
        [
            new ArchiveWorldVisitData { SourceLogName = "old.log", WorldName = "Old", JoinTime = "2026-06-05 08:00:00", LeaveTime = "2026-06-05 09:00:00" },
        ]);
        await db.UpsertArchiveWorldVisitsAsync(
        [
            new ArchiveWorldVisitData { SourceLogName = "new.log", WorldName = "Alpha", JoinTime = "2026-06-05 10:00:00", LeaveTime = "2026-06-05 12:00:00" },
            new ArchiveWorldVisitData { SourceLogName = "new.log", WorldName = "Beta", JoinTime = "2026-06-05 11:00:00", LeaveTime = null },
        ]);

        var before = await db.LookupWorldNameFromArchiveAsync("2026-06-05 08:30:00");
        var overlap = await db.LookupWorldNameFromArchiveAsync("2026-06-05 11:30:00");
        var openEnded = await db.LookupWorldNameFromArchiveAsync("2026-06-06 00:00:00");

        Assert.Null(before);
        Assert.Equal("Beta", overlap);
        Assert.Equal("Beta", openEnded);
    }

    /// <summary>
    /// 未解決ワールド一覧が target ごとに source_slot を正しく絞ることを確認する。
    ///
    /// 自動ワールド解決は「全体」「プライマリのみ」「セカンダリのみ」を選んで実行できる。
    /// target が primary/secondary のとき WHERE で slot を絞れないと、別フォルダの写真まで処理されるため、
    /// 各 target の戻り値を比較して検証する。
    /// </summary>
    [Fact]
    public async Task GetUnknownWorldPhotosAsync_FiltersByTargetSlot()
    {
        await db.UpsertPhotoAsync(Photo("/slot1/unknown.jpg", "unknown.jpg", "2026-06-05 10:00:00", sourceSlot: 1));
        await db.UpsertPhotoAsync(Photo("/slot1/blank.jpg", "blank.jpg", "2026-06-05 10:30:00", worldName: "   ", sourceSlot: 1));
        await db.UpsertPhotoAsync(Photo("/slot2/unknown.jpg", "unknown.jpg", "2026-06-05 11:00:00", sourceSlot: 2));
        await db.UpsertPhotoAsync(Photo("/slot1/known.jpg", "known.jpg", "2026-06-05 12:00:00", worldName: "Known", sourceSlot: 1));

        var all = await db.GetUnknownWorldPhotosAsync("all");
        var primary = await db.GetUnknownWorldPhotosAsync("primary");
        var secondary = await db.GetUnknownWorldPhotosAsync("secondary");

        Assert.Equal(["/slot1/unknown.jpg", "/slot1/blank.jpg", "/slot2/unknown.jpg"], all.Select(item => item.photoPath).ToArray());
        Assert.Equal(["/slot1/unknown.jpg", "/slot1/blank.jpg"], primary.Select(item => item.photoPath).ToArray());
        Assert.Equal(["/slot2/unknown.jpg"], secondary.Select(item => item.photoPath).ToArray());
    }

    [Fact]
    public async Task GetUnknownWorldPhotosWithPhashAsync_FiltersByTargetSlotAndBlankWorldNames()
    {
        var hash = new string('a', 64);
        await db.UpsertPhotoAsync(Photo("/slot1/unknown.jpg", "unknown.jpg", "2026-06-05 10:00:00", sourceSlot: 1));
        await db.UpsertPhotoAsync(Photo("/slot2/blank.jpg", "blank.jpg", "2026-06-05 11:00:00", worldName: "   ", sourceSlot: 2));
        await db.UpsertPhotoAsync(Photo("/slot2/literal-unknown.jpg", "literal-unknown.jpg", "2026-06-05 12:00:00", worldName: "unknown", sourceSlot: 2));
        await db.UpsertPhotoAsync(Photo("/slot1/no-hash.jpg", "no-hash.jpg", "2026-06-05 13:00:00", sourceSlot: 1));
        await db.UpdatePhotoPhashAsync("/slot1/unknown.jpg", hash);
        await db.UpdatePhotoPhashAsync("/slot2/blank.jpg", hash);
        await db.UpdatePhotoPhashAsync("/slot2/literal-unknown.jpg", hash);

        var all = await db.GetUnknownWorldPhotosWithPhashAsync();
        var primary = await db.GetUnknownWorldPhotosWithPhashAsync("primary", CancellationToken.None);
        var secondary = await db.GetUnknownWorldPhotosWithPhashAsync("secondary", CancellationToken.None);

        Assert.Equal(["/slot1/unknown.jpg", "/slot2/blank.jpg"], all.Select(item => item.PhotoPath).Order().ToArray());
        Assert.Equal(["/slot1/unknown.jpg"], primary.Select(item => item.PhotoPath).ToArray());
        Assert.Equal(["/slot2/blank.jpg"], secondary.Select(item => item.PhotoPath).ToArray());
    }

    /// <summary>
    /// phash 補完キューの count / batch / update のサイクルを確認する。
    ///
    /// PhashService は未計算写真の件数を表示し、新しい順にバッチ取得して計算結果を書き戻す。
    /// 更新後に同じ写真が再度 pending に出ないこと、既に phash を持つ写真が最初から除外されることを検証する。
    /// </summary>
    [Fact]
    public async Task PendingPhashQueries_ReturnNewestMissingHashesAndDisappearAfterUpdate()
    {
        await db.UpsertPhotoAsync(Photo("/photo/old.jpg", "old.jpg", "2026-06-05 10:00:00"));
        await db.UpsertPhotoAsync(Photo("/photo/new.jpg", "new.jpg", "2026-06-05 12:00:00"));
        await db.UpsertPhotoAsync(Photo("/photo/done.jpg", "done.jpg", "2026-06-05 11:00:00"));
        await db.UpdatePhotoPhashAsync("/photo/done.jpg", new string('a', 64));

        var beforeCount = await db.GetPendingPhashCountAsync();
        var batch = await db.GetPendingPhashBatchAsync(limit: 10);
        await db.UpdatePhotoPhashAsync("/photo/new.jpg", new string('b', 64));
        var afterCount = await db.GetPendingPhashCountAsync();

        Assert.Equal(2, beforeCount);
        Assert.Equal(["/photo/new.jpg", "/photo/old.jpg"], batch.Select(item => item.PhotoPath).ToArray());
        Assert.Equal(1, afterCount);
    }

    /// <summary>
    /// orientation/dimension 補完キューの count / batch / update を確認する。
    ///
    /// orientation が unknown、または寸法が null の写真は補完対象になる。
    /// unreadable は再試行しても読めないことを示す終端状態なので除外される。
    /// 更新後に count が減ることと、新しい順に batch が返ることを検証する。
    /// </summary>
    [Fact]
    public async Task PendingOrientationQueries_ExcludeUnreadableAndDisappearAfterUpdate()
    {
        await db.UpsertPhotoAsync(Photo("/photo/old.jpg", "old.jpg", "2026-06-05 10:00:00", orientation: "unknown"));
        await db.UpsertPhotoAsync(Photo("/photo/new.jpg", "new.jpg", "2026-06-05 12:00:00", orientation: null));
        await db.UpsertPhotoAsync(Photo("/photo/unreadable.jpg", "unreadable.jpg", "2026-06-05 11:00:00", orientation: "unreadable"));

        var beforeCount = await db.GetPendingOrientationCountAsync();
        var batch = await db.GetPendingOrientationBatchAsync(limit: 10);
        await db.UpdatePhotoOrientationAndDimensionsAsync("/photo/new.jpg", "landscape", 1920, 1080);
        var afterCount = await db.GetPendingOrientationCountAsync();

        Assert.Equal(2, beforeCount);
        Assert.Equal(["/photo/new.jpg", "/photo/old.jpg"], batch.Select(item => item.PhotoPath).ToArray());
        Assert.Equal(1, afterCount);
    }

    /// <summary>
    /// 既知/未知 phash の取得と world 更新 API をまとめて確認する。
    ///
    /// WorldService の自動解決は、未知写真の phash と既知ワールド写真の phash を DB から取り出し、
    /// 最短候補が見つかったら UpdatePhotoWorldAsync で world_name/world_id/match_source を保存する。
    /// このテストはその DB 側の入出力形式を固定する。
    /// </summary>
    [Fact]
    public async Task KnownUnknownPhashQueries_AndWorldUpdateSupportSimilarPhotoResolution()
    {
        var knownHash = new string('1', 64);
        var unknownHash = new string('2', 64);
        await db.UpsertPhotoAsync(Photo("/photo/known.jpg", "known.jpg", "2026-06-05 10:00:00", worldName: "Known", worldId: "wrld_known", sourceSlot: 1));
        await db.UpsertPhotoAsync(Photo("/photo/unknown.jpg", "unknown.jpg", "2026-06-05 11:00:00", sourceSlot: 1));
        await db.UpdatePhotoPhashAsync("/photo/known.jpg", knownHash);
        await db.UpdatePhotoPhashAsync("/photo/unknown.jpg", unknownHash);

        var knownRows = await db.GetKnownWorldPhotosAsync(sourceSlot: 1, excludePhotoPath: null);
        var knownRowsWithoutSelf = await db.GetKnownWorldPhotosAsync(sourceSlot: 1, excludePhotoPath: "/photo/known.jpg");
        var unknownRows = await db.GetUnknownWorldPhotosWithPhashAsync();
        await db.UpdatePhotoWorldAsync("/photo/unknown.jpg", "Known", "wrld_known", "phash");
        var updated = await db.GetPhotoRecordAsync("/photo/unknown.jpg");

        var known = Assert.Single(knownRows);
        Assert.Equal("Known", known.WorldName);
        Assert.Equal(knownHash, known.Phash);
        Assert.Empty(knownRowsWithoutSelf);
        var unknown = Assert.Single(unknownRows);
        Assert.Equal("/photo/unknown.jpg", unknown.PhotoPath);
        Assert.Equal(unknownHash, unknown.Phash);
        Assert.Equal("Known", updated?.world_name);
        Assert.Equal("wrld_known", updated?.world_id);
        Assert.Equal("phash", updated?.match_source);
    }

    /// <summary>
    /// PhotoUpsertData を短く生成するためのテスト専用ファクトリ。
    /// 個々のテストで関心のある列だけを named argument で指定し、他は安全な既定値にそろえる。
    /// </summary>
    private static PhotoUpsertData Photo(
        string path,
        string filename,
        string timestamp,
        string? worldName = null,
        string? worldId = null,
        string? orientation = null,
        long? width = null,
        long? height = null,
        long sourceSlot = 1,
        string? lastModifiedUtc = null,
        string? matchSource = null)
        => new()
        {
            PhotoPath = path,
            PhotoFilename = filename,
            Timestamp = timestamp,
            WorldName = worldName,
            WorldId = worldId,
            Orientation = orientation,
            ImageWidth = width,
            ImageHeight = height,
            SourceSlot = sourceSlot,
            LastModifiedUtc = lastModifiedUtc,
            MatchSource = matchSource,
        };
}
