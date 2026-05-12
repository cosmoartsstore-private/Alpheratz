using System;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging.Pdq;
using Alpheratz.Models.Events;

namespace Alpheratz.Services;

// Background worker that fills missing PDQ hashes for photos already in the
// cache. Mirrors legacy alpheratz pdq_hash worker. Only writes the hex hash
// back; quality is computed for logging but not persisted.
public sealed class PhashService
{
    private const int BatchSize = 50;

    private readonly AlpheratzDb db;
    private readonly LocalEventBus eventBus;
    private int isRunning;
    private PhashProgressEvent currentProgress = PhashProgressEvent.Empty;

    public PhashService(AlpheratzDb db, LocalEventBus eventBus)
    {
        AppLogger.Trace("PhashService.ctor: enter");
        this.db = db;
        this.eventBus = eventBus;
        AppLogger.Trace("PhashService.ctor: exit");
    }

    public Task<PhashProgressEvent> GetPhashProgressAsync(CancellationToken ct = default)
        => Task.FromResult(currentProgress);

    public async Task StartPdqAnalysisAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("PhashService.StartPdqAnalysisAsync: enter");

        if (Interlocked.Exchange(ref isRunning, 1) != 0)
        {
            AppLogger.Trace("PhashService.StartPdqAnalysisAsync: skip (already running)");
            return;
        }

        // R2-A-16: 旧実装は finally で常に phash_complete を発火していたため、
        //          例外で中断した場合も「完了したように」UI が見えていた。
        //          成否を変数で追跡し、success のときだけ phash_complete を出す。
        //          失敗時は phash_error を出して上位で再走査などの判断ができるようにする。
        var succeeded = false;
        string? errorMessage = null;
        try
        {
            var total = await db.GetPendingPhashCountAsync(ct).ConfigureAwait(false);
            UpdateProgress(0, total, null);
            if (total == 0)
            {
                AppLogger.Trace("PhashService.StartPdqAnalysisAsync: nothing pending");
                succeeded = true;
                return;
            }

            var done = 0;
            while (!ct.IsCancellationRequested)
            {
                var batch = await db.GetPendingPhashBatchAsync(BatchSize, ct).ConfigureAwait(false);
                if (batch.Count == 0) break;

                foreach (var item in batch)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var image = await PdqImageReader.ReadLumaAsync(item.PhotoPath).ConfigureAwait(false);
                        if (image is null)
                        {
                            AppLogger.Warn($"PhashService: skip (unreadable) [{item.PhotoFilename}]");
                            done++;
                            UpdateProgress(done, total, item.PhotoFilename);
                            continue;
                        }

                        var (luma, w, h) = image.Value;
                        var combinedHex = PdqHasher.ComputeHashVariantsHex(luma, w, h);
                        if (string.IsNullOrEmpty(combinedHex))
                        {
                            AppLogger.Warn($"PhashService: skip (no hash) [{item.PhotoFilename}]");
                            done++;
                            UpdateProgress(done, total, item.PhotoFilename);
                            continue;
                        }
                        await db.UpdatePhotoPhashAsync(item.PhotoPath, combinedHex, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"PhashService: skip [{item.PhotoFilename}]: {ex.Message}");
                    }
                    done++;
                    UpdateProgress(done, total, item.PhotoFilename);
                }
            }
            // ループを break で抜けたかキャンセルされたかを判定。
            // キャンセル時は success にしない（UI に「完了」と誤認させない）。
            succeeded = !ct.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
            AppLogger.Trace("PhashService.StartPdqAnalysisAsync: cancelled");
            errorMessage = "中断されました";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhashService.StartPdqAnalysisAsync: threw: {ex}");
            errorMessage = ex.Message;
        }
        finally
        {
            Interlocked.Exchange(ref isRunning, 0);
            var snapshot = currentProgress;
            UpdateProgress(snapshot.done, snapshot.total, null);
            if (succeeded)
                await eventBus.PublishAsync("phash_complete", new object()).ConfigureAwait(false);
            else
                await eventBus.PublishAsync("phash_error", errorMessage ?? "phash analysis failed").ConfigureAwait(false);
        }

        AppLogger.Trace("PhashService.StartPdqAnalysisAsync: exit");
    }

    private void UpdateProgress(int done, int total, string? current)
    {
        // ローカル変数 snapshot にコピーしてから PublishAsync へ渡す。
        // 旧実装ではフィールド更新と PublishAsync の引数評価の間に別スレッドの
        // UpdateProgress が割り込むことができ、同じ値が二重発火する race があった。
        var snapshot = new PhashProgressEvent { done = done, total = total, current = current };
        currentProgress = snapshot;
        _ = eventBus.PublishAsync("phash_progress", snapshot);
    }
}