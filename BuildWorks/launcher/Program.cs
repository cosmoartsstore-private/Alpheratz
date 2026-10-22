using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using Alpheratz.Core;
using static Alpheratz.Messages.MessageCatalog;

namespace Launcher;

internal static class Program
{
    private const string AppName = AppConstants.ApplicationName;
    private const string RegistryKeyPath = AppConstants.RegistryKeyPath;
    private const string RuntimeExecutableName = "Alpheratz.Frontend.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var runtimeExe = ResolveRuntimeExecutable();
            if (!File.Exists(runtimeExe))
            {
                ShowError(getMsg("Launcher.runtimeMissing", ("path", runtimeExe)));
                return 2;
            }

            Process.Start(BuildRuntimeStartInfo(runtimeExe, args));
            return 0;
        }
        catch (Exception ex)
        {
            var logWritten = TryWriteLaunchError(ex);
            ShowError(logWritten
                ? getMsg("Launcher.startFailedWithLog")
                : getMsg("Launcher.startFailedWithoutLog"));
            return 1;
        }
    }

    /// <summary>runtimeの作業folderと個別引数を保持し、shell文字列へ連結せず引き渡す。</summary>
    internal static ProcessStartInfo BuildRuntimeStartInfo(string runtimeExe, string[] args)
    {
        var startInfo = new ProcessStartInfo
        { FileName = runtimeExe, WorkingDirectory = Path.GetDirectoryName(runtimeExe)!, UseShellExecute = true, };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        return startInfo;
    }

    private static string ResolveRuntimeExecutable()
    {
        var installLocation = ReadRegistryValue("RuntimeLocation");
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            return Path.Combine(installLocation, RuntimeExecutableName);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppConstants.PublisherName, AppName, "app", RuntimeExecutableName);
    }

    private static string ResolveDataRoot()
    {
        var installLocation = ReadRegistryValue(AppConstants.InstallLocationRegistryValue);
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            return Path.Combine(installLocation, AppConstants.DataDirectoryName, AppConstants.LogDirectoryName);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppConstants.PublisherName, AppName, AppConstants.DataDirectoryName, AppConstants.LogDirectoryName);
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
