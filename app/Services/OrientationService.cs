using System;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Scanner;
using Alpheratz.Models.Events;

namespace Alpheratz.Services;

/// <summary>
/// DB に既に存在し orientation / image_width / image_height が欠落している写真を
/// バックグラウンドで埋めるワーカー。
/// 画像をデコードして EXIF の縦横と回転を読み取り UpdatePhotoOrientationAndDimensionsAsync で
/// 書き込む。BatchSize=200 件単位で処理し、各バッチ後に "orientation:progress" を発行。
/// </summary>
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
            var snapshot = currentProgress;
            UpdateProgress(snapshot.processed, snapshot.total, false);
            await eventBus.PublishAsync("orientation_complete", new object()).ConfigureAwait(false);
        }

        AppLogger.Trace("OrientationService.StartOrientationCalculationAsync: exit");
    }

    private void UpdateProgress(int processed, int total, bool running)
    {
        // ローカル変数 snapshot にコピーしてから PublishAsync へ渡す。
        // 旧実装では currentProgress フィールドが先に上書きされて、後段の
        // PublishAsync が新しいスナップショットを発火するパスがあった。
        var snapshot = new OrientationProgressEvent { processed = processed, total = total, running = running };
        currentProgress = snapshot;
        _ = eventBus.PublishAsync("orientation_progress", snapshot);
    }
}