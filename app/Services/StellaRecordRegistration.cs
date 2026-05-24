using System;
using System.IO;
using Alpheratz.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace Alpheratz.Services;

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

    public static void Register(string exePath, string? iconPath = null)
    {
        try
        {
            var dbPath = GetDbPath();
            if (dbPath is null) return;

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
        catch (Exception ex)
        {
            // StellaRecord 未インストール / DB 不在で失敗するのは正常系。
            // ただし真の異常 (DB 破損・権限欠如等) の手掛かりを失わないよう警告ログには残す。
            // Unregister 側 (FIX-08) と対称。
            AppLogger.Warn($"StellaRecordRegistration.Register: failed: {ex.Message}");
        }
    }

    public static void Unregister()
    {
        try
        {
            var dbPath = GetDbPath();
            if (dbPath is null) return;
            if (!File.Exists(dbPath)) return;

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM apps WHERE name = $name";
            cmd.Parameters.AddWithValue("$name", AppName);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // StellaRecord 未インストール / DB 不在で失敗するのは正常系。
            // ただし真の異常（DB 破損・権限欠如等）の手掛かりを失わないよう警告ログには残す。
            AppLogger.Warn($"StellaRecordRegistration.Unregister: failed: {ex.Message}");
        }
    }

    public static bool IsStellaRecordAvailable()
    {
        var dbPath = GetDbPath();
        return dbPath is not null && File.Exists(dbPath);
    }

    private static string? GetDbPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKeyPath);
        return key?.GetValue("DbPath") as string;
    }
}
