using System;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Models;
using Microsoft.UI.Dispatching;

namespace Alpheratz.Shared.Services;

public sealed class ToastService
{
	private static long _idCounter = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;

	public UiObservableCollection<ToastMessage> toasts { get; } = new UiObservableCollection<ToastMessage>();

	public void addToast(string msg, ToastType type = ToastType.info, int duration = 3000)
	{
		try
		{
			long id = Interlocked.Increment(ref _idCounter);
			ToastMessage toast = new ToastMessage(id, msg, type);
			toasts.Add(toast);
			Task.Run(async delegate
			{
				try
				{
					await Task.Delay(duration).ConfigureAwait(continueOnCapturedContext: false);
					DispatcherQueue dispatcherQueue = App.MainWindowInstance?.DispatcherQueue;
					if ((object)dispatcherQueue != null)
					{
						dispatcherQueue.TryEnqueue(delegate
						{
							try
							{
								toasts.Remove(toast);
							}
							catch (Exception value4)
							{
								AppLogger.Error($"ToastService.addToast.remove: threw: {value4}");
							}
						});
						return;
					}
					try
					{
						toasts.Remove(toast);
					}
					catch (Exception value2)
					{
						AppLogger.Error($"ToastService.addToast.remove (no dq): threw: {value2}");
					}
				}
				catch (Exception value3)
				{
					AppLogger.Error($"ToastService.addToast.removalTask: threw: {value3}");
				}
			});
		}
		catch (Exception value)
		{
			AppLogger.Error($"ToastService.addToast: threw: {value}");
		}
	}
}
