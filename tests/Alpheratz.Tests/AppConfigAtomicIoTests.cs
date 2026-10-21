using Alpheratz.Core;

namespace Alpheratz.Tests;

/// <summary>実際の設定fileで、置換失敗時の既存値保護と同時読書きの整合を確認する。</summary>
public sealed class AppConfigAtomicIoTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Alpheratz.ConfigIo.Tests", Guid.NewGuid().ToString("N"));
    private string SettingPath => Path.Combine(directory, "setting.json");

    public AppConfigAtomicIoTests() { Directory.CreateDirectory(directory); }
    public void Dispose()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Alpheratz.ConfigIo.Tests")) + Path.DirectorySeparatorChar, Path.GetFullPath(directory));
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void NullJsonReturnsDefaultsWithoutReplacingTheOriginalFile()
    {
        File.WriteAllText(SettingPath, "null"); var setting = new AppConfig(directory).LoadSetting();
        Assert.Equal("light", setting.ThemeMode); Assert.Equal("standard", setting.ViewMode); Assert.Equal("null", File.ReadAllText(SettingPath));
    }

    [Fact]
    public void LockedReadReturnsDefaultsWithoutDestroyingSavedSettings()
    {
        var config = new AppConfig(directory); config.SaveSetting(new AlpheratzSetting { ThemeMode = "dark" }); var bytes = File.ReadAllBytes(SettingPath);
        using (var locked = new FileStream(SettingPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal("light", config.LoadSetting().ThemeMode);
        Assert.Equal(bytes, File.ReadAllBytes(SettingPath)); Assert.Equal("dark", config.LoadSetting().ThemeMode);
    }

    [Fact]
    public void LockedReplacementPreservesPreviousJsonAndDeletesOnlyTheTemporaryFile()
    {
        var config = new AppConfig(directory); config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = "旧設定" }); var previous = File.ReadAllBytes(SettingPath);
        using (var locked = new FileStream(SettingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<IOException>(() => config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = "新設定" }));
        Assert.Equal(previous, File.ReadAllBytes(SettingPath)); Assert.Empty(Directory.GetFiles(directory, "setting.*.tmp"));
        config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = "再試行" }); Assert.Equal("再試行", config.LoadSetting().PhotoFolderPath);
    }

    [Fact]
    public void DirectoryAtDestinationIsNotReplacedOrDeleted()
    {
        Directory.CreateDirectory(SettingPath); var owned = Path.Combine(SettingPath, "retain.txt"); File.WriteAllText(owned, "保持");
        Assert.Throws<IOException>(() => new AppConfig(directory).SaveSetting(new AlpheratzSetting()));
        Assert.Equal("保持", File.ReadAllText(owned)); Assert.Empty(Directory.GetFiles(directory, "setting.*.tmp"));
    }

    [Fact]
    public async Task ConcurrentReadsObserveACompleteSettingAndLeaveNoTemporaryFiles()
    {
        var config = new AppConfig(directory); config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = "0", SecondaryPhotoFolderPath = "0" });
        await Task.WhenAll(Enumerable.Range(1, 40).Select(index => Task.Run(() =>
        {
            config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = index.ToString(), SecondaryPhotoFolderPath = index.ToString(), TweetTemplates = ["日本語の本文"] });
            var setting = config.LoadSetting(); Assert.Equal(setting.PhotoFolderPath, setting.SecondaryPhotoFolderPath); Assert.Equal(["日本語の本文"], setting.TweetTemplates);
        })));
        Assert.Equal(config.LoadSetting().PhotoFolderPath, config.LoadSetting().SecondaryPhotoFolderPath); Assert.Empty(Directory.GetFiles(directory, "setting.*.tmp"));
        Assert.False(File.ReadAllBytes(SettingPath).AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
    }
}
