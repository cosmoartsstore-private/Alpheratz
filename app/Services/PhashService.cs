using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging.Pdq;
using Alpheratz.Models.Events;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Services;

/// <summary>
/// DB に既に存在し phash 列が空の写真をバックグラウンドで埋めるワーカー。
/// PDQ ハッシュ計算 (PdqHasher) → hex 文字列で UpdatePhotoPhashAsync で書き込む。
/// DB スキーマに quality 列がないため、保存用の4方向ハッシュでは quality を計算しない。
/// 進捗は "phash_progress" / 完了は "phash_complete" を LocalEventBus で発行する。
/// </summary>
public sealed class PhashService
{
    private const int ChunkSize = 30;
    private const int MaxParallelChunks = 8;
    // 同時実行数より多いチャンクを取得し、画像ごとの処理時間差を次の割り当てで均す。
    // DB更新とキャンセル時の確定単位は従来どおり30件に保つ。
    private const int ChunkWavesPerBatch = 2;
    // 画面側が識別できない細かさの通知を積み上げず、解析本体と UI キューのメモリを守る。
    private const long ProgressPublishIntervalMilliseconds = 100;
    private static int AnalysisParallelism
        => Math.Clamp(Environment.ProcessorCount / 2, 1, MaxParallelChunks);

    private readonly AlpheratzDb db;
    private readonly LocalEventBus eventBus;
    private int isRunning;
    private PhashProgressEvent currentProgress = PhashProgressEvent.Empty;
    private readonly object progressLock = new();
    private Task progressPublishTail = Task.CompletedTask;
    private long lastProgressPublishedAt;
    private int progressPublicationCount;

    /// <summary>補完対象 DB と進捗通知先を受け取ってワーカーを作成する。</summary>
    public PhashService(AlpheratzDb db, LocalEventBus eventBus)
    {
        AppLogger.Trace("PhashService.ctor: enter");
        this.db = db;
        this.eventBus = eventBus;
        AppLogger.Trace("PhashService.ctor: exit");
    }

