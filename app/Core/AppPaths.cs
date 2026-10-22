using System;
using System.IO;
using Microsoft.Win32;

namespace Alpheratz.Core;

public static class AppPaths
{

    /// <summary>テスト時に利用者のデータ領域へ書き込まないための一時データdirectory。</summary>
    internal static string? DataDirOverrideForTests { get; set; }

    public static string? InstallLocation { get; } = ResolveInstallLocation();

    /// <summary>registry、実行ファイル隣接 Data、LocalAppData の順でインストール場所を決める。</summary>
    private static string? ResolveInstallLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppConstants.RegistryKeyPath);
            var path = key?.GetValue(AppConstants.InstallLocationRegistryValue) as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                return path;
        }
        catch { /* registry を参照できない場合は次の候補を使用する。 */ }

        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is not null && Directory.Exists(Path.Combine(exeDir, AppConstants.DataDirectoryName)))
            return exeDir;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppConstants.PublisherName, AppConstants.ApplicationName);
    }

    /// <summary>アプリデータのルートdirectoryを返す。</summary>
    public static string? GetDataDir()
        => EnsureDir(DataDirOverrideForTests
            ?? (InstallLocation is null ? null : Path.Combine(InstallLocation, AppConstants.DataDirectoryName)));

    /// <summary>ログ保存directoryを返す。</summary>
    public static string? GetLogDir() => EnsureDir(GetDataDir() is { } d ? Path.Combine(d, AppConstants.LogDirectoryName) : null);

    /// <summary>設定 JSON 保存directoryを返す。</summary>
    public static string? GetSettingDir() => EnsureDir(GetDataDir() is { } d ? Path.Combine(d, AppConstants.DatabaseDirectoryName) : null);

    /// <summary>画像cacheなどの一時データ保存directoryを返す。</summary>
    public static string? GetCacheDir() => EnsureDir(GetDataDir() is { } d ? Path.Combine(d, AppConstants.CacheDirectoryName) : null);

    /// <summary>SQLite DB 保存directoryを返す。</summary>
    public static string? GetDbDir() => EnsureDir(GetDataDir() is { } d ? Path.Combine(d, AppConstants.DatabaseDirectoryName) : null);

    /// <summary>SQLite DB ファイルのパスを返す。</summary>
    public static string? GetDbPath() => GetDbDir() is { } d ? Path.Combine(d, AppConstants.DatabaseFileName) : null;

    /// <summary>指定 source_slot のサムネイルcachedirectoryを返す。</summary>
    public static string? GetImgCacheDir(long sourceSlot = 1)
    {
        if (sourceSlot is not (1 or 2)) return null;
        var slotName = sourceSlot == 2 ? "2nd-cache" : "1st-cache";
        return EnsureDir(GetCacheDir() is { } c ? Path.Combine(c, slotName, "imgCache") : null);
    }

    /// <summary>Polaris のログ archive directoryをregistryから探す。</summary>
    public static string? GetPolarisArchiveDir()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppConstants.PolarisRegistryKeyPath);
            var path = key?.GetValue(AppConstants.InstallLocationRegistryValue) as string;
            if (string.IsNullOrWhiteSpace(path)) return null;
            var archiveDir = Path.Combine(path, AppConstants.DataDirectoryName, "archive");
            return Directory.Exists(archiveDir) ? archiveDir : null;
        }
        catch { return null; }
    }

    /// <summary>DB 保存用に Windows パス区切りをスラッシュへ正規化する。</summary>
    public static string NormalizePathForDb(string path) => path.Replace('\\', '/');

    /// <summary>DB形式の区切りをWindowsのファイルAPIへ渡す区切りへ変換する。</summary>
    public static string ToNativePath(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>末尾区切りと相対表記を除いた、比較用の絶対directoryパスを返す。</summary>
    internal static string NormalizeDirectoryPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>大文字小文字や末尾区切りの違いを無視して、同じdirectoryか判定する。</summary>
    internal static bool AreSameDirectory(string first, string second) => string.Equals(NormalizeDirectoryPath(first), NormalizeDirectoryPath(second), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 2 つのdirectoryが同一、または一方が他方の配下なら true を返す。
    /// source_slot 間でこの関係を許すと、同じ写真を別スロットとして安定して管理できない。
    /// </summary>
    internal static bool AreOverlappingDirectories(string first, string second)
    {
        var normalizedFirst = NormalizeDirectoryPath(first);
        var normalizedSecond = NormalizeDirectoryPath(second);
        return IsSameOrParentDirectory(normalizedFirst, normalizedSecond)
            || IsSameOrParentDirectory(normalizedSecond, normalizedFirst);
    }

    private static bool IsSameOrParentDirectory(string parent, string candidate)
    {
        if (string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
            return true;
        var prefix = Path.EndsInDirectorySeparator(parent)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>directoryを作成してからパスを返す。作成できない場合は null を返す。</summary>
    private static string? EnsureDir(string? path)
    {
        if (path is null) return null;
        try { Directory.CreateDirectory(path); return path; }
        catch { return null; }
    }
}
