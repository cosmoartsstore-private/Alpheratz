using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.Settings;

/// <summary>
/// 設定画面の ViewModel。
/// 設定は SettingsService 経由で JSON ファイルに永続化される。
/// 起動時に refreshSettings で読込、SettingsPage の各操作が単一の AlpheratzSettingDto に
/// 結合されて保存される（buildSettingPayload）。
/// </summary>
public partial class SettingsViewModel : UiThreadSafeObservableObject
{
    private readonly SettingsService settingsService;
    private readonly DialogService dialogService;
    private readonly DispatcherService dispatcherService;

    [ObservableProperty] private string photoFolderPath = string.Empty;
    [ObservableProperty] private string secondaryPhotoFolderPath = string.Empty;
    [ObservableProperty] private bool startupEnabled;
    [ObservableProperty] private ThemeMode themeMode = ThemeMode.light;
    [ObservableProperty] private ViewMode viewMode = ViewMode.standard;
    public UiObservableCollection<string> tweetTemplates { get; } = [];
    [ObservableProperty] private string activeTweetTemplate = string.Empty;

    public SettingsViewModel(SettingsService settingsService, DialogService dialogService, DispatcherService? dispatcherService = null)
    {
        AppLogger.Trace("SettingsViewModel.ctor: enter");
        this.settingsService = settingsService;
        this.dialogService = dialogService;
        this.dispatcherService = dispatcherService ?? new DispatcherService();
        AppLogger.Trace("SettingsViewModel.ctor: exit");
    }

    /// <summary>
    /// 現在の VM 状態から永続化用 DTO を作る。overrides を渡すと、指定フィールドだけ
    /// 上書きできる (フォルダ変更のように VM 反映前に保存したいケース向け)。
    /// </summary>
    public AlpheratzSettingDto buildSettingPayload(AlpheratzSettingDto? overrides = null)
    {
        AppLogger.Trace("SettingsViewModel.buildSettingPayload: enter");
        var payload = new AlpheratzSettingDto
        {
            photoFolderPath = overrides?.photoFolderPath ?? PhotoFolderPath,
            secondaryPhotoFolderPath = overrides?.secondaryPhotoFolderPath ?? SecondaryPhotoFolderPath,
            enableStartup = overrides?.enableStartup ?? StartupEnabled,
            themeMode = overrides?.themeMode ?? ThemeMode,
            viewMode = overrides?.viewMode ?? ViewMode,
            tweetTemplates = overrides?.tweetTemplates ?? tweetTemplates,
            activeTweetTemplate = overrides?.activeTweetTemplate ?? ActiveTweetTemplate,
        };
        AppLogger.Trace("SettingsViewModel.buildSettingPayload: exit");
        return payload;
    }

    /// <summary>
    /// 永続化されている設定 JSON を読み込み、VM の全プロパティを上書きする。
    /// 失敗時は例外を呼出側 (ShellViewModel.refreshSettings) へ返し、
    /// 起動失敗として扱えるようにする（設定読込が失敗した状態でデータ層を初期化すると壊れる）。
    /// </summary>
    public async Task refreshSettings()
    {
        AppLogger.Trace("SettingsViewModel.refreshSettings: enter");
        try
        {
            var setting = await settingsService.GetSettingAsync().ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
            {
                PhotoFolderPath = setting.photoFolderPath ?? string.Empty;
                SecondaryPhotoFolderPath = setting.secondaryPhotoFolderPath ?? string.Empty;
                StartupEnabled = setting.enableStartup ?? false;
                ThemeMode = setting.themeMode ?? ThemeMode.light;
                ViewMode = setting.viewMode ?? ViewMode.standard;
                ActiveTweetTemplate = setting.activeTweetTemplate ?? string.Empty;
                tweetTemplates.ReplaceAll(setting.tweetTemplates ?? Array.Empty<string>());
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 起動時の設定読込失敗は ShellViewModel 側で初期化失敗として扱う。
            AppLogger.Error($"SettingsViewModel.refreshSettings: threw: {ex}");
            throw;
        }
        AppLogger.Trace("SettingsViewModel.refreshSettings: exit");
    }

    /// <summary>
    /// フォルダ選択ダイアログを開き、選ばれたパス（または キャンセル時 null）を返す。
    /// 永続化は呼出側で必要に応じて行う (フォルダ変更には UI 上での確認モーダルが入るため、
    /// この関数は「ただ選ぶ」だけにとどめる)。
    /// </summary>
    public async Task<string?> handleChooseFolderPathOnly()
    {
        AppLogger.Trace("SettingsViewModel.handleChooseFolderPathOnly: enter");
        try
        {
            var path = await dialogService.openDirectoryAsync().ConfigureAwait(false);
            AppLogger.Trace($"SettingsViewModel.handleChooseFolderPathOnly: exit path={path ?? "(null)"}");
            return path;
        }
        catch (Exception ex)
        {
            // キャンセルやダイアログ起動失敗は、呼出側で「未選択」と同じ null として扱う。
            AppLogger.Error($"SettingsViewModel.handleChooseFolderPathOnly: threw: {ex}");
            return null;
        }
    }
}
