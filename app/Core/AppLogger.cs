using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Alpheratz.Core;

/// <summary>
/// 単一プロセス内のアプリケーションログを ConcurrentQueue にためて、別スレッドで
/// ファイルに flush する非同期ロガー。
/// Trace は [Conditional("TRACE_LOGGING")] により、TRACE_LOGGING シンボルが定義されていない
/// ビルドでは呼び出しごと（引数の文字列補間も含めて）C# コンパイラレベルで消える。
/// このため Trace を hot path に置いても Release ビルドでは GC プレッシャを生まない。
/// </summary>
public static class AppLogger
{
    private static readonly string _fallbackLogPath = GetFallbackLogPath();
    private static readonly ConcurrentQueue<string> _queue = new();
    private static int _flushing;

    /// <summary>TRACE_LOGGING シンボルの有無を実行時に取得（呼出側ガード用）。</summary>
    public static bool IsTraceEnabled =>
#if TRACE_LOGGING
        true;
#else
        false;
#endif

    public static void Warn(string message) => Enqueue("WARN", message);
    public static void Error(string message) => Enqueue("ERROR", message);
    public static void Info(string message) => Enqueue("INFO", message);

    /// <summary>
    /// 詳細トレース。TRACE_LOGGING シンボルが定義されたビルドのみで実行される
    /// （引数の評価も含めてコンパイラが call site を消す）。Release ビルドではコストゼロ。
    /// </summary>
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
        // 地域設定で format 解釈が揺れないよう InvariantCulture で固定する。
        var line = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] [{level}] {message}";
        _queue.Enqueue(line);

        if (Interlocked.CompareExchange(ref _flushing, 1, 0) == 0)
        {
            ThreadPool.QueueUserWorkItem(_ => Flush());
        }
    }

    private static void Flush()
    {
        // dequeue したログ行は writer の Dispose 直前まで保持しておく。
        // ファイル I/O 例外で writer が落ちたとき、キューから消えたまま捨てられないように
        // pending に積んでおき、失敗時には ConcurrentQueue へ戻す。
        var pending = new List<string>();
        try
        {
            var logDir = AppPaths.GetLogDir();
            var path = logDir is not null ? Path.Combine(logDir, "info.log") : _fallbackLogPath;

            // 同一プロセス内のリーダー（外部ツールでの tail 等）が握っていてもログを書き続けたい。
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8);
            while (_queue.TryDequeue(out var line))
            {
                pending.Add(line);
                writer.WriteLine(line);
            }
        }
        catch
        {
            // 書き込みに失敗した分はキューへ戻して次回 Flush に再挑戦する。
            // 失敗時にここで握りつぶしていた行が永久に失われていたのを修正。
            foreach (var line in pending) _queue.Enqueue(line);
        }
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