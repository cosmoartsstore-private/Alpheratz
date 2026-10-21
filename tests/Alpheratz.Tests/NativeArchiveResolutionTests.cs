using System.Text.Json;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Scanner;
using Alpheratz.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace Alpheratz.Tests;

/// <summary>HKCUを隔離した専用processで、Polaris登録・実ログ・scanner・service・標準DB保存先を接続する。</summary>
public sealed partial class NativeAppConfigTests
{
    /// <summary>実Polaris出力の受渡しと写真への補完、DB再open、元ログ保持を画面なしで検証する。</summary>
    [Fact]
    public Task NativeArchiveConsumesPolarisProducerOutputAndReopensPhotoWorlds() => RunIsolatedAsync(async owner =>
    {
        using var fixture = new ArchiveScenario(owner, registerArchive: false);
        const string name = "output_log_2026-10-08_09-59-50.txt";
        const string hash = "0cbec1da285e131dd7c45d75debd6ca49cb875a1b68283a27d0fef21c3b8afec";
        var external = Environment.GetEnvironmentVariable("POLARIS_HEADLESS_CONSUMER_FIXTURE_ROOT");
        var producer = external ?? Path.Combine(owner.RootDirectory, "standalone-producer");
        if (external is null)
        {
            const string log = """
                2026.10.08 09:59:50 Log        -  VRChat starting
                2026.10.08 09:59:51 Log        -  User Authenticated: Fixture Owner (usr_aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa)
                2026.10.08 10:00:00 Log        -  [Behaviour] Entering Room: 連携・朝のワールド
                2026.10.08 10:00:01 Log        -  [Behaviour] Joining wrld_cccccccc-cccc-cccc-cccc-cccccccccccc:10001~private(usr_aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa)~region(jp)
                2026.10.08 10:05:00 Log        -  [Behaviour] OnPlayerJoined 連携ゲスト (usr_bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb)
                2026.10.08 10:10:00 Log        -  [Behaviour] OnPlayerLeft 連携ゲスト (usr_bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb)
                2026.10.08 10:15:00 Log        -  [VRC Camera] Took screenshot to: C:\Fixture\VRChat_2026-10-08_10-15-00.000_1920x1080.png
                2026.10.08 10:30:00 Log        -  [Behaviour] OnLeftRoom
                2026.10.08 11:00:00 Log        -  [Behaviour] Entering Room: 連携 "夜" ワールド
                2026.10.08 11:00:01 Log        -  [Behaviour] Joining wrld_dddddddd-dddd-dddd-dddd-dddddddddddd:20002~region(us)
                2026.10.08 11:30:00 Log        -  [Behaviour] OnLeftRoom
                """;
            Directory.CreateDirectory(Path.Combine(producer, "Data", "archive"));
            File.WriteAllText(Path.Combine(producer, "Data", "archive", name), log.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n", new System.Text.UTF8Encoding(false));
        }
        else Assert.Equal("polaris-headless-consumer-fixture-v1", File.ReadAllText(Path.Combine(producer, "owner.marker")).Trim());
        var source = Path.Combine(producer, "Data", "archive", name);
        var before = File.ReadAllBytes(source);
        Assert.Equal(hash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(before)).ToLowerInvariant());
        using var registration = Registry.CurrentUser.CreateSubKey(@"Software\CosmoArtsStore\Polaris");
        registration.SetValue("InstallLocation", producer, RegistryValueKind.String);
        Assert.Equal(Path.Combine(producer, "Data", "archive"), AppPaths.GetPolarisArchiveDir());
        var paths = Enumerable.Range(0, 4).Select(i => Path.Combine(owner.RootDirectory, $"photo-{i}.jpg")).ToArray();
        for (var i = 0; i < paths.Length; i++) File.WriteAllBytes(paths[i], [(byte)i, 11, 22]);
        await fixture.SeedPhoto(paths[0], "2026-10-08 10:15:00");
        await fixture.SeedPhoto(paths[1], "2026-10-08 11:15:00", slot: 2);
        await fixture.SeedPhoto(paths[2], "2026-10-08 12:00:00");
        await fixture.SeedPhoto(paths[3], "2026-10-08 10:15:00", world: "手動確定を保持");
        await fixture.Db.SetPhotoFavoriteAsync(paths[0], true);
        await fixture.Db.AddPhotoTagAsync(paths[0], "連携後も保持");
        Assert.Equal(2, await fixture.Service.ResolveUnknownWorldsFromArchiveAsync());
        Assert.Equal(JsonSerializer.Serialize(new object?[][] {
            [name, "連携・朝のワールド", "2026-10-08 10:00:00", "2026-10-08 10:30:00"],
            [name, "連携 \"夜\" ワールド", "2026-10-08 11:00:00", "2026-10-08 11:30:00"],
        }), fixture.ReadRows("SELECT source_log_name,world_name,join_time,leave_time FROM archive_world_visits ORDER BY join_time"));
        SqliteConnection.ClearAllPools();
        var reopened = new AlpheratzDb(); reopened.Initialize();
        var first = (await reopened.GetPhotoRecordAsync(paths[0]))!;
        Assert.Equal("連携・朝のワールド", first.world_name); Assert.Equal("polaris_archive", first.match_source);
        Assert.True(first.is_favorite); Assert.Equal(["連携後も保持"], first.tags);
        var second = (await reopened.GetPhotoRecordAsync(paths[1]))!;
        Assert.Equal("連携 \"夜\" ワールド", second.world_name); Assert.Equal(2L, second.source_slot);
        Assert.Null((await reopened.GetPhotoRecordAsync(paths[2]))!.world_name);
        Assert.Equal("手動確定を保持", (await reopened.GetPhotoRecordAsync(paths[3]))!.world_name);
        Assert.Equal(0, await fixture.Service.ResolveUnknownWorldsFromArchiveAsync());
        Assert.Equal(before, File.ReadAllBytes(source));
        for (var i = 0; i < paths.Length; i++) Assert.Equal(new byte[] { (byte)i, 11, 22 }, File.ReadAllBytes(paths[i]));
        if (Environment.GetEnvironmentVariable("POLARIS_HEADLESS_CONSUMER_RECEIPT_ROOT") is { Length: > 0 } receiptRoot)
        {
            var observation = new { consumer = "Alpheratz", external_producer = external is not null, source_path = source, source_sha256 = hash,
                source_bytes_preserved = true, real_app_paths = true, real_scanner = true, real_service = true, fresh_sqlite_connection = true,
                resolved_photos = 2, reused_resolution_count = 0, photo_worlds = new string?[] { first.world_name, second.world_name, null, "手動確定を保持" },
                favorite_tag_and_originals_preserved = true, visible_windows = 0 };
            using var output = new FileStream(Path.Combine(receiptRoot, "alpheratz-cross-app-observation.json"), FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(output, observation, new JsonSerializerOptions { WriteIndented = true });
        }
    });

    /// <summary>現行PolarisのData/archiveから未知写真を補完し、favorite・tag・原本・他設定を保持する。</summary>
    [Fact]
    public Task NativeArchiveCurrentPolarisLayoutResolvesUnknownPhotosAndPreservesUserData() => RunIsolatedAsync(async owner =>
    {
        using var fixture = new ArchiveScenario(owner, registerArchive: false);
        var polarisInstall = Path.Combine(owner.RootDirectory, "Polaris"); var currentArchive = Path.Combine(polarisInstall, "Data", "archive");
        Assert.StartsWith(Path.GetFullPath(owner.RootDirectory) + Path.DirectorySeparatorChar, Path.GetFullPath(currentArchive));
        Directory.CreateDirectory(currentArchive);
        var logPath = Path.Combine(currentArchive, "output_log_current_layout.txt");
        var logBytes = new System.Text.UTF8Encoding(false).GetBytes("2026.10.08 10:00:00 Log - [Behaviour] Entering Room: 現行保存先のワールド\n2026.10.08 10:30:00 Log - [Behaviour] OnLeftRoom\n");
        File.WriteAllBytes(logPath, logBytes);
        using var registration = Registry.CurrentUser.CreateSubKey(@"Software\CosmoArtsStore\Polaris");
        registration.SetValue("InstallLocation", polarisInstall, RegistryValueKind.String);
        await fixture.SeedPhoto("/photo/current-layout.jpg", "2026-10-08 10:15:00");
        await fixture.Db.SetPhotoFavoriteAsync("/photo/current-layout.jpg", true); await fixture.Db.AddPhotoTagAsync("/photo/current-layout.jpg", "保持");
        await fixture.Db.UpsertArchiveWorldVisitsAsync([ArchiveScenario.Visit("保持する旧履歴")]);
        var config = new AppConfig(); config.SaveSetting(new AlpheratzSetting { ThemeMode = "dark", TweetTemplates = ["保持する本文"] });
        var settings = File.ReadAllBytes(owner.SettingPath); var before = fixture.Snapshot();
        var userBackups = fixture.ReadRows("SELECT * FROM photo_user_data_backup ORDER BY 1, 2");
        var beforeTags = fixture.ReadRows("SELECT * FROM tags ORDER BY id"); var beforeLinks = fixture.ReadRows("SELECT * FROM photo_tags ORDER BY photo_path,tag_id");
        var legacyArchive = Path.Combine(polarisInstall, "archive");
        var found = AppPaths.GetPolarisArchiveDir(); var resolved = await fixture.Service.ResolveUnknownWorldsFromArchiveAsync();
        Assert.Equal(currentArchive, found); Assert.Equal(1, resolved); Assert.False(Directory.Exists(legacyArchive));
        Assert.Equal(beforeTags, fixture.ReadRows("SELECT * FROM tags ORDER BY id")); Assert.Equal(beforeLinks, fixture.ReadRows("SELECT * FROM photo_tags ORDER BY photo_path,tag_id"));
        Assert.Equal(userBackups, fixture.ReadRows("SELECT * FROM photo_user_data_backup ORDER BY 1, 2"));
        Assert.Equal(settings, File.ReadAllBytes(owner.SettingPath)); Assert.Equal(logBytes, File.ReadAllBytes(logPath));
        Assert.Equal(polarisInstall, registration.GetValue("InstallLocation")); Assert.Equal(RegistryValueKind.String, registration.GetValueKind("InstallLocation"));
        var photo = (await fixture.Db.GetPhotoRecordAsync("/photo/current-layout.jpg"))!;
        Assert.Equal("現行保存先のワールド", photo.world_name); Assert.Equal("polaris_archive", photo.match_source);
        Assert.Equal("2026-10-08 10:15:00", photo.timestamp); Assert.Equal(1L, photo.source_slot); Assert.True(photo.is_favorite); Assert.Equal(["保持"], photo.tags);
        Assert.Equal(0, await fixture.Service.ResolveUnknownWorldsFromArchiveAsync());
        if (Environment.GetEnvironmentVariable("ALPHERATZ_POLARIS_LAYOUT_RECEIPT_ROOT") is { Length: > 0 } receiptRoot)
        {
            var observation = new
            {
                real_hkcu_isolated = true, real_app_paths = true, real_scanner = true, real_service = true,
                fixture_root = owner.RootDirectory, fixture_registry_key = GetRegistryPath(Guid.ParseExact(Path.GetFileName(owner.RootDirectory), "N")),
                polaris_install_location = polarisInstall, polaris_archive_directory = currentArchive,
                consumer_legacy_archive_directory = legacyArchive, discovered_archive_directory = found,
                resolved_photo_count = resolved, current_layout_connection_satisfied = true,
                database_rows_before = before, database_rows_after = fixture.Snapshot(),
                user_data_backup_rows_before = userBackups, user_data_backup_rows_after = fixture.ReadRows("SELECT * FROM photo_user_data_backup ORDER BY 1, 2"),
                setting_bytes_before = Convert.ToBase64String(settings), setting_bytes_after = Convert.ToBase64String(File.ReadAllBytes(owner.SettingPath)),
                source_log_bytes_before = Convert.ToBase64String(logBytes), source_log_bytes_after = Convert.ToBase64String(File.ReadAllBytes(logPath)),
                registry_value = registration.GetValue("InstallLocation"), registry_value_kind = registration.GetValueKind("InstallLocation").ToString(),
                tags_and_photo_links_preserved = true, user_data_backup_preserved = true, setting_bytes_preserved = true,
                source_log_bytes_preserved = true, legacy_archive_created = false, registry_value_preserved = true,
                actual_polaris_process_started = false, actual_installer_executed = false,
                build_configuration = typeof(NativeAppConfigTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
                    .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration,
                verification_complete = false,
            };
            using var output = new FileStream(Path.Combine(receiptRoot, "alpheratz-polaris-current-layout-observation.json"), FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(output, observation, new JsonSerializerOptions { WriteIndented = true });
        }
    });

    [Fact]
    public Task NativeArchiveServiceReadsRegisteredLogsAndResolvesPhotosWithoutChangingKnownWorlds() => RunIsolatedAsync(async owner =>
    {
        using var fixture = new ArchiveScenario(owner); await fixture.SeedPhoto("/slot1/a.jpg", "2026-10-08 10:15:00");
        await fixture.SeedPhoto("/slot2/b.jpg", "2026-10-08 11:15:00", slot: 2); await fixture.SeedPhoto("/known/c.jpg", "2026-10-08 10:15:00", world: "手動確定");
        await fixture.Db.SetPhotoFavoriteAsync("/slot1/a.jpg", true); await fixture.Db.AddPhotoTagAsync("/slot1/a.jpg", "保持");
        fixture.WriteLog("output_log_2026-10-08.txt", """
            2026.10.08 10:00:00 Log - [Behaviour] Entering Room: 朝の "ワールド"
            2026.10.08 10:30:00 Log - [Behaviour] OnLeftRoom
            2026.10.08 11:00:00 Log - [Behaviour] Entering Room: 夜のワールド
            """);
        fixture.WriteLog("output_log_2026-10-07.txt", """
            2026.10.07 10:00:00 Log - [Behaviour] Entering Room: 前日
            2026.10.07 10:30:00 Log - [Behaviour] OnLeftRoom
            """);
        fixture.WriteLog("ignored.txt", "2026.10.08 11:10:00 Log - [Behaviour] Entering Room: 対象外");
        await fixture.Db.UpsertArchiveWorldVisitsAsync([ArchiveScenario.Visit("古いcache")]);

        Assert.Equal(2, (await fixture.Service.ResolveUnknownWorldsFromArchiveAsync()));
        var primary = (await fixture.Db.GetPhotoRecordAsync("/slot1/a.jpg"))!;
        Assert.Equal("朝の \"ワールド\"", primary.world_name); Assert.Equal("polaris_archive", primary.match_source); Assert.True(primary.is_favorite); Assert.Equal(["保持"], primary.tags);
        var secondary = (await fixture.Db.GetPhotoRecordAsync("/slot2/b.jpg"))!; Assert.Equal("夜のワールド", secondary.world_name); Assert.Equal(2L, secondary.source_slot);
        var known = (await fixture.Db.GetPhotoRecordAsync("/known/c.jpg"))!; Assert.Equal("手動確定", known.world_name); Assert.Equal("manual", known.match_source);
        var expected = JsonSerializer.Serialize(new object?[][]
        {
            ["output_log_2026-10-07.txt", "前日", "2026-10-07 10:00:00", "2026-10-07 10:30:00"],
            ["output_log_2026-10-08.txt", "朝の \"ワールド\"", "2026-10-08 10:00:00", "2026-10-08 10:30:00"],
            ["output_log_2026-10-08.txt", "夜のワールド", "2026-10-08 11:00:00", null],
        });
        Assert.Equal(expected, fixture.ReadRows("SELECT source_log_name, world_name, join_time, leave_time FROM archive_world_visits ORDER BY join_time"));
        Assert.Equal(0, (await fixture.Service.ResolveUnknownWorldsFromArchiveAsync()));
    });

    [Fact]
    public Task NativeArchiveWithoutPolarisRegistrationPreservesExistingCacheAndPhotos() => RunIsolatedAsync(async owner =>
    {
        using var fixture = new ArchiveScenario(owner, registerArchive: false); await fixture.SeedPhoto("/photo/a.jpg", "2026-10-08 10:15:00");
        await fixture.Db.UpsertArchiveWorldVisitsAsync([ArchiveScenario.Visit("保持")]); var original = fixture.Snapshot();
        Assert.Null(AppPaths.GetPolarisArchiveDir()); Assert.Equal(0, (await fixture.Service.ResolveUnknownWorldsFromArchiveAsync()));
        Assert.Equal(original, fixture.Snapshot()); Assert.False(Directory.Exists(fixture.ArchiveDirectory));
    });

    [Fact]
    public Task NativeEmptyArchiveClearsTheOldVisitCacheWithoutChangingPhotos() => RunIsolatedAsync(async owner =>
    {
        using var fixture = new ArchiveScenario(owner); await fixture.SeedPhoto("/photo/a.jpg", "2026-10-08 10:15:00");
        await fixture.Db.UpsertArchiveWorldVisitsAsync([ArchiveScenario.Visit("旧履歴")]); var photos = fixture.ReadRows("SELECT * FROM photos ORDER BY photo_path");
        fixture.WriteLog("memo.txt", "2026.10.08 10:00:00 Log - [Behaviour] Entering Room: 対象外");
        Assert.Equal(0, (await fixture.Service.ResolveUnknownWorldsFromArchiveAsync()));
        Assert.Equal("[]", fixture.ReadRows("SELECT * FROM archive_world_visits")); Assert.Equal(photos, fixture.ReadRows("SELECT * FROM photos ORDER BY photo_path"));
    });

    [Fact]
    public Task NativeArchiveCancellationPreservesStateAndTheSameServiceSupportsRetry() => RunIsolatedAsync(async owner =>
    {
        using var fixture = new ArchiveScenario(owner); await fixture.SeedPhoto("/photo/a.jpg", "2026-10-08 10:15:00"); fixture.WriteTwoVisitLog();
        await fixture.Db.UpsertArchiveWorldVisitsAsync([ArchiveScenario.Visit("旧履歴")]); var original = fixture.Snapshot();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.ResolveUnknownWorldsFromArchiveAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken); Assert.Equal(original, fixture.Snapshot());
        Assert.Equal(1, await fixture.Service.ResolveUnknownWorldsFromArchiveAsync());
        Assert.Equal("第1", (await fixture.Db.GetPhotoRecordAsync("/photo/a.jpg"))!.world_name);
    });

    [Fact]
    public Task NativeArchiveImportFailureKeepsOldVisitsAndPhotosUntilRetry() => RunIsolatedAsync(async owner =>
    {
        using var fixture = new ArchiveScenario(owner); await fixture.SeedPhoto("/photo/a.jpg", "2026-10-08 10:15:00"); fixture.WriteTwoVisitLog();
        await fixture.Db.UpsertArchiveWorldVisitsAsync([ArchiveScenario.Visit("旧履歴")]); var original = fixture.Snapshot();
        fixture.Execute("CREATE TRIGGER reject_native_archive AFTER INSERT ON archive_world_visits WHEN NEW.world_name='第2' BEGIN SELECT RAISE(ABORT, '検証用の取込失敗'); END;");
        var error = await Assert.ThrowsAsync<SqliteException>(() => fixture.Service.ResolveUnknownWorldsFromArchiveAsync());
        Assert.Equal(19, error.SqliteErrorCode); Assert.Equal(original, fixture.Snapshot());
        fixture.Execute("DROP TRIGGER reject_native_archive;"); Assert.Equal(1, await fixture.Service.ResolveUnknownWorldsFromArchiveAsync());
        Assert.Equal("第1", (await fixture.Db.GetPhotoRecordAsync("/photo/a.jpg"))!.world_name);
    });

    /// <summary>訪問cacheのcommitと写真補完のtransactionは別であるため、それぞれの保存状態を確認する。</summary>
    [Fact]
    public Task NativePhotoResolutionFailureKeepsTheImportedVisitsButRollsBackAllPhotoUpdates() => RunIsolatedAsync(async owner =>
    {
        using var fixture = new ArchiveScenario(owner); await fixture.SeedPhoto("/photo/a.jpg", "2026-10-08 10:15:00"); await fixture.SeedPhoto("/photo/b.jpg", "2026-10-08 11:15:00");
        fixture.WriteTwoVisitLog(); await fixture.Db.UpsertArchiveWorldVisitsAsync([ArchiveScenario.Visit("旧履歴")]);
        var photos = fixture.ReadRows("SELECT * FROM photos ORDER BY photo_path");
        fixture.Execute("""
            CREATE TRIGGER reject_native_photo AFTER UPDATE OF world_name ON photos
            WHEN (SELECT COUNT(*) FROM photos WHERE match_source='polaris_archive')=2
            BEGIN SELECT RAISE(ABORT, '検証用の写真補完失敗'); END;
            """);
        var error = await Assert.ThrowsAsync<SqliteException>(() => fixture.Service.ResolveUnknownWorldsFromArchiveAsync()); Assert.Equal(19, error.SqliteErrorCode);
        Assert.Equal(photos, fixture.ReadRows("SELECT * FROM photos ORDER BY photo_path"));
        var worlds = JsonSerializer.Deserialize<string[][]>(fixture.ReadRows("SELECT world_name FROM archive_world_visits ORDER BY join_time"))!;
        Assert.Equal(["第1", "第2"], worlds.Select(row => row[0]));
        fixture.Execute("DROP TRIGGER reject_native_photo;"); Assert.Equal(2, await fixture.Service.ResolveUnknownWorldsFromArchiveAsync());
        Assert.Equal("第1", (await fixture.Db.GetPhotoRecordAsync("/photo/a.jpg"))!.world_name);
        Assert.Equal("第2", (await fixture.Db.GetPhotoRecordAsync("/photo/b.jpg"))!.world_name);
    });

    /// <summary>読込失敗のログをskipする現行結果を記録する。利用者向け失敗表示の確認とは分ける。</summary>
    [Fact]
    public Task NativeLockedArchiveLogIsSkippedWhileReadableLogsReplaceTheVisitCache() => RunIsolatedAsync(async owner =>
    {
        using var fixture = new ArchiveScenario(owner); await fixture.SeedPhoto("/photo/a.jpg", "2026-10-08 10:15:00"); await fixture.SeedPhoto("/photo/b.jpg", "2026-10-08 11:15:00");
        fixture.WriteLog("output_log_locked.txt", "2026.10.08 10:00:00 Log - [Behaviour] Entering Room: locked");
        fixture.WriteLog("output_log_readable.txt", "2026.10.08 11:00:00 Log - [Behaviour] Entering Room: readable");
        await fixture.Db.UpsertArchiveWorldVisitsAsync([ArchiveScenario.Visit("旧履歴")]); var lockedPath = Path.Combine(fixture.ArchiveDirectory, "output_log_locked.txt");
        var bytes = File.ReadAllBytes(lockedPath);
        using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(1, (await fixture.Service.ResolveUnknownWorldsFromArchiveAsync()));
            Assert.Null((await fixture.Db.GetPhotoRecordAsync("/photo/a.jpg"))!.world_name);
            Assert.Equal("readable", (await fixture.Db.GetPhotoRecordAsync("/photo/b.jpg"))!.world_name);
            Assert.Equal("[[\"readable\"]]", fixture.ReadRows("SELECT world_name FROM archive_world_visits"));
        }
        Assert.Equal(bytes, File.ReadAllBytes(lockedPath)); Assert.Equal(1, (await fixture.Service.ResolveUnknownWorldsFromArchiveAsync()));
        Assert.Equal("locked", (await fixture.Db.GetPhotoRecordAsync("/photo/a.jpg"))!.world_name);
    });

    private sealed class ArchiveScenario : IDisposable
    {
        private readonly string dbPath;
        public string ArchiveDirectory { get; }
        public AlpheratzDb Db { get; }
        public WorldService Service { get; }
        private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();

        public ArchiveScenario(RegistryFixture owner, bool registerArchive = true)
        {
            Assert.Equal(Path.GetFileName(owner.RootDirectory), Registry.CurrentUser.GetValue("FixtureId")); Assert.Null(AppPaths.DataDirOverrideForTests);
            dbPath = Assert.IsType<string>(AppPaths.GetDbPath());
            Assert.StartsWith(Path.GetFullPath(owner.InstallDirectory) + Path.DirectorySeparatorChar, Path.GetFullPath(dbPath));
            Db = new AlpheratzDb(); Db.Initialize(); var scanner = new PhotoScanner(new AppConfig(), Db, new LocalEventBus()); Service = new WorldService(Db, scanner);
            var polaris = Path.Combine(owner.RootDirectory, "Polaris"); ArchiveDirectory = Path.Combine(polaris, "Data", "archive");
            if (registerArchive)
            {
                Directory.CreateDirectory(ArchiveDirectory); using var key = Registry.CurrentUser.CreateSubKey(@"Software\CosmoArtsStore\Polaris"); key.SetValue("InstallLocation", polaris);
                Assert.Equal(ArchiveDirectory, AppPaths.GetPolarisArchiveDir());
            }
        }

        public void Dispose()
        {
            using var connection = new SqliteConnection(ConnectionString); SqliteConnection.ClearPool(connection);
        }

        public Task SeedPhoto(string path, string timestamp, int slot = 1, string? world = null) => Db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = path, PhotoFilename = Path.GetFileName(path), Timestamp = timestamp, SourceSlot = slot, WorldName = world, MatchSource = world is null ? null : "manual",
        });

