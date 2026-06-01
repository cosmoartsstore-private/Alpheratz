using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Scanner;
using Alpheratz.Models.Events;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Alpheratz.Services;

public sealed class OrientationService
{
	private const int BatchSize = 200;

	private readonly AlpheratzDb db;

	private readonly LocalEventBus eventBus;

	private int isRunning;

	private OrientationProgressEvent currentProgress = new OrientationProgressEvent();

	private Task _publishTail = Task.CompletedTask;

	private readonly object _publishLock = new object();

	public OrientationService(AlpheratzDb db, LocalEventBus eventBus)
	{
		this.db = db;
		this.eventBus = eventBus;
	}

	public Task<OrientationProgressEvent> GetOrientationProgressAsync(CancellationToken ct = default(CancellationToken))
	{
		return Task.FromResult(currentProgress);
	}

	public async Task StartOrientationCalculationAsync(CancellationToken ct = default(CancellationToken))
	{
		if (Interlocked.Exchange(ref isRunning, 1) != 0)
		{
			return;
		}
		try
		{
			_ = 3;
			try
			{
				int total = await db.GetPendingOrientationCountAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				UpdateProgress(0, total, running: true);
				if (total == 0)
				{
					goto end_IL_0056;
				}
				int done = 0;
				while (!ct.IsCancellationRequested)
				{
					IReadOnlyList<PendingOrientationItem> readOnlyList = await db.GetPendingOrientationBatchAsync(200, ct).ConfigureAwait(continueOnCapturedContext: false);
					if (readOnlyList.Count == 0)
					{
						break;
					}
					foreach (PendingOrientationItem item in readOnlyList)
					{
						if (!ct.IsCancellationRequested)
						{
							try
							{
								var (orientation, width, height) = await ProbeWithBitmapDecoderAsync(item.PhotoPath, ct).ConfigureAwait(continueOnCapturedContext: false);
								await db.UpdatePhotoOrientationAndDimensionsAsync(item.PhotoPath, orientation, width, height, ct).ConfigureAwait(continueOnCapturedContext: false);
							}
							catch (Exception ex)
							{
								AppLogger.Warn("orientation skip [" + item.PhotoFilename + "]: " + ex.Message);
							}
							done++;
							UpdateProgress(done, total, running: true);
							continue;
						}
						break;
					}
				}
				goto end_IL_003b;
				end_IL_0056:;
			}
			catch (Exception value)
			{
				AppLogger.Error($"OrientationService.StartOrientationCalculationAsync: threw: {value}");
				goto end_IL_003b;
			}
			end_IL_003b:;
		}
		finally
		{
			Interlocked.Exchange(ref isRunning, 0);
			OrientationProgressEvent orientationProgressEvent = currentProgress;
			UpdateProgress(orientationProgressEvent.processed, orientationProgressEvent.total, running: false);
			await eventBus.PublishAsync("orientation_complete", new object()).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private static async Task<(string? orientation, long? width, long? height)> ProbeWithBitmapDecoderAsync(string photoPath, CancellationToken ct)
	{
		_ = 2;
		try
		{
			using IRandomAccessStreamWithContentType stream = await (await StorageFile.GetFileFromPathAsync(photoPath.Replace('/', Path.DirectorySeparatorChar)).AsTask(ct).ConfigureAwait(continueOnCapturedContext: false)).OpenReadAsync().AsTask(ct).ConfigureAwait(continueOnCapturedContext: false);
			BitmapDecoder obj = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(continueOnCapturedContext: false);
			long num = obj.OrientedPixelWidth;
			long num2 = obj.OrientedPixelHeight;
			if (num <= 0 || num2 <= 0)
			{
				return (orientation: "unknown", width: null, height: null);
			}
			string item = ((num2 > num) ? "portrait" : "landscape");
			return (orientation: item, width: num, height: num2);
		}
		catch (Exception ex)
		{
			AppLogger.Warn("BitmapDecoder probe failed, falling back to header [" + photoPath + "]: " + ex.Message);
			return PhotoScanner.ProbeImageDimensions(photoPath);
		}
	}

	private void UpdateProgress(int processed, int total, bool running)
	{
		OrientationProgressEvent snapshot = new OrientationProgressEvent
		{
			processed = processed,
			total = total,
			running = running
		};
		currentProgress = snapshot;
		lock (_publishLock)
		{
			_publishTail = _publishTail.ContinueWith((Task _) => eventBus.PublishAsync("orientation_progress", snapshot), TaskScheduler.Default).Unwrap();
		}
	}
}
