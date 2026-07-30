using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Services;

/// <summary>
/// StellaRecord のローカルランチャー DB へ Alpheratz を登録・解除する。
/// 任意連携のため、public API では連携失敗を呼び出し元へ伝播しない。
/// </summary>
internal static class StellaRecordRegistration
{
    private const string AppName = "Alpheratz";
    private const string LegacyAppName = "Alpheratz " + "v" + "2";
    private static string AppDescription => getMsg("StellaRecordRegistration.appDescription");
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

    /// <summary>
    /// StellaRecord の apps テーブルで確認した一意制約の形を表す。
    /// </summary>
    private enum AppsSchemaKind
    {
        /// <summary>現行 schema。path が UNIQUE で、name は重複を許可する。</summary>
        CurrentPathUnique,

        /// <summary>旧 schema。category 列を持ち、path UNIQUE ではない。</summary>
        LegacyCategory,

        /// <summary>既知の UNIQUE 制約が見つからない場合の保守的な fallback。</summary>
        PathDeduplication,
    }

    /// <summary>
    /// レジストリから StellaRecord DB を解決し、指定された実行ファイルを登録する。
    /// 任意連携のため、レジストリ・DB・ファイル操作の失敗は握りつぶす。
    /// </summary>
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
            // StellaRecord 未導入環境でも本体起動を妨げない。
        }
    }

    /// <summary>
    /// レジストリから StellaRecord DB を解決し、現在の Alpheratz 登録を削除する。
    /// 任意連携のため、削除失敗は呼び出し元へ伝播しない。
    /// </summary>
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

    /// <summary>
    /// StellaRecord DB ファイルを解決できるか確認する。
    /// 任意連携のため、確認中の失敗は false として扱う。
    /// </summary>
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

    /// <summary>
    /// 指定 DB へ Alpheratz を登録する。
    /// レジストリ解決後の実 DB と、呼び出し元が明示した DB の両方を同じ手順で更新する。
    /// </summary>
    internal static void RegisterToDatabase(string dbPath, string exePath, string? iconPath = null)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return;

        byte[]? iconData = null;
        if (iconPath is not null && File.Exists(iconPath))
            iconData = File.ReadAllBytes(iconPath);

        using var conn = OpenConnection(dbPath);
        EnsureAppsTable(conn);
        using var transaction = conn.BeginTransaction();
        RemoveLegacyAppName(conn, transaction);

        var schemaKind = DetectAppsSchema(conn);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = BuildRegistrationSql(schemaKind);

        cmd.Parameters.AddWithValue("$name", AppName);
        cmd.Parameters.AddWithValue("$description", AppDescription);
        cmd.Parameters.AddWithValue("$path", exePath);
        cmd.Parameters.AddWithValue("$icon", (object?)iconData ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>
    /// 指定 DB から Alpheratz の登録を削除する。旧 launcher 名の残存行も同時に削除する。
    /// </summary>
    internal static void UnregisterFromDatabase(string dbPath, string? exePath = null)
    {
        if (!File.Exists(dbPath)) return;

        using var conn = OpenConnection(dbPath);
        var schemaKind = DetectAppsSchema(conn);
        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            cmd.CommandText = "DELETE FROM apps WHERE name = $name OR name = $legacyName";
            cmd.Parameters.AddWithValue("$name", AppName);
            cmd.Parameters.AddWithValue("$legacyName", LegacyAppName);
        }
        else
        {
            cmd.CommandText = BuildUnregistrationSql(schemaKind);
            cmd.Parameters.AddWithValue("$path", exePath);
            cmd.Parameters.AddWithValue("$name", AppName);
            cmd.Parameters.AddWithValue("$legacyName", LegacyAppName);
        }
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// StellaRecord DB として参照できるファイルが存在するか確認する。
    /// </summary>
    internal static bool IsDatabaseAvailable(string? dbPath)
    {
        return dbPath is not null && File.Exists(dbPath);
    }

    /// <summary>
    /// SQLite 接続を開き、StellaRecord 連携用の待機時間を設定する。
    /// </summary>
    private static SqliteConnection OpenConnection(string dbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        var conn = new SqliteConnection(connectionString);
        try
        {
            conn.Open();

            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();

            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    /// <summary>
    /// DB が未作成の場合に、現行 StellaRecord schema と同じ apps テーブルを作成する。
    /// </summary>
    private static void EnsureAppsTable(SqliteConnection conn)
    {
        using var createCmd = conn.CreateCommand();
        createCmd.CommandText = CreateTableSql;
        createCmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 旧 Alpheratz v2 登録名の行を同じ登録トランザクション内で削除する。
    /// </summary>
    private static void RemoveLegacyAppName(SqliteConnection conn, SqliteTransaction transaction)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "DELETE FROM apps WHERE name = $legacyName";
        cmd.Parameters.AddWithValue("$legacyName", LegacyAppName);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// apps テーブルの一意制約から登録方式を判定する。
    /// 現行 schema は path UNIQUE、旧 schema は category 列の有無で扱う。
    /// </summary>
    private static AppsSchemaKind DetectAppsSchema(SqliteConnection conn)
    {
        if (HasSingleColumnUniqueIndex(conn, "apps", "path"))
            return AppsSchemaKind.CurrentPathUnique;

        if (HasColumn(conn, "apps", "category"))
            return AppsSchemaKind.LegacyCategory;

        return AppsSchemaKind.PathDeduplication;
    }

    /// <summary>
    /// 判定済み schema に対応する登録 SQL を組み立てる。
    /// </summary>
    private static string BuildRegistrationSql(AppsSchemaKind schemaKind)
    {
        if (schemaKind == AppsSchemaKind.CurrentPathUnique)
        {
            // 現行 StellaRecord schema は path が一意キーで、name は一意ではない。
            // 同じ path の再登録だけを更新し、別 path の同名行は schema の許容範囲として残す。
            return """
                INSERT INTO apps (name, description, path, icon)
                VALUES ($name, $description, $path, $icon)
                ON CONFLICT(path) DO UPDATE SET
                    name = excluded.name,
                    description = excluded.description,
                    icon = excluded.icon
                """;
        }

        if (schemaKind == AppsSchemaKind.LegacyCategory)
        {
            // 旧 StellaRecord schema は category 列を持ち、path UNIQUE ではない。
            // name UNIQUE の有無に依存せず、現行名の Alpheratz 行を置き換える。
            return """
                DELETE FROM apps WHERE name = $name;
                INSERT INTO apps (name, description, path, category, icon)
                VALUES ($name, $description, $path, 'fastparty', $icon)
                """;
        }

        // 想定外 schema では path の重複だけを先に消し、同じ exePath の多重登録を避ける。
        return """
            DELETE FROM apps WHERE path = $path;
            INSERT INTO apps (name, description, path, icon)
            VALUES ($name, $description, $path, $icon)
            """;
    }

    /// <summary>
    /// 判定済み schema に対応する登録解除 SQL を組み立てる。
    /// </summary>
    private static string BuildUnregistrationSql(AppsSchemaKind schemaKind)
    {
        if (schemaKind == AppsSchemaKind.LegacyCategory)
        {
            // 旧 schema は path が一意キーではないため、path が変わっていても現行名を削除対象に含める。
            return "DELETE FROM apps WHERE path = $path OR name = $name OR name = $legacyName";
        }

        // path が唯一確認できる識別子である schema では、同名の別 path を別インストールとして扱う。
        // path UNIQUE が確認できない fallback でも、削除範囲を広げず指定 path と旧名の残骸だけを消す。
        return "DELETE FROM apps WHERE path = $path OR name = $legacyName";
    }

    /// <summary>
    /// 指定テーブルに指定列が存在するか PRAGMA table_info で確認する。
    /// </summary>
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

    /// <summary>
    /// 指定列だけを対象にする非 partial の UNIQUE index または UNIQUE 制約が存在するか確認する。
    /// </summary>
    private static bool HasSingleColumnUniqueIndex(SqliteConnection conn, string table, string column)
    {
        var uniqueIndexes = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA index_list({QuoteIdentifier(table)})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var isUnique = !reader.IsDBNull(2) && reader.GetInt64(2) == 1;
                var isPartial = !reader.IsDBNull(4) && reader.GetInt64(4) == 1;
                if (isUnique && !isPartial)
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
                if (!reader.IsDBNull(2) && string.Equals(reader.GetString(2), column, StringComparison.OrdinalIgnoreCase))
                    hasTargetColumn = true;
            }
            if (columnCount == 1 && hasTargetColumn)
                return true;
        }

        return false;
    }

    /// <summary>
    /// PRAGMA の identifier を二重引用符で quote する。
    /// PRAGMA では SQL parameter を使えないため、identifier の二重引用符を escape して注入を防ぐ。
    /// </summary>
    private static string QuoteIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// StellaRecord のレジストリから DB パスを解決する。
    /// InstallLocation から見つかる公式配置を優先し、見つからない場合だけ DbPath を参照する。
    /// </summary>
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
