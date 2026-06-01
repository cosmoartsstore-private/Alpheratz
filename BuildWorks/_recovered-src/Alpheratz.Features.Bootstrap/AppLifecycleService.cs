using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;

namespace Alpheratz.Features.Bootstrap;

public sealed class AppLifecycleService : INotifyPropertyChanged
{
	private readonly object gate = new object();

	private AppLifecyclePhase currentPhase;

	public AppLifecyclePhase CurrentPhase
	{
		get
		{
			lock (gate)
			{
				return currentPhase;
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public event EventHandler<AppLifecyclePhase>? PhaseAdvanced;

	public void advanceTo(AppLifecyclePhase next)
	{
		lock (gate)
		{
			if (next <= currentPhase)
			{
				return;
			}
			_ = currentPhase;
			currentPhase = next;
		}
		try
		{
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CurrentPhase"));
		}
		catch (Exception value)
		{
			AppLogger.Error($"AppLifecycleService.advanceTo: PropertyChanged subscriber threw: {value}");
		}
		try
		{
			this.PhaseAdvanced?.Invoke(this, next);
		}
		catch (Exception value2)
		{
			AppLogger.Error($"AppLifecycleService.advanceTo: PhaseAdvanced subscriber threw: {value2}");
		}
	}

	public bool isAtLeast(AppLifecyclePhase phase)
	{
		lock (gate)
		{
			return currentPhase >= phase;
		}
	}

	public Task waitForAsync(AppLifecyclePhase phase, CancellationToken ct = default(CancellationToken))
	{
		if (isAtLeast(phase))
		{
			return Task.CompletedTask;
		}
		TaskCompletionSource tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		EventHandler<AppLifecyclePhase> handler = null;
		handler = delegate(object? _, AppLifecyclePhase p)
		{
			if (p >= phase)
			{
				PhaseAdvanced -= handler;
				tcs.TrySetResult();
			}
		};
		PhaseAdvanced += handler;
		if (ct.CanBeCanceled)
		{
			ct.Register(delegate
			{
				PhaseAdvanced -= handler;
				tcs.TrySetCanceled(ct);
			});
		}
		if (isAtLeast(phase))
		{
			PhaseAdvanced -= handler;
			tcs.TrySetResult();
		}
		return tcs.Task;
	}
}
