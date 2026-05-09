using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Alpheratz.Core;

public static class AppLogger
{
    private static readonly string _fallbackLogPath = GetFallbackLogPath();
    private static readonly ConcurrentQueue<string> _queue = new();
    private static int _flushing;

    public static void Warn(string message) => Enqueue("WARN", message);
    public static void Error(string message) => Enqueue("ERROR", message);
    public static void Info(string message) => Enqueue("INFO", message);

    [Conditional("TRACE_LOGGING")]
    public static void Trace(string message) => Enqueue("TRACE", message);

    private static string GetFallbackLogPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "CosmoArtsStore", "Alpheratz", "Data", "logs");
        try { Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, "info.log");
    }

    private static void Enqueue(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        _queue.Enqueue(line);

        if (Interlocked.CompareExchange(ref _flushing, 1, 0) == 0)
        {
            ThreadPool.QueueUserWorkItem(_ => Flush());
        }
    }

    private static void Flush()
    {
        try
        {
            var logDir = AppPaths.GetLogDir();
            var path = logDir is not null ? Path.Combine(logDir, "info.log") : _fallbackLogPath;

            using var writer = new StreamWriter(path, append: true, System.Text.Encoding.UTF8);
            while (_queue.TryDequeue(out var line))
            {
                writer.WriteLine(line);
            }
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
            if (!_queue.IsEmpty && Interlocked.CompareExchange(ref _flushing, 1, 0) == 0)
            {
                ThreadPool.QueueUserWorkItem(_ => Flush());
            }
        }
    }
}
