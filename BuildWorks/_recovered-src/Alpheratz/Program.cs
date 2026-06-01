using System;
using System.Threading;
using Alpheratz.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Alpheratz;

internal class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		try
		{
			Application.Start(delegate
			{
				try
				{
					SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
					new App();
				}
				catch (Exception value2)
				{
					AppLogger.Error($"Program.Main.AppStart: fatal: {value2}");
					throw;
				}
			});
		}
		catch (Exception value)
		{
			AppLogger.Error($"Program.Main: Application.Start threw: {value}");
			throw;
		}
	}
}
