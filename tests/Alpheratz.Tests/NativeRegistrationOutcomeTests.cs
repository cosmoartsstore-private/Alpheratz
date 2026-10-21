using Alpheratz.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace Alpheratz.Tests;

public sealed partial class NativeAppConfigTests
{
    /// <summary>登録UIが非表示でも存在する公開serviceの保存先選択・登録・解除を隔離HKCUで検証する。</summary>
    [Fact]
    public Task RegistrationPublicEntryPrefersInstalledDatabasePreservesOtherRowsAndRetriesAfterLock() => RunIsolated(fixture =>
    {
        var install = Path.Combine(fixture.RootDirectory, "StellaRecord"); var directory = Path.Combine(install, "Data", "db"); Directory.CreateDirectory(directory);
        var dbPath = Path.Combine(directory, "stellarecord.db"); var fallback = Path.Combine(fixture.RootDirectory, "fallback.db");
        StellaRecordRegistration.RegisterToDatabase(dbPath, "保持する他app.exe"); StellaRecordRegistration.RegisterToDatabase(fallback, "fallbackの他app.exe");
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\CosmoArtsStore\StellaRecord"); key.SetValue("InstallLocation", install); key.SetValue("DbPath", fallback);
        Assert.True(StellaRecordRegistration.IsStellaRecordAvailable()); var fallbackBytes = ReadShared(fallback);
        var executable = Environment.ProcessPath!;
        StellaRecordRegistration.Register(executable); StellaRecordRegistration.Register(executable);
        Assert.Equal(new[] { "保持する他app.exe", executable }.Order(), Paths(dbPath).Order()); Assert.Equal(fallbackBytes, ReadShared(fallback));
        using (var pool = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString())) SqliteConnection.ClearPool(pool);
        using (var locked = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.None)) { StellaRecordRegistration.Register("再試行.exe"); StellaRecordRegistration.Unregister(); }
        Assert.Equal(new[] { "保持する他app.exe", executable }.Order(), Paths(dbPath).Order());
        StellaRecordRegistration.Register("再試行.exe"); Assert.Contains("再試行.exe", Paths(dbPath));
        StellaRecordRegistration.Unregister(); Assert.DoesNotContain(executable, Paths(dbPath)); Assert.Contains("保持する他app.exe", Paths(dbPath)); Assert.Contains("再試行.exe", Paths(dbPath));
        Assert.Equal(fallbackBytes, ReadShared(fallback));
        key.DeleteValue("InstallLocation"); Assert.True(StellaRecordRegistration.IsStellaRecordAvailable()); StellaRecordRegistration.Register(executable);
        Assert.Contains(executable, Paths(fallback)); StellaRecordRegistration.Unregister(); Assert.Equal(["fallbackの他app.exe"], Paths(fallback));
        key.DeleteValue("DbPath"); Assert.False(StellaRecordRegistration.IsStellaRecordAvailable()); StellaRecordRegistration.Register(executable); StellaRecordRegistration.Unregister();

        static string[] Paths(string path)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = "SELECT path FROM apps ORDER BY id"; using var reader = command.ExecuteReader(); var values = new List<string>();
            while (reader.Read()) values.Add(reader.GetString(0)); return values.ToArray();
        }
        static byte[] ReadShared(string path) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var bytes = new MemoryStream(); file.CopyTo(bytes); return bytes.ToArray(); }
    });
}
