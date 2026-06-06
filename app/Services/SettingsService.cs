using System;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Shared.Models;

namespace Alpheratz.Services;

/// <summary>
/// AppConfig の保存形式を画面向け DTO に変換するサービス。
/// 読み書きを同じ lock で直列化し、設定ファイルのスナップショット整合性を保つ。
/// </summary>
public sealed class SettingsService
{
    private readonly AppConfig _config;
    // LoadSetting → mutate → SaveSetting を並列実行すると、片方の変更が別の保存で上書きされる。
    // 読み書き全体を同じロックで囲み、設定ファイルを単一の状態として扱う。
    private readonly object _writeGate = new();

    /// <summary>設定ストアを受け取ってサービスを作成する。</summary>
    public SettingsService(AppConfig config)
    {
        AppLogger.Trace("SettingsService.ctor: enter");
        _config = config;
        AppLogger.Trace("SettingsService.ctor: exit");
    }

    /// <summary>保存済み設定を画面向け DTO に変換して返す。</summary>
    public Task<AlpheratzSettingDto> GetSettingAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("SettingsService.GetSettingAsync: enter");
        try
        {
            // 保存中の中間状態を読まないよう、書き込みと同じロックでスナップショットを取る。
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
                    openWorldLinkOnPost = s.OpenWorldLinkOnPost,
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

    /// <summary>DTO の指定項目だけを既存設定へ反映して保存する。</summary>
    public Task SaveSettingAsync(AlpheratzSettingDto dto, CancellationToken ct = default)
    {
        AppLogger.Trace("SettingsService.SaveSettingAsync: enter");
        try
        {
            // DTO のコレクション参照をそのまま保持すると、呼出側の後続編集で保存内容が揺れる。
            // 保存時点の値として扱うため、テンプレート一覧はコピーして保持する。
            lock (_writeGate)
            {
                var s = _config.LoadSetting();
                s.PhotoFolderPath = dto.photoFolderPath ?? s.PhotoFolderPath;
                s.SecondaryPhotoFolderPath = dto.secondaryPhotoFolderPath ?? s.SecondaryPhotoFolderPath;
                if (dto.themeMode.HasValue) s.ThemeMode = dto.themeMode.Value.ToString();
                if (dto.viewMode.HasValue) s.ViewMode = dto.viewMode.Value.ToString();
                if (dto.enableStartup.HasValue) s.EnableStartup = dto.enableStartup.Value;
                if (dto.openWorldLinkOnPost.HasValue) s.OpenWorldLinkOnPost = dto.openWorldLinkOnPost.Value;
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

    /// <summary>自動起動の希望値を保存する。</summary>
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