    /// <summary>現在の PDQ 解析進捗を返す。</summary>
    public Task<PhashProgressEvent> GetPhashProgressAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (progressLock)
            return Task.FromResult(currentProgress);
    }

    /// <summary>現在 DB に残っている未計算 phash 件数を返す。</summary>
    public Task<int> GetPendingPhashCountAsync(CancellationToken ct = default)
        => db.GetPendingPhashCountAsync(ct);

    /// <summary>未計算 phash を新しい写真から順に補完する。多重起動中は何もせず戻る。</summary>
    public async Task StartPdqAnalysisAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("PhashService.StartPdqAnalysisAsync: enter");

        if (Interlocked.Exchange(ref isRunning, 1) != 0)
        {
            AppLogger.Trace("PhashService.StartPdqAnalysisAsync: skip (already running)");
            return;
        }

        var analysisStartedAt = Stopwatch.GetTimestamp();
        var managedBytesBefore = GC.GetTotalMemory(forceFullCollection: false);
        var workingSetBefore = Environment.WorkingSet;
        var measuredTotal = 0;
        var parallelism = AnalysisParallelism;
        // 中断や例外時に完了イベントを出すと、UI が正常完了として扱ってしまう。
        // 成否を明示的に保持し、成功時だけ complete、失敗時は error を発行する。
        var succeeded = false;
        string? errorMessage = null;
        try
        {
            var total = await db.GetPendingPhashCountAsync(ct).ConfigureAwait(false);
            measuredTotal = total;
            UpdateProgress(0, total, null, reset: true);
            if (total == 0)
            {
                AppLogger.Trace("PhashService.StartPdqAnalysisAsync: nothing pending");
                succeeded = true;
                return;
            }

            var done = 0;
            var batchSize = ChunkSize * parallelism * ChunkWavesPerBatch;
            using var writeGate = new SemaphoreSlim(1, 1);
            while (!ct.IsCancellationRequested)
            {
                var batch = await db.GetPendingPhashBatchAsync(batchSize, ct).ConfigureAwait(false);
                if (batch.Count == 0) break;

                var chunks = batch.Chunk(ChunkSize).Select(static chunk => chunk.ToArray()).ToArray();
                await Parallel.ForEachAsync(chunks, new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = parallelism,
                }, async (chunk, token) =>
                {
                    var updates = new List<AlpheratzDb.PhotoPhashUpdate>(chunk.Length);
                    foreach (var item in chunk)
                    {
                        token.ThrowIfCancellationRequested();
                        updates.Add(await BuildPhashUpdateAsync(item, token).ConfigureAwait(false));
                    }

                    await writeGate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await db.UpdatePhotoPhashesAsync(updates, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        writeGate.Release();
                    }

                    foreach (var item in chunk)
                    {
                        var currentDone = Interlocked.Increment(ref done);
                        UpdateProgress(currentDone, total, item.PhotoFilename);
                    }
                }).ConfigureAwait(false);
            }
            // ループを break で抜けたかキャンセルされたかを判定。
            // キャンセル時は success にしない（UI に「完了」と誤認させない）。
            succeeded = !ct.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
            AppLogger.Trace("PhashService.StartPdqAnalysisAsync: cancelled");
            errorMessage = getMsg("PhashService.cancelled");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhashService.StartPdqAnalysisAsync: threw: {ex}");
            errorMessage = getMsg("PhashService.failed");
        }
        finally
        {
            try
            {
                PhashProgressEvent snapshot;
                lock (progressLock)
                    snapshot = currentProgress;
                UpdateProgress(snapshot.done, snapshot.total, null, forcePublish: true);
                await WaitForProgressPublicationsAsync().ConfigureAwait(false);
                if (succeeded)
                    await eventBus.PublishAsync(EventNames.PhashComplete, new object()).ConfigureAwait(false);
                else
                    await eventBus.PublishAsync(EventNames.PhashError, errorMessage ?? getMsg("PhashService.failed")).ConfigureAwait(false);
            }
            finally
            {
                // 最終進捗と完了／エラー通知までを 1 回の実行として扱い、次回起動との通知混在を防ぐ。
                Interlocked.Exchange(ref isRunning, 0);
            }

            int publications;
            PhashProgressEvent finalSnapshot;
            lock (progressLock)
            {
                publications = progressPublicationCount;
                finalSnapshot = currentProgress;
            }
            AppLogger.Info(
                $"Performance.Pdq total={measuredTotal} done={finalSnapshot.done} parallelism={parallelism} " +
                $"progress_publications={publications} " +
                $"elapsed_ms={Stopwatch.GetElapsedTime(analysisStartedAt).TotalMilliseconds:F3} " +
                $"managed_delta_bytes={GC.GetTotalMemory(forceFullCollection: false) - managedBytesBefore} " +
                $"working_set_delta_bytes={Environment.WorkingSet - workingSetBefore}");
        }

        AppLogger.Trace("PhashService.StartPdqAnalysisAsync: exit");
    }

    /// <summary>1枚の画像を読み込み、DBへ保存する phash 更新行へ変換する。</summary>
    private static async Task<AlpheratzDb.PhotoPhashUpdate> BuildPhashUpdateAsync(
        PendingPhashItem item,
        CancellationToken ct)
    {
        try
        {
            using var image = await PdqImageReader.ReadPooledLumaAsync(item.PhotoPath, ct).ConfigureAwait(false);
            if (image is null)
            {
                AppLogger.Warn($"PhashService: skip (unreadable) [{item.PhotoFilename}]");
                return new AlpheratzDb.PhotoPhashUpdate(item.PhotoPath, "unreadable");
            }

            var combinedHex = PdqHasher.ComputeHashVariantsHex(image.Buffer, image.Width, image.Height);
            if (string.IsNullOrEmpty(combinedHex))
            {
                AppLogger.Warn($"PhashService: skip (no hash) [{item.PhotoFilename}]");
                return new AlpheratzDb.PhotoPhashUpdate(item.PhotoPath, "unreadable");
            }

            return new AlpheratzDb.PhotoPhashUpdate(item.PhotoPath, combinedHex);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.Warn($"PhashService: skip [{item.PhotoFilename}]: {ex.Message}");
            return new AlpheratzDb.PhotoPhashUpdate(item.PhotoPath, "unreadable");
        }
    }

    /// <summary>進捗スナップショットを更新し、イベントバスへ通知する。</summary>
    private void UpdateProgress(
        int done,
        int total,
        string? current,
        bool reset = false,
        bool forcePublish = false)
    {
        lock (progressLock)
        {
            // 複数チャンクが同時に完了すると、小さい done のスレッドが後からここへ来ることがある。
            // その通知を採用すると画面の進捗が巻き戻るため、同じ解析中は単調増加だけを許可する。
            if (!reset && total == currentProgress.total && done < currentProgress.done)
                return;

            var snapshot = new PhashProgressEvent { done = done, total = total, current = current };
            currentProgress = snapshot;
            var now = Environment.TickCount64;
            if (reset)
                progressPublicationCount = 0;

            var reachedEnd = total > 0 && done >= total;
            if (!reset
                && !forcePublish
                && !reachedEnd
                && now - lastProgressPublishedAt < ProgressPublishIntervalMilliseconds)
            {
                return;
            }

            lastProgressPublishedAt = now;
            progressPublicationCount++;
            progressPublishTail = progressPublishTail.ContinueWith(
                _ => eventBus.PublishAsync(EventNames.PhashProgress, snapshot),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap();
        }
    }

    /// <summary>完了・エラー通知の前に、それまでの進捗通知がすべて配信されるのを待つ。</summary>
    private Task WaitForProgressPublicationsAsync()
    {
        lock (progressLock)
            return progressPublishTail;
    }
}
