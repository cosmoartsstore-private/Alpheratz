using System;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Scanner;
using Alpheratz.Models.Events;

namespace Alpheratz.Services;

// Background worker that fills missing orientation/width/height for photos
// already in the cache. Mirrors legacy alpheratz orientation.rs.
public sealed class OrientationService
{
    private const int BatchSize = 200;

    private readonly AlpheratzDb db;
    private readonly LocalEventBus eventBus;
    private int isRunning;
    private OrientationProgressEvent currentProgress = new();

    public OrientationService(AlpheratzDb db, LocalEventBus eventBus)
    {
        AppLogger.Trace("OrientationService.ctor: enter");
        this.db = db;
        this.eventBus = eventBus;
        AppLogger.Trace("OrientationService.ctor: exit");
    }

    public Task<OrientationProgressEvent> GetOrientationProgressAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("OrientationService.GetOrientationProgressAsync: enter");
        return Task.FromResult(currentProgress);
    }

    public async Task StartOrientationCalculationAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("OrientationService.StartOrientationCalculationAsync: enter");

        // Single-flight: skip if a worker is already running.
        if (Interlocked.Exchange(ref isRunning, 1) != 0)
        {
            AppLogger.Trace("OrientationService.StartOrientationCalculationAsync: skip (already running)");
            return;
        }

        try
        {
            var total = await db.GetPendingOrientationCountAsync(ct).ConfigureAwait(false);
            UpdateProgress(0, total, true);
            if (total == 0)
            {
                AppLogger.Trace("OrientationService.StartOrientationCalculationAsync: nothing pending");
                return;
            }

            var done = 0;
            while (!ct.IsCancellationRequested)
            {
                var batch = await db.GetPendingOrientationBatchAsync(BatchSize, ct).ConfigureAwait(false);
                if (batch.Count == 0) break;

                foreach (var item in batch)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        var (orientation, w, h) = PhotoScanner.ProbeImageDimensions(item.PhotoPath);
                        await db.UpdatePhotoOrientationAndDimensionsAsync(
                            item.PhotoPath, orientation, w, h, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Continue: a single broken file should not abort the whole batch
                        // (legacy alpheratz logs and skips).
                        AppLogger.Warn($"orientation skip [{item.PhotoFilename}]: {ex.Message}");
                    }

                    done++;
                    UpdateProgress(done, total, true);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"OrientationService.StartOrientationCalculationAsync: threw: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref isRunning, 0);
            UpdateProgress(currentProgress.processed, currentProgress.total, false);
            await eventBus.PublishAsync("orientation_complete", new object()).ConfigureAwait(false);
        }

        AppLogger.Trace("OrientationService.StartOrientationCalculationAsync: exit");
    }

    private void UpdateProgress(int processed, int total, bool running)
    {
        currentProgress = new OrientationProgressEvent { processed = processed, total = total, running = running };
        _ = eventBus.PublishAsync("orientation_progress", currentProgress);
    }
}
