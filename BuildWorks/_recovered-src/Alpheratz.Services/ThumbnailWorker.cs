using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Imaging;

namespace Alpheratz.Services;

public sealed class ThumbnailWorker
{
	private const int MaxConcurrency = 2;

	private readonly ThumbnailService thumbnailService;

	public ThumbnailWorker(ThumbnailService thumbnailService)
	{
		this.thumbnailService = thumbnailService;
	}

	public Task GenerateGridAsync(IReadOnlyList<(string path, long slot)> targets, Action<ThumbnailResult> onReady, CancellationToken ct = default(CancellationToken))
	{
		return RunAsync("Grid", targets, thumbnailService.EnsureGridThumbAsync, onReady, ct);
	}

	private async Task RunAsync(string kindLabel, IReadOnlyList<(string path, long slot)> targets, Func<string, long, CancellationToken, Task<string>> ensure, Action<ThumbnailResult> onReady, CancellationToken ct)
	{
		if (targets.Count == 0)
		{
			return;
		}
		try
		{
			SemaphoreSlim sem = new SemaphoreSlim(2);
			try
			{
				await Task.WhenAll(targets.Select<(string, long), Task>(async delegate((string path, long slot) target)
				{
					await sem.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
					try
					{
						if (ct.IsCancellationRequested)
						{
							return;
						}
						string thumbPath = await ensure(target.path, target.slot, ct).ConfigureAwait(continueOnCapturedContext: false);
						try
						{
							onReady(new ThumbnailResult(target.path, target.slot, thumbPath));
						}
						catch (Exception ex2)
						{
							AppLogger.Warn("ThumbnailWorker.onReady threw: " + ex2.Message);
						}
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception ex4)
					{
						AppLogger.Warn($"{kindLabel} thumb skip [{target.path}]: {ex4.Message}");
					}
					finally
					{
						sem.Release();
					}
				})).ConfigureAwait(continueOnCapturedContext: false);
			}
			finally
			{
				if (sem != null)
				{
					((IDisposable)sem).Dispose();
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			_ = 0;
		}
	}
}
