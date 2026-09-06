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
            ct.ThrowIfCancellationRequested();
            // 保存中の中間状態を読まないよう、書き込みと同じロックでスナップショットを取る。
            AlpheratzSettingDto dto;
            lock (_writeGate)
            {
                var s = _config.LoadSetting();
                var startupPreference = _config.GetStartupPreference();
                dto = new AlpheratzSettingDto
                {
                    photoFolderPath = s.PhotoFolderPath,
                    secondaryPhotoFolderPath = s.SecondaryPhotoFolderPath,
                    themeMode = s.ThemeMode switch { "dark" => ThemeMode.dark, _ => ThemeMode.light },
                    viewMode = s.ViewMode switch { "gallery" => ViewMode.gallery, _ => ViewMode.standard },
                    enableStartup = startupPreference.enabled,
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

    /// <summary>
    /// DTO の一般設定だけを既存設定へ反映して保存する。
    /// 写真フォルダは DB 整理要求と一体で扱うため、SaveFolderChangeAsync だけが変更できる。
    /// </summary>
    public Task SaveSettingAsync(AlpheratzSettingDto dto, CancellationToken ct = default)
    {
        AppLogger.Trace("SettingsService.SaveSettingAsync: enter");
        try
        {
            ct.ThrowIfCancellationRequested();
            // DTO のコレクション参照をそのまま保持すると、呼出側の後続編集で保存内容が揺れる。
            // 保存時点の値として扱うため、テンプレート一覧はコピーして保持する。
            lock (_writeGate)
            {
                var s = _config.LoadSetting();
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

    /// <summary>
    /// フォルダパスと旧データ整理要求を同じ設定保存へ反映する。
    /// 保存済みパスが確認画面表示時から変わっていた場合は、古い操作で上書きしない。
    /// </summary>
    public Task<PendingFolderCleanupSetting> SaveFolderChangeAsync(
        int sourceSlot,
        string expectedCurrentPath,
        string committedPath,
        CancellationToken ct = default)
    {
        if (sourceSlot is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(sourceSlot), sourceSlot, "source_slot は 1 または 2 である必要があります");

        ct.ThrowIfCancellationRequested();
        lock (_writeGate)
        {
            var setting = _config.LoadSetting();
            if (setting.PendingFolderCleanup is not null)
                throw new InvalidOperationException("前回の写真フォルダ整理が完了していません");

            var savedPath = sourceSlot == 1
                ? setting.PhotoFolderPath
                : setting.SecondaryPhotoFolderPath;
            if (!string.Equals(savedPath, expectedCurrentPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("写真フォルダ設定が確認後に変更されました。設定画面を開き直してください");

            var otherPath = sourceSlot == 1
                ? setting.SecondaryPhotoFolderPath
                : setting.PhotoFolderPath;
            if (!string.IsNullOrWhiteSpace(committedPath)
                && !string.IsNullOrWhiteSpace(otherPath)
                && AppPaths.AreOverlappingDirectories(committedPath, otherPath))
            {
                throw new InvalidOperationException("1st と 2nd の写真フォルダには、同じフォルダや親子関係のフォルダを設定できません");
            }

            var cleanup = new PendingFolderCleanupSetting
            {
                OperationId = Guid.NewGuid().ToString("N"),
                SourceSlot = sourceSlot,
                CommittedPath = committedPath,
            };
            if (sourceSlot == 1)
                setting.PhotoFolderPath = committedPath;
            else
                setting.SecondaryPhotoFolderPath = committedPath;
            setting.PendingFolderCleanup = cleanup;
            _config.SaveSetting(setting);
            return Task.FromResult(CloneCleanup(cleanup));
        }
    }

    /// <summary>未完了のフォルダ整理要求をスナップショットとして返す。</summary>
    public Task<PendingFolderCleanupSetting?> GetPendingFolderCleanupAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_writeGate)
        {
            var cleanup = _config.LoadSetting().PendingFolderCleanup;
            return Task.FromResult(cleanup is null ? null : CloneCleanup(cleanup));
        }
    }

    /// <summary>対象操作が現在も最新で、保存済みパスと一致しているか確認する。</summary>
    public Task<bool> IsPendingFolderCleanupCurrentAsync(
        PendingFolderCleanupSetting cleanup,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_writeGate)
        {
            var setting = _config.LoadSetting();
            var current = setting.PendingFolderCleanup;
            if (current is null
                || !string.Equals(current.OperationId, cleanup.OperationId, StringComparison.Ordinal)
                || current.SourceSlot != cleanup.SourceSlot)
            {
                return Task.FromResult(false);
            }

            var savedPath = current.SourceSlot == 1
                ? setting.PhotoFolderPath
                : setting.SecondaryPhotoFolderPath;
            return Task.FromResult(string.Equals(savedPath, current.CommittedPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// DB／キャッシュ整理が完了した操作だけ、operationId の compare-and-set で要求を解除する。
    /// 設定パスは手動編集され得るが、対象 slot のリセット完了後なら現在パスを次の走査で再構築できる。
    /// </summary>
    public Task ClearPendingFolderCleanupAsync(string operationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_writeGate)
        {
            var setting = _config.LoadSetting();
            var current = setting.PendingFolderCleanup;
            if (current is null)
                return Task.CompletedTask;
            if (!string.Equals(current.OperationId, operationId, StringComparison.Ordinal))
                throw new InvalidOperationException("別の写真フォルダ整理要求へ更新されています");

            setting.PendingFolderCleanup = null;
            _config.SaveSetting(setting);
            return Task.CompletedTask;
        }
    }

    private static PendingFolderCleanupSetting CloneCleanup(PendingFolderCleanupSetting cleanup)
        => new()
        {
            OperationId = cleanup.OperationId,
            SourceSlot = cleanup.SourceSlot,
            CommittedPath = cleanup.CommittedPath,
        };

    /// <summary>自動起動の希望値を保存する。</summary>
    public Task SaveStartupPreferenceAsync(bool enabled, CancellationToken ct = default)
    {
        AppLogger.Trace($"SettingsService.SaveStartupPreferenceAsync: enter enabled={enabled}");
        try
        {
            ct.ThrowIfCancellationRequested();
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
