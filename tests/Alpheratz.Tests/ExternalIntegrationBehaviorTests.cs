using Alpheratz.Services;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>
/// 任意外部連携で使うローカル永続化の振る舞いを確認する。
/// </summary>
public sealed class ExternalIntegrationBehaviorTests : IDisposable
{
    private readonly string tempDir;

    public ExternalIntegrationBehaviorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.ExternalIntegration.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public void StellaRecordRegistration_RegisterToDatabaseCreatesCurrentAppRecordWithIcon()
    {
        var dbPath = Path.Combine(tempDir, "stella.sqlite3");
        var iconPath = Path.Combine(tempDir, "icon.png");
        File.WriteAllBytes(iconPath, [1, 2, 3, 4]);

        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe", iconPath);

        using var conn = OpenConnection(dbPath);
        Assert.False(HasColumn(conn, "apps", "category"));

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, description, path, length(icon) FROM apps";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Alpheratz", reader.GetString(0));
        Assert.Equal("VRChat写真ギャラリー化・ワールドリンク展開サポートアプリ", reader.GetString(1));
        Assert.Equal("F:/apps/Alpheratz.exe", reader.GetString(2));
        Assert.Equal(4, reader.GetInt32(3));
        Assert.False(reader.Read());
    }

    [Fact]
    public void StellaRecordRegistration_RegisterToDatabaseUpdatesSamePathAndAllowsMissingIcon()
    {
        var dbPath = Path.Combine(tempDir, "stella.sqlite3");
        var iconPath = Path.Combine(tempDir, "icon.png");
        File.WriteAllBytes(iconPath, [9, 8, 7]);

        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe", iconPath);
        var firstId = QueryAppId(dbPath, "F:/apps/Alpheratz.exe");

        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe", Path.Combine(tempDir, "missing.png"));
        var secondId = QueryAppId(dbPath, "F:/apps/Alpheratz.exe");

        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*), max(path), max(icon IS NULL) FROM apps WHERE path = 'F:/apps/Alpheratz.exe'";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("F:/apps/Alpheratz.exe", reader.GetString(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public void StellaRecordRegistration_RegisterToDatabaseUsesCurrentPathUniqueSchemaWithoutNameUniqueness()
    {
        var dbPath = Path.Combine(tempDir, "current-stella.sqlite3");
        CreateCurrentAppsTable(dbPath);
        InsertAppRecord(dbPath, "Alpheratz", "old install", "F:/old/Alpheratz.exe");

        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/new/Alpheratz.exe");
        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/new/Alpheratz.exe");

        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                count(*),
                sum(CASE WHEN path = $oldPath THEN 1 ELSE 0 END),
                sum(CASE WHEN path = $newPath THEN 1 ELSE 0 END)
            FROM apps
            WHERE name = $name
            """;
        cmd.Parameters.AddWithValue("$oldPath", "F:/old/Alpheratz.exe");
        cmd.Parameters.AddWithValue("$newPath", "F:/new/Alpheratz.exe");
        cmd.Parameters.AddWithValue("$name", "Alpheratz");
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(2, Convert.ToInt32(reader.GetValue(0)));
        Assert.Equal(1, Convert.ToInt32(reader.GetValue(1)));
        Assert.Equal(1, Convert.ToInt32(reader.GetValue(2)));
    }

    [Fact]
    public void StellaRecordRegistration_RegisterToDatabaseQuotesPragmaIndexNames()
    {
        var dbPath = Path.Combine(tempDir, "quoted-index.sqlite3");
        using (var conn = OpenConnection(dbPath))
        {
            using var create = conn.CreateCommand();
            create.CommandText = """
                CREATE TABLE apps (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    path TEXT NOT NULL,
                    icon BLOB
                )
                """;
            create.ExecuteNonQuery();

            using var index = conn.CreateCommand();
            index.CommandText = $"CREATE UNIQUE INDEX {QuoteIdentifier("ix_apps_path\"; SELECT 1; --")} ON apps(path)";
            index.ExecuteNonQuery();
        }

        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe");

        using var verifyConn = OpenConnection(dbPath);
        using var verify = verifyConn.CreateCommand();
        verify.CommandText = "SELECT count(*) FROM apps WHERE path = $path";
        verify.Parameters.AddWithValue("$path", "F:/apps/Alpheratz.exe");
        Assert.Equal(1, Convert.ToInt32(verify.ExecuteScalar()));
    }

    [Fact]
    public void StellaRecordRegistration_RegisterToDatabaseDoesNotTreatPartialUniquePathAsCurrentSchema()
    {
        var dbPath = Path.Combine(tempDir, "partial-path-index.sqlite3");
        using (var conn = OpenConnection(dbPath))
        {
            using var create = conn.CreateCommand();
            create.CommandText = """
                CREATE TABLE apps (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    path TEXT NOT NULL,
                    icon BLOB
                )
                """;
            create.ExecuteNonQuery();

            using var index = conn.CreateCommand();
            index.CommandText = "CREATE UNIQUE INDEX ix_apps_path_partial ON apps(path) WHERE icon IS NOT NULL";
            index.ExecuteNonQuery();
        }

        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe");
        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe");

        using var verifyConn = OpenConnection(dbPath);
        using var verify = verifyConn.CreateCommand();
        verify.CommandText = "SELECT count(*) FROM apps WHERE path = $path";
        verify.Parameters.AddWithValue("$path", "F:/apps/Alpheratz.exe");
        Assert.Equal(1, Convert.ToInt32(verify.ExecuteScalar()));
    }

    [Fact]
    public void StellaRecordRegistration_RegisterToDatabaseIgnoresExpressionUniqueIndexes()
    {
        var dbPath = Path.Combine(tempDir, "expression-index.sqlite3");
        using (var conn = OpenConnection(dbPath))
        {
            using var create = conn.CreateCommand();
            create.CommandText = """
                CREATE TABLE apps (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    path TEXT NOT NULL,
                    icon BLOB
                )
                """;
            create.ExecuteNonQuery();

            using var index = conn.CreateCommand();
            index.CommandText = "CREATE UNIQUE INDEX ix_apps_lower_path ON apps(lower(path))";
            index.ExecuteNonQuery();
        }

        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe");
        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe");

        using var verifyConn = OpenConnection(dbPath);
        using var verify = verifyConn.CreateCommand();
        verify.CommandText = "SELECT count(*) FROM apps WHERE path = $path";
        verify.Parameters.AddWithValue("$path", "F:/apps/Alpheratz.exe");
        Assert.Equal(1, Convert.ToInt32(verify.ExecuteScalar()));
    }

    [Fact]
    public void StellaRecordRegistration_RegisterToDatabaseSupportsLegacyCategorySchema()
    {
        var dbPath = Path.Combine(tempDir, "legacy-stella.sqlite3");
        using (var conn = OpenConnection(dbPath))
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE apps (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    description TEXT NOT NULL DEFAULT '',
                    path TEXT NOT NULL,
                    category TEXT NOT NULL DEFAULT 'thirdparty',
                    icon BLOB
                )
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText = """
                INSERT INTO apps (name, description, path, category, icon)
                VALUES ($name, 'legacy', 'F:/legacy/Alpheratz.exe', 'fastparty', NULL)
                """;
            cmd.Parameters.AddWithValue("$name", LegacyLauncherName());
            cmd.ExecuteNonQuery();
        }

        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/old/Alpheratz.exe");
        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/new/Alpheratz.exe");

        using var verifyConn = OpenConnection(dbPath);
        using var verify = verifyConn.CreateCommand();
        verify.CommandText = "SELECT count(*), max(path), max(category) FROM apps WHERE name = 'Alpheratz'";
        using var reader = verify.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("F:/new/Alpheratz.exe", reader.GetString(1));
        Assert.Equal("fastparty", reader.GetString(2));

        using var legacy = verifyConn.CreateCommand();
        legacy.CommandText = "SELECT count(*) FROM apps WHERE name = $name";
        legacy.Parameters.AddWithValue("$name", LegacyLauncherName());
        Assert.Equal(0, Convert.ToInt32(legacy.ExecuteScalar()));
    }

    [Fact]
    public void StellaRecordRegistration_RegisterToDatabaseSupportsLegacyCategorySchemaWithoutNameUnique()
    {
        var dbPath = Path.Combine(tempDir, "legacy-category-no-name-unique.sqlite3");
        using (var conn = OpenConnection(dbPath))
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE apps (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    path TEXT NOT NULL,
                    category TEXT NOT NULL,
                    icon BLOB
                )
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText = """
                INSERT INTO apps (name, description, path, category, icon)
                VALUES ('Alpheratz', 'old', 'F:/old/Alpheratz.exe', 'fastparty', NULL)
                """;
            cmd.ExecuteNonQuery();
        }

        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/new/Alpheratz.exe");
        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/new/Alpheratz.exe");

        using var verifyConn = OpenConnection(dbPath);
        using var verify = verifyConn.CreateCommand();
        verify.CommandText = "SELECT count(*), max(path), max(category) FROM apps WHERE name = 'Alpheratz'";
        using var reader = verify.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("F:/new/Alpheratz.exe", reader.GetString(1));
        Assert.Equal("fastparty", reader.GetString(2));
    }

    [Fact]
    public void StellaRecordRegistration_RegisterToDatabaseRollsBackFallbackDeleteWhenInsertFails()
    {
        var dbPath = Path.Combine(tempDir, "fallback-rollback.sqlite3");
        using (var conn = OpenConnection(dbPath))
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE apps (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    path TEXT NOT NULL,
                    icon BLOB,
                    required_value TEXT NOT NULL
                )
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText = """
                INSERT INTO apps (name, description, path, icon, required_value)
                VALUES ('Alpheratz', 'existing', 'F:/apps/Alpheratz.exe', NULL, 'keep')
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText = """
                INSERT INTO apps (name, description, path, icon, required_value)
                VALUES ($legacyName, 'legacy', 'F:/old/Alpheratz.exe', NULL, 'legacy-keep')
                """;
            cmd.Parameters.AddWithValue("$legacyName", LegacyLauncherName());
            cmd.ExecuteNonQuery();
        }

        Assert.Throws<SqliteException>(() =>
            StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe"));

        using var verifyConn = OpenConnection(dbPath);
        using var verify = verifyConn.CreateCommand();
        verify.CommandText = "SELECT count(*), max(required_value) FROM apps WHERE path = 'F:/apps/Alpheratz.exe'";
        using var reader = verify.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("keep", reader.GetString(1));

        using var legacy = verifyConn.CreateCommand();
        legacy.CommandText = "SELECT count(*), max(required_value) FROM apps WHERE name = $legacyName";
        legacy.Parameters.AddWithValue("$legacyName", LegacyLauncherName());
        using var legacyReader = legacy.ExecuteReader();
        Assert.True(legacyReader.Read());
        Assert.Equal(1, legacyReader.GetInt32(0));
        Assert.Equal("legacy-keep", legacyReader.GetString(1));
    }

    [Fact]
    public void StellaRecordRegistration_RegisterToDatabaseRollsBackLegacyCategoryDeleteWhenInsertFails()
    {
        var dbPath = Path.Combine(tempDir, "legacy-category-rollback.sqlite3");
        using (var conn = OpenConnection(dbPath))
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE apps (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    path TEXT NOT NULL,
                    category TEXT NOT NULL,
                    icon BLOB,
                    required_value TEXT NOT NULL
                )
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText = """
                INSERT INTO apps (name, description, path, category, icon, required_value)
                VALUES ('Alpheratz', 'existing', 'F:/old/Alpheratz.exe', 'fastparty', NULL, 'keep')
                """;
            cmd.ExecuteNonQuery();
        }

        Assert.Throws<SqliteException>(() =>
            StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/new/Alpheratz.exe"));

        using var verifyConn = OpenConnection(dbPath);
        using var verify = verifyConn.CreateCommand();
        verify.CommandText = "SELECT count(*), max(path), max(required_value) FROM apps WHERE name = 'Alpheratz'";
        using var reader = verify.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("F:/old/Alpheratz.exe", reader.GetString(1));
        Assert.Equal("keep", reader.GetString(2));
    }

    [Fact]
    public void StellaRecordRegistration_UnregisterFromDatabaseRemovesRequestedPathAndLegacyNameOnly()
    {
        var dbPath = Path.Combine(tempDir, "stella.sqlite3");
        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe");
        InsertAppRecord(dbPath, "Alpheratz", "other install", "F:/other/Alpheratz.exe");
        InsertLegacyAlpheratzApp(dbPath);
        InsertOtherApp(dbPath);

        StellaRecordRegistration.UnregisterFromDatabase(dbPath, "F:/apps/Alpheratz.exe");
        StellaRecordRegistration.UnregisterFromDatabase(Path.Combine(tempDir, "missing.sqlite3"));

        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, path FROM apps ORDER BY name, path";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Alpheratz", reader.GetString(0));
        Assert.Equal("F:/other/Alpheratz.exe", reader.GetString(1));
        Assert.True(reader.Read());
        Assert.Equal("OtherApp", reader.GetString(0));
        Assert.Equal("F:/other.exe", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void StellaRecordRegistration_UnregisterFromDatabaseRemovesLegacyCategoryNameRows()
    {
        var dbPath = Path.Combine(tempDir, "legacy-category-unregister.sqlite3");
        using (var conn = OpenConnection(dbPath))
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE apps (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    path TEXT NOT NULL,
                    category TEXT NOT NULL,
                    icon BLOB
                )
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText = """
                INSERT INTO apps (name, description, path, category, icon)
                VALUES
                    ('Alpheratz', 'old', 'F:/old/Alpheratz.exe', 'fastparty', NULL),
                    ('Alpheratz', 'other', 'F:/other/Alpheratz.exe', 'fastparty', NULL),
                    ($legacyName, 'legacy', 'F:/legacy/Alpheratz.exe', 'fastparty', NULL),
                    ('OtherApp', 'other', 'F:/other.exe', 'fastparty', NULL)
                """;
            cmd.Parameters.AddWithValue("$legacyName", LegacyLauncherName());
            cmd.ExecuteNonQuery();
        }

        StellaRecordRegistration.UnregisterFromDatabase(dbPath, "F:/missing/Alpheratz.exe");

        using var verifyConn = OpenConnection(dbPath);
        using var verify = verifyConn.CreateCommand();
        verify.CommandText = "SELECT name, path FROM apps ORDER BY name, path";
        using var reader = verify.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("OtherApp", reader.GetString(0));
        Assert.Equal("F:/other.exe", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void StellaRecordRegistration_UnregisterFromDatabaseUsesPathOnlyForFallbackSchema()
    {
        var dbPath = Path.Combine(tempDir, "fallback-unregister.sqlite3");
        using (var conn = OpenConnection(dbPath))
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE apps (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    path TEXT NOT NULL,
                    icon BLOB
                )
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText = """
                INSERT INTO apps (name, description, path, icon)
                VALUES
                    ('Alpheratz', 'current', 'F:/apps/Alpheratz.exe', NULL),
                    ('Alpheratz', 'other', 'F:/other/Alpheratz.exe', NULL),
                    ($legacyName, 'legacy', 'F:/legacy/Alpheratz.exe', NULL),
                    ('OtherApp', 'other', 'F:/other.exe', NULL)
                """;
            cmd.Parameters.AddWithValue("$legacyName", LegacyLauncherName());
            cmd.ExecuteNonQuery();
        }

        StellaRecordRegistration.UnregisterFromDatabase(dbPath, "F:/apps/Alpheratz.exe");

        using var verifyConn = OpenConnection(dbPath);
        using var verify = verifyConn.CreateCommand();
        verify.CommandText = "SELECT name, path FROM apps ORDER BY name, path";
        using var reader = verify.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Alpheratz", reader.GetString(0));
        Assert.Equal("F:/other/Alpheratz.exe", reader.GetString(1));
        Assert.True(reader.Read());
        Assert.Equal("OtherApp", reader.GetString(0));
        Assert.Equal("F:/other.exe", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void StellaRecordRegistration_IsDatabaseAvailableChecksPathExistence()
    {
        var dbPath = Path.Combine(tempDir, "stella.sqlite3");
        File.WriteAllText(dbPath, "");

        Assert.False(StellaRecordRegistration.IsDatabaseAvailable(null));
        Assert.False(StellaRecordRegistration.IsDatabaseAvailable(Path.Combine(tempDir, "missing.sqlite3")));
        Assert.True(StellaRecordRegistration.IsDatabaseAvailable(dbPath));
    }

    private static SqliteConnection OpenConnection(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return conn;
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

    private static void CreateCurrentAppsTable(string dbPath)
    {
        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE apps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                description TEXT NOT NULL DEFAULT '',
                path TEXT NOT NULL UNIQUE,
                icon BLOB,
                registered_at DATETIME DEFAULT (datetime('now', 'localtime'))
            )
            """;
        cmd.ExecuteNonQuery();
    }

    private static void InsertOtherApp(string dbPath)
    {
        InsertAppRecord(dbPath, "OtherApp", "other", "F:/other.exe");
    }

    private static void InsertLegacyAlpheratzApp(string dbPath)
    {
        InsertAppRecord(dbPath, LegacyLauncherName(), "legacy", "F:/old/Alpheratz.exe");
    }

    private static void InsertAppRecord(string dbPath, string name, string description, string path)
    {
        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO apps (name, description, path, icon)
            VALUES ($name, $description, $path, NULL)
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$description", description);
        cmd.Parameters.AddWithValue("$path", path);
        cmd.ExecuteNonQuery();
    }

    private static long QueryAppId(string dbPath, string path)
    {
        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM apps WHERE path = $path";
        cmd.Parameters.AddWithValue("$path", path);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string QuoteIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string LegacyLauncherName() => "Alpheratz " + "v" + "2";
}
