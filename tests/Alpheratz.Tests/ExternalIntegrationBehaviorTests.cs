using Alpheratz.Services;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>
/// Tests local persistence used by optional integrations.
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
        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe", Path.Combine(tempDir, "missing.png"));

        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*), max(path), max(icon IS NULL) FROM apps WHERE path = 'F:/apps/Alpheratz.exe'";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("F:/apps/Alpheratz.exe", reader.GetString(1));
        Assert.Equal(1, reader.GetInt32(2));
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
    public void StellaRecordRegistration_UnregisterFromDatabaseRemovesOnlyAlpheratzAndIgnoresMissingDatabase()
    {
        var dbPath = Path.Combine(tempDir, "stella.sqlite3");
        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe");
        InsertLegacyAlpheratzApp(dbPath);
        InsertOtherApp(dbPath);

        StellaRecordRegistration.UnregisterFromDatabase(dbPath, "F:/apps/Alpheratz.exe");
        StellaRecordRegistration.UnregisterFromDatabase(Path.Combine(tempDir, "missing.sqlite3"));

        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM apps ORDER BY name";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("OtherApp", reader.GetString(0));
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
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void InsertOtherApp(string dbPath)
    {
        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO apps (name, description, path, icon)
            VALUES ('OtherApp', 'other', 'F:/other.exe', NULL)
            """;
        cmd.ExecuteNonQuery();
    }

    private static void InsertLegacyAlpheratzApp(string dbPath)
    {
        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO apps (name, description, path, icon)
            VALUES ($name, 'legacy', 'F:/old/Alpheratz.exe', NULL)
            """;
        cmd.Parameters.AddWithValue("$name", LegacyLauncherName());
        cmd.ExecuteNonQuery();
    }

    private static string LegacyLauncherName() => "Alpheratz " + "v" + "2";
}
