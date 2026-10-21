using System.Diagnostics;
using System.Text;
using Alpheratz.Core;

namespace Alpheratz.Tests;

/// <summary>実際のlog workerをfixture固有のDataへ向け、記録内容とrotationを検証する。</summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class AppLoggerTests
{
    [Fact]
    public async Task Fatal_WritesUtf8TruncatesLongMessagesAndAllowsNull()
    {
        var path = GetLogPath();
        File.WriteAllText(path, string.Empty, Encoding.UTF8);
        var marker = Guid.NewGuid().ToString("N");
        var longMessage = marker + new string('あ', 8001);
        AppLogger.Fatal($"{marker}:日本語の記録");
        AppLogger.Fatal(longMessage);
        AppLogger.Fatal(null!);
        AppLogger.Fatal($"{marker}:完了");

        var content = await ReadAfterFlush(path, value => value.Contains($"{marker}:完了", StringComparison.Ordinal));
        Assert.Contains($"[FATAL] {marker}:日本語の記録", content);
        Assert.Contains($"[FATAL] {longMessage[..8000]}...<truncated>", content);
        Assert.DoesNotContain(longMessage, content);
        Assert.Contains("[FATAL] " + Environment.NewLine, content);
        Assert.Matches(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[FATAL\]", content);
    }

    [Theory]
    [InlineData(1024 * 1024, false)]
    [InlineData(1024 * 1024 + 1, true)]
    public async Task Fatal_RotatesOnlyAboveTheByteLimit(int length, bool rotates)
    {
        var path = GetLogPath();
        var rotatedPath = path + ".1";
        File.WriteAllBytes(path, Enumerable.Repeat((byte)'x', length).ToArray());
        File.WriteAllText(rotatedPath, "旧backup", Encoding.UTF8);
        var marker = Guid.NewGuid().ToString("N");
        AppLogger.Fatal(marker);
        var content = await ReadAfterFlush(path, value => value.Contains(marker, StringComparison.Ordinal));

        if (rotates)
        {
            Assert.Equal(length, new FileInfo(rotatedPath).Length);
            Assert.Equal(new string('x', length), File.ReadAllText(rotatedPath, Encoding.UTF8));
            Assert.DoesNotContain(new string('x', 100), content);
        }
        else
        {
            Assert.StartsWith(new string('x', length), content);
            Assert.Equal("旧backup", File.ReadAllText(rotatedPath, Encoding.UTF8));
        }
    }

    [Fact]
    public async Task Fatal_TruncatesTheCurrentLogWhenRotationDestinationIsADirectory()
    {
        var path = GetLogPath();
        var rotatedPath = path + ".1";
        if (File.Exists(rotatedPath)) File.Delete(rotatedPath);
        Directory.CreateDirectory(rotatedPath);
        var retained = Path.Combine(rotatedPath, "保持.txt");
        File.WriteAllText(retained, "保持", Encoding.UTF8);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)'x', 1024 * 1024 + 1).ToArray());
        try
        {
            var marker = Guid.NewGuid().ToString("N");
            AppLogger.Fatal(marker);
            var content = await ReadAfterFlush(path, value => value.Contains(marker, StringComparison.Ordinal));
            Assert.DoesNotContain(new string('x', 100), content);
            Assert.Equal("保持", File.ReadAllText(retained, Encoding.UTF8));
        }
        finally
        {
            File.Delete(retained);
            Directory.Delete(rotatedPath);
        }
    }

    [Fact]
    public async Task Fatal_ConcurrentMessagesAreWrittenOnceWithoutCorruption()
    {
        var path = GetLogPath();
        File.WriteAllText(path, string.Empty, Encoding.UTF8);
        var marker = Guid.NewGuid().ToString("N");
        Parallel.For(0, 100, index => AppLogger.Fatal($"{marker}:{index:D3}:記録"));
        var content = await ReadAfterFlush(path, value => value.Split(Environment.NewLine).Count(line => line.Contains(marker, StringComparison.Ordinal)) == 100);
        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < 100; index++) Assert.Single(lines, line => line.EndsWith($"[FATAL] {marker}:{index:D3}:記録", StringComparison.Ordinal));
    }

    /// <summary>保存先lock中にqueueが満杯になっても、解除後の新しい通知で保持済みlogを保存できる。</summary>
    [Fact]
    public async Task Fatal_FullQueueAfterWriteFailureCanFlushRetainedMessagesWhenDestinationRecovers()
    {
        const string markerVariable = "ALPHERATZ_LOGGER_FULL_QUEUE_CHILD";
        if (Environment.GetEnvironmentVariable(markerVariable) != "1")
        {
            await NativeDispatcherQueueTests.RunChildProcess(typeof(AppLoggerTests), nameof(Fatal_FullQueueAfterWriteFailureCanFlushRetainedMessagesWhenDestinationRecovers), markerVariable); return;
        }
        var path = GetLogPath(); File.WriteAllText(path, "保持する既存log\n", Encoding.UTF8);
        var suppress = typeof(AppLogger).GetField("_suppressFlushUntilUtcTicks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var marker = Guid.NewGuid().ToString("N");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            AppLogger.Fatal(marker + ":first");
            var timer = Stopwatch.StartNew();
            while ((long)suppress.GetValue(null)! == 0 && timer.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
            Assert.True((long)suppress.GetValue(null)! > 0, "保存先lockの故障が発生しません。");
            for (var index = 0; index < 511; index++) AppLogger.Fatal($"{marker}:{index:D3}");
        }
        // 待機時間そのものは製品契約ではない。故障後の抑止期間終了を再現して再操作する。
        suppress.SetValue(null, 0L); AppLogger.Fatal(marker + ":retry-trigger");
        var content = await ReadAfterFlush(path, text => text.Contains(marker + ":510", StringComparison.Ordinal));
        Assert.StartsWith("保持する既存log", content); Assert.Contains(marker + ":first", content);
        for (var index = 0; index < 511; index++) Assert.Single(content.Split(Environment.NewLine), line => line.EndsWith($"[FATAL] {marker}:{index:D3}", StringComparison.Ordinal));
        AppLogger.Fatal(marker + ":after-recovery"); Assert.Contains(marker + ":after-recovery", await ReadAfterFlush(path, text => text.Contains(marker + ":after-recovery", StringComparison.Ordinal)));
        await File.WriteAllTextAsync(Environment.GetEnvironmentVariable(NativeDispatcherQueueTests.ReceiptVariable)!, "completed");
    }

    private static string GetLogPath() => Path.Combine(AppPaths.GetLogDir() ?? throw new InvalidOperationException("fixtureのlog directoryを取得できません"), "info.log");

    private static async Task<string> ReadAfterFlush(string path, Func<string, bool> isComplete)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            try
            {
                string content;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8)) content = reader.ReadToEnd();
                if (isComplete(content))
                {
                    // 書込みを妨げずに完了記録を待ち、その後にhandleの解放を確認する。
                    using var finished = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return content;
                }
            }
            catch (IOException) { }
            await Task.Delay(10);
        }
        throw new TimeoutException("fixtureへのlog flushが完了しませんでした");
    }
}
