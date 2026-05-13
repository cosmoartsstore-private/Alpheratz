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

public partial class SettingsViewModel : UiThreadSafeObservableObject
{
    private readonly SettingsService settingsService;
    private readonly DialogService dialogService;

    [ObservableProperty] private string photoFolderPath = string.Empty;
    [ObservableProperty] private string secondaryPhotoFolderPath = string.Empty;
    [ObservableProperty] private bool startupEnabled;
    [ObservableProperty] private ThemeMode themeMode = ThemeMode.light;
    [ObservableProperty] private ViewMode viewMode = ViewMode.standard;
    public UiObservableCollection<string> tweetTemplates { get; } = [];
    [ObservableProperty] private string activeTweetTemplate = string.Empty;

    public SettingsViewModel(SettingsService settingsService, DialogService dialogService)
    {
        AppLogger.Trace("SettingsViewModel.ctor: enter");
        this.settingsService = settingsService;
        this.dialogService = dialogService;
        AppLogger.Trace("SettingsViewModel.ctor: exit");
    }

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

    public async Task refreshSettings()
    {
        AppLogger.Trace("SettingsViewModel.refreshSettings: enter");
        try
        {
            var setting = await settingsService.GetSettingAsync().ConfigureAwait(false);
            PhotoFolderPath = setting.photoFolderPath ?? string.Empty;
            SecondaryPhotoFolderPath = setting.secondaryPhotoFolderPath ?? string.Empty;
            StartupEnabled = setting.enableStartup ?? false;
            ThemeMode = setting.themeMode ?? ThemeMode.light;
            ViewMode = setting.viewMode ?? ViewMode.standard;
            ActiveTweetTemplate = setting.activeTweetTemplate ?? string.Empty;

            tweetTemplates.Clear();
            foreach (var template in setting.tweetTemplates ?? [])
            {
                tweetTemplates.Add(template);
            }
        }
        catch (Exception ex)
        {
            // Rethrow: ShellViewModel.refreshSettings awaits and treats this
            // as fatal during startup hydration so dataReady never flips.
            AppLogger.Error($"SettingsViewModel.refreshSettings: threw: {ex}");
            throw;
        }
        AppLogger.Trace("SettingsViewModel.refreshSettings: exit");
    }

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
            // Continue: dialog cancellation or platform failure should not
            // crash the workflow; legacy alpheratz returns null here.
            AppLogger.Error($"SettingsViewModel.handleChooseFolderPathOnly: threw: {ex}");
            return null;
        }
    }
}
