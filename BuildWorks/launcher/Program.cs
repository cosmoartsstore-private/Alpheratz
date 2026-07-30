using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Launcher;

internal static class Program
{
    private const string AppName = "Alpheratz";
    private const string RegistryKeyPath = @"Software\CosmoArtsStore\Alpheratz";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var runtimeExe = ResolveRuntimeExecutable();
            if (!File.Exists(runtimeExe))
            {
                ShowError($"Alpheratz runtime executable was not found.\n\n{runtimeExe}");
                return 2;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = runtimeExe,
                WorkingDirectory = Path.GetDirectoryName(runtimeExe)!,
                UseShellExecute = true,
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            Process.Start(startInfo);
            return 0;
        }
        catch (Exception ex)
        {
            var logWritten = TryWriteLaunchError(ex);
            ShowError(logWritten
                ? "Alpheratzを起動できませんでした。詳細は Data\\logs\\launcher_error.log を確認してください。"
                : "Alpheratzを起動できませんでした。エラー情報も保存できませんでした。");
            return 1;
        }
    }

    private static string ResolveRuntimeExecutable()
    {
        var installLocation = ReadRegistryValue("RuntimeLocation");
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            return Path.Combine(installLocation, "Alpheratz.Frontend.exe");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            localAppData,
            "CosmoArtsStore",
            AppName,
            "app",
            "Alpheratz.Frontend.exe");
    }

    private static string ResolveDataRoot()
    {
        var installLocation = ReadRegistryValue("InstallLocation");
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            return Path.Combine(installLocation, "Data", "logs");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "CosmoArtsStore", AppName, "Data", "logs");
    }

    private static string? ReadRegistryValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
        return key?.GetValue(valueName) as string;
    }

    private static bool TryWriteLaunchError(Exception ex)
    {
        try
        {
            var logDir = ResolveDataRoot();
            Directory.CreateDirectory(logDir);
            File.WriteAllText(Path.Combine(logDir, "launcher_error.log"), ex.ToString());
            return true;
        }
        catch
        {
            // ログ保存が失敗しても、起動エラーの画面表示は継続する。
            return false;
        }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