        public static ArchiveWorldVisitData Visit(string world) => new() { SourceLogName = "old.log", WorldName = world, JoinTime = "2026-10-08 10:00:00", LeaveTime = "2026-10-08 12:00:00" };
        public void WriteLog(string filename, string content)
        {
            Assert.Equal(Path.GetFileName(filename), filename); var path = Path.Combine(ArchiveDirectory, filename);
            Assert.StartsWith(Path.GetFullPath(ArchiveDirectory) + Path.DirectorySeparatorChar, Path.GetFullPath(path)); File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        }

        public void WriteTwoVisitLog() => WriteLog("output_log_fixture.txt", """
            2026.10.08 10:00:00 Log - [Behaviour] Entering Room: 第1
            2026.10.08 10:30:00 Log - [Behaviour] OnLeftRoom
            2026.10.08 11:00:00 Log - [Behaviour] Entering Room: 第2
            2026.10.08 11:30:00 Log - [Behaviour] OnLeftRoom
            """);

        public void Execute(string sql)
        {
            using var connection = new SqliteConnection(ConnectionString); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
        }

        public string ReadRows(string sql)
        {
            using var connection = new SqliteConnection(ConnectionString); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql;
            using var reader = command.ExecuteReader(); var rows = new List<object?[]>();
            while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
            return JsonSerializer.Serialize(rows);
        }

        public string Snapshot() => JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["photos"] = ReadRows("SELECT * FROM photos ORDER BY photo_path"), ["visits"] = ReadRows("SELECT * FROM archive_world_visits ORDER BY id"),
            ["tags"] = ReadRows("SELECT * FROM tags ORDER BY id"), ["links"] = ReadRows("SELECT * FROM photo_tags ORDER BY photo_path, tag_id"),
            ["sequence"] = ReadRows("SELECT * FROM sqlite_sequence ORDER BY name"),
        });
    }
}
