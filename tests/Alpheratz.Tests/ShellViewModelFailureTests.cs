using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Models;
using Alpheratz.Models.Events;
using Alpheratz.Shared.Models;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>設定保存と旧データ整理の失敗を、保存内容・公開state・通知・再試行へ対応付ける。</summary>
public sealed partial class ShellViewModelBehaviorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartupPreferenceSuccessCommitsBothViewModelsAndReportsTheSelectedValue(bool enabled)
    {
        await using var harness = ShellTestHarness.Create();
        harness.Config.SaveSetting(new AlpheratzSetting { EnableStartup = !enabled, StartupPreferenceSet = false, ThemeMode = "dark" }); await harness.ViewModel.refreshSettings();
        harness.ViewModel.settingsViewModel.StartupEnabled = enabled;
        await harness.ViewModel.handleStartupPreference(enabled);
        Assert.Equal(enabled, harness.ViewModel.StartupEnabled); Assert.Equal(enabled, harness.ViewModel.settingsViewModel.StartupEnabled);
        Assert.Equal((enabled, true), harness.Config.GetStartupPreference()); Assert.Equal("dark", harness.Config.LoadSetting().ThemeMode);
        AssertShellToast(harness, enabled ? "Alpheratzをログイン時に起動するよう設定しました。" : "Alpheratzのログイン時起動を無効にしました。", ToastType.info);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartupPreferenceSaveFailureRestoresTheOptimisticSettingsValueAndKeepsStorage(bool enabled)
    {
        await using var harness = ShellTestHarness.Create();
        harness.Config.SaveSetting(new AlpheratzSetting { EnableStartup = !enabled, StartupPreferenceSet = true }); await harness.ViewModel.refreshSettings();
        harness.ViewModel.settingsViewModel.StartupEnabled = enabled; var bytes = File.ReadAllBytes(harness.SettingPath);
        using (var locked = new FileStream(harness.SettingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await harness.ViewModel.handleStartupPreference(enabled);
        Assert.Equal(!enabled, harness.ViewModel.StartupEnabled); Assert.Equal(!enabled, harness.ViewModel.settingsViewModel.StartupEnabled);
        Assert.Equal(bytes, File.ReadAllBytes(harness.SettingPath)); Assert.Equal((!enabled, true), harness.Config.GetStartupPreference());
        AssertShellToast(harness, "ログイン時起動の設定を変更できませんでした。設定をもう一度切り替えてください。", ToastType.error);
        harness.ToastService.toasts.Clear(); await harness.ViewModel.handleStartupPreference(enabled);
        Assert.Equal(enabled, harness.ViewModel.StartupEnabled); Assert.Equal(enabled, harness.ViewModel.settingsViewModel.StartupEnabled); Assert.Equal((enabled, true), harness.Config.GetStartupPreference());
    }

    [Theory]
    [InlineData(ThemeMode.light, ThemeMode.dark)]
    [InlineData(ThemeMode.dark, ThemeMode.light)]
    public async Task ThemeSaveFailurePreservesBothViewModelsAndSupportsRetry(ThemeMode previous, ThemeMode next)
    {
        await using var harness = ShellTestHarness.Create(); harness.Config.SaveSetting(new AlpheratzSetting { ThemeMode = previous.ToString() }); await harness.ViewModel.refreshSettings();
        var bytes = File.ReadAllBytes(harness.SettingPath);
        using (var locked = new FileStream(harness.SettingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(await harness.ViewModel.handleThemeChange(next));
        Assert.Equal(previous, harness.ViewModel.ThemeMode); Assert.Equal(previous, harness.ViewModel.settingsViewModel.ThemeMode); Assert.Equal(bytes, File.ReadAllBytes(harness.SettingPath));
        AssertShellToast(harness, "テーマを変更できませんでした。テーマをもう一度切り替えてください。", ToastType.error);
        harness.ToastService.toasts.Clear(); Assert.True(await harness.ViewModel.handleThemeChange(next));
        Assert.Equal(next, harness.ViewModel.ThemeMode); Assert.Equal(next, harness.ViewModel.settingsViewModel.ThemeMode); Assert.Equal(next.ToString(), harness.Config.LoadSetting().ThemeMode);
    }

    [Theory]
    [InlineData(ViewMode.standard, ViewMode.gallery)]
    [InlineData(ViewMode.gallery, ViewMode.standard)]
    public async Task ViewModeSaveFailureKeepsDisplayAndGroupingUntilRetry(ViewMode previous, ViewMode next)
    {
        await using var harness = ShellTestHarness.Create(); harness.Config.SaveSetting(new AlpheratzSetting { ViewMode = previous.ToString() }); await harness.ViewModel.refreshSettings();
        harness.ViewModel.galleryViewModel.filtersState.GroupingMode = GroupingMode.world; var bytes = File.ReadAllBytes(harness.SettingPath);
        using (var locked = new FileStream(harness.SettingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await harness.ViewModel.handleSetViewMode(next);
        Assert.Equal(previous, harness.ViewModel.ViewMode); Assert.Equal(previous, harness.ViewModel.galleryViewModel.displayState.ViewMode);
        Assert.Equal(GroupingMode.world, harness.ViewModel.galleryViewModel.filtersState.GroupingMode); Assert.Equal(bytes, File.ReadAllBytes(harness.SettingPath));
        AssertShellToast(harness, "写真の表示方法を変更できませんでした。表示方法をもう一度切り替えてください。", ToastType.error);
        harness.ToastService.toasts.Clear(); await harness.ViewModel.handleSetViewMode(next);
        Assert.Equal(next, harness.ViewModel.ViewMode); Assert.Equal(next, harness.ViewModel.galleryViewModel.displayState.ViewMode); Assert.Equal(next.ToString(), harness.Config.LoadSetting().ViewMode);
        Assert.Equal(next == ViewMode.gallery ? GroupingMode.none : GroupingMode.world, harness.ViewModel.galleryViewModel.filtersState.GroupingMode);
    }

    /// <summary>保存失敗時はTwoWay binding相当の先行値を保存済みの値へ戻し、同じ操作で再試行できる。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WorldLinkSaveFailureRestoresCommittedValuesAndSupportsTheSameToggleRetry(bool enabled)
    {
        await using var harness = ShellTestHarness.Create(); harness.Config.SaveSetting(new AlpheratzSetting { OpenWorldLinkOnPost = !enabled }); await harness.ViewModel.refreshSettings();
        harness.ViewModel.settingsViewModel.OpenWorldLinkOnPost = enabled; var bytes = File.ReadAllBytes(harness.SettingPath);
        using (var locked = new FileStream(harness.SettingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await harness.ViewModel.handleOpenWorldOnPostPreference(enabled);
        Assert.Equal(!enabled, harness.ViewModel.OpenWorldLinkOnPost); Assert.Equal(!enabled, harness.ViewModel.templatePageViewModel.OpenWorldLinkOnPost);
        Assert.Equal(!enabled, harness.ViewModel.settingsViewModel.OpenWorldLinkOnPost); Assert.Equal(bytes, File.ReadAllBytes(harness.SettingPath));
        AssertShellToast(harness, "投稿時のワールドリンク設定を変更できませんでした。設定をもう一度切り替えてください。", ToastType.error);
        harness.ToastService.toasts.Clear();
        harness.ViewModel.settingsViewModel.OpenWorldLinkOnPost = enabled; await harness.ViewModel.handleOpenWorldOnPostPreference(enabled);
        Assert.Equal(enabled, harness.Config.LoadSetting().OpenWorldLinkOnPost); Assert.Equal(enabled, harness.ViewModel.OpenWorldLinkOnPost); Assert.Equal(enabled, harness.ViewModel.templatePageViewModel.OpenWorldLinkOnPost); Assert.Equal(enabled, harness.ViewModel.settingsViewModel.OpenWorldLinkOnPost);
    }

    [Theory]
    [InlineData(0, "pending")]
    [InlineData(3, "pending")]
    [InlineData(1, "")]
    [InlineData(2, " ")]
    public async Task InvalidCleanupMarkerBlocksLoadingAndScanningWithoutDeletingEitherSlot(int slot, string operation)
    {
        await using var harness = ShellTestHarness.Create();
        harness.Config.SaveSetting(new AlpheratzSetting { PendingFolderCleanup = new PendingFolderCleanupSetting { SourceSlot = slot, OperationId = operation, CommittedPath = "" } });
        using var firstCache = new ShellCacheFixture(1); using var secondCache = new ShellCacheFixture(2);
        await SeedShellPhoto(harness, 1); await SeedShellPhoto(harness, 2); var bytes = File.ReadAllBytes(harness.SettingPath); var progressCount = 0;
        await using var progress = harness.EventBus.Subscribe<ScanProgressDto>(EventNames.ScanProgress, _ => { progressCount++; return Task.CompletedTask; });
        await harness.ViewModel.initialize();
        Assert.Equal("error", harness.ViewModel.ScanStatus); Assert.Empty(harness.ViewModel.galleryViewModel.photosState.photos); Assert.Equal(bytes, File.ReadAllBytes(harness.SettingPath));
        Assert.NotNull(await harness.Db.GetPhotoRecordAsync("/slot1/retain.jpg")); Assert.NotNull(await harness.Db.GetPhotoRecordAsync("/slot2/retain.jpg"));
        Assert.Equal("保持", File.ReadAllText(firstCache.Path)); Assert.Equal("保持", File.ReadAllText(secondCache.Path));
        AssertShellToast(harness, "写真フォルダの設定は保存されましたが、以前の写真情報を整理できませんでした。次回の起動時に再試行します。", ToastType.error);
        harness.ToastService.toasts.Clear(); await harness.ViewModel.startScan(); Assert.Equal(0, progressCount); Assert.Equal("error", harness.ViewModel.ScanStatus);
        Assert.Equal(bytes, File.ReadAllBytes(harness.SettingPath));
        AssertShellToast(harness, "写真フォルダの変更処理が完了していないため、写真の読み込みを保留しています。アプリを再起動すると再試行します。", ToastType.info);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FolderSaveFailureKeepsBothSlotsAndClearsTheFailedConfirmation(int slot)
    {
        await using var harness = ShellTestHarness.Create(); var oldFolder = Path.Combine(harness.TempDir, "old"); var newFolder = Path.Combine(harness.TempDir, "new");
        Directory.CreateDirectory(oldFolder); Directory.CreateDirectory(newFolder); SaveShellFolder(harness, slot, oldFolder); await harness.ViewModel.refreshSettings();
        await SeedShellPhoto(harness, 1); await SeedShellPhoto(harness, 2); harness.ViewModel.promptFolderChange(slot, newFolder); var bytes = File.ReadAllBytes(harness.SettingPath);
        using (var locked = new FileStream(harness.SettingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await harness.ViewModel.applyFolderChange(slot, newFolder);
        Assert.Equal(bytes, File.ReadAllBytes(harness.SettingPath)); Assert.Null(harness.Config.LoadSetting().PendingFolderCleanup); Assert.Equal(oldFolder, GetShellFolder(harness, slot));
        Assert.NotNull(await harness.Db.GetPhotoRecordAsync("/slot1/retain.jpg")); Assert.NotNull(await harness.Db.GetPhotoRecordAsync("/slot2/retain.jpg"));
        Assert.False(harness.ViewModel.IsApplyingFolderChange); Assert.Null(harness.ViewModel.PendingFolderPath); Assert.Equal("idle", harness.ViewModel.ScanStatus);
        AssertShellToast(harness, "写真フォルダを変更できませんでした。設定で写真フォルダを選び直してください。", ToastType.error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FolderCleanupFailureKeepsTheCommittedRequestAndRollsBackDeletionUntilRetry(int slot)
    {
        await using var harness = ShellTestHarness.Create(); var oldFolder = Path.Combine(harness.TempDir, "old"); var newFolder = Path.Combine(harness.TempDir, "new");
        Directory.CreateDirectory(oldFolder); Directory.CreateDirectory(newFolder); SaveShellFolder(harness, slot, oldFolder); await harness.ViewModel.refreshSettings();
        await SeedShellPhoto(harness, 1); await SeedShellPhoto(harness, 2); var path = $"/slot{slot}/retain.jpg"; await harness.Db.AddPhotoTagAsync(path, "保持");
        using var cache = new ShellCacheFixture(slot);
        ExecuteShellSql(harness, "CREATE TRIGGER reject_photo_delete BEFORE DELETE ON photos BEGIN SELECT RAISE(ABORT, '検証用の削除失敗'); END;");
        harness.ViewModel.promptFolderChange(slot, newFolder); await harness.ViewModel.applyFolderChange(slot, newFolder);
        var saved = harness.Config.LoadSetting(); var cleanup = Assert.IsType<PendingFolderCleanupSetting>(saved.PendingFolderCleanup);
        Assert.Equal(slot, cleanup.SourceSlot); Assert.False(string.IsNullOrWhiteSpace(cleanup.OperationId)); Assert.Equal(newFolder, cleanup.CommittedPath); Assert.Equal(newFolder, GetShellFolder(harness, slot));
        Assert.NotNull(await harness.Db.GetPhotoRecordAsync(path)); Assert.Equal(["保持"], await harness.Db.GetPhotoTagsAsync(path));
        Assert.Equal("保持", File.ReadAllText(cache.Path));
        Assert.NotNull(await harness.Db.GetPhotoRecordAsync($"/slot{3 - slot}/retain.jpg")); Assert.False(harness.ViewModel.IsApplyingFolderChange); Assert.Null(harness.ViewModel.PendingFolderPath);
        AssertShellToast(harness, "写真フォルダの設定は保存されましたが、以前の写真情報を整理できませんでした。次回の起動時に再試行します。", ToastType.error);
        harness.ToastService.toasts.Clear(); await harness.ViewModel.startScan(); Assert.Equal("idle", harness.ViewModel.ScanStatus); Assert.NotNull(harness.Config.LoadSetting().PendingFolderCleanup);
        ExecuteShellSql(harness, "DROP TRIGGER reject_photo_delete;"); harness.ToastService.toasts.Clear();
        await harness.ViewModel.applyFolderChange(slot, newFolder); await harness.ViewModel.DisposeAsync();
        Assert.Null(harness.Config.LoadSetting().PendingFolderCleanup); Assert.Null(await harness.Db.GetPhotoRecordAsync(path)); Assert.Empty(await harness.Db.GetPhotoTagsAsync(path));
        Assert.False(File.Exists(cache.Path));
        Assert.NotNull(await harness.Db.GetPhotoRecordAsync($"/slot{3 - slot}/retain.jpg")); Assert.Equal(["保持"], await harness.Db.GetAllTagsAsync());
        Assert.False(harness.ViewModel.IsApplyingFolderChange); Assert.Equal(newFolder, GetShellFolder(harness, slot));
        Assert.Contains(harness.ToastService.toasts, toast => toast.Msg == "以前の写真情報を整理しました。" && toast.Type == ToastType.info);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StaleResetConfirmationCannotClearTheNewFolderOrDeleteItsMetadata(int slot)
    {
        await using var harness = ShellTestHarness.Create(); var oldFolder = Path.Combine(harness.TempDir, "old"); var newFolder = Path.Combine(harness.TempDir, "new");
        Directory.CreateDirectory(oldFolder); Directory.CreateDirectory(newFolder); SaveShellFolder(harness, slot, oldFolder); await harness.ViewModel.refreshSettings();
        harness.ViewModel.handleResetFolder(slot); SaveShellFolder(harness, slot, newFolder); await harness.ViewModel.refreshSettings();
        await SeedShellPhoto(harness, slot); var bytes = File.ReadAllBytes(harness.SettingPath);
        await harness.ViewModel.executeResetFolder(slot, expectedPath: oldFolder);
        Assert.Equal(newFolder, GetShellFolder(harness, slot)); Assert.Equal(bytes, File.ReadAllBytes(harness.SettingPath)); Assert.NotNull(await harness.Db.GetPhotoRecordAsync($"/slot{slot}/retain.jpg"));
        Assert.Null(harness.Config.LoadSetting().PendingFolderCleanup); Assert.Null(harness.ViewModel.PendingResetRequest); Assert.False(harness.ViewModel.IsApplyingFolderChange); Assert.Empty(harness.ToastService.toasts);
    }

    private static void AssertShellToast(ShellTestHarness harness, string message, ToastType type)
    {
        var toast = Assert.Single(harness.ToastService.toasts); Assert.Equal(message, toast.Msg); Assert.Equal(type, toast.Type);
    }

    private static Task SeedShellPhoto(ShellTestHarness harness, int slot) => harness.Db.UpsertPhotoAsync(new PhotoUpsertData
    {
        PhotoPath = $"/slot{slot}/retain.jpg", PhotoFilename = "retain.jpg", Timestamp = "2026-06-05 10:00:00", SourceSlot = slot,
    });

    private static void SaveShellFolder(ShellTestHarness harness, int slot, string path) => harness.Config.SaveSetting(new AlpheratzSetting
    {
        PhotoFolderPath = slot == 1 ? path : "", SecondaryPhotoFolderPath = slot == 2 ? path : "",
    });

    private static string GetShellFolder(ShellTestHarness harness, int slot) => slot == 1 ? harness.ViewModel.PhotoFolderPath : harness.ViewModel.SecondaryPhotoFolderPath;

    private static void ExecuteShellSql(ShellTestHarness harness, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = harness.DbPath }.ToString()); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    private sealed class ShellCacheFixture : IDisposable
    {
        public string Path { get; }
        public ShellCacheFixture(int slot)
        {
            var data = Assert.IsType<string>(AppPaths.DataDirOverrideForTests);
            Assert.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Alpheratz.AppPaths.Tests")) + System.IO.Path.DirectorySeparatorChar, System.IO.Path.GetFullPath(data));
            var cache = AppPaths.GetImgCacheDir(slot)!; Path = System.IO.Path.Combine(cache, $"shell-{Guid.NewGuid():N}.fixture");
            Assert.StartsWith(System.IO.Path.GetFullPath(data) + System.IO.Path.DirectorySeparatorChar, System.IO.Path.GetFullPath(Path)); File.WriteAllText(Path, "保持");
        }
        public void Dispose() => File.Delete(Path);
    }
}
