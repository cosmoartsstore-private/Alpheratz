using System;
using Microsoft.UI.Dispatching;

namespace Alpheratz.Core;

public static class UiThread
{
	public static DispatcherQueue? Queue { get; set; }

	public static void Run(Action action)
	{
		DispatcherQueue queue = Queue;
		if ((object)queue == null || queue.HasThreadAccess)
		{
			action();
		}
		else if (!queue.TryEnqueue(delegate
		{
			action();
		}))
		{
			AppLogger.Warn("UiThread.Run: TryEnqueue failed (DispatcherQueue likely shutting down)");
		}
	}
}
