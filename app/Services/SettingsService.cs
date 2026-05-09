using System;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Shared.Models;

namespace Alpheratz.Services;

public sealed class SettingsService
{
    private readonly AppConfig _config;

    public SettingsService(AppConfig config)
    {
        AppLogger.Trace("SettingsService.ctor: enter");
        _config = config;
        AppLogger.Trace("SettingsService.ctor: exit");
    }

    public Task<AlpheratzSettingDto> GetSettingAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("SettingsService.GetSettingAsync: enter");
        try
        {
            var s = _config.LoadSetting();
            var dto = new AlpheratzSettingDto
            {
                photoFolderPath = s.PhotoFolderPath,
                secondaryPhotoFolderPath = s.SecondaryPhotoFolderPath,
                themeMode = s.ThemeMode switch { "dark" => ThemeMode.dark, _ => ThemeMode.light },
                viewMode = s.ViewMode switch { "gallery" => ViewMode.gallery, _ => ViewMode.standard },
                enableStartup = s.EnableStartup,
                enableMasonryLayout = s.EnableMasonryLayout,
                tweetTemplates = s.TweetTemplates,
                activeTweetTemplate = s.ActiveTweetTemplate,
            };
            AppLogger.Trace("SettingsService.GetSettingAsync: exit");
            return Task.FromResult(dto);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SettingsService.GetSettingAsync: threw: {ex}");
            throw;
        }
    }

    public Task SaveSettingAsync(AlpheratzSettingDto dto, CancellationToken ct = default)
    {
        AppLogger.Trace("SettingsService.SaveSettingAsync: enter");
        try
        {
            var s = _config.LoadSetting();
            s.PhotoFolderPath = dto.photoFolderPath ?? s.PhotoFolderPath;
            s.SecondaryPhotoFolderPath = dto.secondaryPhotoFolderPath ?? s.SecondaryPhotoFolderPath;
            if (dto.themeMode.HasValue) s.ThemeMode = dto.themeMode.Value.ToString();
            if (dto.viewMode.HasValue) s.ViewMode = dto.viewMode.Value.ToString();
            if (dto.enableStartup.HasValue) s.EnableStartup = dto.enableStartup.Value;
            if (dto.enableMasonryLayout.HasValue) s.EnableMasonryLayout = dto.enableMasonryLayout.Value;
            if (dto.tweetTemplates is not null) s.TweetTemplates = new System.Collections.Generic.List<string>(dto.tweetTemplates);
            if (dto.activeTweetTemplate is not null) s.ActiveTweetTemplate = dto.activeTweetTemplate;
            _config.SaveSetting(s);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SettingsService.SaveSettingAsync: threw: {ex}");
            throw;
        }
        AppLogger.Trace("SettingsService.SaveSettingAsync: exit");
        return Task.CompletedTask;
    }

    public Task SaveStartupPreferenceAsync(bool enabled, CancellationToken ct = default)
    {
        AppLogger.Trace($"SettingsService.SaveStartupPreferenceAsync: enter enabled={enabled}");
        try
        {
            _config.SaveStartupPreference(enabled);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SettingsService.SaveStartupPreferenceAsync: threw: {ex}");
            throw;
        }
        AppLogger.Trace("SettingsService.SaveStartupPreferenceAsync: exit");
        return Task.CompletedTask;
    }
}
