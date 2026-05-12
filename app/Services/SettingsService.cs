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
    // R2-A-21: LoadSetting → mutate → SaveSetting の Read-Modify-Write が複数スレッドから
    //          並列に走ると、片方の TweetTemplates 等が他方で上書きされるなどの race が起きうる。
    //          ファイル/状態を介して直列化するため、SaveSettingAsync 全体を lock で囲む。
    private readonly object _writeGate = new();

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
            // R2-A-21: 読み込み中に SaveSetting が走ると一貫性のないスナップショットを掴む可能性があるため、
            //          書込ロックと同じ lock で囲んで保護する。読み込みは短時間なので contention は問題にならない。
            AlpheratzSettingDto dto;
            lock (_writeGate)
            {
                var s = _config.LoadSetting();
                dto = new AlpheratzSettingDto
                {
                    photoFolderPath = s.PhotoFolderPath,
                    secondaryPhotoFolderPath = s.SecondaryPhotoFolderPath,
                    themeMode = s.ThemeMode switch { "dark" => ThemeMode.dark, _ => ThemeMode.light },
                    viewMode = s.ViewMode switch { "gallery" => ViewMode.gallery, _ => ViewMode.standard },
                    enableStartup = s.EnableStartup,
                    enableMasonryLayout = s.EnableMasonryLayout,
                    // TweetTemplates はスナップショットコピーを返す（後続の Save が編集中の参照を踏まないように）。
                    tweetTemplates = s.TweetTemplates is null ? null : new System.Collections.Generic.List<string>(s.TweetTemplates),
                    activeTweetTemplate = s.ActiveTweetTemplate,
                };
            }
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
            // R2-A-21: Load → mutate → Save は単一ロックで完全に直列化する。
            //          dto.tweetTemplates の参照を直接保持すると、呼出側がさらに編集したときに
            //          ファイルへ書き出した内容と実メモリで乖離するため、必ずコピーを取る。
            lock (_writeGate)
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
            lock (_writeGate)
            {
                _config.SaveStartupPreference(enabled);
            }
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