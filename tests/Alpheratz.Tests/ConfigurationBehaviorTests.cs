using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;

namespace Alpheratz.Tests;

/// <summary>
/// 設定ファイルと SettingsService の保存・読込境界を検証するテスト。
///
/// 設定は起動直後のテーマ、写真フォルダ、表示モード、投稿テンプレートに影響するため、
/// 破損ファイルや部分更新の扱いが不安定だとアプリ全体の初期状態が揺れる。
/// ここでは AppConfig にテスト専用ディレクトリを注入し、実ユーザー設定や Windows の Run レジストリには触れずに
/// JSON 保存、既定値フォールバック、DTO 変換、部分更新の仕様を固定する。
/// </summary>
public sealed class ConfigurationBehaviorTests : IDisposable
{
    private readonly string tempDir;

    /// <summary>
    /// 設定ファイル保存先として使う一時ディレクトリを用意する。
    /// AppConfig の通常実行パスは AppPaths に委ねるが、テストでは明示ディレクトリを渡して
    /// 開発者の実設定を読み書きしないようにする。
    /// </summary>
    public ConfigurationBehaviorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.Configuration.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    /// <summary>
    /// テスト用の設定ディレクトリを削除する。
    /// 後片付け失敗は本体仕様ではないため、アサーション結果を優先する。
    /// </summary>
    public void Dispose()
    {
        try { Directory.Delete(tempDir, recursive: true); }
        catch { }
    }

    /// <summary>
    /// 設定ファイルが存在しない場合と壊れている場合に既定値へ戻ることを確認する。
    ///
    /// 設定 JSON はユーザー編集やクラッシュ中の書き込みで壊れる可能性がある。
    /// 起動時に例外で停止すると復旧できないため、LoadSetting は警告ログだけ残して
    /// AlpheratzSetting の既定値を返す現在仕様を固定する。
    /// </summary>
    [Fact]
    public void AppConfig_LoadSetting_ReturnsDefaultsWhenFileIsMissingOrInvalid()
    {
        var config = new AppConfig(tempDir);

        var missing = config.LoadSetting();
        File.WriteAllText(Path.Combine(tempDir, "setting.json"), "{ invalid json");
        var invalid = config.LoadSetting();

        Assert.Equal("light", missing.ThemeMode);
        Assert.Equal("standard", missing.ViewMode);
        Assert.Equal("light", invalid.ThemeMode);
        Assert.Equal("standard", invalid.ViewMode);
        Assert.NotEmpty(invalid.TweetTemplates);
    }

    /// <summary>
    /// AppConfig.SaveSetting が設定値を JSON として保存し、別インスタンスから読み直せることを確認する。
    ///
    /// AppConfig は lock で読み書きを直列化する薄いストアだが、保存先ディレクトリ作成と UTF-8 JSON 出力も担う。
    /// 写真フォルダ、テーマ、表示モード、投稿テンプレート、アクティブテンプレートが失われないことを
    /// 別の AppConfig インスタンスで読み直して検証する。
    /// </summary>
    [Fact]
    public void AppConfig_SaveSetting_PersistsSettingJson()
    {
        var config = new AppConfig(tempDir);
        config.SaveSetting(new AlpheratzSetting
        {
            PhotoFolderPath = "F:/photos/primary",
            SecondaryPhotoFolderPath = "F:/photos/secondary",
            ThemeMode = "dark",
            ViewMode = "gallery",
            TweetTemplates = ["one", "two"],
            ActiveTweetTemplate = "two",
        });

        var reloaded = new AppConfig(tempDir).LoadSetting();

        Assert.Equal("F:/photos/primary", reloaded.PhotoFolderPath);
        Assert.Equal("F:/photos/secondary", reloaded.SecondaryPhotoFolderPath);
        Assert.Equal("dark", reloaded.ThemeMode);
        Assert.Equal("gallery", reloaded.ViewMode);
        Assert.Equal(["one", "two"], reloaded.TweetTemplates);
        Assert.Equal("two", reloaded.ActiveTweetTemplate);
    }

    /// <summary>
    /// SettingsService.GetSettingAsync が保存形式を画面向け DTO へ変換することを確認する。
    ///
    /// 保存ファイルでは themeMode/viewMode は文字列だが、ViewModel では enum として扱う。
    /// ここで変換がずれると起動直後の表示モードやテーマが UI と保存値で不一致になるため、
    /// dark/gallery とテンプレート一覧が DTO に正しく反映されることを検証する。
    /// </summary>
    [Fact]
    public async Task SettingsService_GetSettingAsync_MapsStoredStringsToDtoEnums()
    {
        var config = new AppConfig(tempDir);
        config.SaveSetting(new AlpheratzSetting
        {
            PhotoFolderPath = "F:/photos",
            ThemeMode = "dark",
            ViewMode = "gallery",
            EnableStartup = true,
            TweetTemplates = ["alpha", "beta"],
            ActiveTweetTemplate = "beta",
        });
        var service = new SettingsService(config);

        var dto = await service.GetSettingAsync();

        Assert.Equal("F:/photos", dto.photoFolderPath);
        Assert.Equal(ThemeMode.dark, dto.themeMode);
        Assert.Equal(ViewMode.gallery, dto.viewMode);
        Assert.True(dto.enableStartup);
        Assert.Equal(["alpha", "beta"], dto.tweetTemplates);
        Assert.Equal("beta", dto.activeTweetTemplate);
    }

