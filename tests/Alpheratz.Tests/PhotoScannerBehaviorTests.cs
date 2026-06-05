using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Scanner;
using Alpheratz.Models;
using Alpheratz.Models.Events;

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
    /// テストで作成したファイルを削除する。
    /// 削除失敗は本体仕様の検証対象ではないため、アサーション結果を隠さないように握りつぶす。
    /// </summary>
    public void Dispose()
    {
        try { Directory.Delete(tempDir, recursive: true); }
        catch { }
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
        File.WriteAllText(Path.Combine(nested, "b.webp"), "");
        File.WriteAllText(Path.Combine(root, "memo.txt"), "");
        File.WriteAllText(Path.Combine(cache, "ignored.jpg"), "");
        File.WriteAllText(Path.Combine(hidden, "ignored.png"), "");
        var files = new List<(long slot, string filename, string path)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        PhotoScanner.CollectPhotosRecursive(7, root, files, visited, CancellationToken.None);

        var names = files.Select(f => f.filename).OrderBy(x => x).ToArray();
        Assert.Equal(["a.png", "b.webp"], names);
        Assert.All(files, f => Assert.Equal(7, f.slot));
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
        Assert.Contains(progressEvents, progress => progress.current_world == "横長ワールド");
        Assert.Contains(progressEvents, progress => progress.processed == 2 && progress.total == 2);
        Assert.Equal(2, completedCount);
        Assert.Null(deleted);
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
        Assert.Contains(missingPath, errors[0]);
        Assert.Equal(0, completedCount);
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
}
