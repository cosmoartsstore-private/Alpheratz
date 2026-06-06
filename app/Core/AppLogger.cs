using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Alpheratz.Core;

public static class AppLogger
{
    private const int MaxQueuedLines = 512;
    private const int MaxMessageChars = 8000;
    private const long MaxLogBytes = 1024 * 1024;
    private static readonly string _fallbackLogPath = GetFallbackLogPath();
    private static readonly ConcurrentQueue<string> _queue = new();
    private static readonly bool _verboseLogs =
        string.Equals(Environment.GetEnvironmentVariable("ALPHERATZ_VERBOSE_LOGS"), "1", StringComparison.OrdinalIgnoreCase);

    private static int _flushing;
    private static int _queuedLines;
    private static long _suppressFlushUntilUtcTicks;

    public static bool IsTraceEnabled =>
#if TRACE_LOGGING
        true;
#else
        false;
#endif

    public static void Warn(string message)
    {
        if (_verboseLogs) Enqueue("WARN", message);
    }

    public static void Error(string message)
    {
        if (_verboseLogs) Enqueue("ERROR", message);
    }

    public static void Fatal(string message) => Enqueue("FATAL", message);

    public static void Info(string message)
    {
        if (_verboseLogs) Enqueue("INFO", message);
    }

    [Conditional("TRACE_LOGGING")]
    public static void Trace(string message)
    {
        if (_verboseLogs) Enqueue("TRACE", message);
    }

    private static string GetFallbackLogPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "CosmoArtsStore", "Alpheratz", "Data", "logs");
        try { Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, "info.log");
    }

    private static void Enqueue(string level, string message)
    {
        if (Interlocked.Increment(ref _queuedLines) > MaxQueuedLines)
        {
            Interlocked.Decrement(ref _queuedLines);
            return;
        }

        var rawMessage = message ?? string.Empty;
        var safeMessage = rawMessage.Length <= MaxMessageChars
            ? rawMessage
            : string.Concat(rawMessage.AsSpan(0, MaxMessageChars), "...<truncated>");
        var line = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] [{level}] {safeMessage}";
        _queue.Enqueue(line);
        TryScheduleFlush();
    }

    private static void TryScheduleFlush()
    {
        if (DateTime.UtcNow.Ticks < Volatile.Read(ref _suppressFlushUntilUtcTicks)) return;

        if (Interlocked.CompareExchange(ref _flushing, 1, 0) == 0)
        {
            ThreadPool.QueueUserWorkItem(_ => Flush());
        }
    }

    private static void Flush()
    {
        var pending = new List<string>();
        var writeFailed = false;

        try
        {
            var logDir = AppPaths.GetLogDir();
            var path = logDir is not null ? Path.Combine(logDir, "info.log") : _fallbackLogPath;
            EnsureDirectoryFor(path);
            RotateIfOverBudget(path);

            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(fs, Encoding.UTF8);
            while (_queue.TryDequeue(out var line))
            {
                Interlocked.Decrement(ref _queuedLines);
                pending.Add(line);
                writer.WriteLine(line);
            }
        }
        catch
        {
            writeFailed = true;
            Volatile.Write(ref _suppressFlushUntilUtcTicks, DateTime.UtcNow.AddSeconds(30).Ticks);
            foreach (var line in pending) Requeue(line);
        }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
            if (!writeFailed && !_queue.IsEmpty)
            {
                TryScheduleFlush();
            }
        }
    }

    private static void Requeue(string line)
    {
        if (Interlocked.Increment(ref _queuedLines) > MaxQueuedLines)
        {
            Interlocked.Decrement(ref _queuedLines);
            return;
        }

        _queue.Enqueue(line);
    }

    private static void EnsureDirectoryFor(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        }
        catch
        {
        }
    }

    private static void RotateIfOverBudget(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaxLogBytes) return;

            var rotatedPath = path + ".1";
            try
            {
                if (File.Exists(rotatedPath)) File.Delete(rotatedPath);
                File.Move(path, rotatedPath);
            }
            catch
            {
                File.WriteAllText(path, string.Empty, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
