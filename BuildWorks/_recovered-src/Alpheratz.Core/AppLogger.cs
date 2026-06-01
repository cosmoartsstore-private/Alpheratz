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
	private static readonly string _fallbackLogPath = GetFallbackLogPath();

	private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();

	private static int _flushing;

	public static bool IsTraceEnabled => false;

	public static void Warn(string message)
	{
		Enqueue("WARN", message);
	}

	public static void Error(string message)
	{
		Enqueue("ERROR", message);
	}

	public static void Info(string message)
	{
		Enqueue("INFO", message);
	}

	[Conditional("TRACE_LOGGING")]
	public static void Trace(string message)
	{
		Enqueue("TRACE", message);
	}

	private static string GetFallbackLogPath()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string text = Path.Combine(folderPath, "CosmoArtsStore", "Alpheratz", "Data", "logs");
		try
		{
			Directory.CreateDirectory(text);
		}
		catch
		{
		}
		return Path.Combine(text, "info.log");
	}

	private static void Enqueue(string level, string message)
	{
		string item = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] [{level}] {message}";
		_queue.Enqueue(item);
		if (Interlocked.CompareExchange(ref _flushing, 1, 0) == 0)
		{
			ThreadPool.QueueUserWorkItem(delegate
			{
				Flush();
			});
		}
	}

	private static void Flush()
	{
		List<string> list = new List<string>();
		try
		{
			string logDir = AppPaths.GetLogDir();
			using FileStream stream = new FileStream((logDir != null) ? Path.Combine(logDir, "info.log") : _fallbackLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
			using StreamWriter streamWriter = new StreamWriter(stream, Encoding.UTF8);
			string result;
			while (_queue.TryDequeue(out result))
			{
				list.Add(result);
				streamWriter.WriteLine(result);
			}
		}
		catch
		{
			foreach (string item in list)
			{
				_queue.Enqueue(item);
			}
		}
		finally
		{
			Interlocked.Exchange(ref _flushing, 0);
			if (!_queue.IsEmpty && Interlocked.CompareExchange(ref _flushing, 1, 0) == 0)
			{
				ThreadPool.QueueUserWorkItem(delegate
				{
					Flush();
				});
			}
		}
	}
}
