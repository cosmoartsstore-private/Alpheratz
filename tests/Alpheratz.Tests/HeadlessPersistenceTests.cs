using System.Diagnostics;
using System.Text.Json;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.Template;
using Alpheratz.Services;
using Alpheratz.Shared.Services;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>新しいprocessと接続から、設定・写真情報・backup・投稿内容を同じ値として再利用する。</summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class HeadlessPersistenceTests
{
    private const string ChildRoot = "ALPHERATZ_HEADLESS_RESTART_ROOT";

    [Fact]
    public async Task NewProcessReopensSettingsDatabaseBackupAndSamePhotoTemplateWithoutChangingSources()
    {
        if (Environment.GetEnvironmentVariable(ChildRoot) is { } childRoot)
        {
            Assert.Equal(Path.Combine(Path.GetTempPath(), "Alpheratz.HeadlessRestart.Tests"), Path.GetDirectoryName(childRoot));
            var settings = new AppConfig(Path.Combine(childRoot, "settings")).LoadSetting();
            Assert.Equal("dark", settings.ThemeMode); Assert.Equal("gallery", settings.ViewMode);
            Assert.True(settings.EnableStartup); Assert.True(settings.StartupPreferenceSet); Assert.True(settings.OpenWorldLinkOnPost);
            Assert.Equal(["本文 {world-name} {tags}", "別本文"], settings.TweetTemplates); Assert.Equal("本文 {world-name} {tags}", settings.ActiveTweetTemplate);
            var db = new AlpheratzDb(Path.Combine(childRoot, "photos.db")); db.Initialize();
            var rows = await new PhotoService(db).GetPhotosAsync(new Alpheratz.Models.PhotoQueryPayload(null, null, null, null, null, true, ["夜"], null, null, null));
            var saved = Assert.Single(rows.items); Assert.Equal("所有画像.png", saved.photo_filename); Assert.Equal("再利用ワールド", saved.world_name);
            Assert.Equal(2L, saved.source_slot); Assert.Equal(["夜"], saved.tags); Assert.True(saved.is_favorite);
            var vm = new TemplatePageViewModel(null!, null!, new ToastService()); vm.applySettings(settings.TweetTemplates, settings.ActiveTweetTemplate);
            Assert.Equal("本文 再利用ワールド #夜", vm.buildTweetText(vm.ActiveTweetTemplate, PhotoThumbnailItem.FromDto(saved)));
            var restore = await new PhotoService(db).RestorePhotoUserDataBackupAsync(); Assert.Equal(1, restore.MatchedPhotoCount);
            Assert.Equal("保持する原本byte", await File.ReadAllTextAsync(Path.Combine(childRoot, "所有画像.png")));
            await File.WriteAllTextAsync(Path.Combine(childRoot, "readback.json"), JsonSerializer.Serialize(new { verified = true, saved_photo = saved.photo_filename, active_template = vm.ActiveTweetTemplate }));
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "Alpheratz.HeadlessRestart.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "photos.db");
        try
        {
            var source = Path.Combine(root, "所有画像.png"); await File.WriteAllTextAsync(source, "保持する原本byte");
            var config = new AppConfig(Path.Combine(root, "settings")); config.SaveSetting(new AlpheratzSetting { ThemeMode = "dark", ViewMode = "gallery", EnableStartup = true, StartupPreferenceSet = true, OpenWorldLinkOnPost = true, TweetTemplates = ["本文 {world-name} {tags}", "別本文"], ActiveTweetTemplate = "本文 {world-name} {tags}" });
            var db = new AlpheratzDb(dbPath); db.Initialize();
            var path = AppPaths.NormalizePathForDb(source);
            await db.UpsertPhotoAsync(new PhotoUpsertData { PhotoPath = path, PhotoFilename = "所有画像.png", WorldName = "再利用ワールド", WorldId = "wrld_owned", Timestamp = "2026-10-20 12:34:56", SourceSlot = 2, Orientation = "landscape", ImageWidth = 100, ImageHeight = 50 });
            await db.SetPhotoFavoriteAsync(path, true); await db.AddPhotoTagAsync(path, "夜"); await new PhotoService(db).CreatePhotoUserDataBackupAsync();
            var settingsBefore = File.ReadAllBytes(Path.Combine(root, "settings", "setting.json"));
            var start = NativeDispatcherQueueTests.CreateChildStart(typeof(HeadlessPersistenceTests), nameof(NewProcessReopensSettingsDatabaseBackupAndSamePhotoTemplateWithoutChangingSources), ChildRoot, root);
            using var child = Process.Start(start)!; var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch { if (!child.HasExited) child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); throw; }
            Assert.True(child.ExitCode == 0, $"child exit={child.ExitCode}\n{await stdout}\n{await stderr}");
            using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "readback.json"))); Assert.True(receipt.RootElement.GetProperty("verified").GetBoolean());
            Assert.Equal(settingsBefore, File.ReadAllBytes(Path.Combine(root, "settings", "setting.json"))); Assert.Equal("保持する原本byte", await File.ReadAllTextAsync(source));
            var fresh = await new AlpheratzDb(dbPath).GetPhotoRecordAsync(path); Assert.NotNull(fresh); Assert.True(fresh.is_favorite); Assert.Equal(["夜"], fresh.tags);
        }
        finally
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()); SqliteConnection.ClearPool(connection);
            Assert.Equal(Path.Combine(Path.GetTempPath(), "Alpheratz.HeadlessRestart.Tests"), Path.GetDirectoryName(root)); Directory.Delete(root, recursive: true);
        }
    }
}
