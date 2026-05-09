using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alpheratz.Core;

public sealed class AppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _lock = new();

    public AlpheratzSetting LoadSetting()
    {
        AppLogger.Trace("AppConfig.LoadSetting: enter");
        lock (_lock)
        {
            var path = AppPaths.GetSettingDir() is { } d ? Path.Combine(d, "setting.json") : null;
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
                    // Continue: legacy alpheratz logs the warning and falls
                    // back to defaults so first-run / corrupted settings do
                    // not block startup.
                    AppLogger.Warn($"設定ファイルの読み込みに失敗しました: {ex.Message}");
                }
            }
            AppLogger.Trace("AppConfig.LoadSetting: exit (defaults)");
            return new AlpheratzSetting();
        }
    }

    public void SaveSetting(AlpheratzSetting setting)
    {
        AppLogger.Trace("AppConfig.SaveSetting: enter");
        try
        {
            lock (_lock)
            {
                var dir = AppPaths.GetSettingDir()
                    ?? throw new InvalidOperationException("設定ファイルの保存先を取得できません");
                var path = Path.Combine(dir, "setting.json");
                var json = JsonSerializer.Serialize(setting, JsonOptions);
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            // Rethrow: caller is SettingsService which propagates further so
            // the UI can surface a toast or error UI.
            AppLogger.Error($"AppConfig.SaveSetting: threw: {ex}");
            throw;
        }
        AppLogger.Trace("AppConfig.SaveSetting: exit");
    }

    public (bool enabled, bool preferenceSet) GetStartupPreference()
    {
        AppLogger.Trace("AppConfig.GetStartupPreference: enter");
        var s = LoadSetting();
        AppLogger.Trace($"AppConfig.GetStartupPreference: exit enabled={s.EnableStartup} set={s.StartupPreferenceSet}");
        return (s.EnableStartup, s.StartupPreferenceSet);
    }

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
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                key.SetValue("Alpheratz", $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue("Alpheratz", throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            // Continue: registry write is best-effort; legacy alpheratz only
            // warns on failure.
            AppLogger.Warn($"自動起動の設定に失敗しました: {ex.Message}");
        }
        AppLogger.Trace("AppConfig.SetStartupEnabled: exit");
    }
}
