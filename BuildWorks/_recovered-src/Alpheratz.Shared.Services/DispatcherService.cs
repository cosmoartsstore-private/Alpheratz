using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Dispatching;

namespace Alpheratz.Shared.Services;

public sealed class DispatcherService
{
	private readonly DispatcherQueue dispatcherQueue;

	public DispatcherService(DispatcherQueue dispatcherQueue)
	{
		this.dispatcherQueue = dispatcherQueue;
	}

	public void requestAnimationFrame(Action action)
	{
		try
		{
			dispatcherQueue.TryEnqueue(delegate
			{
				try
				{
					action();
				}
				catch (Exception value2)
				{
					AppLogger.Error($"DispatcherService.requestAnimationFrame.action: threw: {value2}");
				}
			});
		}
		catch (Exception value)
		{
			AppLogger.Error($"DispatcherService.requestAnimationFrame: threw: {value}");
		}
	}

	public Task RunOnUiThread(Action action)
	{
		if (dispatcherQueue.HasThreadAccess)
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				AppLogger.Error($"DispatcherService.RunOnUiThread.inline: threw: {ex}");
				return Task.FromException(ex);
			}
			return Task.CompletedTask;
		}
		TaskCompletionSource tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!dispatcherQueue.TryEnqueue(delegate
		{
			try
			{
				action();
				tcs.SetResult();
			}
			catch (Exception ex2)
			{
				AppLogger.Error($"DispatcherService.RunOnUiThread.action: threw: {ex2}");
				tcs.SetException(ex2);
			}
		}))
		{
			AppLogger.Error("DispatcherService.RunOnUiThread: TryEnqueue failed");
			tcs.SetException(new InvalidOperationException("DispatcherQueue が利用できません"));
		}
		return tcs.Task;
	}

	public async Task setTimeout(Action action, int milliseconds)
	{
		try
		{
			await Task.Delay(milliseconds).ConfigureAwait(continueOnCapturedContext: false);
			dispatcherQueue.TryEnqueue(delegate
			{
				try
				{
					action();
				}
				catch (Exception value2)
				{
					AppLogger.Error($"DispatcherService.setTimeout.action: threw: {value2}");
				}
			});
		}
		catch (Exception value)
		{
			AppLogger.Error($"DispatcherService.setTimeout: threw: {value}");
		}
	}
}
