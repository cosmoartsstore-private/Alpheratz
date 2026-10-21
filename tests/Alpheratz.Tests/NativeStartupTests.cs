using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Features.Bootstrap;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.Shell;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Alpheratz.Tests;

[CollectionDefinition("Native WinUI startup", DisableParallelization = true)]
public sealed class NativeStartupCollection;

/// <summary>本体のProgram.Mainを専用process・HKCU・非表示desktopで実行し、実際のXAMLと起動遷移を確認する。</summary>
[Collection("Native WinUI startup")]
public sealed partial class NativeStartupTests
{
    private const string ChildMarker = "ALPHERATZ_NATIVE_STARTUP_TEST_CHILD";
    private const string FixtureIdVariable = "ALPHERATZ_NATIVE_STARTUP_FIXTURE_ID";
    private const string PhaseVariable = "ALPHERATZ_NATIVE_STARTUP_PHASE";
    private const string EvidenceDirectoryVariable = "ALPHERATZ_STARTUP_TEST_EVIDENCE_DIR";
    private const string FailureMessage = "必要なデータを準備できませんでした。アプリを終了して、もう一度起動してください。繰り返す場合は販売ページからお問い合わせください。";

    [Fact]
    public Task NativeStartup_DefaultThemeReachesActualShell() => RunIsolatedAsync("default", null, false);

    [Fact]
    public Task NativeStartup_SavedDarkThemeReachesActualShell() => RunIsolatedAsync("dark", "dark", false);

    [Fact]
    public Task NativeStartup_CorruptDatabaseShowsFailureAndExitButton() => RunIsolatedAsync("corrupt", "dark", true);

    [Fact]
    public Task NativeStartup_LockedDatabasePreservesSavedDataAndRestarts() => RunIsolatedAsync("recovery", "dark", false);

    private static async Task RunIsolatedAsync(string kind, string? theme, bool corrupt, [CallerMemberName] string testName = "")
    {
        if (Environment.GetEnvironmentVariable(ChildMarker) == testName)
        {
            var id = Guid.ParseExact(Environment.GetEnvironmentVariable(FixtureIdVariable)!, "N");
            await RunCurrentEntryAsync(id, kind, theme, corrupt); return;
        }

        var fixtureId = Guid.NewGuid(); var root = FixtureDirectory(fixtureId); Directory.CreateDirectory(root);
        var inputDesktop = CurrentInputDesktopName();
        var desktopName = "AlpheratzStartup_" + fixtureId.ToString("N");
        var desktop = CreateDesktop(desktopName, 0, 0, 0, 0x01ff, 0); Assert.NotEqual(nint.Zero, desktop);
        var start = NativeDispatcherQueueTests.CreateChildStart(typeof(NativeStartupTests), testName, ChildMarker, testName);
        start.Environment[ChildMarker] = testName; start.Environment[FixtureIdVariable] = fixtureId.ToString("N"); start.Environment["ALPHERATZ_VERBOSE_LOGS"] = "1";
        try
        {
            if (kind == "recovery") await VerifyRecoveryAsync(start, desktopName, root);
            else await RunStageAsync(start, desktopName, root, kind);
            Assert.Equal(inputDesktop, CurrentInputDesktopName());
            if (Environment.GetEnvironmentVariable(EvidenceDirectoryVariable) is { } evidenceDirectory)
            {
                var workspace = FindWorkspace(); var evidenceRoot = Path.GetFullPath(Path.Combine(workspace, ".claude", "audit-work", "checks")) + Path.DirectorySeparatorChar;
                var destination = Path.GetFullPath(Path.Combine(evidenceDirectory, $"alpheratz-startup-{kind}-evidence.json"));
                Assert.StartsWith(evidenceRoot, destination, StringComparison.OrdinalIgnoreCase); Assert.True(Directory.Exists(evidenceDirectory));
                File.Copy(Path.Combine(root, "completed.json"), destination, overwrite: false);
            }
        }
        finally
        {
            if (Environment.GetEnvironmentVariable(EvidenceDirectoryVariable) is { } evidenceDirectory)
            {
                var evidenceRoot = Path.GetFullPath(Path.Combine(FindWorkspace(), ".claude", "audit-work", "checks")) + Path.DirectorySeparatorChar;
                Assert.StartsWith(evidenceRoot, Path.GetFullPath(evidenceDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                foreach (var name in new[] { "stdout.log", "stderr.log", "locked.stdout.log", "locked.stderr.log", "retry.stdout.log", "retry.stderr.log", "timeout-step.log", "progress-step.log", "xaml-load-errors.log", "toast-binding-observation.json", "tag-picker-observation.json", "general-preferences-observation.json", "template-preferences-observation.json", "photo-unload-observation.json", "起動検証/Data/logs/info.log" })
                    if (File.Exists(Path.Combine(root, name))) File.Copy(Path.Combine(root, name), Path.Combine(evidenceDirectory, $"alpheratz-startup-{kind}-{Path.GetFileName(name)}"), overwrite: false);
            }
            Registry.CurrentUser.DeleteSubKeyTree(RegistryPath(fixtureId), throwOnMissingSubKey: false);
            using var remaining = Registry.CurrentUser.OpenSubKey(RegistryPath(fixtureId)); Assert.Null(remaining);
            Assert.Equal(Path.GetFullPath(FixtureDirectory(fixtureId)), Path.GetFullPath(root));
            Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Alpheratz.Startup.Tests")), Path.GetDirectoryName(Path.GetFullPath(root)));
            Assert.False(File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)); Directory.Delete(root, recursive: true); Assert.False(Directory.Exists(root));
            Assert.True(CloseDesktop(desktop), "専用desktopを閉じられません。");
            Assert.Equal(inputDesktop, CurrentInputDesktopName());
        }
    }

    private static async Task<JsonElement> RunStageAsync(ProcessStartInfo start, string desktop, string root, string kind, string phase = "")
    {
        start.Environment[PhaseVariable] = phase;
        var child = await RunChildOnDesktopAsync(start, desktop, root, phase);
        Assert.True(child.ExitCode == 0, $"起動検証processの終了code={child.ExitCode}\n{child.Output}\n{child.Error}");
        var receiptPath = Path.Combine(root, phase.Length == 0 ? "completed.json" : $"completed-{phase}.json");
        Assert.True(File.Exists(receiptPath), $"起動検証の完了記録がありません。\n{child.Output}\n{child.Error}");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(receiptPath)); var receipt = document.RootElement;
        Assert.Equal(kind, receipt.GetProperty("kind").GetString()); Assert.True(receipt.GetProperty("actual_main_body").GetBoolean());
        Assert.Equal("test assembly Main", receipt.GetProperty("test_child_entry").GetString()); Assert.Equal(start.Environment[ChildMarker], receipt.GetProperty("child_fact").GetString());
        Assert.True(receipt.GetProperty("entry_returned").GetBoolean()); Assert.True(receipt.GetProperty("private_desktop_preserved").GetBoolean());
        Assert.Equal(phase, receipt.GetProperty("phase").GetString()); return receipt.Clone();
    }

