using System;
using System.IO;
using Microsoft.Win32;

namespace Alpheratz.Core;

public static class AppPaths
{
    private const string RegistryKeyPath = @"Software\CosmoArtsStore\Alpheratz";
    private const string PolarisKeyPath = @"Software\CosmoArtsStore\Polaris";

    public static string? InstallLocation { get; } = ResolveInstallLocation();

    private static string? ResolveInstallLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            var path = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                return path;
        }
        catch { /* registry unavailable */ }

        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is not null && Directory.Exists(Path.Combine(exeDir, "Data")))
            return exeDir;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "CosmoArtsStore", "Alpheratz");
    }

    public static string? GetDataDir() => EnsureDir(InstallLocation is null ? null : Path.Combine(InstallLocation, "Data"));

    public static string? GetLogDir() => EnsureDir(GetDataDir() is { } d ? Path.Combine(d, "logs") : null);

    public static string? GetSettingDir() => EnsureDir(GetDataDir() is { } d ? Path.Combine(d, "db") : null);

    public static string? GetCacheDir() => EnsureDir(GetDataDir() is { } d ? Path.Combine(d, "cache") : null);

    public static string? GetDbDir() => EnsureDir(GetDataDir() is { } d ? Path.Combine(d, "db") : null);

    public static string? GetDbPath() => GetDbDir() is { } d ? Path.Combine(d, "Alpheratz.db") : null;

    public static string? GetDbBackupDir() => EnsureDir(GetDbDir() is { } d ? Path.Combine(d, "backup") : null);

    public static string? GetImgCacheDir(long sourceSlot = 1)
    {
        var slotName = sourceSlot == 2 ? "2nd-cache" : "1st-cache";
        return EnsureDir(GetCacheDir() is { } c ? Path.Combine(c, slotName, "imgCache") : null);
    }

    public static string? GetPolarisArchiveDir()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PolarisKeyPath);
            var path = key?.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(path)) return null;
            var archiveDir = Path.Combine(path, "archive");
            return Directory.Exists(archiveDir) ? archiveDir : null;
        }
        catch { return null; }
    }

    public static string NormalizePathForDb(string path) => path.Replace('\\', '/');

    private static string? EnsureDir(string? path)
    {
        if (path is null) return null;
        try { Directory.CreateDirectory(path); return path; }
        catch { return null; }
    }
}
