using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using Alpheratz.Core;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Alpheratz.Tests;

[CollectionDefinition("Alpheratz registry child", DisableParallelization = true)]
public sealed class NativeAppConfigTestCollection { }

/// <summary>標準AppPathsと実際のRun登録を専用processへ分離し、設定の保存と失敗時の復元を検証する。</summary>
[Collection("Alpheratz registry child")]
public sealed partial class NativeAppConfigTests
{
    private const string ChildMarker = "ALPHERATZ_NATIVE_CONFIG_TEST_CHILD";
    private const string FixtureIdVariable = "ALPHERATZ_NATIVE_CONFIG_TEST_ID";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [Fact]
    public Task EnableAndDisableUpdateOnlyTheLauncherRegistrationAndKeepUnrelatedSettings() => RunIsolated(fixture =>
    {
        var config = new AppConfig();
        config.SaveSetting(new AlpheratzSetting { ThemeMode = "dark", PhotoFolderPath = "写真", TweetTemplates = ["本文"], StartupPreferenceSet = false });
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        run.SetValue("OtherApp", "%KEEP%\\other.exe", RegistryValueKind.ExpandString);
        config.SaveStartupPreference(true);
        Assert.Equal($"\"{fixture.LauncherPath}\"", run.GetValue("Alpheratz")); Assert.Equal(RegistryValueKind.String, run.GetValueKind("Alpheratz"));
        Assert.Equal((true, true), config.GetStartupPreference()); AssertRetainedSettings(config.LoadSetting());
        config.SaveStartupPreference(false); config.SaveStartupPreference(false);
        Assert.Null(run.GetValue("Alpheratz")); Assert.Equal((false, true), config.GetStartupPreference()); AssertRetainedSettings(config.LoadSetting());
        Assert.Equal("%KEEP%\\other.exe", run.GetValue("OtherApp", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        Assert.Equal(RegistryValueKind.ExpandString, run.GetValueKind("OtherApp")); Assert.Empty(Directory.GetFiles(fixture.SettingDirectory, "setting.*.tmp"));
    });

    [Fact]
    public Task CurrentRunValueDeterminesEnabledStateIndependentlyOfTheStoredFlag() => RunIsolated(fixture =>
    {
        var config = new AppConfig(); config.SaveSetting(new AlpheratzSetting { EnableStartup = true, StartupPreferenceSet = false });
        Assert.Equal((false, false), config.GetStartupPreference());
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        var values = new (object value, RegistryValueKind kind, bool enabled)[]
        {
            ($"\"{fixture.LauncherPath.ToUpperInvariant()}\"", RegistryValueKind.String, true),
            (fixture.LauncherPath, RegistryValueKind.String, false),
            ($"\"{Path.Combine(fixture.InstallDirectory, "app", "Alpheratz.Frontend.exe")}\"", RegistryValueKind.String, false),
            ($"\"{fixture.LauncherPath}\" --argument", RegistryValueKind.String, false),
            ("%ALPHERATZ_TEST_ROOT%\\Alpheratz.exe", RegistryValueKind.ExpandString, false),
            ("", RegistryValueKind.String, false), (1, RegistryValueKind.DWord, false), (new byte[] { 1, 2 }, RegistryValueKind.Binary, false),
        };
        foreach (var (value, kind, enabled) in values)
        {
            run.SetValue("Alpheratz", value, kind);
            config.SaveSetting(new AlpheratzSetting { EnableStartup = !enabled, StartupPreferenceSet = true });
            var bytes = File.ReadAllBytes(fixture.SettingPath);
            Assert.Equal((enabled, true), config.GetStartupPreference()); Assert.Equal(bytes, File.ReadAllBytes(fixture.SettingPath));
        }
    });

    [Fact]
    public Task MissingAndMalformedSettingsRetainTheCurrentRegistrationWithoutCreatingAReplacement() => RunIsolated(fixture =>
    {
        var config = new AppConfig(); using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        run.SetValue("Alpheratz", $"\"{fixture.LauncherPath}\"", RegistryValueKind.String);
        Assert.Equal((true, false), config.GetStartupPreference()); Assert.False(File.Exists(fixture.SettingPath));
        File.WriteAllText(fixture.SettingPath, "{破損"); var bytes = File.ReadAllBytes(fixture.SettingPath);
        Assert.Equal((true, false), config.GetStartupPreference()); Assert.Equal(bytes, File.ReadAllBytes(fixture.SettingPath));
        Assert.Equal($"\"{fixture.LauncherPath}\"", run.GetValue("Alpheratz"));
    });

    [Fact]
    public Task ExplicitSettingDirectoryBypassesTheRunRegistrationOnBothUpdates() => RunIsolated(fixture =>
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey); run.SetValue("Alpheratz", "%KEEP%\\old.exe", RegistryValueKind.ExpandString);
        var config = new AppConfig(Path.Combine(fixture.RootDirectory, "explicit"));
        foreach (var enabled in new[] { true, false })
        {
            config.SaveStartupPreference(enabled); Assert.Equal((enabled, true), config.GetStartupPreference());
            Assert.Equal("%KEEP%\\old.exe", run.GetValue("Alpheratz", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
            Assert.Equal(RegistryValueKind.ExpandString, run.GetValueKind("Alpheratz"));
        }
        Assert.False(File.Exists(fixture.SettingPath));
    });

    [Fact]
    public Task LockedSettingReplacementFailsBeforeChangingTheRunValueAndCanBeRetried() => RunIsolated(fixture =>
    {
        var config = new AppConfig(); config.SaveSetting(new AlpheratzSetting { EnableStartup = false, StartupPreferenceSet = false, ThemeMode = "dark" });
        using var run = Registry.CurrentUser.CreateSubKey(RunKey); run.SetValue("Alpheratz", 73, RegistryValueKind.DWord);
        var bytes = File.ReadAllBytes(fixture.SettingPath);
        using (var locked = new FileStream(fixture.SettingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() => config.SaveStartupPreference(true)); Assert.Throws<IOException>(() => config.SaveStartupPreference(false));
            Assert.Equal(73, run.GetValue("Alpheratz")); Assert.Equal(RegistryValueKind.DWord, run.GetValueKind("Alpheratz"));
        }
        Assert.Equal(bytes, File.ReadAllBytes(fixture.SettingPath)); Assert.Empty(Directory.GetFiles(fixture.SettingDirectory, "setting.*.tmp"));
        config.SaveStartupPreference(true); Assert.Equal((true, true), config.GetStartupPreference()); Assert.Equal("dark", config.LoadSetting().ThemeMode);
    });

    [Fact]
    public Task UnavailableStandardSettingDirectoryFailsBeforeChangingTheRegistration() => RunIsolated(fixture =>
    {
        var dataDirectory = Path.Combine(fixture.InstallDirectory, "Data"); Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(fixture.SettingDirectory, "保持");
        using var run = Registry.CurrentUser.CreateSubKey(RunKey); run.SetValue("Alpheratz", $"\"{fixture.LauncherPath}\"", RegistryValueKind.String);
        var config = new AppConfig(); Assert.Equal((true, false), config.GetStartupPreference());
        foreach (var enabled in new[] { true, false })
        {
            var error = Assert.Throws<InvalidOperationException>(() => config.SaveStartupPreference(enabled)); Assert.Equal("設定ファイルの保存先を取得できません", error.Message);
            Assert.Equal($"\"{fixture.LauncherPath}\"", run.GetValue("Alpheratz")); Assert.Equal("保持", File.ReadAllText(fixture.SettingDirectory));
        }
    });

    [Fact]
    public Task MissingLauncherRestoresEveryExistingValueKindWithoutSavingTheNewPreference() => RunIsolated(fixture =>
    {
        fixture.RemoveLauncher(); var config = new AppConfig();
        config.SaveSetting(new AlpheratzSetting { ThemeMode = "dark", EnableStartup = false, StartupPreferenceSet = false });
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        var values = new (object value, RegistryValueKind kind)[]
        {
            ("old.exe", RegistryValueKind.String), ("%KEEP%\\old.exe", RegistryValueKind.ExpandString),
            (new[] { "旧登録", "二行目" }, RegistryValueKind.MultiString), (new byte[] { 0, 1, 255 }, RegistryValueKind.Binary),
            (73, RegistryValueKind.DWord), (0x123456789L, RegistryValueKind.QWord),
        };
        foreach (var (value, kind) in values)
        {
            run.SetValue("Alpheratz", value, kind); var bytes = File.ReadAllBytes(fixture.SettingPath);
            var error = Assert.Throws<FileNotFoundException>(() => config.SaveStartupPreference(true));
            Assert.Equal("自動起動へ登録する Alpheratz launcher が見つかりません", error.Message);
            Assert.Equal(kind, run.GetValueKind("Alpheratz")); Assert.Equal(value, run.GetValue("Alpheratz", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
            Assert.Equal(bytes, File.ReadAllBytes(fixture.SettingPath)); Assert.Equal((false, false), config.GetStartupPreference());
            Assert.Equal("dark", config.LoadSetting().ThemeMode); Assert.Empty(Directory.GetFiles(fixture.SettingDirectory, "setting.*.tmp"));
        }
        File.WriteAllText(fixture.LauncherPath, "fixture"); config.SaveStartupPreference(true); Assert.Equal((true, true), config.GetStartupPreference());
    });

    [Fact]
    public Task MissingLauncherRestoresAnAbsentRunValueAndKeepsOtherRegistrations() => RunIsolated(fixture =>
    {
        fixture.RemoveLauncher(); var config = new AppConfig();
        Assert.Throws<FileNotFoundException>(() => config.SaveStartupPreference(true));
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true); Assert.NotNull(run); Assert.Null(run.GetValue("Alpheratz"));
        run.SetValue("OtherApp", "保持"); Assert.Throws<FileNotFoundException>(() => config.SaveStartupPreference(true));
        Assert.Null(run.GetValue("Alpheratz")); Assert.Equal("保持", run.GetValue("OtherApp")); Assert.Equal((false, false), config.GetStartupPreference());
        config.SaveStartupPreference(false); Assert.Equal((false, true), config.GetStartupPreference()); Assert.False(config.LoadSetting().EnableStartup);
    });

    [Fact]
    public Task DeniedRunUpdateReportsBothUpdateAndRestoreFailuresAndKeepsTheSavedPreference() => RunIsolated(fixture =>
    {
        var config = new AppConfig(); config.SaveSetting(new AlpheratzSetting { EnableStartup = false, StartupPreferenceSet = false, ThemeMode = "dark" });
        using var run = Registry.CurrentUser.CreateSubKey(RunKey); run.SetValue("Alpheratz", "保持", RegistryValueKind.String);
        var bytes = File.ReadAllBytes(fixture.SettingPath);
        using (var denied = new RunPermissionScope(run, deniedAccess: "0x00000002"))
        {
            var error = Assert.Throws<AggregateException>(() => config.SaveStartupPreference(true));
            Assert.Equal("自動起動設定の更新と以前の登録への復元に失敗しました", error.Message.Split(" (")[0]); Assert.Equal(2, error.InnerExceptions.Count);
            Assert.All(error.InnerExceptions, item => Assert.IsType<UnauthorizedAccessException>(item)); Assert.Equal("保持", run.GetValue("Alpheratz"));
        }
        Assert.Equal(bytes, File.ReadAllBytes(fixture.SettingPath)); Assert.Equal((false, false), config.GetStartupPreference());
        config.SaveStartupPreference(true); Assert.Equal((true, true), config.GetStartupPreference());
    });

    [Fact]
    public Task DeniedRunReadReturnsDisabledButRejectsSavingBeforeTheUpdateStage() => RunIsolated(fixture =>
    {
        var config = new AppConfig(); config.SaveSetting(new AlpheratzSetting { EnableStartup = true, StartupPreferenceSet = true });
        using var run = Registry.CurrentUser.CreateSubKey(RunKey); run.SetValue("Alpheratz", $"\"{fixture.LauncherPath}\"", RegistryValueKind.String);
        var bytes = File.ReadAllBytes(fixture.SettingPath);
        using (var denied = new RunPermissionScope(run, deniedAccess: "0x00000001"))
        {
            Assert.Equal((false, true), config.GetStartupPreference()); Assert.Throws<SecurityException>(() => config.SaveStartupPreference(false));
            Assert.Equal($"\"{fixture.LauncherPath}\"", run.GetValue("Alpheratz"));
        }
        Assert.Equal(bytes, File.ReadAllBytes(fixture.SettingPath)); Assert.Equal((true, true), config.GetStartupPreference());
        config.SaveStartupPreference(false); Assert.Equal((false, true), config.GetStartupPreference());
    });

    [Fact]
    public Task PolarisArchiveResolutionRequiresAnExistingArchiveAndNeverCreatesIt() => RunIsolated(fixture =>
    {
        Assert.Null(AppPaths.GetPolarisArchiveDir());
        using var polaris = Registry.CurrentUser.CreateSubKey(@"Software\CosmoArtsStore\Polaris");
        var install = Path.Combine(fixture.RootDirectory, "Polaris"); var archive = Path.Combine(install, "Data", "archive");
        foreach (var value in new[] { "", " ", install })
        {
            polaris.SetValue("InstallLocation", value); Assert.Null(AppPaths.GetPolarisArchiveDir()); Assert.False(Directory.Exists(archive));
        }
        var legacyArchive = Path.Combine(install, "archive"); Directory.CreateDirectory(legacyArchive);
        var legacyLog = Path.Combine(legacyArchive, "output_log_legacy.txt"); File.WriteAllText(legacyLog, "保持する旧配置の内容");
        Assert.Null(AppPaths.GetPolarisArchiveDir()); Assert.False(Directory.Exists(archive));
        Directory.CreateDirectory(archive); polaris.SetValue("InstallLocation", install); Assert.Equal(archive, AppPaths.GetPolarisArchiveDir());
        Assert.Equal("保持する旧配置の内容", File.ReadAllText(legacyLog));
        polaris.SetValue("InstallLocation", 73, RegistryValueKind.DWord); Assert.Null(AppPaths.GetPolarisArchiveDir()); Assert.True(Directory.Exists(archive));
    });

    private static void AssertRetainedSettings(AlpheratzSetting setting)
    {
        Assert.Equal("dark", setting.ThemeMode); Assert.Equal("写真", setting.PhotoFolderPath); Assert.Equal(["本文"], setting.TweetTemplates);
    }

    private static Task RunIsolated(Action<RegistryFixture> verify, [CallerMemberName] string testName = "")
        => RunIsolatedAsync(fixture => { verify(fixture); return Task.CompletedTask; }, testName);

    private static async Task RunIsolatedAsync(Func<RegistryFixture, Task> verify, [CallerMemberName] string testName = "")
    {
        if (Environment.GetEnvironmentVariable(ChildMarker) == testName)
        {
            var id = Guid.ParseExact(Environment.GetEnvironmentVariable(FixtureIdVariable)!, "N");
            using (var fixture = new RegistryFixture(id)) await verify(fixture);
            await File.WriteAllTextAsync(Path.Combine(GetFixtureDirectory(id), "completed.receipt"), "completed"); return;
        }

        var fixtureId = Guid.NewGuid(); var directory = GetFixtureDirectory(fixtureId); Directory.CreateDirectory(directory);
        var receiptPath = Path.Combine(directory, "completed.receipt");
        var start = NativeDispatcherQueueTests.CreateChildStart(typeof(NativeAppConfigTests), testName, ChildMarker, testName);
        start.Environment[FixtureIdVariable] = fixtureId.ToString("N"); start.Environment["ALPHERATZ_VERBOSE_LOGS"] = "0";
        try
        {
            using var child = Process.Start(start) ?? throw new InvalidOperationException("専用test processを起動できません。");
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
            catch { if (!child.HasExited) child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); throw; }
            var output = await stdout; var error = await stderr;
            Assert.True(child.ExitCode == 0, $"専用processの終了code={child.ExitCode}\n{output}\n{error}");
            Assert.True(File.Exists(receiptPath), $"専用testの完了記録がありません。\n{output}\n{error}"); Assert.Equal("completed", await File.ReadAllTextAsync(receiptPath));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(GetRegistryPath(fixtureId), throwOnMissingSubKey: false);
            Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Alpheratz.Registry.Tests")) + Path.DirectorySeparatorChar, Path.GetFullPath(directory));
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string GetFixtureDirectory(Guid id) => Path.Combine(Path.GetTempPath(), "Alpheratz.Registry.Tests", id.ToString("N"));
    private static string GetRegistryPath(Guid id) => @"Software\CosmoArtsStoreTests\Alpheratz\" + id.ToString("N");

    /// <summary>HKCUの差替えを検証してからproductionを呼び、差替え解除後に所有するkeyだけを削除する。</summary>
    private sealed class RegistryFixture : IDisposable
    {
        private static readonly nint CurrentUserHandle = unchecked((int)0x80000001);
        private readonly Guid id;
        private readonly RegistryKey isolated;
        private bool mapped;
        public string RootDirectory { get; }
        public string InstallDirectory { get; }
        public string LauncherPath => Path.Combine(InstallDirectory, "Alpheratz.exe");
        public string SettingDirectory => Path.Combine(InstallDirectory, "Data", "db");
        public string SettingPath => Path.Combine(SettingDirectory, "setting.json");

        public RegistryFixture(Guid id)
        {
            this.id = id; RootDirectory = GetFixtureDirectory(id); InstallDirectory = Path.Combine(RootDirectory, "Alpheratz 起動fixture"); Directory.CreateDirectory(InstallDirectory);
            isolated = Registry.CurrentUser.CreateSubKey(GetRegistryPath(id)); isolated.SetValue("FixtureId", id.ToString("N"));
            try
            {
                using (var install = isolated.CreateSubKey(@"Software\CosmoArtsStore\Alpheratz")) install.SetValue("InstallLocation", InstallDirectory);
                File.WriteAllText(LauncherPath, "fixture");
                var mappingStatus = RegOverridePredefKey(CurrentUserHandle, isolated.Handle);
                Assert.True(mappingStatus == 0, $"HKCUの差替え失敗: status={mappingStatus}, predefined=0x{CurrentUserHandle:X}, isolated=0x{isolated.Handle.DangerousGetHandle():X}"); mapped = true;
                Assert.Equal(id.ToString("N"), Registry.CurrentUser.GetValue("FixtureId")); VerifyAppPaths();
            }
            catch { Dispose(); throw; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void VerifyAppPaths()
        {
            Assert.Null(AppPaths.DataDirOverrideForTests); Assert.Equal(InstallDirectory, AppPaths.InstallLocation);
        }

        public void RemoveLauncher()
        {
            // 開発環境の隣接launcherが偶然選ばれると、欠落caseを検証できないため事前に確認する。
            var runtime = Path.GetDirectoryName(Environment.ProcessPath)!;
            var install = string.Equals(Path.GetFileName(runtime), "app", StringComparison.OrdinalIgnoreCase) ? Directory.GetParent(runtime)!.FullName : runtime;
            Assert.False(File.Exists(Path.Combine(install, "Alpheratz.exe"))); File.Delete(LauncherPath);
        }

        public void Dispose()
        {
            if (mapped) { Assert.Equal(0, RestorePredefinedKey(CurrentUserHandle, 0)); mapped = false; }
            isolated.Dispose(); Registry.CurrentUser.DeleteSubKeyTree(GetRegistryPath(id), throwOnMissingSubKey: false);
        }

        [DllImport("advapi32.dll")] private static extern int RegOverridePredefKey(nint key, SafeRegistryHandle replacement);
        [DllImport("advapi32.dll", EntryPoint = "RegOverridePredefKey")] private static extern int RestorePredefinedKey(nint key, nint replacement);
    }

    /// <summary>一時Run keyだけのaccessを拒否し、保持済みhandleで元のDACLへ必ず戻す。</summary>
    private sealed class RunPermissionScope : IDisposable
    {
        private readonly SafeRegistryHandle handle;
        private readonly byte[] original = [];

        public RunPermissionScope(RegistryKey run, string deniedAccess)
        {
            Assert.Equal(0, RegOpenKeyEx(run.Handle, null, 0, 0xf003f, out handle));
            try
            {
                uint length = 0; Assert.Equal(122, RegGetKeySecurity(handle, 4, null, ref length)); var saved = new byte[length];
                Assert.Equal(0, RegGetKeySecurity(handle, 4, saved, ref length)); original = saved;
                using var identity = WindowsIdentity.GetCurrent(); var sid = identity.User!.Value;
                Assert.True(ConvertStringSecurityDescriptorToSecurityDescriptor($"D:P(D;;{deniedAccess};;;{sid})(A;;KA;;;{sid})", 1, out var descriptor, out _));
                try { Assert.Equal(0, SetKeySecurity(handle, 4, descriptor)); }
                finally { LocalFree(descriptor); }
            }
            catch
            {
                // constructor中の失敗ではusingのDisposeが呼ばれないため、ここでもDACLとhandleを戻す。
                try { if (original.Length > 0) Assert.Equal(0, RestoreKeySecurity(handle, 4, original)); }
                finally { handle.Dispose(); }
                throw;
            }
        }

        public void Dispose()
        {
            try { Assert.Equal(0, RestoreKeySecurity(handle, 4, original)); }
            finally { handle.Dispose(); }
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")] private static extern int RegOpenKeyEx(SafeRegistryHandle key, string? subKey, uint options, uint access, out SafeRegistryHandle result);
        [DllImport("advapi32.dll")] private static extern int RegGetKeySecurity(SafeRegistryHandle key, uint information, byte[]? descriptor, ref uint length);
        [DllImport("advapi32.dll", EntryPoint = "RegSetKeySecurity")] private static extern int SetKeySecurity(SafeRegistryHandle key, uint information, nint descriptor);
        [DllImport("advapi32.dll", EntryPoint = "RegSetKeySecurity")] private static extern int RestoreKeySecurity(SafeRegistryHandle key, uint information, byte[] descriptor);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string descriptor, uint revision, out nint result, out uint size);
        [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    }
}