    /// <summary>保存済みDataの排他lockだけを解除し、同じ設定・DB・写真のまま本体を再起動する。</summary>
    private static async Task VerifyRecoveryAsync(ProcessStartInfo start, string desktop, string root)
    {
        var install = Path.Combine(root, "起動検証"); var dbDirectory = Path.Combine(install, "Data", "db"); Directory.CreateDirectory(dbDirectory);
        var dbPath = Path.Combine(dbDirectory, "Alpheratz.db"); var settingPath = Path.Combine(dbDirectory, "setting.json");
        var photoPath = Path.Combine(install, "保存済み写真.png");
        File.WriteAllBytes(photoPath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII="));
        WriteSavedSetting(settingPath, "dark"); new AlpheratzDb(dbPath).Initialize();
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = """
                INSERT INTO photos (photo_path,photo_filename,world_id,world_name,timestamp,phash,orientation,image_width,image_height,source_slot,is_favorite,match_source,is_missing)
                    VALUES ($path,'保存済み写真.png','wrld_fixture','保存済みワールド','2026-10-01 10:20:30',$phash,'square',1,1,1,1,'manual',0);
                INSERT INTO tags (id,name) VALUES (17,'保持するタグ');
                INSERT INTO photo_tags (photo_path,tag_id) VALUES ($path,17);
                INSERT INTO photo_user_data_backup (photo_filename,is_favorite,tags) VALUES ('保存済み写真.png',1,'["保持するタグ"]');
                INSERT INTO archive_world_visits (id,source_log_name,world_name,join_time,leave_time)
                    VALUES (23,'fixture.log','保持する履歴','2026-10-01 10:00:00','2026-10-01 11:00:00');
                """;
            command.Parameters.AddWithValue("$path", photoPath.Replace('\\', '/')); command.Parameters.AddWithValue("$phash", new string('0', 64)); command.ExecuteNonQuery();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)"; command.ExecuteNonQuery(); SqliteConnection.ClearPool(connection);
        }
        var originalSetting = File.ReadAllBytes(settingPath); var originalPhoto = File.ReadAllBytes(photoPath); var originalDatabase = ReadSharedBytes(dbPath); var originalSnapshot = DatabaseSnapshot(dbPath);
        JsonElement failed;
        using (var locked = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            failed = await RunStageAsync(start, desktop, root, "recovery", "locked");
            Assert.True(failed.GetProperty("failure_visible").GetBoolean()); Assert.True(failed.GetProperty("exit_button_invoked").GetBoolean());
            Assert.False(failed.GetProperty("shell_observed").GetBoolean()); Assert.Equal("sdkReady", failed.GetProperty("final_phase").GetString());
            using var bytes = new MemoryStream(); locked.CopyTo(bytes); Assert.Equal(originalDatabase, bytes.ToArray());
        }
        Assert.Equal(originalSnapshot, DatabaseSnapshot(dbPath)); Assert.Equal(originalSetting, File.ReadAllBytes(settingPath)); Assert.Equal(originalPhoto, File.ReadAllBytes(photoPath));
        var retry = await RunStageAsync(start, desktop, root, "recovery", "retry");
        Assert.True(retry.GetProperty("shell_observed").GetBoolean()); Assert.False(retry.GetProperty("failure_visible").GetBoolean()); Assert.Equal("uiReady", retry.GetProperty("final_phase").GetString());
        Assert.Equal(1, retry.GetProperty("saved_photo_count").GetInt32()); Assert.Equal(failed.GetProperty("install_location").GetString(), retry.GetProperty("install_location").GetString());
        Assert.Equal(failed.GetProperty("data_directory").GetString(), retry.GetProperty("data_directory").GetString());
        Assert.Equal(originalSnapshot, DatabaseSnapshot(dbPath)); Assert.Equal(originalSetting, File.ReadAllBytes(settingPath)); Assert.Equal(originalPhoto, File.ReadAllBytes(photoPath));
        var receipt = new { kind = "recovery", actual_main_body = true, entry_returned = true, private_desktop_preserved = true, stage_count = 2, stages = new[] { failed, retry },
            same_data_directory = true, database_bytes_preserved_after_failure = true, database_rows_preserved = true, setting_bytes_preserved = true, photo_bytes_preserved = true,
            database_snapshot = originalSnapshot, setting_sha256 = HashFile(settingPath), photo_sha256 = HashFile(photoPath), verification_complete = false };
        await File.WriteAllTextAsync(Path.Combine(root, "completed.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    private static string DatabaseSnapshot(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); connection.Open();
        var tables = new Dictionary<string, string>();
        foreach (var table in new[] { "photos", "tags", "photo_tags", "photo_user_data_backup", "archive_world_visits", "sqlite_sequence" })
        {
            using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY rowid"; using var reader = command.ExecuteReader(); var rows = new List<object?[]>();
            while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
            tables[table] = JsonSerializer.Serialize(rows);
        }
        return JsonSerializer.Serialize(tables);
    }

    private static void WriteSavedSetting(string path, string theme) => File.WriteAllText(path, $$"""{"themeMode":"{{theme}}","photoFolderPath":"","secondaryPhotoFolderPath":"","enableStartup":false,"startupPreferenceSet":true,"tweetTemplates":["起動検証専用"],"activeTweetTemplate":"起動検証専用"}""", new UTF8Encoding(false));

    private static async Task RunCurrentEntryAsync(Guid id, string kind, string? theme, bool corrupt)
    {
        var root = FixtureDirectory(id); Assert.True(Directory.Exists(root));
        var install = Path.Combine(root, "起動検証"); var dbDirectory = Path.Combine(install, "Data", "db"); Directory.CreateDirectory(dbDirectory);
        var phase = Environment.GetEnvironmentVariable(PhaseVariable) ?? ""; var hasSavedPhoto = kind == "recovery"; var failure = corrupt || phase == "locked";
        var photoModalScenario = IsPhotoModalScenario(kind);
        var generalSettingsScenario = IsGeneralSettingsScenario(kind); var templateSettingsScenario = IsTemplateSettingsScenario(kind);
        var galleryScenario = IsGalleryScenario(kind);
        var seededDataScenario = photoModalScenario || generalSettingsScenario || templateSettingsScenario || galleryScenario;
        Assert.True(hasSavedPhoto ? phase is "locked" or "retry" : phase.Length == 0, "起動検証のstage指定が不正です。");
        var settingPath = Path.Combine(dbDirectory, "setting.json");
        if (theme is not null && !hasSavedPhoto) WriteSavedSetting(settingPath, theme);
        var originalSetting = File.Exists(settingPath) ? File.ReadAllBytes(settingPath) : null;
        var dbPath = Path.Combine(dbDirectory, "Alpheratz.db"); byte[] corruptBytes = Encoding.UTF8.GetBytes("起動検証専用の破損SQLiteファイル");
        var logPath = Path.Combine(install, "Data", "logs", "info.log"); var originalLog = File.Exists(logPath) ? ReadSharedBytes(logPath) : [];
        if (corrupt) File.WriteAllBytes(dbPath, corruptBytes);
        // 本体loggerはfallback directoryも作成するため、既存であることを要求し、実際の書込先は専用Dataで確認する。
        Assert.True(Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CosmoArtsStore", "Alpheratz", "Data", "logs")));
        using var isolated = Registry.CurrentUser.CreateSubKey(RegistryPath(id)); isolated.SetValue("FixtureId", id.ToString("N")); isolated.SetValue("Owner", root);
        using (var key = isolated.CreateSubKey(@"Software\CosmoArtsStore\Alpheratz")) key.SetValue("InstallLocation", install);
        using (var key = isolated.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) key.SetValue("UnrelatedFixture", "保持する値");
        Assert.Equal(0, RegOverridePredefKey(CurrentUserHandle, isolated.Handle));
        // XAMLのnative error detailを所有領域に保存する。診断の書込失敗で元の例外を置き換えない。
        EventHandler<FirstChanceExceptionEventArgs> xamlFailure = (_, args) =>
        {
            if (args.Exception is not Microsoft.UI.Xaml.Markup.XamlParseException) return;
            try
            {
                var details = args.Exception.Data.Cast<System.Collections.DictionaryEntry>().Select(entry => new { key = entry.Key as string, value = entry.Value as string, type = entry.Value?.GetType().FullName }).ToArray();
                File.AppendAllText(Path.Combine(root, "xaml-load-errors.log"), JsonSerializer.Serialize(new { exception = args.Exception.ToString(), hresult = args.Exception.HResult, details }) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        };
        if (seededDataScenario) AppDomain.CurrentDomain.FirstChanceException += xamlFailure;
        try
        {
            Assert.Equal(id.ToString("N"), Registry.CurrentUser.GetValue("FixtureId")); Assert.Equal(root, Registry.CurrentUser.GetValue("Owner")); VerifyPaths(install);
            if (seededDataScenario) await SeedPhotoModalDatabase(install, dbPath, kind == "photo-tags-delete-filter-retry");
            if (galleryScenario) PhotoSql(dbPath, "UPDATE photos SET is_favorite=1 WHERE photo_filename='写真①.png'");
            var desktopName = "AlpheratzStartup_" + id.ToString("N");
            var completed = new TaskCompletionSource<StartupObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
            var observation = new StartupObservation();
            // UIがnative呼出しで停止した場合も、所有領域へ最後の進行状態を保存する。UI objectは別threadから参照しない。
            using var stopObservation = new ManualResetEventSlim();
            var progressThread = seededDataScenario ? new Thread(() =>
            {
                var previous = "";
                while (!stopObservation.Wait(100))
                {
                    var current = observation.Step;
                    if (current == previous) continue;
                    File.WriteAllText(Path.Combine(root, "progress-step.log"), current, new UTF8Encoding(false)); previous = current;
                }
            }) { IsBackground = true } : null;
            progressThread?.Start();
            var uiThread = new Thread(() => RunMainOnDesktop(desktopName, theme, failure, hasSavedPhoto, kind, observation, completed)) { IsBackground = true };
            uiThread.SetApartmentState(ApartmentState.STA); uiThread.Start();
            StartupObservation observed;
            try
            {
                try { observed = await completed.Task.WaitAsync(TimeSpan.FromSeconds(25)); }
                catch (TimeoutException) { await File.WriteAllTextAsync(Path.Combine(root, "timeout-step.log"), phase + ": " + observation.Step, new UTF8Encoding(false)); throw; }
            }
            finally { stopObservation.Set(); if (progressThread is not null) Assert.True(progressThread.Join(TimeSpan.FromSeconds(2)), "実画面の進行記録threadが終了しません。"); }
            Assert.True(uiThread.Join(TimeSpan.FromSeconds(5)), "本体入口のthreadが終了しません。");
            Assert.True(observed.DesktopPreserved); Assert.True(observed.EntryReturned); Assert.True(observed.BootstrapObserved);
            Assert.Equal(failure ? "sdkReady" : "uiReady", observed.FinalPhase);
            if (originalSetting is not null)
            {
                // 設定保存は形式を正規化するため、操作終了後の全fieldを初期値へ照合する。
                // 保存操作をしない既存caseは、JSONのbyte列まで保持する契約を維持する。
                if (generalSettingsScenario || templateSettingsScenario) Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<AlpheratzSetting>(originalSetting)), JsonSerializer.Serialize(new AppConfig().LoadSetting()));
                else Assert.Equal(originalSetting, File.ReadAllBytes(settingPath));
            }
            if (corrupt) Assert.Equal(corruptBytes, ReadSharedBytes(dbPath));
            else if (!failure)
            {
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString()); connection.Open();
                using var count = connection.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM photos"; Assert.Equal(seededDataScenario ? 2L : hasSavedPhoto ? 1L : 0L, count.ExecuteScalar());
                count.CommandText = "PRAGMA integrity_check"; Assert.Equal("ok", count.ExecuteScalar());
            }
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); Assert.NotNull(run); Assert.Null(run.GetValue("Alpheratz")); Assert.Equal("保持する値", run.GetValue("UnrelatedFixture"));
            if (failure)
            {
                var timer = Stopwatch.StartNew();
                while ((!File.Exists(logPath) || !ReadSharedText(logPath).Contains("App.OnLaunchedCore: DB initialize failed", StringComparison.Ordinal)) && timer.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(20);
                Assert.Contains("App.OnLaunchedCore: DB initialize failed", ReadSharedText(logPath));
            }
            else if (File.Exists(logPath))
            {
                var currentLog = ReadSharedBytes(logPath); Assert.Equal(originalLog, currentLog.Take(originalLog.Length).ToArray());
                Assert.DoesNotContain("[FATAL]", Encoding.UTF8.GetString(currentLog.AsSpan(originalLog.Length)));
            }
            var assembly = typeof(App).Assembly.Location;
            var resources = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.xbf", SearchOption.AllDirectories).OrderBy(path => path).ToDictionary(path => Path.GetRelativePath(AppContext.BaseDirectory, path), HashFile);
            Assert.Contains("App.xbf", resources.Keys); Assert.Contains("MainWindow.xbf", resources.Keys);
            var configuration = typeof(NativeStartupTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
            Assert.Equal(configuration, typeof(App).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration);
            AppContext.TryGetSwitch("System.Globalization.Invariant", out var invariantGlobalization);
            var coreLibrary = typeof(object).Assembly.Location;
            using var process = Process.GetCurrentProcess();
            var runtimeModules = process.Modules.Cast<ProcessModule>().Where(module =>
                new[] { "Microsoft.UI.Xaml.dll", "Microsoft.WindowsAppRuntime.dll", "Microsoft.WindowsAppRuntime.Bootstrap.dll" }.Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(module => module.ModuleName, module => new { path = module.FileName, sha256 = HashFile(module.FileName) }, StringComparer.OrdinalIgnoreCase);
            var resourceMap = Path.Combine(AppContext.BaseDirectory, "resources.pri");
            if (configuration == "Release")
            {
                // 本体のRelease bodyをsystem runtimeで代用せず、専用exeの実際の同梱runtimeと照合する。
                // 内部flagの固定値ではなく、本体runtimeとの一致とタグ操作に必要な日本語cultureを確認する。
                using var productRuntime = JsonDocument.Parse(ReadSharedText(Path.Combine(AppContext.BaseDirectory, "Alpheratz.Frontend.runtimeconfig.json")));
                var productProperties = productRuntime.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");
                var productInvariant = productProperties.TryGetProperty("System.Globalization.Invariant", out var productFlag) && productFlag.GetBoolean();
                Assert.Equal(productInvariant, invariantGlobalization);
                Assert.Equal("ja-JP", System.Globalization.CultureInfo.GetCultureInfo("ja-JP").Name);
                Assert.Equal(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(coreLibrary), ignoreCase: true);
                foreach (var moduleName in new[] { "Microsoft.UI.Xaml.dll", "Microsoft.WindowsAppRuntime.dll" })
                {
                    Assert.True(runtimeModules.TryGetValue(moduleName, out var module), $"Release runtimeが読み込まれていません: {moduleName}");
                    Assert.Equal(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(module!.path), ignoreCase: true);
                }
                Assert.DoesNotContain("Microsoft.WindowsAppRuntime.Bootstrap.dll", runtimeModules.Keys, StringComparer.OrdinalIgnoreCase);
                Assert.True(File.Exists(resourceMap), "Releaseのtheme resource mapがありません。");
            }
            var receipt = new { kind, phase, test_child_entry = "test assembly Main", child_fact = Environment.GetEnvironmentVariable(ChildMarker),
                test_child_executable = Environment.ProcessPath, test_child_executable_sha256 = HashFile(Environment.ProcessPath!),
                test_assembly_sha256 = HashFile(typeof(NativeStartupTests).Assembly.Location),
                build_configuration = configuration, invariant_globalization = invariantGlobalization, core_library = coreLibrary, core_library_sha256 = HashFile(coreLibrary),
                native_runtime_modules = runtimeModules, resources_pri_sha256 = File.Exists(resourceMap) ? HashFile(resourceMap) : null,
                actual_main_body = true, entry_invocation = "Program.Main via reflection on a private STA desktop", actual_app = true, actual_xaml = true, release_pe_entry = false, installed_launcher = false,
                bootstrap_observed = observed.BootstrapObserved, shell_observed = observed.ShellObserved, failure_visible = observed.FailureVisible, exit_button_invoked = observed.ExitButtonInvoked,
                final_phase = observed.FinalPhase, theme = observed.Theme, entry_returned = observed.EntryReturned, private_desktop_preserved = observed.DesktopPreserved, desktop = desktopName,
                install_location = install, data_directory = Path.Combine(install, "Data"), setting_preserved = originalSetting is not null && originalSetting.SequenceEqual(File.ReadAllBytes(settingPath)),
                general_settings_mutation_scenario = generalSettingsScenario, template_settings_mutation_scenario = templateSettingsScenario,
                setting_values_preserved = originalSetting is not null, corrupt_database_preserved = corrupt,
                registry_run_preserved = true, saved_photo_count = observed.SavedPhotoCount, photo_modal_checks = observed.PhotoModalChecks,
                gallery_checks = observed.GalleryChecks,
                assembly, assembly_sha256 = HashFile(assembly), xbf_sha256 = resources, verification_complete = false };
            await File.WriteAllTextAsync(Path.Combine(root, phase.Length == 0 ? "completed.json" : $"completed-{phase}.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
        finally
        {
            if (seededDataScenario) AppDomain.CurrentDomain.FirstChanceException -= xamlFailure;
            Assert.Equal(0, RestorePredefinedKey(CurrentUserHandle, 0));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyPaths(string install)
    {
        Assert.Null(AppPaths.DataDirOverrideForTests); Assert.Equal(install, AppPaths.InstallLocation); Assert.Equal(Path.Combine(install, "Data"), AppPaths.GetDataDir());
    }

    private static void RunMainOnDesktop(string desktopName, string? theme, bool failureExpected, bool hasSavedPhoto, string kind, StartupObservation observation, TaskCompletionSource<StartupObservation> completed)
    {
        var originalDesktop = GetThreadDesktop(GetCurrentThreadId()); Exception? failure = null; App? startedApp = null;
        var timerWatch = Stopwatch.StartNew(); var observing = false; var timer = nuint.Zero;
        // Win32 timerを先に登録し、本体のApplication.Startが作るmessage loopでUIを観測する。本体の起動手順は差し替えない。
        TimerCallback callback = (_, _, _, _) =>
        {
            if (observing) return;
            observation.Step = "timer-waiting-for-app";
            try
            {
                Assert.True(timerWatch.Elapsed < TimeSpan.FromSeconds(15), "本体App／Bootstrapが起動観測可能な状態になりません。");
                // Bootstrap中もtimerは動く。専用threadの実HWND表示を先に待ち、activation前のWinUI static getterを避ける。
                if (!HasStartedNativeWindow()) { observation.Step = "waiting-for-native-window"; return; }
                startedApp = Assert.IsType<App>(Application.Current); var window = Assert.IsType<MainWindow>(App.MainWindowInstance);
                if (window.Content is not Grid host || host.Children.Count != 1 || host.Children[0] is not BootstrapPage bootstrap) { observation.Step = "waiting-for-bootstrap"; return; }
                var lifecycle = App.Services!.GetRequiredService<AppLifecycleService>();
                if ((!failureExpected && lifecycle.CurrentPhase == AppLifecyclePhase.sdkReady) || (failureExpected && Assert.IsType<StackPanel>(bootstrap.FindName("StartupErrorPanel")).Visibility != Visibility.Visible))
                { observation.Step = "waiting-for-database-initialization"; return; }
                observing = true; _ = VerifyStartedUiAsync(startedApp, window, observation, theme, failureExpected, hasSavedPhoto, kind, timerWatch).ContinueWith(task =>
                {
                    if (task.Exception is not null) { failure = task.Exception.GetBaseException(); observation.Step = "ui-verification-failed: " + failure; }
                }, TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception error) { failure = error; observing = true; observation.Step = "timer-failed: " + error; startedApp?.Exit(); }
        };
        try
        {
            observation.Step = "entering-main";
            Assert.Equal(desktopName, DesktopName(GetThreadDesktop(GetCurrentThreadId())));
            timer = SetTimer(0, 0, 50, callback); Assert.NotEqual(nuint.Zero, timer);
            var entry = typeof(App).Assembly.GetType("Alpheratz.Program")!.GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic)!;
            entry.Invoke(null, [Array.Empty<string>()]); observation.EntryReturned = true;
            Assert.Null(failure); Assert.True(observation.BootstrapObserved); Assert.True(failureExpected ? observation.ExitButtonInvoked : observation.ShellObserved);
        }
        catch (Exception error) { failure = error is TargetInvocationException { InnerException: { } inner } ? inner : error; }
        finally
        {
            if (timer != 0) KillTimer(0, timer); GC.KeepAlive(callback);
            observation.DesktopPreserved = GetThreadDesktop(GetCurrentThreadId()) == originalDesktop;
            if (failure is null) completed.TrySetResult(observation); else completed.TrySetException(failure);
        }
    }

    private static async Task VerifyStartedUiAsync(App app, MainWindow window, StartupObservation observed, string? theme, bool failureExpected, bool hasSavedPhoto, string kind, Stopwatch timer)
    {
        try
        {
            observed.Step = "bootstrap";
            Assert.Equal("Alpheratz", window.Title); Assert.True(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
            Assert.Equal(theme == "dark" ? ApplicationTheme.Dark : ApplicationTheme.Light, app.RequestedTheme); observed.Theme = app.RequestedTheme.ToString();
            var host = Assert.IsType<Grid>(window.Content); var bootstrap = Assert.IsType<BootstrapPage>(Assert.Single(host.Children)); observed.BootstrapObserved = true;
            var lifecycle = App.Services!.GetRequiredService<AppLifecycleService>();
            if (failureExpected)
            {
                await WaitForLoadedAsync(bootstrap, timer);
                Assert.Equal(AppLifecyclePhase.sdkReady, lifecycle.CurrentPhase);
                var errorPanel = Assert.IsType<StackPanel>(bootstrap.FindName("StartupErrorPanel")); Assert.Equal(Visibility.Visible, errorPanel.Visibility);
                Assert.Equal(1, Assert.IsType<StackPanel>(bootstrap.FindName("SplashContent")).Opacity);
                Assert.Equal(["Alpheratzを起動できませんでした", FailureMessage], errorPanel.Children.OfType<TextBlock>().Select(text => text.Text).ToArray());
                var exit = Assert.Single(Descendants(errorPanel).OfType<Button>()); Assert.Equal("アプリを終了", exit.Content); observed.FailureVisible = true;
                await Task.Delay(100); Assert.IsType<BootstrapPage>(Assert.Single(host.Children)); Assert.Equal(AppLifecyclePhase.sdkReady, lifecycle.CurrentPhase);
                observed.FinalPhase = lifecycle.CurrentPhase.ToString(); observed.ExitButtonInvoked = true;
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(exit) ?? new ButtonAutomationPeer(exit); Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke)).Invoke();
                return;
            }

            Assert.Equal(AppLifecyclePhase.servicesReady, lifecycle.CurrentPhase); Assert.Equal(Visibility.Collapsed, Assert.IsType<StackPanel>(bootstrap.FindName("StartupErrorPanel")).Visibility);
            observed.Step = "waiting-for-shell";
            while (host.Children.Single() is not ShellPage || lifecycle.CurrentPhase < AppLifecyclePhase.dataReady)
            {
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), $"本体Shellへ遷移しません。phase={lifecycle.CurrentPhase}, root={host.Children.Single().GetType().Name}"); await Task.Delay(50);
            }
            var shell = Assert.IsType<ShellPage>(Assert.Single(host.Children)); await WaitForLoadedAsync(shell, timer);
            // Loaded後の実queue tickで通知されるuiReadyまで待ち、初回layout前の過渡的なdataReadyを完了条件にしない。
            while (lifecycle.CurrentPhase != AppLifecyclePhase.uiReady) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "本体ShellのuiReady通知が完了しません。"); await Task.Delay(20); }
            observed.Step = "checking-shell";
            Assert.Equal(theme == "dark" ? ElementTheme.Dark : ElementTheme.Light, host.ActualTheme); Assert.Equal(host.ActualTheme, shell.ActualTheme);
            var viewModel = App.Services!.GetRequiredService<ShellViewModel>(); Assert.Same(viewModel, shell.DataContext); Assert.Single(Descendants(shell).OfType<GalleryPage>());
            Assert.Equal("", viewModel.PhotoFolderPath); Assert.Equal("", viewModel.SecondaryPhotoFolderPath); Assert.Equal("idle", viewModel.ScanStatus);
            if (hasSavedPhoto)
            {
                var photo = Assert.Single(viewModel.galleryViewModel.photosState.photos); Assert.Equal(1, viewModel.galleryViewModel.photosState.TotalCount);
                Assert.Equal(Path.Combine(AppPaths.InstallLocation!, "保存済み写真.png").Replace('\\', '/'), photo.PhotoPath); Assert.Equal("保存済み写真.png", photo.PhotoFilename);
                Assert.Equal("保存済みワールド", photo.WorldName); Assert.Equal("wrld_fixture", photo.WorldId); Assert.True(photo.IsFavorite); Assert.Equal(["保持するタグ"], photo.Tags);
                Assert.Same(photo, Assert.Single(viewModel.galleryViewModel.photosState.displayItems).Photo); observed.SavedPhotoCount = 1;
            }
            else if (IsGeneralSettingsScenario(kind)) await VerifyGeneralSettings(shell, viewModel, observed, kind);
            else if (IsTemplateSettingsScenario(kind)) await VerifyTemplateSettings(shell, viewModel, observed, kind);
            else if (IsPhotoModalScenario(kind)) await VerifyActualPhotoModal(shell, viewModel, observed, kind);
            else if (IsGalleryScenario(kind)) await VerifyActualGalleryFilters(shell, viewModel, observed);
            else Assert.Empty(viewModel.galleryViewModel.photosState.photos);
            Assert.DoesNotContain(Descendants(shell), child => child is BootstrapPage); observed.ShellObserved = true; observed.FinalPhase = lifecycle.CurrentPhase.ToString();
            observed.Step = "disposing-viewmodel"; await viewModel.DisposeAsync(); observed.Step = "closing-window"; window.Close(); app.Exit(); observed.Step = "completed";
        }
        catch { window.Close(); app.Exit(); throw; }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static bool HasStartedNativeWindow()
    {
        var found = false;
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            var title = new StringBuilder(64); GetWindowText(window, title, title.Capacity); found |= title.ToString() == "Alpheratz"; return true;
        }, 0);
        return found;
    }

    private static async Task WaitForLoadedAsync(FrameworkElement element, Stopwatch timer)
    {
        while (!element.IsLoaded) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), $"{element.GetType().Name}のLoadedが完了しません。"); await Task.Delay(20); }
    }

    private sealed class StartupObservation
    {
        public bool BootstrapObserved, ShellObserved, FailureVisible, ExitButtonInvoked, EntryReturned, DesktopPreserved;
        public string FinalPhase = "", Theme = "";
        public int SavedPhotoCount;
        public List<string> PhotoModalChecks = [];
        public List<string> GalleryChecks = [];
        public volatile string Step = "waiting-for-app";
    }

    private static string FixtureDirectory(Guid id) => Path.Combine(Path.GetTempPath(), "Alpheratz.Startup.Tests", id.ToString("N"));
    private static string RegistryPath(Guid id) => @"Software\CosmoArtsStoreTests\AlpheratzStartup\" + id.ToString("N");
    private static string HashFile(string path)
    {
        // runtime DLLは大きいため、証拠採取でUI終了後にfile全体のbyte配列を確保しない。
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static byte[] ReadSharedBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }

    private static string ReadSharedText(string path) => Encoding.UTF8.GetString(ReadSharedBytes(path));

    /// <summary>STA初期化より先にdesktopを指定し、所有するprocess treeだけをjobで回収する。</summary>
    private static async Task<(int ExitCode, string Output, string Error)> RunChildOnDesktopAsync(ProcessStartInfo start, string desktop, string root, string phase = "")
    {
        var stdoutPath = Path.Combine(root, phase.Length == 0 ? "stdout.log" : phase + ".stdout.log"); var stderrPath = Path.Combine(root, phase.Length == 0 ? "stderr.log" : phase + ".stderr.log");
        var attributes = new SecurityAttributes { Size = (uint)Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
        using var stdout = CreateFile(stdoutPath, 0x40000000, 7, ref attributes, 1, 0x80, 0);
        using var stderr = CreateFile(stderrPath, 0x40000000, 7, ref attributes, 1, 0x80, 0);
        Assert.False(stdout.IsInvalid); Assert.False(stderr.IsInvalid);
        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>(), Desktop = desktop, Flags = 0x100, StdOutput = stdout.DangerousGetHandle(), StdError = stderr.DangerousGetHandle() };
        var executable = Path.GetFullPath(start.FileName); Assert.True(File.Exists(executable));
        var command = new StringBuilder(QuoteArgument(executable) + " " + string.Join(" ", start.ArgumentList.Select(QuoteArgument)));
        var environment = string.Join('\0', start.Environment.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item => item.Key + "=" + item.Value)) + "\0\0";
        var block = Marshal.StringToHGlobalUni(environment); var process = new ProcessInformation(); var job = CreateJobObject(0, null); Assert.NotEqual(nint.Zero, job);
        try
        {
            var limits = new JobLimits(); limits.Basic.LimitFlags = 0x2000;
            Assert.True(SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<JobLimits>()));
            // 別desktopでのconsole初期化を避け、ProcessStartInfoのCreateNoWindow指定をnative起動にも反映する。
            var creationFlags = 0x404u | (start.CreateNoWindow ? 0x08000000u : 0u);
            Assert.True(CreateProcess(executable, command, 0, 0, true, creationFlags, block, AppContext.BaseDirectory, ref startup, out process), $"専用desktopのprocess起動失敗: {Marshal.GetLastWin32Error()}");
            Assert.True(AssignProcessToJobObject(job, process.Process)); Assert.NotEqual(uint.MaxValue, ResumeThread(process.Thread));
            var timer = Stopwatch.StartNew();
            while (WaitForSingleObject(process.Process, 0) == 258) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(45), "起動検証processが終了しません。"); await Task.Delay(25); }
            Assert.True(GetExitCodeProcess(process.Process, out var exitCode));
            stdout.Dispose(); stderr.Dispose();
            return (unchecked((int)exitCode), await File.ReadAllTextAsync(stdoutPath), await File.ReadAllTextAsync(stderrPath));
        }
        finally
        {
            if (process.Process != 0)
            {
                // job割当の失敗時も、作成したprocess handleだけを対象に終了させる。
                if (WaitForSingleObject(process.Process, 0) == 258) TerminateProcess(process.Process, 1);
                TerminateJobObject(job, 1); Assert.Equal(0u, WaitForSingleObject(process.Process, 5000));
                CloseHandle(process.Thread); CloseHandle(process.Process);
            }
            CloseHandle(job); Marshal.FreeHGlobal(block);
        }
    }

    private static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\""); var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes); result.Append(character); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static string FindWorkspace()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, ".claude", "audit-work", "checks"))) return directory.FullName;
        throw new InvalidOperationException("検証記録用workspaceがありません。");
    }

    private static string CurrentInputDesktopName()
    {
        var desktop = OpenInputDesktop(0, false, 1); Assert.NotEqual(nint.Zero, desktop);
        try { return DesktopName(desktop); } finally { Assert.True(CloseDesktop(desktop)); }
    }

    private static string DesktopName(nint desktop)
    {
        var name = new StringBuilder(256); Assert.True(GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _)); return name.ToString();
    }

    private static readonly nint CurrentUserHandle = unchecked((int)0x80000001);
    private delegate void TimerCallback(nint window, uint message, nuint timer, uint time);
    [return: MarshalAs(UnmanagedType.Bool)] private delegate bool WindowCallback(nint window, nint state);
    [DllImport("advapi32.dll")] private static extern int RegOverridePredefKey(nint key, SafeRegistryHandle replacement);
    [DllImport("advapi32.dll", EntryPoint = "RegOverridePredefKey")] private static extern int RestorePredefinedKey(nint key, nint replacement);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateDesktopW", SetLastError = true)] private static extern nint CreateDesktop(string name, nint device, nint mode, uint flags, uint access, nint attributes);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] private static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder buffer, int length, out int needed);
    [DllImport("user32.dll", SetLastError = true)] private static extern nuint SetTimer(nint window, nuint id, uint interval, TimerCallback callback);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool KillTimer(nint window, nuint id);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, nint state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] private static extern int GetWindowText(nint window, StringBuilder text, int capacity);
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public uint Size; public nint Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public uint Size; public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public ushort Show, ReservedSize; public nint ReservedPointer, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public nint Process, Thread; public uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicJobLimits
    {
        public long ProcessTime, JobTime; public uint LimitFlags; public nuint MinimumWorkingSet, MaximumWorkingSet; public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct JobLimits { public BasicJobLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFileW", SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, ref SecurityAttributes attributes, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcess(string application, StringBuilder command, nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateJobObjectW", SetLastError = true)] private static extern nint CreateJobObject(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(nint job, int type, ref JobLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll")] private static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(nint process, out uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(nint process, uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateJobObject(nint job, uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
