using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Scanner;
using Alpheratz.Models;
using Alpheratz.Models.Events;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Alpheratz.Tests;

/// <summary>
/// PhotoScanner のファイル解析境界を検証するテスト。
///
/// PhotoScanner は実フォルダを再帰走査し、画像ヘッダ、PNG 内 XMP、VRChat ログを読み、
/// DB 更新用の PhotoUpsertData へ変換する。
/// UI から見ると「写真フォルダをスキャンする」だけに見えるが、ここで誤判定すると
/// ワールド名、撮影日時、向き、再スキャン対象がすべて崩れる。
/// そのため、DB や WinUI を起動しない純粋な I/O 境界を小さな一時ファイルで固定する。
/// </summary>
public sealed class PhotoScannerBehaviorTests : IDisposable
{
    private readonly string tempDir;

    /// <summary>
    /// 各テスト専用の一時ディレクトリを用意する。
    /// PhotoScanner の対象は実ファイルなので、テストごとにディレクトリを分けて
    /// 更新時刻・ファイル名・ログ内容が他テストと混ざらないようにする。
    /// </summary>
    public PhotoScannerBehaviorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.PhotoScanner.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    /// <summary>
    /// 専用DBの接続プールを解放し、テストで作成したファイルを削除する。
    /// 清掃失敗はテスト結果へ反映する。
    /// </summary>
    public void Dispose()
    {
        Assert.Equal(Path.Combine(Path.GetTempPath(), "Alpheratz.PhotoScanner.Tests"), Path.GetDirectoryName(tempDir));
        foreach (var path in Directory.EnumerateFiles(tempDir, "*.db", SearchOption.TopDirectoryOnly))
        {
            using var pool = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); SqliteConnection.ClearPool(pool);
        }
        Directory.Delete(tempDir, recursive: true); Assert.False(Directory.Exists(tempDir));
    }

    /// <summary>
    /// XMP 文字列から VRChat のワールド名とワールド ID を取り出せることを確認する。
    ///
    /// PNG チャンクを読む前段とは独立して、正規表現の入力となる XMP 本文だけを検証する。
    /// vrc:WorldDisplayName と vrc:WorldID の順序が通常の XMP と異なっても拾える必要があるため、
    /// 両タグを含む最小 XML で現在仕様を固定する。
    /// </summary>
    [Fact]
    public void ParseVrcFromXmp_ExtractsWorldNameAndId()
    {
        var xmp = "<x:xmpmeta><vrc:WorldID>wrld_123</vrc:WorldID><vrc:WorldDisplayName>星の工房</vrc:WorldDisplayName></x:xmpmeta>";

        var (name, id) = PhotoScanner.ParseVrcFromXmp(xmp);

        Assert.Equal("星の工房", name);
        Assert.Equal("wrld_123", id);
    }

    /// <summary>
    /// PNG の iTXt チャンク内に入った VRChat XMP を読み出せることを確認する。
    ///
    /// VRChat 写真ではワールド情報が通常の画像ヘッダではなく XMP の iTXt チャンクに入る。
    /// このテストは IHDR + iTXt + IEND だけの最小 PNG を作り、実画像データが無くても
    /// ExtractVrcMetadataFromPng がチャンク構造を正しく辿れることを検証する。
    /// </summary>
    [Fact]
    public void ExtractVrcMetadataFromPng_ReadsWorldMetadataFromItxtChunk()
    {
        var path = Path.Combine(tempDir, "with-metadata.png");
        File.WriteAllBytes(path, PngWithItxt(width: 320, height: 640, "水面の街", "wrld_water"));

        var (name, id) = PhotoScanner.ExtractVrcMetadataFromPng(path);

        Assert.Equal("水面の街", name);
        Assert.Equal("wrld_water", id);
    }

    /// <summary>
    /// フル解析がファイル名日時、PNG XMP、画像寸法を PhotoUpsertData に反映することを確認する。
    ///
    /// 新規写真や更新済み写真では ScanRefreshKind.Full が使われる。
    /// この経路では既存 DB 値に頼らず、現在のファイルから撮影日時・ワールド情報・向きを読み直すため、
    /// 最小 PNG に VRChat XMP と縦長寸法を入れて、metadata/portrait/width/height が保存値になることを固定する。
    /// </summary>
    [Fact]
    public void AnalyzePhoto_FullRefreshUsesFileMetadataAndDimensions()
    {
        var path = Path.Combine(tempDir, "VRChat_2026-06-05_10-20-30.123.png");
        File.WriteAllBytes(path, PngWithItxt(width: 480, height: 960, "縦長ワールド", "wrld_portrait"));

        var data = PhotoScanner.AnalyzePhoto(path, Path.GetFileName(path), slot: 2, existing: null, ScanRefreshKind.Full);

        Assert.Equal(AppPaths.NormalizePathForDb(path), data.PhotoPath);
        Assert.Equal("2026-06-05 10:20:30", data.Timestamp);
        Assert.Equal("縦長ワールド", data.WorldName);
        Assert.Equal("wrld_portrait", data.WorldId);
        Assert.Equal("metadata", data.MatchSource);
        Assert.Equal("portrait", data.Orientation);
        Assert.Equal(480, data.ImageWidth);
        Assert.Equal(960, data.ImageHeight);
        Assert.Equal(2, data.SourceSlot);
    }

    /// <summary>
    /// パスのみ更新の再スキャンでは、既存のワールド情報と画像情報を保持することを確認する。
    ///
    /// ファイル名だけ変わった写真を ScanRefreshKind.PathOnly で処理するときに
    /// PNG メタデータや画像寸法を読み直すと、ユーザーが後から補正したワールド情報を上書きする危険がある。
    /// 現在仕様では既存 DB 値をそのまま維持するため、壊れた画像ファイルを渡しても既存値が残ることを検証する。
    /// </summary>
    [Fact]
    public void AnalyzePhoto_PathOnlyRefreshPreservesExistingMetadata()
    {
        var path = Path.Combine(tempDir, "renamed.png");
        File.WriteAllText(path, "not a real image");
        var existing = new ExistingPhotoInfo
        {
            PhotoFilename = "old.png",
            PhotoPath = AppPaths.NormalizePathForDb(path),
            WorldId = "wrld_existing",
            WorldName = "既存ワールド",
            MatchSource = "manual",
            Orientation = "landscape",
            ImageWidth = 1920,
            ImageHeight = 1080,
            SourceSlot = 1,
        };

        var data = PhotoScanner.AnalyzePhoto(path, "renamed.png", slot: 1, existing, ScanRefreshKind.PathOnly);

        Assert.Equal("既存ワールド", data.WorldName);
        Assert.Equal("wrld_existing", data.WorldId);
        Assert.Equal("manual", data.MatchSource);
        Assert.Equal("landscape", data.Orientation);
        Assert.Equal(1920, data.ImageWidth);
        Assert.Equal(1080, data.ImageHeight);
    }

    /// <summary>
    /// MetadataOnly の再スキャンで、PNG メタデータのワールド情報を読み直すことを確認する。
    ///
    /// MetadataOnly は古い DB に world_name が無い写真へ PNG XMP から補完するための軽量経路だが、
    /// ResolveWorldInfo は PNG XMP が存在する場合、その metadata を既存値より優先する。
    /// 一方で寸法と orientation は既存値を維持するため、world と画像情報の扱いを分けて固定する。
    /// </summary>
    [Fact]
    public void AnalyzePhoto_MetadataOnlyRefreshUsesPngWorldMetadataAndKeepsExistingDimensions()
    {
        var path = Path.Combine(tempDir, "metadata-existing.png");
        File.WriteAllBytes(path, PngWithItxt(width: 640, height: 320, "PNG側ワールド", "wrld_png"));
        var existing = new ExistingPhotoInfo
        {
            PhotoFilename = "metadata-existing.png",
            PhotoPath = AppPaths.NormalizePathForDb(path),
            WorldName = "既存ワールド",
            WorldId = "wrld_existing",
            MatchSource = "manual",
            Orientation = "landscape",
            ImageWidth = 640,
            ImageHeight = 320,
            SourceSlot = 1,
        };

        var data = PhotoScanner.AnalyzePhoto(path, "metadata-existing.png", slot: 1, existing, ScanRefreshKind.MetadataOnly);

        Assert.Equal("PNG側ワールド", data.WorldName);
        Assert.Equal("wrld_png", data.WorldId);
        Assert.Equal("metadata", data.MatchSource);
        Assert.Equal("landscape", data.Orientation);
        Assert.Equal(640, data.ImageWidth);
        Assert.Equal(320, data.ImageHeight);
    }

    /// <summary>
    /// 読めない画像ファイルの寸法取得が unreadable ではなく unknown として返ることを確認する。
    ///
    /// PhotoScanner.ProbeImageDimensions は scanner の前段で失敗を例外として外へ出さず、
    /// DB へ保存できる orientation 値へ落とし込む。
    /// 空ファイルを入力し、警告ログ経由で unknown/null/null になる現在仕様を固定する。
    /// </summary>
    [Fact]
    public void ProbeImageDimensions_ReturnsUnknownForUnreadableImageFile()
    {
        var path = Path.Combine(tempDir, "broken.jpg");
        File.WriteAllText(path, "not an image");

        var result = PhotoScanner.ProbeImageDimensions(path);

        Assert.Equal("unknown", result.orientation);
        Assert.Null(result.width);
        Assert.Null(result.height);
    }

    /// <summary>
    /// VRChat XMP を含まない PNG では、メタデータ抽出が null/null を返すことを確認する。
    ///
    /// 通常の PNG には iTXt が無い、または VRChat 固有タグが無いことがある。
    /// その場合にチャンク走査で例外にせず、後続の archive や既存値維持へ任せるための null を返す。
    /// </summary>
    [Fact]
    public void ExtractVrcMetadataFromPng_ReturnsNullsWhenItxtMetadataIsMissing()
    {
        var path = Path.Combine(tempDir, "without-metadata.png");
        File.WriteAllBytes(path, PngWithoutItxt(width: 320, height: 240));

        var (name, id) = PhotoScanner.ExtractVrcMetadataFromPng(path);

        Assert.Null(name);
        Assert.Null(id);
    }

    /// <summary>
    /// 再帰列挙が対応拡張子だけを拾い、無視対象ディレクトリを辿らないことを確認する。
    ///
    /// 写真フォルダにはサムネイルキャッシュやツールの vendor フォルダが混ざることがある。
    /// それらを走査すると無駄な I/O と誤登録が起きるため、対応拡張子の写真だけを収集し、
    /// cache やドット始まりのディレクトリをスキップする現在仕様を固定する。
    /// </summary>
    [Fact]
    public void CollectPhotosRecursive_FiltersSupportedFilesAndSkippedDirectories()
    {
        var root = Path.Combine(tempDir, "photos");
        var nested = Path.Combine(root, "nested");
        var cache = Path.Combine(root, "cache");
        var hidden = Path.Combine(root, ".hidden");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(hidden);
        File.WriteAllText(Path.Combine(root, "a.png"), "");
        File.WriteAllText(Path.Combine(nested, "b.jpg"), "");
        File.WriteAllText(Path.Combine(nested, "c.JPEG"), "");
        File.WriteAllText(Path.Combine(nested, "unsupported.webp"), "");
        File.WriteAllText(Path.Combine(root, "memo.txt"), "");
        File.WriteAllText(Path.Combine(cache, "ignored.jpg"), "");
        File.WriteAllText(Path.Combine(hidden, "ignored.png"), "");
        var files = new List<(long slot, string filename, string path)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var isComplete = PhotoScanner.CollectPhotosRecursive(7, root, files, visited, CancellationToken.None);

        var names = files.Select(f => f.filename).OrderBy(x => x).ToArray();
        Assert.True(isComplete);
        Assert.Equal(["a.png", "b.jpg", "c.JPEG"], names);
        Assert.All(files, f => Assert.Equal(7, f.slot));
    }

    /// <summary>
    /// 読み取れない root は空フォルダではなく不完全走査として返すことを確認する。
    ///
    /// 親列挙後の改名・削除や一時的なストレージ切断でも、属性取得時には同じ失敗が発生する。
    /// 呼出側は false のスロットを欠落写真の物理削除対象から外す必要がある。
    /// </summary>
    [Fact]
    public void CollectPhotosRecursive_ReturnsIncompleteWhenRootCannotBeRead()
    {
        var missingRoot = Path.Combine(tempDir, "removed-before-attributes");
        var files = new List<(long slot, string filename, string path)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var isComplete = PhotoScanner.CollectPhotosRecursive(
            1,
            missingRoot,
            files,
            visited,
            CancellationToken.None);

        Assert.False(isComplete);
        Assert.Empty(files);
    }

    /// <summary>
    /// 巨大な 1 行を読むときに最大長で打ち切り、次の行へ復帰できることを確認する。
    ///
    /// VRChat の output_log は破損や外部ツールの追記で異常に長い行を含むことがある。
    /// File.ReadLines で丸ごと読むと大きな文字列が確保されるため、ReadCappedLines は
    /// 行単位で 64KiB に制限する。制限後も次行を捨てないことが重要である。
    /// </summary>
    [Fact]
    public void ReadCappedLines_TruncatesHugeLineAndContinuesWithFollowingLines()
    {
        var path = Path.Combine(tempDir, "output_log_huge.txt");
        File.WriteAllText(path, "short\r\n" + new string('x', 70_000) + "\nend");

        var lines = PhotoScanner.ReadCappedLines(path).ToArray();

        Assert.Equal(3, lines.Length);
        Assert.Equal("short", lines[0]);
        Assert.Equal(64 * 1024, lines[1].Length);
        Assert.Equal("end", lines[2]);
    }

    /// <summary>
    /// VRChat ログの入退室行から Polaris archive 用の訪問区間を作れることを確認する。
    ///
    /// archive 連携では「ワールド不明写真の撮影時刻が、どのワールド滞在区間に入るか」を後続 DB が判定する。
    /// その前段として、Entering Room と OnLeftRoom の対応を読み取り、
    /// Leave が無い最後の区間は未確定区間として LeaveTime=null で残す必要がある。
    /// </summary>
    [Fact]
    public void LoadVisitsFromLog_ParsesClosedAndOpenWorldVisitIntervals()
    {
        var path = Path.Combine(tempDir, "output_log_2026-06-05.txt");
        File.WriteAllLines(path,
        [
            "2026.06.05 10:00:00 Log        -  [Behaviour] Entering Room: 朝のワールド",
            "2026.06.05 10:10:00 Log        -  [Behaviour] OnLeftRoom",
            "2026.06.05 10:20:00 Log        -  [Behaviour] Entering Room: 夜のワールド",
        ]);
        var visits = new List<ArchiveWorldVisitData>();

        PhotoScanner.LoadVisitsFromLog(path, visits);

        Assert.Equal(2, visits.Count);
        Assert.Equal("朝のワールド", visits[0].WorldName);
        Assert.Equal("2026-06-05 10:00:00", visits[0].JoinTime);
        Assert.Equal("2026-06-05 10:10:00", visits[0].LeaveTime);
        Assert.Equal("夜のワールド", visits[1].WorldName);
        Assert.Equal("2026-06-05 10:20:00", visits[1].JoinTime);
        Assert.Null(visits[1].LeaveTime);
    }

    /// <summary>
    /// Polaris archive ディレクトリ内の output_log_*.txt から訪問履歴をまとめて読み込めることを確認する。
    ///
    /// LoadPolarisWorldVisits は Directory.GetFiles の結果をファイル名順に並べ、各ログを LoadVisitsFromLog へ渡す。
    /// archive が存在しない場合は例外ではなく空リストを返すため、そのフォールバックも同時に固定する。
    /// </summary>
    [Fact]
    public void LoadPolarisWorldVisits_ReadsSortedLogFilesAndReturnsEmptyForMissingDirectory()
    {
        var archive = Path.Combine(tempDir, "archive");
        Directory.CreateDirectory(archive);
        File.WriteAllLines(Path.Combine(archive, "output_log_2026-06-06.txt"),
        [
            "2026.06.06 10:00:00 Log        -  [Behaviour] Entering Room: 2日目",
            "2026.06.06 10:10:00 Log        -  [Behaviour] OnLeftRoom",
        ]);
        File.WriteAllLines(Path.Combine(archive, "output_log_2026-06-05.txt"),
        [
            "2026.06.05 10:00:00 Log        -  [Behaviour] Entering Room: 1日目",
            "2026.06.05 10:10:00 Log        -  [Behaviour] OnLeftRoom",
        ]);

        var visits = PhotoScanner.LoadPolarisWorldVisits(archive);
        var missing = PhotoScanner.LoadPolarisWorldVisits(Path.Combine(tempDir, "missing-archive"));

        Assert.Equal(["1日目", "2日目"], visits.Select(visit => visit.WorldName).ToArray());
        Assert.Empty(missing);
    }

    /// <summary>
    /// ScanAsync が設定済み写真フォルダを実際に走査し、DB upsert、進捗通知、削除検出を行うことを確認する。
    ///
    /// 初回スキャンでは PNG の XMP・寸法・ファイル名日時が DB に保存され、scan:completed が発行される。
    /// 2回目では片方のファイルを消してから再スキャンし、今回見つからなかった写真が DB から削除されることを検証する。
    /// このテストは DoScanAsync の主要経路を通し、公開 API である ScanAsync から観察できる仕様だけを固定する。
    /// </summary>
    [Fact]
    public async Task ScanAsync_IndexesConfiguredFoldersPublishesProgressAndDeletesMissingPhotos()
    {
        var photoRoot = Path.Combine(tempDir, "photos");
        var nested = Path.Combine(photoRoot, "nested");
        var settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(nested);
        var firstPath = Path.Combine(photoRoot, "VRChat_2026-06-05_10-20-30.123.png");
        var secondPath = Path.Combine(nested, "VRChat_2026-06-05_10-21-30.123.png");
        File.WriteAllBytes(firstPath, PngWithItxt(width: 480, height: 960, "縦長ワールド", "wrld_portrait"));
        File.WriteAllBytes(secondPath, PngWithItxt(width: 960, height: 480, "横長ワールド", "wrld_landscape"));
        var config = new AppConfig(settingsDir);
        config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = photoRoot });
        var db = new AlpheratzDb(Path.Combine(tempDir, "scan.db"));
        db.Initialize();
        var bus = new LocalEventBus();
        var progressEvents = new List<ScanProgressDto>();
        var completedCount = 0;
        bus.Subscribe<ScanProgressDto>(EventNames.ScanProgress, progress =>
        {
            progressEvents.Add(progress);
            return Task.CompletedTask;
        });
        bus.Subscribe(EventNames.ScanCompleted, () =>
        {
            completedCount++;
            return Task.CompletedTask;
        });
        var scanner = new PhotoScanner(config, db, bus);

        await scanner.ScanAsync();
        var first = await db.GetPhotoRecordAsync(AppPaths.NormalizePathForDb(firstPath));
        var second = await db.GetPhotoRecordAsync(AppPaths.NormalizePathForDb(secondPath));
        File.Delete(secondPath);
        await scanner.ScanAsync();
        var deleted = await db.GetPhotoRecordAsync(AppPaths.NormalizePathForDb(secondPath));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("縦長ワールド", first.world_name);
        Assert.Equal("wrld_portrait", first.world_id);
        Assert.Equal("portrait", first.orientation);
        Assert.Equal(480, first.image_width);
        Assert.Equal(960, first.image_height);
        Assert.Equal("2026-06-05 10:20:30", first.timestamp);
        Assert.Equal("横長ワールド", second.world_name);
        Assert.Equal(2, progressEvents.Max(progress => progress.total));
        Assert.Contains(progressEvents, progress => progress.processed == 2 && progress.total == 2);
        Assert.All(progressEvents, progress => Assert.InRange(progress.processed, 0, progress.total));
        Assert.Equal(2, completedCount);
        Assert.Null(deleted);
    }

    /// <summary>
    /// 1st と 2nd が同じ実フォルダを指す場合、走査前に設定エラーとして停止することを確認する。
    ///
    /// 同じ写真を先に見つけた slot へ誤分類すると、後の slot リセットでタグ等を失うため、
    /// 既存設定に重複が残っていても DB 更新や欠落削除へ進めない。
    /// </summary>
    [Fact]
    public async Task ScanAsync_RejectsOverlappingFolderConfigurationBeforeDatabaseChanges()
    {
        var photoRoot = Path.Combine(tempDir, "partial-root");
        var settingsDir = Path.Combine(tempDir, "settings-partial");
        Directory.CreateDirectory(photoRoot);
        var foundPath = Path.Combine(photoRoot, "VRChat_2026-06-05_10-20-30.123.png");
        File.WriteAllBytes(foundPath, PngWithItxt(width: 640, height: 480, "継続登録ワールド", "wrld_partial"));

        var config = new AppConfig(settingsDir);
        config.SaveSetting(new AlpheratzSetting
        {
            PhotoFolderPath = photoRoot,
            // GetFullPath では primary と同じになる別表記を使い、2nd root を重複訪問として不完全にする。
            SecondaryPhotoFolderPath = Path.Combine(photoRoot, "."),
        });
        var db = new AlpheratzDb(Path.Combine(tempDir, "partial.db"));
        db.Initialize();
        var retainedPath = AppPaths.NormalizePathForDb(Path.Combine(photoRoot, "missing-secondary.jpg"));
        await db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = retainedPath,
            PhotoFilename = "missing-secondary.jpg",
            Timestamp = "2026-06-05 09:00:00",
            SourceSlot = 2,
        });
        var bus = new LocalEventBus();
        var errors = new List<string>();
        var completedCount = 0;
        bus.Subscribe<string>(EventNames.ScanError, error =>
        {
            errors.Add(error);
            return Task.CompletedTask;
        });
        bus.Subscribe(EventNames.ScanCompleted, () =>
        {
            completedCount++;
            return Task.CompletedTask;
        });
        var scanner = new PhotoScanner(config, db, bus);

        await scanner.ScanAsync();

        var retained = await db.GetPhotoRecordAsync(retainedPath);
        var inserted = await db.GetPhotoRecordAsync(AppPaths.NormalizePathForDb(foundPath));
        Assert.NotNull(retained);
        Assert.Null(inserted);
        Assert.Equal(0, completedCount);
        Assert.Contains(errors, error => error.Contains("重複", StringComparison.Ordinal));
    }

    /// <summary>
    /// ScanAsync が未変更の既存写真に対して MetadataOnly と PathOnly の差分更新を選ぶことを確認する。
    ///
    /// MetadataOnly は「画像寸法は既にあるが、PNG XMP からワールドだけ補完したい」場合に使われる。
    /// PathOnly は「同じパスのファイル名だけが DB と違う」場合に使われ、ユーザー補正済みのワールドや寸法を保持する。
    /// どちらもフル再解析より副作用が小さいため、既存値が不要に上書きされないことを ScanAsync 経由で検証する。
    /// </summary>
    [Fact]
    public async Task ScanAsync_UsesMetadataOnlyAndPathOnlyRefreshForUnmodifiedExistingPhotos()
    {
        var photoRoot = Path.Combine(tempDir, "refresh");
        var settingsDir = Path.Combine(tempDir, "settings-refresh");
        Directory.CreateDirectory(photoRoot);
        var metadataPath = Path.Combine(photoRoot, "metadata.png");
        var renamedPath = Path.Combine(photoRoot, "renamed.png");
        File.WriteAllBytes(metadataPath, PngWithItxt(width: 300, height: 600, "補完ワールド", "wrld_metadata"));
        File.WriteAllBytes(renamedPath, PngWithItxt(width: 600, height: 300, "読まれないワールド", "wrld_ignored"));
        var metadataMtime = File.GetLastWriteTimeUtc(metadataPath).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var renamedMtime = File.GetLastWriteTimeUtc(renamedPath).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var config = new AppConfig(settingsDir);
        config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = photoRoot });
        var db = new AlpheratzDb(Path.Combine(tempDir, "refresh.db"));
        db.Initialize();
        await db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = AppPaths.NormalizePathForDb(metadataPath),
            PhotoFilename = "metadata.png",
            Timestamp = "2026-06-05 10:00:00",
            LastModifiedUtc = metadataMtime,
            Orientation = "landscape",
            ImageWidth = 111,
            ImageHeight = 222,
            SourceSlot = 1,
        });
        await db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = AppPaths.NormalizePathForDb(renamedPath),
            PhotoFilename = "old-name.png",
            Timestamp = "2026-06-05 10:00:00",
            LastModifiedUtc = renamedMtime,
            WorldName = "手動ワールド",
            WorldId = "wrld_manual",
            MatchSource = "manual",
            Orientation = "portrait",
            ImageWidth = 333,
            ImageHeight = 444,
            SourceSlot = 1,
        });
        var scanner = new PhotoScanner(config, db, new LocalEventBus());

        await scanner.ScanAsync();
        var metadata = await db.GetPhotoRecordAsync(AppPaths.NormalizePathForDb(metadataPath));
        var renamed = await db.GetPhotoRecordAsync(AppPaths.NormalizePathForDb(renamedPath));

        Assert.NotNull(metadata);
        Assert.Equal("補完ワールド", metadata.world_name);
        Assert.Equal("wrld_metadata", metadata.world_id);
        Assert.Equal("metadata", metadata.match_source);
        Assert.Equal("landscape", metadata.orientation);
        Assert.Equal(111, metadata.image_width);
        Assert.Equal(222, metadata.image_height);
        Assert.NotNull(renamed);
        Assert.Equal("renamed.png", renamed.photo_filename);
        Assert.Equal("手動ワールド", renamed.world_name);
        Assert.Equal("wrld_manual", renamed.world_id);
        Assert.Equal("manual", renamed.match_source);
        Assert.Equal("portrait", renamed.orientation);
        Assert.Equal(333, renamed.image_width);
        Assert.Equal(444, renamed.image_height);
    }

    /// <summary>
    /// 大小文字だけが異なる既存パスを同じ写真として扱い、DB の主キー表記を維持することを確認する。
    /// Windows では同じファイルを指していても SQLite の既定主キー比較は大小文字を区別するため、
    /// 発見時の表記で upsert すると重複行になる。ファイル名更新を伴う再走査でこの境界を固定する。
    /// </summary>
    [Fact]
    public async Task ScanAsync_CaseVariantPathPreservesExistingPrimaryKeyWithoutDuplicateRow()
    {
        var photoRoot = Path.Combine(tempDir, "case-variant");
        var settingsDir = Path.Combine(tempDir, "settings-case-variant");
        Directory.CreateDirectory(photoRoot);
        var photoPath = Path.Combine(photoRoot, "current-name.jpg");
        File.WriteAllText(photoPath, "not an image");
        var normalizedPath = AppPaths.NormalizePathForDb(photoPath);
        var storedPath = normalizedPath.ToUpperInvariant();
        var lastModifiedUtc = File.GetLastWriteTimeUtc(photoPath)
            .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var config = new AppConfig(settingsDir);
        config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = photoRoot });
        var db = new AlpheratzDb(Path.Combine(tempDir, "case-variant.db"));
        db.Initialize();
        await db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = storedPath,
            PhotoFilename = "old-name.jpg",
            Timestamp = "2026-06-05 10:00:00",
            LastModifiedUtc = lastModifiedUtc,
            WorldName = "既存ワールド",
            WorldId = "wrld_existing",
            MatchSource = "manual",
            Orientation = "landscape",
            ImageWidth = 1920,
            ImageHeight = 1080,
            SourceSlot = 1,
        });
        var scanner = new PhotoScanner(config, db, new LocalEventBus());

        await scanner.ScanAsync();

        var page = await db.GetPhotosPageAsync(new PhotoQueryParams());
        var record = Assert.Single(page.Items);
        Assert.Equal(1, page.Total);
        Assert.Equal(storedPath, record.photo_path);
        Assert.Equal("current-name.jpg", record.photo_filename);
        Assert.Equal("既存ワールド", record.world_name);
    }

    /// <summary>
    /// 同じパスの画像内容が更新された場合、旧画像のワールド情報と phash を破棄して
    /// 再解析対象へ戻すことを確認する。
    /// </summary>
    [Fact]
    public async Task ScanAsync_ContentChangeClearsExistingWorldMetadataAndPhash()
    {
        var photoRoot = Path.Combine(tempDir, "content-change");
        var settingsDir = Path.Combine(tempDir, "settings-content-change");
        Directory.CreateDirectory(photoRoot);
        var photoPath = Path.Combine(photoRoot, "VRChat_2026-06-05_10-20-30.123.png");
        File.WriteAllBytes(photoPath, PngWithItxt(320, 640, "変更前", "wrld_before"));
        File.SetLastWriteTimeUtc(photoPath, new DateTime(2026, 6, 5, 1, 0, 0, DateTimeKind.Utc));
        var config = new AppConfig(settingsDir);
        config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = photoRoot });
        var db = new AlpheratzDb(Path.Combine(tempDir, "content-change.db"));
        db.Initialize();
        var scanner = new PhotoScanner(config, db, new LocalEventBus());

        await scanner.ScanAsync();
        var normalizedPath = AppPaths.NormalizePathForDb(photoPath);
        await db.UpdatePhotoPhashAsync(normalizedPath, new string('a', 64));

        File.WriteAllBytes(photoPath, PngWithoutItxt(640, 320));
        File.SetLastWriteTimeUtc(photoPath, new DateTime(2026, 6, 6, 1, 0, 0, DateTimeKind.Utc));
        await scanner.ScanAsync();

        var record = await db.GetPhotoRecordAsync(normalizedPath, includePhash: true);
        Assert.NotNull(record);
        Assert.Null(record.world_name);
        Assert.Null(record.world_id);
        Assert.Equal("unresolved", record.match_source);
        Assert.Null(record.phash);
        Assert.Equal(1, await db.GetPendingPhashCountAsync());
    }

    /// <summary>
    /// 設定された写真フォルダが存在しない場合に scan:error が発行され、scan:completed は発行されないことを確認する。
    ///
    /// ユーザーが設定画面で存在しないパスを保存した場合、スキャンは既定フォルダへ勝手にフォールバックせず、
    /// 明示的なエラーとして UI に通知する。誤ったフォルダで DB を更新しないための重要な早期 return である。
    /// </summary>
    [Fact]
    public async Task ScanAsync_PublishesErrorWhenConfiguredFolderIsMissing()
    {
        var settingsDir = Path.Combine(tempDir, "settings-missing");
        var missingPath = Path.Combine(tempDir, "does-not-exist");
        var config = new AppConfig(settingsDir);
        config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = missingPath });
        var db = new AlpheratzDb(Path.Combine(tempDir, "missing.db"));
        db.Initialize();
        var bus = new LocalEventBus();
        var errors = new List<string>();
        var completedCount = 0;
        bus.Subscribe<string>(EventNames.ScanError, message =>
        {
            errors.Add(message);
            return Task.CompletedTask;
        });
        bus.Subscribe(EventNames.ScanCompleted, () =>
        {
            completedCount++;
            return Task.CompletedTask;
        });
        var scanner = new PhotoScanner(config, db, bus);

        await scanner.ScanAsync();

        Assert.Single(errors);
        Assert.Equal("写真フォルダ1が見つかりません。設定からフォルダを選び直してください。", errors[0]);
        Assert.Equal(0, completedCount);
    }

    /// <summary>
    /// 両方の写真フォルダが空なら、既定の Pictures\VRChat を暗黙利用せず未設定エラーにする。
    ///
    /// 明示リセット後に scanner が別経路から呼ばれても、旧既定フォルダを再取込しないことが重要である。
    /// DB の既存行も欠落扱いで削除しない。
    /// </summary>
    [Fact]
    public async Task ScanAsync_PublishesUnsetErrorWithoutDefaultFolderFallback()
    {
        var settingsDir = Path.Combine(tempDir, "settings-unset");
        var config = new AppConfig(settingsDir);
        config.SaveSetting(new AlpheratzSetting());
        var db = new AlpheratzDb(Path.Combine(tempDir, "unset.db"));
        db.Initialize();
        const string existingPath = "/slot1/existing.jpg";
        await db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = existingPath,
            PhotoFilename = "existing.jpg",
            Timestamp = "2026-06-05 10:00:00",
            SourceSlot = 1,
        });
        var bus = new LocalEventBus();
        var errors = new List<string>();
        var completedCount = 0;
        bus.Subscribe<string>(EventNames.ScanError, message =>
        {
            errors.Add(message);
            return Task.CompletedTask;
        });
        bus.Subscribe(EventNames.ScanCompleted, () =>
        {
            completedCount++;
            return Task.CompletedTask;
        });
        var scanner = new PhotoScanner(config, db, bus);

        await scanner.ScanAsync();

        Assert.Single(errors);
        Assert.Equal("写真フォルダが設定されていません。設定からフォルダを選択してください。", errors[0]);
        Assert.Equal(0, completedCount);
        Assert.NotNull(await db.GetPhotoRecordAsync(existingPath));
    }

    /// <summary>実行前・進捗待機中・終了後の取消しで保存内容を保持し、同じscannerから再読込できる。</summary>
    [Fact]
    public async Task RequestCancel_BeforeDuringAndAfterScanPreservesDataAndAllowsRetry()
    {
        var photoRoot = Path.Combine(tempDir, "cancel-photos"); Directory.CreateDirectory(photoRoot);
        var imagePath = Path.Combine(photoRoot, "VRChat_2026-06-05_10-20-30.123.png"); var imageBytes = PngWithItxt(320, 640, "再試行ワールド", "wrld_retry"); File.WriteAllBytes(imagePath, imageBytes);
        var sourcePath = AppPaths.NormalizePathForDb(imagePath); var missingPath = AppPaths.NormalizePathForDb(Path.Combine(photoRoot, "missing.png")); var databasePath = Path.Combine(tempDir, "cancel.db");
        var config = new AppConfig(Path.Combine(tempDir, "cancel-settings")); config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = photoRoot });
        var db = new AlpheratzDb(databasePath); db.Initialize();
        await db.UpsertPhotoAsync(new PhotoUpsertData { PhotoPath = sourcePath, PhotoFilename = Path.GetFileName(imagePath), Timestamp = "2026-06-05 10:20:30", WorldId = "wrld_before", WorldName = "保持用", LastModifiedUtc = "2000-01-01T00:00:00Z" });
        await db.UpsertPhotoAsync(new PhotoUpsertData { PhotoPath = missingPath, PhotoFilename = "missing.png", Timestamp = "2026-05-05 10:00:00", WorldName = "欠落前" });
        using (var seed = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString()))
        {
            seed.Open(); using var command = seed.CreateCommand(); command.Parameters.AddWithValue("$source", sourcePath); command.Parameters.AddWithValue("$filename", Path.GetFileName(imagePath));
            command.CommandText = """
                UPDATE photos SET is_favorite=1, phash='retained-hash' WHERE photo_path=$source;
                INSERT INTO tags(id,name) VALUES(7,'保持用タグ');
                INSERT INTO photo_tags(photo_path,tag_id) VALUES($source,7);
                INSERT INTO photo_user_data_backup(photo_filename,is_favorite,tags) VALUES($filename,1,'["保持用タグ"]');
                INSERT INTO archive_world_visits(id,source_log_name,world_name,join_time,leave_time) VALUES(9,'output_log_fixture.txt','訪問履歴','2026-05-05 09:00:00','2026-05-05 10:00:00');
                """;
            command.ExecuteNonQuery();
        }
        var before = ReadScanDatabaseRows(databasePath); var progress = new List<ScanProgressDto>(); var cancelled = new List<string>(); var errors = new List<string>(); var completed = 0;
        var progressStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var releaseProgress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var holdFirstProgress = true;
        var bus = new LocalEventBus();
        await using var progressSub = bus.Subscribe<ScanProgressDto>(EventNames.ScanProgress, payload => { progress.Add(payload); if (!holdFirstProgress) return Task.CompletedTask; holdFirstProgress = false; progressStarted.TrySetResult(); return releaseProgress.Task; });
        await using var cancelledSub = bus.Subscribe<string>(EventNames.ScanCancelled, payload => { cancelled.Add(payload); return Task.CompletedTask; });
        await using var errorSub = bus.Subscribe<string>(EventNames.ScanError, payload => { errors.Add(payload); return Task.CompletedTask; });
        await using var completeSub = bus.Subscribe(EventNames.ScanCompleted, () => { completed++; return Task.CompletedTask; });
        var scanner = new PhotoScanner(config, db, bus); scanner.RequestCancel(); scanner.RequestCancel();
        Assert.Empty(progress); Assert.Empty(cancelled); Assert.Empty(errors); Assert.Equal(before, ReadScanDatabaseRows(databasePath));
        var scan = scanner.ScanAsync();
        try
        {
            await progressStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(scan.IsCompleted);
            var collecting = Assert.Single(progress); Assert.Equal(0, collecting.processed); Assert.Equal(0, collecting.total); Assert.Equal("写真を確認しています…", collecting.current_world); Assert.Equal("scan", collecting.phase);
            scanner.RequestCancel(); scanner.RequestCancel(); Assert.False(scan.IsCompleted); releaseProgress.TrySetResult(); await scan.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { "写真の読み込みを中止しました。" }, cancelled); Assert.Empty(errors); Assert.Equal(0, completed); Assert.Single(progress);
            var afterCancellation = ReadScanDatabaseRows(databasePath); Assert.Equal(before, afterCancellation); Assert.Equal(imageBytes, File.ReadAllBytes(imagePath));
            scanner.RequestCancel(); Assert.Single(cancelled); Assert.Equal(before, ReadScanDatabaseRows(databasePath));
            await scanner.ScanAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, completed); Assert.Single(cancelled); Assert.Empty(errors); Assert.Null(await db.GetPhotoRecordAsync(missingPath));
            var restored = Assert.IsType<PhotoRecordDto>(await db.GetPhotoRecordAsync(sourcePath)); Assert.Equal("再試行ワールド", restored.world_name); Assert.Equal("wrld_retry", restored.world_id);
            Assert.Equal(new[] { "保持用タグ" }, await db.GetPhotoTagsAsync(sourcePath)); Assert.Equal(imageBytes, File.ReadAllBytes(imagePath));
            var afterRetry = ReadScanDatabaseRows(databasePath); Assert.Equal(before.Skip(1), afterRetry.Skip(1));
            using (var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString()))
            {
                verify.Open(); using var command = verify.CreateCommand(); command.CommandText = "SELECT is_favorite FROM photos WHERE photo_path=$source"; command.Parameters.AddWithValue("$source", sourcePath); Assert.Equal(1L, command.ExecuteScalar());
            }
            scanner.RequestCancel(); Assert.Single(cancelled); Assert.Equal(1, completed);
            var evidenceDir = Environment.GetEnvironmentVariable("ALPHERATZ_STARTUP_TEST_EVIDENCE_DIR");
            if (!string.IsNullOrEmpty(evidenceDir)) File.WriteAllText(Path.Combine(evidenceDir, "alpheratz-scanner-cancellation-lifecycle-evidence.json"), JsonSerializer.Serialize(new {
                owned_root = tempDir, database_before = before, database_after_cancellation = afterCancellation, database_after_retry = afterRetry,
                cancelled_messages = cancelled.ToArray(), error_messages = errors.ToArray(), completed_notifications = completed, initial_progress = collecting,
                source_bytes_preserved = true, same_scanner_retry_succeeded = true, idle_cancel_and_active_cancel_controlled = true, verification_complete = false }));
        }
        finally { releaseProgress.TrySetResult(); await scan.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    /// <summary>専用DBの全6tableを独立SQLで読み、取消し時の全行と再試行時の対象外tableを比較する。</summary>
    private static string[] ReadScanDatabaseRows(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString()); connection.Open(); var result = new List<string>();
        foreach (var table in new[] { "photos", "photo_tags", "tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence" })
        {
            using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY rowid"; using var reader = command.ExecuteReader(); var rows = new List<object?[]>();
            while (reader.Read()) { var row = new object?[reader.FieldCount]; reader.GetValues(row); rows.Add(row.Select(value => value is DBNull ? null : value).ToArray()); }
            result.Add(JsonSerializer.Serialize(rows));
        }
        return result.ToArray();
    }

    /// <summary>
    /// テスト用の PNG バイト列を作る。
    /// IHDR は PhotoScanner.ProbeImageDimensions が幅高さを読むために必要で、
    /// iTXt は VRChat XMP の格納形式を再現するために必要である。
    /// CRC は ExtractVrcMetadataFromPng が検証しないため 0 埋めでよい。
    /// </summary>
    private static byte[] PngWithItxt(int width, int height, string worldName, string worldId)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(ms, "IHDR", Ihdr(width, height));

        var xmp = $"<x:xmpmeta><vrc:WorldDisplayName>{worldName}</vrc:WorldDisplayName><vrc:WorldID>{worldId}</vrc:WorldID></x:xmpmeta>";
        var keyword = System.Text.Encoding.Latin1.GetBytes("XML:com.adobe.xmp");
        var xmpBytes = System.Text.Encoding.UTF8.GetBytes(xmp);
        using var data = new MemoryStream();
        data.Write(keyword);
        data.WriteByte(0);
        data.WriteByte(0);
        data.WriteByte(0);
        data.WriteByte(0);
        data.WriteByte(0);
        data.Write(xmpBytes);
        WriteChunk(ms, "iTXt", data.ToArray());
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    /// <summary>
    /// XMP iTXt を持たない最小 PNG を作る。
    /// ExtractVrcMetadataFromPng の「メタデータなし」経路を検証するため、IHDR と IEND だけを書き込む。
    /// </summary>
    private static byte[] PngWithoutItxt(int width, int height)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(ms, "IHDR", Ihdr(width, height));
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    /// <summary>
    /// PNG の IHDR データ部を作る。
    /// 幅高さ以外の値は一般的な 8bit truecolor 相当の固定値でよく、今回の解析対象には影響しない。
    /// </summary>
    private static byte[] Ihdr(int width, int height)
    {
        var data = new byte[13];
        WriteBigEndian(data, 0, width);
        WriteBigEndian(data, 4, height);
        data[8] = 8;
        data[9] = 2;
        return data;
    }

    /// <summary>
    /// PNG チャンクを書き込む。
    /// PhotoScanner は CRC を読んで捨てるだけなので、テストデータでは 4 バイトの 0 を置く。
    /// </summary>
    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length);
        stream.Write(System.Text.Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write([0, 0, 0, 0]);
    }

    /// <summary>
    /// PNG 仕様で使う 32bit big-endian 値を書き込む。
    /// 画像生成ではなく、PhotoScanner のヘッダ読み取りを検証するための最小補助である。
    /// </summary>
    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    /// <summary>Exifの8種類の向きを両endianで読み、表示寸法を写真メタデータへ引き渡す。</summary>
    [Theory]
    [InlineData(true, 1, "portrait", 80L, 240L)]
    [InlineData(true, 2, "portrait", 80L, 240L)]
    [InlineData(true, 3, "portrait", 80L, 240L)]
    [InlineData(true, 4, "portrait", 80L, 240L)]
    [InlineData(true, 5, "landscape", 240L, 80L)]
    [InlineData(true, 6, "landscape", 240L, 80L)]
    [InlineData(true, 7, "landscape", 240L, 80L)]
    [InlineData(true, 8, "landscape", 240L, 80L)]
    [InlineData(false, 1, "portrait", 80L, 240L)]
    [InlineData(false, 2, "portrait", 80L, 240L)]
    [InlineData(false, 3, "portrait", 80L, 240L)]
    [InlineData(false, 4, "portrait", 80L, 240L)]
    [InlineData(false, 5, "landscape", 240L, 80L)]
    [InlineData(false, 6, "landscape", 240L, 80L)]
    [InlineData(false, 7, "landscape", 240L, 80L)]
    [InlineData(false, 8, "landscape", 240L, 80L)]
    public void ProbeImageDimensions_JpegExifOrientationsReachPhotoMetadata(bool littleEndian, int exifOrientation, string expectedOrientation, long expectedWidth, long expectedHeight)
    {
        var path = Path.Combine(tempDir, "VRChat_2026-06-05_10-20-30.123.jpg");
        var bytes = JpegExifFixture(ExifOrientationPayload(littleEndian, (ushort)exifOrientation)); File.WriteAllBytes(path, bytes);
        var probe = PhotoScanner.ProbeImageDimensions(path);
        var photo = PhotoScanner.AnalyzePhoto(path, Path.GetFileName(path), slot: 2, existing: null, ScanRefreshKind.Full);
        SaveJpegExifEvidence($"orientation-{littleEndian}-{exifOrientation}", path, bytes, probe, photo);
        Assert.Equal((expectedOrientation, (long?)expectedWidth, (long?)expectedHeight), probe);
        Assert.Equal(expectedOrientation, photo.Orientation); Assert.Equal(expectedWidth, photo.ImageWidth); Assert.Equal(expectedHeight, photo.ImageHeight);
        Assert.Equal("2026-06-05 10:20:30", photo.Timestamp); Assert.Equal(2L, photo.SourceSlot);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <summary>Exifが破損していても、通常向きとして有効なSOF寸法を保持する契約を確認する。</summary>
    [Theory]
    [InlineData(true, "short-header")]
    [InlineData(false, "short-header")]
    [InlineData(true, "invalid-byte-order")]
    [InlineData(false, "invalid-byte-order")]
    [InlineData(true, "invalid-magic")]
    [InlineData(false, "invalid-magic")]
    [InlineData(true, "ifd-outside")]
    [InlineData(false, "ifd-outside")]
    [InlineData(true, "ifd-before-overflow")]
    [InlineData(false, "ifd-before-overflow")]
    [InlineData(true, "ifd-near-int-max")]
    [InlineData(false, "ifd-near-int-max")]
    [InlineData(true, "ifd-int-max")]
    [InlineData(false, "ifd-int-max")]
    [InlineData(true, "ifd-above-int-max")]
    [InlineData(false, "ifd-above-int-max")]
    [InlineData(true, "ifd-unsigned-max")]
    [InlineData(false, "ifd-unsigned-max")]
    [InlineData(true, "truncated-entry")]
    [InlineData(false, "truncated-entry")]
    [InlineData(true, "no-entries")]
    [InlineData(false, "no-entries")]
    [InlineData(true, "wrong-type")]
    [InlineData(false, "wrong-type")]
    [InlineData(true, "wrong-count")]
    [InlineData(false, "wrong-count")]
    [InlineData(true, "orientation-zero")]
    [InlineData(false, "orientation-zero")]
    [InlineData(true, "orientation-nine")]
    [InlineData(false, "orientation-nine")]
    public void ProbeImageDimensions_JpegExifMalformedMetadataPreservesFrameDimensions(bool littleEndian, string corruption)
    {
        var exif = ExifOrientationPayload(littleEndian, 6);
        switch (corruption)
        {
            case "short-header": exif = exif[..10]; break;
            case "invalid-byte-order": exif[6] = (byte)'Z'; exif[7] = (byte)'Z'; break;
            case "invalid-magic": exif[8] = 0; exif[9] = 0; break;
            case "truncated-entry": exif = exif[..27]; break;
            case "no-entries": exif[14] = 0; exif[15] = 0; break;
            case "wrong-type": exif[18] = 0; exif[19] = 0; break;
            case "wrong-count": exif[20] = 0; exif[21] = 0; exif[22] = 0; exif[23] = 0; break;
            case "orientation-zero": exif = ExifOrientationPayload(littleEndian, 0); break;
            case "orientation-nine": exif = ExifOrientationPayload(littleEndian, 9); break;
            default:
                var offset = corruption switch
                {
                    "ifd-outside" => 32u,
                    "ifd-before-overflow" => 0x7ffffffdu,
                    "ifd-near-int-max" => 0x7ffffffeu,
                    "ifd-int-max" => 0x7fffffffu,
                    "ifd-above-int-max" => 0x80000000u,
                    "ifd-unsigned-max" => 0xffffffffu,
                    _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
                };
                if (littleEndian) System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(exif.AsSpan(10, 4), offset);
                else System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(exif.AsSpan(10, 4), offset);
                break;
        }
        var path = Path.Combine(tempDir, "malformed-exif.jpg"); var bytes = JpegExifFixture(exif); File.WriteAllBytes(path, bytes);
        var probe = PhotoScanner.ProbeImageDimensions(path);
        SaveJpegExifEvidence($"malformed-{littleEndian}-{corruption}", path, bytes, probe);
        Assert.Equal(("portrait", (long?)80, (long?)240), probe);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <summary>SOF0/SOF2とExifの配置順に依存せず、後続の非Exif APP1で向きを失わない。</summary>
    [Theory]
    [InlineData(0xc0, false, false)]
    [InlineData(0xc0, true, true)]
    [InlineData(0xc2, false, true)]
    [InlineData(0xc2, true, false)]
    public void ProbeImageDimensions_JpegExifSurvivesFrameOrderAndNonExifApp1(int frameMarker, bool exifAfterFrame, bool xmpAfterExif)
    {
        var path = Path.Combine(tempDir, "frame-order.JPEG");
        var bytes = JpegExifFixture(ExifOrientationPayload(false, 7), (byte)frameMarker, exifAfterFrame, xmpAfterExif); File.WriteAllBytes(path, bytes);
        var probe = PhotoScanner.ProbeImageDimensions(path);
        SaveJpegExifEvidence($"layout-{frameMarker}-{exifAfterFrame}-{xmpAfterExif}", path, bytes, probe);
        Assert.Equal(("landscape", (long?)240, (long?)80), probe); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <summary>padding後のIFDと先行する別tagを読み飛ばし、2番目のorientationを取得する。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProbeImageDimensions_JpegExifFindsOrientationAfterAnotherIfdEntry(bool littleEndian)
    {
        var original = ExifOrientationPayload(littleEndian, 8); var exif = new byte[52]; original.AsSpan(0, 14).CopyTo(exif);
        if (littleEndian) System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(exif.AsSpan(10, 4), 16);
        else System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(exif.AsSpan(10, 4), 16);
        exif[littleEndian ? 22 : 23] = 2;
        original.AsSpan(16, 12).CopyTo(exif.AsSpan(24)); original.AsSpan(16, 12).CopyTo(exif.AsSpan(36));
        exif[24] = littleEndian ? (byte)0x23 : (byte)0xc1; exif[25] = littleEndian ? (byte)0xc1 : (byte)0x23;
        var path = Path.Combine(tempDir, "padded-ifd.jpeg"); var bytes = JpegExifFixture(exif); File.WriteAllBytes(path, bytes);
        var probe = PhotoScanner.ProbeImageDimensions(path); SaveJpegExifEvidence($"padded-ifd-{littleEndian}", path, bytes, probe);
        Assert.Equal(("landscape", (long?)240, (long?)80), probe); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <summary>存在確認後にfolderが外れた場合はslotを空と扱わず、再接続後の完全scanでだけ整理する。</summary>
    [Fact]
    public async Task IncompleteSlotPreservesIndexedPhotosAndWarnsUntilTheSameFolderCanBeRescanned()
    {
        var folder = Path.Combine(tempDir, "photos"); var offline = Path.Combine(tempDir, "offline"); Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "VRChat_2026-10-20_10-00-00.000.png"); var original = PngWithItxt(80, 40, "再接続ワールド", "wrld_reconnect"); File.WriteAllBytes(source, original);
        var stale = AppPaths.NormalizePathForDb(Path.Combine(folder, "missing.png")); var db = new AlpheratzDb(Path.Combine(tempDir, "incomplete.db")); db.Initialize();
        await db.UpsertPhotoAsync(new PhotoUpsertData { PhotoPath = stale, PhotoFilename = "missing.png", SourceSlot = 1, Timestamp = "2026-10-19 10:00:00", WorldName = "保持する旧ワールド" });
        await db.SetPhotoFavoriteAsync(stale, true); await db.AddPhotoTagAsync(stale, "保持するタグ");
        await db.UpsertPhotoAsync(new PhotoUpsertData { PhotoPath = "/slot2/keep.jpg", PhotoFilename = "keep.jpg", SourceSlot = 2, Timestamp = "2026-10-19 10:00:00" });
        var before = JsonSerializer.Serialize(await db.GetPhotoRecordAsync(stale)); var config = new AppConfig(Path.Combine(tempDir, "settings")); config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = folder });
        var bus = new LocalEventBus(); var moved = false; var warnings = new List<string>();
        await using var progress = bus.Subscribe<ScanProgressDto>(EventNames.ScanProgress, _ => { if (!moved) { moved = true; Directory.Move(folder, offline); } return Task.CompletedTask; });
        await using var warning = bus.Subscribe<string>(EventNames.ScanWarning, value => { warnings.Add(value); return Task.CompletedTask; });
        var scanner = new PhotoScanner(config, db, bus);
        try { await scanner.ScanAsync(); }
        finally { if (Directory.Exists(offline)) Directory.Move(offline, folder); }
        Assert.True(moved); Assert.Equal(before, JsonSerializer.Serialize(await db.GetPhotoRecordAsync(stale)));
        Assert.NotNull(await db.GetPhotoRecordAsync("/slot2/keep.jpg")); Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Equal("写真フォルダ1を完全に読み取れませんでした。フォルダへのアクセス権を確認してください。見つからない写真の整理は次回の読み込みまで延期します。", Assert.Single(warnings));
        await progress.DisposeAsync(); warnings.Clear(); await scanner.ScanAsync();
        Assert.Null(await db.GetPhotoRecordAsync(stale)); Assert.NotNull(await db.GetPhotoRecordAsync("/slot2/keep.jpg"));
        var indexed = await db.GetPhotoRecordAsync(AppPaths.NormalizePathForDb(source)); Assert.NotNull(indexed); Assert.Equal("再接続ワールド", indexed.world_name); Assert.Equal(1L, indexed.source_slot);
        Assert.Empty(warnings); Assert.Equal(original, File.ReadAllBytes(source));
    }

    /// <summary>標準のTIFF header・IFD0を固定byteで作り、値の格納だけをBCLで行う。</summary>
    private static byte[] ExifOrientationPayload(bool littleEndian, ushort orientation)
    {
        byte[] payload = littleEndian
            ? [0x45, 0x78, 0x69, 0x66, 0, 0, 0x49, 0x49, 0x2a, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
            : [0x45, 0x78, 0x69, 0x66, 0, 0, 0x4d, 0x4d, 0, 0x2a, 0, 0, 0, 8, 0, 1, 1, 0x12, 0, 3, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0];
        if (littleEndian) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(24, 2), orientation);
        else System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(24, 2), orientation);
        return payload;
    }

    /// <summary>ヘッダ解析用の80×240 JPEGを作る。圧縮画素のdecodeはこのfixtureの確認範囲外。</summary>
    private static byte[] JpegExifFixture(byte[] exif, byte frameMarker = 0xc0, bool exifAfterFrame = false, bool xmpAfterExif = false)
    {
        using var stream = new MemoryStream(); stream.Write([0xff, 0xd8]);
        if (exifAfterFrame) WriteSegment(frameMarker, [8, 0, 0xf0, 0, 0x50, 3, 1, 0x11, 0, 2, 0x11, 1, 3, 0x11, 1]);
        WriteSegment(0xe1, exif);
        if (xmpAfterExif) WriteSegment(0xe1, "http://ns.adobe.com/xap/1.0/\0<x:xmpmeta/>"u8.ToArray());
        if (!exifAfterFrame) WriteSegment(frameMarker, [8, 0, 0xf0, 0, 0x50, 3, 1, 0x11, 0, 2, 0x11, 1, 3, 0x11, 1]);
        stream.Write([0xff, 0xd9]); return stream.ToArray();

        void WriteSegment(byte marker, byte[] payload)
        {
            var length = checked((ushort)(payload.Length + 2)); stream.Write([0xff, marker, (byte)(length >> 8), (byte)length]); stream.Write(payload);
        }
    }

    /// <summary>選択実行時だけ入力・読取後byte・実測値を新規保存し、失敗した期待値比較にも参照を残す。</summary>
    private static void SaveJpegExifEvidence(string name, string path, byte[] input, (string? orientation, long? width, long? height) observed, PhotoUpsertData? photo = null)
    {
        var evidenceDir = Environment.GetEnvironmentVariable("ALPHERATZ_JPEG_EXIF_TEST_EVIDENCE_DIR"); if (string.IsNullOrEmpty(evidenceDir)) return;
        var after = File.ReadAllBytes(path);
        using (var file = new FileStream(Path.Combine(evidenceDir, name + ".jpg"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(input);
        using (var file = new FileStream(Path.Combine(evidenceDir, name + ".after.jpg"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(after);
        using var report = new StreamWriter(new FileStream(Path.Combine(evidenceDir, name + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.None), new System.Text.UTF8Encoding(false));
        report.Write(JsonSerializer.Serialize(new { case_name = name, owned_root = Path.GetDirectoryName(path), source_path = path,
            observed_orientation = observed.orientation, observed_width = observed.width, observed_height = observed.height,
            metadata_orientation = photo?.Orientation, metadata_width = photo?.ImageWidth, metadata_height = photo?.ImageHeight,
            source_bytes_preserved = input.AsSpan().SequenceEqual(after), verification_complete = false }));
    }
}
