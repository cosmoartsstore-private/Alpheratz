using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging.Pdq;
using Alpheratz.Models.Events;

namespace Alpheratz.Services;

public sealed class PhashService
{
	private const int BatchSize = 50;

	private readonly AlpheratzDb db;

	private readonly LocalEventBus eventBus;

	private int isRunning;

	private PhashProgressEvent currentProgress = PhashProgressEvent.Empty;

	public PhashService(AlpheratzDb db, LocalEventBus eventBus)
	{
		this.db = db;
		this.eventBus = eventBus;
	}

	public Task<PhashProgressEvent> GetPhashProgressAsync(CancellationToken ct = default(CancellationToken))
	{
		return Task.FromResult(currentProgress);
	}

	public async Task StartPdqAnalysisAsync(CancellationToken ct = default(CancellationToken))
	{
		if (Interlocked.Exchange(ref isRunning, 1) != 0)
		{
			return;
		}
		bool succeeded = false;
		string errorMessage = null;
		try
		{
			_ = 3;
			try
			{
				int total = await db.GetPendingPhashCountAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				UpdateProgress(0, total, null);
				if (total == 0)
				{
					succeeded = true;
					return;
				}
				int done = 0;
				while (!ct.IsCancellationRequested)
				{
					IReadOnlyList<PendingPhashItem> readOnlyList = await db.GetPendingPhashBatchAsync(50, ct).ConfigureAwait(continueOnCapturedContext: false);
					if (readOnlyList.Count == 0)
					{
						break;
					}
					foreach (PendingPhashItem item in readOnlyList)
					{
						if (ct.IsCancellationRequested)
						{
							break;
						}
						try
						{
							(float[], int, int)? tuple = await PdqImageReader.ReadLumaAsync(item.PhotoPath).ConfigureAwait(continueOnCapturedContext: false);
							if (!tuple.HasValue)
							{
								AppLogger.Warn("PhashService: skip (unreadable) [" + item.PhotoFilename + "]");
								done++;
								UpdateProgress(done, total, item.PhotoFilename);
								continue;
							}
							(float[], int, int) value = tuple.Value;
							float[] item2 = value.Item1;
							int item3 = value.Item2;
							int item4 = value.Item3;
							string text = PdqHasher.ComputeHashVariantsHex(item2, item3, item4);
							if (string.IsNullOrEmpty(text))
							{
								AppLogger.Warn("PhashService: skip (no hash) [" + item.PhotoFilename + "]");
								done++;
								UpdateProgress(done, total, item.PhotoFilename);
								continue;
							}
							await db.UpdatePhotoPhashAsync(item.PhotoPath, text, ct).ConfigureAwait(continueOnCapturedContext: false);
						}
						catch (Exception ex)
						{
							AppLogger.Warn("PhashService: skip [" + item.PhotoFilename + "]: " + ex.Message);
						}
						done++;
						UpdateProgress(done, total, item.PhotoFilename);
					}
				}
				succeeded = !ct.IsCancellationRequested;
			}
			catch (OperationCanceledException)
			{
				errorMessage = "中断されました";
			}
			catch (Exception ex3)
			{
				AppLogger.Error($"PhashService.StartPdqAnalysisAsync: threw: {ex3}");
				errorMessage = ex3.Message;
			}
		}
		finally
		{
			Interlocked.Exchange(ref isRunning, 0);
			PhashProgressEvent phashProgressEvent = currentProgress;
			UpdateProgress(phashProgressEvent.done, phashProgressEvent.total, null);
			if (!succeeded)
			{
				await eventBus.PublishAsync("phash_error", errorMessage ?? "phash analysis failed").ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				await eventBus.PublishAsync("phash_complete", new object()).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}

	private void UpdateProgress(int done, int total, string? current)
	{
		PhashProgressEvent payload = (currentProgress = new PhashProgressEvent
		{
			done = done,
			total = total,
			current = current
		});
		eventBus.PublishAsync("phash_progress", payload);
	}
}
