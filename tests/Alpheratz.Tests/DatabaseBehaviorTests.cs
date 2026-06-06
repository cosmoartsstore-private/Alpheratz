using Alpheratz.Core.Database;
using Alpheratz.Models;
using Alpheratz.Shared.Models;

namespace Alpheratz.Tests;

/// <summary>
/// SQLite 永続化層の振る舞いを、実ファイルの一時DBで確認するテスト。
///
/// このテスト群は、UIやアプリ起動シーケンスを通さずに AlpheratzDb を直接呼び出す。
/// その理由は、日付フィルタ、タグ削除、ワールド情報反映のような不具合が
/// DBのWHERE句やUPDATE句に閉じており、画面操作よりもDB境界で検証した方が
/// 失敗原因を狭く特定できるためである。
/// </summary>
public sealed class DatabaseBehaviorTests : IDisposable
{
    private readonly string tempDir;
    private readonly AlpheratzDb db;

    /// <summary>
    /// 各テスト専用の一時ディレクトリとSQLiteファイルを作成する。
    ///
    /// 本番の AppPaths から取得されるDBを使うと、開発者環境の実データを書き換える危険がある。
    /// そのため、テストでは AlpheratzDb の明示パス指定を使い、完全に独立したDBだけを操作する。
    /// </summary>
    public DatabaseBehaviorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        db = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
        db.Initialize();
    }

    /// <summary>
    /// テスト完了後に一時DBとサイドカーのWAL/SHMファイルを削除する。
    ///
    /// SQLiteはWALモードで複数ファイルを作るため、DBファイル単体ではなく
    /// ディレクトリごと削除する。失敗時はテスト結果を優先し、クリーンアップ例外は握り潰す。
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
    /// EndDate に yyyy-MM-dd が渡された場合、同日 23:59:59 までを含める仕様を確認する。
    ///
    /// ギャラリーのカレンダーやプリセットは日付だけをDBへ渡す。
    /// DB内の timestamp は "yyyy-MM-dd HH:mm:ss" なので、単純な文字列比較で
    /// "timestamp <= yyyy-MM-dd" とすると、その日の全写真が除外される。
    /// このテストは、終了日当日の始端と終端が取得され、翌日の写真は除外されることを固定する。
    /// </summary>
    [Fact]
    public async Task GetPhotosPageAsync_TreatsDateOnlyEndDateAsInclusiveWholeDay()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 00:00:00"));
        await db.UpsertPhotoAsync(Photo("/photo/b.jpg", "b.jpg", "2026-06-05 23:59:59"));
        await db.UpsertPhotoAsync(Photo("/photo/c.jpg", "c.jpg", "2026-06-06 00:00:00"));

        var page = await db.GetPhotosPageAsync(new PhotoQueryParams
        {
            StartDate = "2026-06-05",
            EndDate = "2026-06-05",
        });

        Assert.Equal(2, page.Total);
        Assert.All(page.Items, item => Assert.StartsWith("2026-06-05", item.timestamp, StringComparison.Ordinal));
    }

    /// <summary>
    /// 類似写真からワールド情報を反映した場合、コピー元の由来ではなく phash として保存することを確認する。
    ///
    /// コピー元写真は archive 補完やメタデータ由来など、別の match_source を持つことがある。
    /// しかし対象写真に対する操作の由来は「類似写真からの反映」なので、DB上の match_source は
    /// phash に統一される。この仕様が崩れると、詳細モーダルの表示と再読込後のDB値が食い違う。
    /// </summary>
    [Fact]
    public async Task ApplyWorldMatchFromPhotoAsync_StoresTargetMatchSourceAsPhash()
    {
        await db.UpsertPhotoAsync(Photo(
            "/photo/source.jpg",
            "source.jpg",
            "2026-06-05 10:00:00",
            worldId: "wrld_source",
            worldName: "Source World",
            matchSource: "polaris_archive"));
        await db.UpsertPhotoAsync(Photo("/photo/target.jpg", "target.jpg", "2026-06-05 10:01:00"));

        await db.ApplyWorldMatchFromPhotoAsync("/photo/target.jpg", "/photo/source.jpg");

        var page = await db.GetPhotosPageAsync(new PhotoQueryParams { PhotoPathExact = "/photo/target.jpg" });
        var target = Assert.Single(page.Items);
        Assert.Equal("wrld_source", target.world_id);
        Assert.Equal("Source World", target.world_name);
        Assert.Equal("phash", target.match_source);
    }

    /// <summary>
    /// タグマスタ削除が中間テーブルと件数取得に反映されることを確認する。
    ///
    /// タグマスタの削除は tags だけでなく photo_tags の紐付けも消す。
    /// ギャラリーのタグ件数は photo_tags と photos のJOIN結果を参照するため、
    /// 削除後の件数辞書に対象タグが残らないこと、写真一覧のタグ配列にも残らないことを検証する。
    /// </summary>
    [Fact]
    public async Task DeleteTagMasterAsync_RemovesPhotoTagLinksAndFilterCounts()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00"));
        await db.UpsertPhotoAsync(Photo("/photo/b.jpg", "b.jpg", "2026-06-05 11:00:00"));
        await db.AddPhotoTagAsync("/photo/a.jpg", "delete-me");
        await db.AddPhotoTagAsync("/photo/b.jpg", "delete-me");
        await db.AddPhotoTagAsync("/photo/b.jpg", "keep-me");

        await db.DeleteTagMasterAsync("delete-me");

        var counts = await db.GetTagFilterCountsAsync();
        Assert.False(counts.ContainsKey("delete-me"));
        Assert.Equal(1, counts["keep-me"]);

        var page = await db.GetPhotosPageAsync(new PhotoQueryParams());
        Assert.DoesNotContain(page.Items, item => item.tags.Contains("delete-me"));
        Assert.Contains(page.Items, item => item.tags.Contains("keep-me"));
    }

    /// <summary>
    /// 写真一覧クエリがワールド不明、複数ワールド、orientation、favorite、source_slot を正しく絞ることを確認する。
    ///
    /// worldExacts では UI 側の「ワールド不明」を unknown という内部値で渡す。
    /// unknown 単独指定では world_name IS NULL だけを取り、通常ワールドとの混在指定では OR 条件にする。
    /// 併せて orientation/favorite/source_slot の AND 条件を重ねても意図した写真だけが残ることを固定する。
    /// </summary>
    [Fact]
    public async Task GetPhotosPageAsync_FiltersUnknownWorldAndCombinesSimpleFilters()
    {
        await db.UpsertPhotoAsync(Photo("/photo/unknown.jpg", "unknown.jpg", "2026-06-05 10:00:00", orientation: "portrait", sourceSlot: 1));
        await db.UpsertPhotoAsync(Photo("/photo/blank-unknown.jpg", "blank-unknown.jpg", "2026-06-05 10:30:00", worldName: "   ", orientation: "portrait", sourceSlot: 1));
        await db.UpsertPhotoAsync(Photo("/photo/alpha.jpg", "alpha.jpg", "2026-06-05 11:00:00", worldName: "Alpha", orientation: "portrait", sourceSlot: 1));
        await db.UpsertPhotoAsync(Photo("/photo/beta.jpg", "beta.jpg", "2026-06-05 12:00:00", worldName: "Beta", orientation: "landscape", sourceSlot: 2));
        await db.UpsertPhotoAsync(Photo("/photo/literal-unknown.jpg", "literal-unknown.jpg", "2026-06-05 13:00:00", worldName: "unknown", sourceSlot: 1));
        await db.SetPhotoFavoriteAsync("/photo/alpha.jpg", true);
        await db.SetPhotoFavoriteAsync("/photo/beta.jpg", true);

        var unknownOnly = await db.GetPhotosPageAsync(new PhotoQueryParams { WorldExacts = [WorldFilterValues.Unknown] });
        var mixedWorlds = await db.GetPhotosPageAsync(new PhotoQueryParams { WorldExacts = ["Alpha", WorldFilterValues.Unknown] });
        var literalUnknown = await db.GetPhotosPageAsync(new PhotoQueryParams { WorldExacts = ["unknown"] });
        var combined = await db.GetPhotosPageAsync(new PhotoQueryParams
        {
            Orientation = "portrait",
            FavoritesOnly = true,
            SourceSlot = 1,
        });

        Assert.Equal(["/photo/blank-unknown.jpg", "/photo/unknown.jpg"], unknownOnly.Items.Select(item => item.photo_path).Order().ToArray());
        Assert.Equal(["/photo/alpha.jpg", "/photo/blank-unknown.jpg", "/photo/unknown.jpg"], mixedWorlds.Items.Select(item => item.photo_path).Order().ToArray());
        Assert.Equal(["/photo/literal-unknown.jpg"], literalUnknown.Items.Select(item => item.photo_path).ToArray());
        Assert.Equal(["/photo/alpha.jpg"], combined.Items.Select(item => item.photo_path).ToArray());
    }

    /// <summary>
    /// worldAsc ソートがワールド名昇順、同一ワールド内では timestamp 降順になることを確認する。
    ///
    /// ワールド別表示では一覧を world_name COLLATE NOCASE ASC でまとめ、同じワールド内は新しい写真を先に出す。
    /// ここでは null ワールドを除いた exact 指定にし、SQLite の null 並び順に依存しない形で現在仕様を検証する。
    /// </summary>
    [Fact]
    public async Task GetPhotosPageAsync_WorldAscSortOrdersByWorldThenNewestTimestamp()
    {
        await db.UpsertPhotoAsync(Photo("/photo/beta-old.jpg", "beta-old.jpg", "2026-06-05 09:00:00", worldName: "Beta"));
        await db.UpsertPhotoAsync(Photo("/photo/alpha.jpg", "alpha.jpg", "2026-06-05 10:00:00", worldName: "Alpha"));
        await db.UpsertPhotoAsync(Photo("/photo/beta-new.jpg", "beta-new.jpg", "2026-06-05 11:00:00", worldName: "Beta"));

        var page = await db.GetPhotosPageAsync(new PhotoQueryParams
        {
            WorldExacts = ["Alpha", "Beta"],
            Sort = SortMode.worldAsc,
        });

        Assert.Equal(
            ["/photo/alpha.jpg", "/photo/beta-new.jpg", "/photo/beta-old.jpg"],
            page.Items.Select(item => item.photo_path).ToArray());
    }

    /// <summary>
    /// UpdatePhotoWorldNameAsync が world_name と match_source だけを上書きすることを確認する。
    ///
    /// PDQ や外部推定で world_id を持たずにワールド名だけ確定する経路がある。
    /// この API はその用途向けなので、world_name と match_source が保存され、既存の写真レコードが残ることを検証する。
    /// </summary>
    [Fact]
    public async Task UpdatePhotoWorldNameAsync_StoresResolvedNameAndMatchSource()
    {
        await db.UpsertPhotoAsync(Photo("/photo/target.jpg", "target.jpg", "2026-06-05 10:00:00"));

        await db.UpdatePhotoWorldNameAsync("/photo/target.jpg", "Resolved World", "archive");
        var record = await db.GetPhotoRecordAsync("/photo/target.jpg");

        Assert.NotNull(record);
        Assert.Equal("Resolved World", record.world_name);
        Assert.Equal("archive", record.match_source);
    }

    /// <summary>
    /// GetWorldFilterOptionsAsync が未解決ワールドを含め、写真枚数降順で候補を返すことを確認する。
    ///
    /// フィルタパネルでは world_name=null を「ワールド不明」として表示するため、DB 側で除外してはいけない。
    /// 集計順は枚数が多いワールドを先にし、同数の場合は名前順にする現在仕様を固定する。
    /// </summary>
    [Fact]
    public async Task GetWorldFilterOptionsAsync_IncludesUnknownWorldAndOrdersByCount()
    {
        await db.UpsertPhotoAsync(Photo("/photo/alpha-1.jpg", "alpha-1.jpg", "2026-06-05 10:00:00", worldName: "Alpha"));
        await db.UpsertPhotoAsync(Photo("/photo/alpha-2.jpg", "alpha-2.jpg", "2026-06-05 11:00:00", worldName: "Alpha"));
        await db.UpsertPhotoAsync(Photo("/photo/alpha-3.jpg", "alpha-3.jpg", "2026-06-05 11:30:00", worldName: "Alpha"));
        await db.UpsertPhotoAsync(Photo("/photo/alpha-trimmed.jpg", "alpha-trimmed.jpg", "2026-06-05 11:45:00", worldName: " Alpha "));
        await db.UpsertPhotoAsync(Photo("/photo/beta.jpg", "beta.jpg", "2026-06-05 12:00:00", worldName: "Beta"));
        await db.UpsertPhotoAsync(Photo("/photo/unknown.jpg", "unknown.jpg", "2026-06-05 13:00:00"));
        await db.UpsertPhotoAsync(Photo("/photo/empty.jpg", "empty.jpg", "2026-06-05 13:30:00", worldName: ""));
        await db.UpsertPhotoAsync(Photo("/photo/blank.jpg", "blank.jpg", "2026-06-05 14:00:00", worldName: "   "));

        var options = await db.GetWorldFilterOptionsAsync();

        Assert.Equal("Alpha", options[0].world_name);
        Assert.Equal(4, options[0].count);
        Assert.Contains(options, option => option.world_name is null && option.count == 3);
        Assert.Contains(options, option => option.world_name == "Beta" && option.count == 1);
        Assert.DoesNotContain(options, option => option.world_name is "" or "   " or " Alpha ");
    }

    /// <summary>
    /// PhotoUpsertData を短く生成するためのテスト専用ファクトリ。
    ///
    /// テストで重要なのは timestamp、world、match_source の差分である。
    /// それ以外の列はDBスキーマのNOT NULL制約や既定値を満たす最小値に固定し、
    /// 各テストの意図がデータ準備に埋もれないようにする。
    /// </summary>
    private static PhotoUpsertData Photo(
        string path,
        string filename,
        string timestamp,
        string? worldId = null,
        string? worldName = null,
        string? matchSource = null,
        string? orientation = null,
        long sourceSlot = 1)
        => new()
        {
            PhotoPath = path,
            PhotoFilename = filename,
            Timestamp = timestamp,
            WorldId = worldId,
            WorldName = worldName,
            MatchSource = matchSource,
            Orientation = orientation,
            SourceSlot = sourceSlot,
        };
}
