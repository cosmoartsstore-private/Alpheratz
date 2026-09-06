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
    private const string StartupRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupRegistryValueName = "Alpheratz";
    private const string LauncherExecutableName = "Alpheratz.exe";
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
        // 明示保存先はテスト・移行用なので Windows レジストリへ触れず、保存値を返す。
        // 通常実行では、画面上の状態を実際の Run 登録へ合わせる。
        var enabled = string.IsNullOrWhiteSpace(settingDir)
            ? IsStartupRegistrationCurrent()
            : s.EnableStartup;
        AppLogger.Trace($"AppConfig.GetStartupPreference: exit enabled={enabled} set={s.StartupPreferenceSet}");
        return (enabled, s.StartupPreferenceSet);
    }

    /// <summary>自動起動の希望値を設定と Windows の Run 登録へ一体として反映する。</summary>
    public void SaveStartupPreference(bool enabled)
    {
        AppLogger.Trace($"AppConfig.SaveStartupPreference: enter enabled={enabled}");
        var s = LoadSetting();
        try
        {
            // 明示保存先はテスト・移行用であり、利用者の Run 登録へ触れない。
            if (!string.IsNullOrWhiteSpace(settingDir))
            {
                s.EnableStartup = enabled;
                s.StartupPreferenceSet = true;
                SaveSetting(s);
                return;
            }

            // 先に旧内容を保存して書込み可能か確認する。ここで失敗した場合は Windows 側を変更しない。
            SaveSetting(s);
            var previousRegistration = CaptureStartupRegistration();
            try
            {
                SetStartupEnabled(enabled);
                s.EnableStartup = enabled;
                s.StartupPreferenceSet = true;
                SaveSetting(s);
            }
            catch (Exception updateException)
            {
                try
                {
                    RestoreStartupRegistration(previousRegistration);
                }
                catch (Exception restoreException)
                {
                    throw new AggregateException(
                        "自動起動設定の更新と以前の登録への復元に失敗しました",
                        updateException,
                        restoreException);
                }
                throw;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AppConfig.SaveStartupPreference: threw: {ex}");
            throw;
        }
        AppLogger.Trace("AppConfig.SaveStartupPreference: exit");
    }

    /// <summary>Windows の Run キーを更新する。失敗は呼出側へ返し、設定値の成功扱いを防ぐ。</summary>
    private static void SetStartupEnabled(bool enabled)
    {
        AppLogger.Trace($"AppConfig.SetStartupEnabled: enter enabled={enabled}");
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                StartupRegistryKeyPath, writable: true)
                ?? throw new InvalidOperationException("Windows の自動起動設定を開けません");
            if (enabled)
            {
                var launcherPath = ResolveStartupExecutablePath();
                key.SetValue(
                    StartupRegistryValueName,
                    QuoteExecutablePath(launcherPath),
                    Microsoft.Win32.RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(StartupRegistryValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"自動起動の設定に失敗しました: {ex}");
            throw;
        }
        AppLogger.Trace("AppConfig.SetStartupEnabled: exit");
    }

    /// <summary>現在の Run 登録が、インストール先の外側 launcher を正しく指しているか確認する。</summary>
    private static bool IsStartupRegistrationCurrent()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegistryKeyPath, writable: false);
            var registered = key?.GetValue(
                StartupRegistryValueName,
                defaultValue: null,
                options: Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (registered is null)
                return false;

            var expected = QuoteExecutablePath(ResolveStartupExecutablePath());
            return string.Equals(registered, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"自動起動の現在値を確認できませんでした: {ex.Message}");
            return false;
        }
    }

    /// <summary>内部の WinUI 本体ではなく、インストールルートの launcher を返す。</summary>
    private static string ResolveStartupExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(AppPaths.InstallLocation))
        {
            var installedLauncher = Path.GetFullPath(Path.Combine(AppPaths.InstallLocation, LauncherExecutableName));
            if (File.Exists(installedLauncher))
                return installedLauncher;
        }

        // レジストリが利用できない開発配置でも、app\Alpheratz.Frontend.exe の1階層上を確認する。
        var processPath = Environment.ProcessPath;
        var runtimeDirectory = string.IsNullOrWhiteSpace(processPath)
            ? null
            : Path.GetDirectoryName(processPath);
        var installDirectory = runtimeDirectory is not null
            && string.Equals(Path.GetFileName(runtimeDirectory), "app", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(runtimeDirectory)?.FullName
                : runtimeDirectory;
        if (!string.IsNullOrWhiteSpace(installDirectory))
        {
            var adjacentLauncher = Path.GetFullPath(Path.Combine(installDirectory, LauncherExecutableName));
            if (File.Exists(adjacentLauncher))
                return adjacentLauncher;
        }

        throw new FileNotFoundException("自動起動へ登録する Alpheratz launcher が見つかりません");
    }

    private static string QuoteExecutablePath(string path) => $"\"{path}\"";

    private readonly record struct StartupRegistrationSnapshot(
        bool Exists,
        object? Value,
        Microsoft.Win32.RegistryValueKind Kind);

    /// <summary>更新失敗時に Run 登録を正確に戻せるよう、値と型を保存する。</summary>
    private static StartupRegistrationSnapshot CaptureStartupRegistration()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegistryKeyPath, writable: false);
        var value = key?.GetValue(
            StartupRegistryValueName,
            defaultValue: null,
            options: Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value is null
            ? new StartupRegistrationSnapshot(false, null, Microsoft.Win32.RegistryValueKind.String)
            : new StartupRegistrationSnapshot(true, value, key!.GetValueKind(StartupRegistryValueName));
    }

    private static void RestoreStartupRegistration(StartupRegistrationSnapshot snapshot)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
            StartupRegistryKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows の自動起動設定を復元できません");
        if (snapshot.Exists)
            key.SetValue(StartupRegistryValueName, snapshot.Value!, snapshot.Kind);
        else
            key.DeleteValue(StartupRegistryValueName, throwOnMissingValue: false);
    }

    // コンストラクタで明示された保存先を優先し、未指定時だけ標準の AppPaths を使う。
    private string? GetSettingDir()
    {
        if (string.IsNullOrWhiteSpace(settingDir)) return AppPaths.GetSettingDir();
        try { Directory.CreateDirectory(settingDir); return settingDir; }
        catch { return null; }
    }
}