    /// <summary>
    /// SettingsService.SaveSettingAsync が DTO の非 null 項目だけを既存設定へ反映し、
    /// テンプレート一覧を保存時点のスナップショットとしてコピーすることを確認する。
    ///
    /// 設定画面では一部の項目だけを保存する経路がある。
    /// null を「空で上書き」と扱うと既存のフォルダや表示モードが消えるため、非 null の項目だけを反映する。
    /// また tweetTemplates は呼び出し元が後から編集できるコレクションなので、保存時にコピーされないと
    /// Save 後の UI 操作で保存済み設定まで変化してしまう。この2点を同時に固定する。
    /// </summary>
    [Fact]
    public async Task SettingsService_SaveSettingAsync_MergesProvidedValuesAndCopiesTemplates()
    {
        var config = new AppConfig(tempDir);
        config.SaveSetting(new AlpheratzSetting
        {
            PhotoFolderPath = "F:/old-primary",
            SecondaryPhotoFolderPath = "F:/old-secondary",
            ThemeMode = "dark",
            ViewMode = "gallery",
            TweetTemplates = ["before"],
            ActiveTweetTemplate = "before",
        });
        var service = new SettingsService(config);
        var mutableTemplates = new List<string> { "after-1", "after-2" };

        await service.SaveSettingAsync(new AlpheratzSettingDto
        {
            photoFolderPath = "F:/new-primary",
            themeMode = ThemeMode.light,
            tweetTemplates = mutableTemplates,
            activeTweetTemplate = "after-2",
        });
        mutableTemplates.Add("mutated-after-save");
        var saved = config.LoadSetting();

        Assert.Equal("F:/new-primary", saved.PhotoFolderPath);
        Assert.Equal("F:/old-secondary", saved.SecondaryPhotoFolderPath);
        Assert.Equal("light", saved.ThemeMode);
        Assert.Equal("gallery", saved.ViewMode);
        Assert.Equal(["after-1", "after-2"], saved.TweetTemplates);
        Assert.Equal("after-2", saved.ActiveTweetTemplate);
    }

    /// <summary>
    /// AppConfig.GetStartupPreference が保存済みの自動起動希望値と「明示設定済み」フラグを返すことを確認する。
    ///
    /// 起動時の自動起動同期では、ユーザーがまだ選択していない既定値と、明示的に無効を選んだ状態を区別する必要がある。
    /// 設定ファイル内の EnableStartup と StartupPreferenceSet をそのまま読み出すことを固定し、
    /// Windows の Run レジストリには触れない。
    /// </summary>
    [Fact]
    public void AppConfig_GetStartupPreferenceReadsStoredStartupFlags()
    {
        var config = new AppConfig(tempDir);
        config.SaveSetting(new AlpheratzSetting
        {
            EnableStartup = true,
            StartupPreferenceSet = true,
        });

        var preference = config.GetStartupPreference();

        Assert.True(preference.enabled);
        Assert.True(preference.preferenceSet);
    }

    /// <summary>
    /// AppConfig.SaveSetting が保存先ディレクトリを作れない場合に例外を返すことを確認する。
    ///
    /// 設定保存の失敗は UI で通知すべき状態なので、既定値へ黙って戻したり成功扱いにしてはいけない。
    /// ここでは保存先として既存ファイルのパスを渡し、Directory.CreateDirectory が失敗する経路を使って
    /// 実ユーザー設定やレジストリに触れずに保存失敗を再現する。
    /// </summary>
    [Fact]
    public void AppConfig_SaveSettingThrowsWhenConfiguredDirectoryCannotBeCreated()
    {
        var blockedPath = Path.Combine(tempDir, "setting-location-is-file");
        File.WriteAllText(blockedPath, "not a directory");
        var config = new AppConfig(blockedPath);

        var ex = Assert.Throws<InvalidOperationException>(() => config.SaveSetting(new AlpheratzSetting()));

        Assert.Contains("保存先", ex.Message);
    }

    /// <summary>
    /// SettingsService.SaveSettingAsync が AppConfig の保存失敗を呼出元へ再スローすることを確認する。
    ///
    /// SettingsService は DTO をマージする層であり、永続化できなかった事実を握りつぶすと
    /// 設定画面が「保存できた」ように見えてしまう。
    /// 保存先を既存ファイルにして AppConfig.SaveSetting を失敗させ、サービス境界でも例外が残ることを検証する。
    /// </summary>
    [Fact]
    public async Task SettingsService_SaveSettingAsyncRethrowsStoreFailures()
    {
        var blockedPath = Path.Combine(tempDir, "service-setting-location-is-file");
        File.WriteAllText(blockedPath, "not a directory");
        var service = new SettingsService(new AppConfig(blockedPath));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveSettingAsync(new AlpheratzSettingDto { photoFolderPath = "F:/photos" }));
    }

    /// <summary>
    /// SettingsService.SaveStartupPreferenceAsync が自動起動希望値の保存失敗を再スローすることを確認する。
    ///
    /// AppConfig.SaveStartupPreference は設定ファイルを更新してから Windows Run キーへ反映する。
    /// このテストでは設定ファイル保存を先に失敗させるため、レジストリを変更せずに
    /// SettingsService 側の例外伝播だけを検証できる。
    /// </summary>
    [Fact]
    public async Task SettingsService_SaveStartupPreferenceAsyncRethrowsStoreFailuresBeforeRegistryUpdate()
    {
        var blockedPath = Path.Combine(tempDir, "startup-location-is-file");
        File.WriteAllText(blockedPath, "not a directory");
        var service = new SettingsService(new AppConfig(blockedPath));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveStartupPreferenceAsync(enabled: true));
    }
}
