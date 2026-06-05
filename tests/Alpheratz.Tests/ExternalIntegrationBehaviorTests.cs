using Alpheratz.Services;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>
/// 外部アプリ連携で使う永続化境界を検証するテスト。
///
/// StellaRecord 連携はユーザー環境のレジストリから相手側 DB を見つける任意機能だが、
/// DB へ書くレコードの形式は Alpheratz 側で責任を持つ必要がある。
/// ここではレジストリを使わずテスト専用 SQLite ファイルに対して登録・解除を実行し、
/// 外部連携が利用可能な環境で保存される内容を固定する。
/// </summary>
public sealed class ExternalIntegrationBehaviorTests : IDisposable
{
    private readonly string tempDir;

    /// <summary>
    /// StellaRecord 連携用の一時 DB とアイコンファイルを置くディレクトリを用意する。
    /// テストは実ユーザーの StellaRecord 設定やレジストリに触れない。
    /// </summary>
    public ExternalIntegrationBehaviorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.ExternalIntegration.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    /// <summary>
    /// テストで作成した SQLite DB と補助ファイルを削除する。
    /// 削除失敗は本体仕様ではないため、例外は破棄してテスト結果を優先する。
    /// </summary>
    public void Dispose()
    {
        try { Directory.Delete(tempDir, recursive: true); }
        catch { }
    }

    /// <summary>
    /// StellaRecordRegistration.RegisterToDatabase が apps テーブルを作り、Alpheratz の登録を保存することを確認する。
    ///
    /// 外部 DB が空でも初回登録できるように、テーブル作成と INSERT は同じ経路で実行される。
    /// category は StellaRecord 側の分類に合わせて fastparty とし、アイコンファイルが存在する場合は
    /// BLOB として保存する現在仕様を検証する。
    /// </summary>
    [Fact]
    public void StellaRecordRegistration_RegisterToDatabaseCreatesAppRecordWithIcon()
    {
        var dbPath = Path.Combine(tempDir, "stella.sqlite3");
        var iconPath = Path.Combine(tempDir, "icon.png");
        File.WriteAllBytes(iconPath, [1, 2, 3, 4]);

        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe", iconPath);

        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, description, path, category, length(icon) FROM apps";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Alpheratz v2", reader.GetString(0));
        Assert.Equal("VRChatフレンド情報・ログ閲覧（新版）", reader.GetString(1));
        Assert.Equal("F:/apps/Alpheratz.exe", reader.GetString(2));
        Assert.Equal("fastparty", reader.GetString(3));
        Assert.Equal(4, reader.GetInt32(4));
        Assert.False(reader.Read());
    }

    /// <summary>
    /// 同じアプリ名で再登録した場合に既存レコードが置換され、存在しないアイコンは NULL として保存されることを確認する。
    ///
    /// SettingsPage の登録ボタンは何度押されてもよい操作なので、apps テーブルに重複行を増やしてはいけない。
    /// また配布やユーザー環境の問題でアイコンファイルが無い場合でも、登録自体は継続してパスを更新する。
    /// </summary>
    [Fact]
    public void StellaRecordRegistration_RegisterToDatabaseReplacesExistingRecordAndAllowsMissingIcon()
    {
        var dbPath = Path.Combine(tempDir, "stella.sqlite3");
        var iconPath = Path.Combine(tempDir, "icon.png");
        File.WriteAllBytes(iconPath, [9, 8, 7]);
        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/old/Alpheratz.exe", iconPath);

        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/new/Alpheratz.exe", Path.Combine(tempDir, "missing.png"));

        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*), max(path), max(icon IS NULL) FROM apps WHERE name = 'Alpheratz v2'";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("F:/new/Alpheratz.exe", reader.GetString(1));
        Assert.Equal(1, reader.GetInt32(2));
    }

    /// <summary>
    /// StellaRecordRegistration.UnregisterFromDatabase が Alpheratz の行だけを削除し、DB 不在時は何もしないことを確認する。
    ///
    /// 連携解除は StellaRecord 側の他アプリ登録を壊してはいけない。
    /// さらにユーザーが先に相手 DB を消している場合でも、設定画面の解除操作が例外で止まらない現在仕様を固定する。
    /// </summary>
    [Fact]
    public void StellaRecordRegistration_UnregisterFromDatabaseRemovesOnlyAlpheratzAndIgnoresMissingDatabase()
    {
        var dbPath = Path.Combine(tempDir, "stella.sqlite3");
        StellaRecordRegistration.RegisterToDatabase(dbPath, "F:/apps/Alpheratz.exe");
        InsertOtherApp(dbPath);

        StellaRecordRegistration.UnregisterFromDatabase(dbPath);
        StellaRecordRegistration.UnregisterFromDatabase(Path.Combine(tempDir, "missing.sqlite3"));

        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM apps ORDER BY name";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("OtherApp", reader.GetString(0));
        Assert.False(reader.Read());
    }

    /// <summary>
    /// StellaRecordRegistration.IsDatabaseAvailable が null、存在しないパス、存在する DB を区別することを確認する。
    ///
    /// レジストリから得た値の検証はこの小さな境界へ集約している。
    /// null や削除済み DB を false として扱うことで、連携先が無い環境では登録ボタンを実質的に無効化できる。
    /// </summary>
    [Fact]
    public void StellaRecordRegistration_IsDatabaseAvailableChecksPathExistence()
    {
        var dbPath = Path.Combine(tempDir, "stella.sqlite3");
        File.WriteAllText(dbPath, "");

        Assert.False(StellaRecordRegistration.IsDatabaseAvailable(null));
        Assert.False(StellaRecordRegistration.IsDatabaseAvailable(Path.Combine(tempDir, "missing.sqlite3")));
        Assert.True(StellaRecordRegistration.IsDatabaseAvailable(dbPath));
    }

    /// <summary>
    /// 指定した SQLite DB を開く。
    /// 各テストは読み取り直前に接続を作り、書き込み経路が接続を閉じていることも間接的に確認する。
    /// </summary>
    private static SqliteConnection OpenConnection(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return conn;
    }

    /// <summary>
    /// StellaRecord 側に既に別アプリ登録がある状態を作る。
    /// UnregisterFromDatabase が name 条件で Alpheratz だけを削除することを検証するための補助データ。
    /// </summary>
    private static void InsertOtherApp(string dbPath)
    {
        using var conn = OpenConnection(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO apps (name, description, path, category, icon)
            VALUES ('OtherApp', 'other', 'F:/other.exe', 'thirdparty', NULL)
            """;
        cmd.ExecuteNonQuery();
    }
}
