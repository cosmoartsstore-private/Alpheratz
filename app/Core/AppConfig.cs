using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alpheratz.Core;

/// <summary>
/// 設定 JSON と自動起動設定の読み書きを扱う設定ストア。
/// 破損した設定ファイルは既定値へ戻し、保存失敗は呼出側へ通知する。
/// </summary>
public sealed class AppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _lock = new();
    private readonly string? settingDir;

    // テストや移行処理では設定ディレクトリを明示できるようにし、通常実行では AppPaths を使う。
    public AppConfig(string? settingDir = null)
    {
        this.settingDir = settingDir;
    }

    /// <summary>設定ファイルを読み込む。未作成または破損時は既定設定を返す。</summary>
    public AlpheratzSetting LoadSetting()
    {
        AppLogger.Trace("AppConfig.LoadSetting: enter");
        lock (_lock)
        {
            var path = GetSettingDir() is { } d ? Path.Combine(d, "setting.json") : null;
            if (path is not null && File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                    var s = JsonSerializer.Deserialize<AlpheratzSetting>(json);
                    if (s is not null)
                    {
                        AppLogger.Trace("AppConfig.LoadSetting: exit (file loaded)");
                        return s;
                    }
                }
                catch (Exception ex)
                {
                    // 初回起動や破損設定で起動を止めないため、警告だけ残して既定値へ戻す。
                    AppLogger.Warn($"設定ファイルの読み込みに失敗しました: {ex.Message}");
                }
            }
            AppLogger.Trace("AppConfig.LoadSetting: exit (defaults)");
            return new AlpheratzSetting();
        }
    }

    /// <summary>現在の設定を JSON として保存する。保存先を作れない場合は例外を返す。</summary>
    public void SaveSetting(AlpheratzSetting setting)
    {
        AppLogger.Trace("AppConfig.SaveSetting: enter");
        string? tempPath = null;
        try
        {
            lock (_lock)
            {
                var dir = GetSettingDir()
                    ?? throw new InvalidOperationException("設定ファイルの保存先を取得できません");
                var path = Path.Combine(dir, "setting.json");
                var json = JsonSerializer.Serialize(setting, JsonOptions);
                tempPath = Path.Combine(dir, $"setting.{Guid.NewGuid():N}.tmp");

                // 最終ファイルへ直接書くと、プロセス終了や書込み失敗で既存設定まで破損する。
                // 同一ディレクトリの作業ファイルをディスクへ反映してから rename し、
                // 読込側には常に旧版か新版のどちらか一方だけを見せる。
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                    File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(tempPath, path);
                tempPath = null;
            }
        }
        catch (Exception ex)
        {
            // 保存失敗は UI 側で通知する必要があるため、ログ後に呼出側へ返す。
            AppLogger.Error($"AppConfig.SaveSetting: threw: {ex}");
            throw;
        }
        finally
        {
            if (tempPath is not null)
            {
                try { File.Delete(tempPath); }
                catch (Exception ex) { AppLogger.Warn($"設定の作業ファイルを削除できません [{tempPath}]: {ex.Message}"); }
            }
        }
        AppLogger.Trace("AppConfig.SaveSetting: exit");
    }

    /// <summary>自動起動の希望値と、ユーザーが一度でも明示設定したかを返す。</summary>
    public (bool enabled, bool preferenceSet) GetStartupPreference()
    {
        AppLogger.Trace("AppConfig.GetStartupPreference: enter");
        var s = LoadSetting();
        AppLogger.Trace($"AppConfig.GetStartupPreference: exit enabled={s.EnableStartup} set={s.StartupPreferenceSet}");
        return (s.EnableStartup, s.StartupPreferenceSet);
    }

    /// <summary>自動起動の希望値を保存し、可能なら Windows の Run キーへも反映する。</summary>
    public void SaveStartupPreference(bool enabled)
    {
        AppLogger.Trace($"AppConfig.SaveStartupPreference: enter enabled={enabled}");
        try
        {
            var s = LoadSetting();
            s.EnableStartup = enabled;
            s.StartupPreferenceSet = true;
            SaveSetting(s);
            SetStartupEnabled(enabled);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AppConfig.SaveStartupPreference: threw: {ex}");
            throw;
        }
        AppLogger.Trace("AppConfig.SaveStartupPreference: exit");
    }

    /// <summary>Windows の Run キーを更新する。権限や環境の都合で失敗しても設定保存は維持する。</summary>
    private static void SetStartupEnabled(bool enabled)
    {
        AppLogger.Trace($"AppConfig.SetStartupEnabled: enter enabled={enabled}");
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null)
            {
                AppLogger.Trace("AppConfig.SetStartupEnabled: skip (Run key unavailable)");
                return;
            }
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                {
                    AppLogger.Warn("自動起動へ登録する実行ファイルのパスを取得できませんでした");
                    return;
                }
                key.SetValue("Alpheratz", $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue("Alpheratz", throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            // レジストリ更新は環境依存なので、失敗しても設定ファイル側の保存は取り消さない。
            AppLogger.Warn($"自動起動の設定に失敗しました: {ex.Message}");
        }
        AppLogger.Trace("AppConfig.SetStartupEnabled: exit");
    }

    // コンストラクタで明示された保存先を優先し、未指定時だけ標準の AppPaths を使う。
    private string? GetSettingDir()
    {
        if (string.IsNullOrWhiteSpace(settingDir)) return AppPaths.GetSettingDir();
        try { Directory.CreateDirectory(settingDir); return settingDir; }
        catch { return null; }
    }
}
