using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace Alpheratz.Services;

/// <summary>
/// Registers Alpheratz in StellaRecord's local launcher database.
/// Failures are intentionally swallowed at the public boundary because this is an optional integration.
/// </summary>
internal static class StellaRecordRegistration
{
    private const string AppName = "Alpheratz";
    private const string LegacyAppName = "Alpheratz " + "v" + "2";
    private const string AppDescription = "VRChat写真ギャラリー化・ワールドリンク展開サポートアプリ";
    private const string RegKeyPath = @"Software\CosmoArtsStore\StellaRecord";

    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS apps (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            name            TEXT NOT NULL,
            description     TEXT NOT NULL DEFAULT '',
            path            TEXT NOT NULL UNIQUE,
            icon            BLOB,
            registered_at   DATETIME DEFAULT (datetime('now', 'localtime'))
        );
        """;

    public static void Register(string exePath, string? iconPath = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath)) return;
            var dbPath = GetDbPath();
            if (dbPath is null) return;
            RegisterToDatabase(dbPath, exePath, iconPath);
        }
        catch
        {
            // StellaRecord may not be installed. The main app must keep starting.
        }
    }

    public static void Unregister()
    {
        try
        {
            var dbPath = GetDbPath();
            if (dbPath is null) return;
            UnregisterFromDatabase(dbPath, Environment.ProcessPath);
        }
        catch { }
    }

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

    internal static void RegisterToDatabase(string dbPath, string exePath, string? iconPath = null)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return;

        byte[]? iconData = null;
        if (iconPath is not null && File.Exists(iconPath))
            iconData = File.ReadAllBytes(iconPath);

        using var conn = OpenConnection(dbPath);
        EnsureAppsTable(conn);
        RemoveLegacyAppName(conn);

        var hasCategory = HasColumn(conn, "apps", "category");
        var hasUniquePath = HasUniqueIndexOnColumn(conn, "apps", "path");

        using var cmd = conn.CreateCommand();
        if (hasCategory && !hasUniquePath)
        {
            cmd.CommandText = """
                INSERT OR REPLACE INTO apps (name, description, path, category, icon)
                VALUES ($name, $description, $path, 'fastparty', $icon)
                """;
        }
        else if (hasUniquePath)
        {
            cmd.CommandText = """
                INSERT INTO apps (name, description, path, icon)
                VALUES ($name, $description, $path, $icon)
                ON CONFLICT(path) DO UPDATE SET
                    name = excluded.name,
                    description = excluded.description,
                    icon = excluded.icon
                """;
        }
        else
        {
            cmd.CommandText = """
                DELETE FROM apps WHERE path = $path;
                INSERT INTO apps (name, description, path, icon)
                VALUES ($name, $description, $path, $icon)
                """;
        }

        cmd.Parameters.AddWithValue("$name", AppName);
        cmd.Parameters.AddWithValue("$description", AppDescription);
        cmd.Parameters.AddWithValue("$path", exePath);
        cmd.Parameters.AddWithValue("$icon", (object?)iconData ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    internal static void UnregisterFromDatabase(string dbPath, string? exePath = null)
    {
        if (!File.Exists(dbPath)) return;

        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            cmd.CommandText = "DELETE FROM apps WHERE name = $name OR name = $legacyName";
            cmd.Parameters.AddWithValue("$name", AppName);
            cmd.Parameters.AddWithValue("$legacyName", LegacyAppName);
        }
        else
        {
            cmd.CommandText = "DELETE FROM apps WHERE path = $path OR name = $name OR name = $legacyName";
            cmd.Parameters.AddWithValue("$path", exePath);
            cmd.Parameters.AddWithValue("$name", AppName);
            cmd.Parameters.AddWithValue("$legacyName", LegacyAppName);
        }
        cmd.ExecuteNonQuery();
    }

    internal static bool IsDatabaseAvailable(string? dbPath)
    {
        return dbPath is not null && File.Exists(dbPath);
    }

    private static SqliteConnection OpenConnection(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();

        return conn;
    }

    private static void EnsureAppsTable(SqliteConnection conn)
    {
        using var createCmd = conn.CreateCommand();
        createCmd.CommandText = CreateTableSql;
        createCmd.ExecuteNonQuery();
    }

    private static void RemoveLegacyAppName(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM apps WHERE name = $legacyName";
        cmd.Parameters.AddWithValue("$legacyName", LegacyAppName);
        cmd.ExecuteNonQuery();
    }

    private static bool HasColumn(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool HasUniqueIndexOnColumn(SqliteConnection conn, string table, string column)
    {
        var uniqueIndexes = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA index_list({QuoteIdentifier(table)})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(2) && reader.GetInt64(2) == 1)
                    uniqueIndexes.Add(reader.GetString(1));
            }
        }

        foreach (var indexName in uniqueIndexes)
        {
            var columnCount = 0;
            var hasTargetColumn = false;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA index_info({QuoteIdentifier(indexName)})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                columnCount++;
                if (string.Equals(reader.GetString(2), column, StringComparison.OrdinalIgnoreCase))
                    hasTargetColumn = true;
            }
            if (columnCount == 1 && hasTargetColumn)
                return true;
        }

        return false;
    }

    private static string QuoteIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string? GetDbPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKeyPath);
        var installLocation = key?.GetValue("InstallLocation") as string;
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            var installedDbPath = Path.Combine(installLocation, "Data", "db", "stellarecord.db");
            if (File.Exists(installedDbPath))
                return installedDbPath;
        }

        return key?.GetValue("DbPath") as string;
    }
}
