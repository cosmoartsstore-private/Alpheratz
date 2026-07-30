using System;
using System.IO;
using Microsoft.Win32;

namespace Alpheratz.Core;

public static class AppPaths
{
    private const string RegistryKeyPath = @"Software\CosmoArtsStore\Alpheratz";
    private const string PolarisKeyPath = @"Software\CosmoArtsStore\Polaris";

    /// <summary>テスト時に利用者のデータ領域へ書き込まないための一時データディレクトリ。</summary>
    internal static string? DataDirOverrideForTests { get; set; }

    public static string? InstallLocation { get; } = ResolveInstallLocation();

    /// <summary>レジストリ、実行ファイル隣接 Data、LocalAppData の順でインストール場所を決める。</summary>
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

    /// <summary>アプリデータのルートディレクトリを返す。</summary>
    public static string? GetDataDir()
        => EnsureDir(DataDirOverrideForTests
            ?? (InstallLocation is null ? null : Path.Combine(InstallLocation, "Data")));

    /// <summary>ログ保存ディレクトリを返す。</summary>
    public static string? GetLogDir() => EnsureDir(GetDataDir() is { } d ? Path.Combine(d, "logs") : null);

    /// <summary>設定 JSON 保存ディレクトリを返す。</summary>
    public static string? GetSettingDir() => EnsureDir(GetDataDir() is { } d ? Path.Combine(d, "db") : null);

    /// <summary>画像キャッシュなどの一時データ保存ディレクトリを返す。</summary>
    public static string? GetCacheDir() => EnsureDir(GetDataDir() is { } d ? Path.Combine(d, "cache") : null);

    /// <summary>SQLite DB 保存ディレクトリを返す。</summary>
    public static string? GetDbDir() => EnsureDir(GetDataDir() is { } d ? Path.Combine(d, "db") : null);

    /// <summary>SQLite DB ファイルのパスを返す。</summary>
    public static string? GetDbPath() => GetDbDir() is { } d ? Path.Combine(d, "Alpheratz.db") : null;

    /// <summary>指定 source_slot のサムネイルキャッシュディレクトリを返す。</summary>
    public static string? GetImgCacheDir(long sourceSlot = 1)
    {
        if (sourceSlot is not (1 or 2)) return null;
        var slotName = sourceSlot == 2 ? "2nd-cache" : "1st-cache";
        return EnsureDir(GetCacheDir() is { } c ? Path.Combine(c, slotName, "imgCache") : null);
    }

    /// <summary>Polaris のログ archive ディレクトリをレジストリから探す。</summary>
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

    /// <summary>DB 保存用に Windows パス区切りをスラッシュへ正規化する。</summary>
    public static string NormalizePathForDb(string path) => path.Replace('\\', '/');

    /// <summary>末尾区切りと相対表記を除いた、比較用の絶対ディレクトリパスを返す。</summary>
    internal static string NormalizeDirectoryPath(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>大文字小文字や末尾区切りの違いを無視して、同じディレクトリか判定する。</summary>
    internal static bool AreSameDirectory(string first, string second)
        => string.Equals(
            NormalizeDirectoryPath(first),
            NormalizeDirectoryPath(second),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 2 つのディレクトリが同一、または一方が他方の配下なら true を返す。
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

    /// <summary>ディレクトリを作成してからパスを返す。作成できない場合は null を返す。</summary>
    private static string? EnsureDir(string? path)
    {
        if (path is null) return null;
        try { Directory.CreateDirectory(path); return path; }
        catch { return null; }
    }
}
