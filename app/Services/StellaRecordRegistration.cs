using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace Alpheratz.Services;

/// <summary>
/// StellaRecord の共有 DB に Alpheratz を登録する補助機能。
/// 連携先が存在しない環境でもアプリ本体は動くため、登録失敗は呼出側へ伝播させない。
/// </summary>
internal static class StellaRecordRegistration
{
    private const string AppName = "Alpheratz v2";
    private const string AppDescription = "VRChatフレンド情報・ログ閲覧（新版）";
    private const string RegKeyPath = @"Software\CosmoArtsStore\StellaRecord";

    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS apps (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            name            TEXT NOT NULL UNIQUE,
            description     TEXT NOT NULL DEFAULT '',
            path            TEXT NOT NULL,
            category        TEXT NOT NULL DEFAULT 'thirdparty' CHECK(category IN ('fastparty', 'thirdparty')),
            icon            BLOB,
            registered_at   DATETIME DEFAULT (datetime('now', 'localtime'))
        );
        """;

    /// <summary>StellaRecord の apps テーブルに実行ファイルパスとアイコンを登録する。</summary>
    public static void Register(string exePath, string? iconPath = null)
    {
        try
        {
            var dbPath = GetDbPath();
            if (dbPath is null) return;
            RegisterToDatabase(dbPath, exePath, iconPath);
        }
        catch
        {
            // StellaRecord 未インストール時は連携だけを諦め、アプリ起動は継続する。
        }
    }

    /// <summary>StellaRecord の apps テーブルから Alpheratz の登録を削除する。</summary>
    public static void Unregister()
    {
        try
        {
            var dbPath = GetDbPath();
            if (dbPath is null) return;
            UnregisterFromDatabase(dbPath);
        }
        catch { }
    }

    /// <summary>StellaRecord の DB パスが設定され、ファイルが存在するかを返す。</summary>
    public static bool IsStellaRecordAvailable()
    {
        try
        {
            return IsDatabaseAvailable(GetDbPath());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>指定 DB へ Alpheratz の登録を作成または更新する。</summary>
    internal static void RegisterToDatabase(string dbPath, string exePath, string? iconPath = null)
    {
        byte[]? iconData = null;
        if (iconPath is not null && File.Exists(iconPath))
            iconData = File.ReadAllBytes(iconPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var createCmd = conn.CreateCommand();
        createCmd.CommandText = CreateTableSql;
        createCmd.ExecuteNonQuery();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO apps (name, description, path, category, icon)
            VALUES ($name, $description, $path, 'fastparty', $icon)
            """;
        cmd.Parameters.AddWithValue("$name", AppName);
        cmd.Parameters.AddWithValue("$description", AppDescription);
        cmd.Parameters.AddWithValue("$path", exePath);
        cmd.Parameters.AddWithValue("$icon", (object?)iconData ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>指定 DB から Alpheratz の登録だけを削除する。</summary>
    internal static void UnregisterFromDatabase(string dbPath)
    {
        if (!File.Exists(dbPath)) return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM apps WHERE name = $name";
        cmd.Parameters.AddWithValue("$name", AppName);
        cmd.ExecuteNonQuery();
    }

    /// <summary>指定された StellaRecord DB パスが実ファイルとして存在するかを返す。</summary>
    internal static bool IsDatabaseAvailable(string? dbPath)
    {
        return dbPath is not null && File.Exists(dbPath);
    }

    /// <summary>StellaRecord がレジストリに保存している DB パスを取得する。</summary>
    private static string? GetDbPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKeyPath);
        return key?.GetValue("DbPath") as string;
    }
}
