using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging.Pdq;
using Alpheratz.Models.Events;

namespace Alpheratz.Services;

/// <summary>
/// DB に既に存在し phash 列が空の写真をバックグラウンドで埋めるワーカー。
/// PDQ ハッシュ計算 (PdqHasher) → hex 文字列で UpdatePhotoPhashAsync で書き込む。
/// quality 値はログ目的のみ（DB スキーマには持っていない）。
/// 進捗は "phash_progress" / 完了は "phash_complete" を LocalEventBus で発行する。
/// </summary>
public sealed class PhashService
{
    private const int ChunkSize = 30;
    private const int MaxParallelChunks = 20;
    private const int BatchSize = ChunkSize * MaxParallelChunks;

    private readonly AlpheratzDb db;
    private readonly LocalEventBus eventBus;
    private int isRunning;
    private PhashProgressEvent currentProgress = PhashProgressEvent.Empty;

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
        => Task.FromResult(currentProgress);

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

        // 中断や例外時に完了イベントを出すと、UI が正常完了として扱ってしまう。
        // 成否を明示的に保持し、成功時だけ complete、失敗時は error を発行する。
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
            using var writeGate = new SemaphoreSlim(1, 1);
            while (!ct.IsCancellationRequested)
            {
                var batch = await db.GetPendingPhashBatchAsync(BatchSize, ct).ConfigureAwait(false);
                if (batch.Count == 0) break;

                var chunks = batch.Chunk(ChunkSize).Select(static chunk => chunk.ToArray()).ToArray();
                await Parallel.ForEachAsync(chunks, new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = MaxParallelChunks,
                }, async (chunk, token) =>
                {
                    var updates = new List<AlpheratzDb.PhotoPhashUpdate>(chunk.Length);
                    foreach (var item in chunk)
                    {
                        token.ThrowIfCancellationRequested();
                        updates.Add(await BuildPhashUpdateAsync(item).ConfigureAwait(false));
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
                await eventBus.PublishAsync(EventNames.PhashComplete, new object()).ConfigureAwait(false);
            else
                await eventBus.PublishAsync(EventNames.PhashError, errorMessage ?? "phash analysis failed").ConfigureAwait(false);
        }

        AppLogger.Trace("PhashService.StartPdqAnalysisAsync: exit");
    }

    /// <summary>1枚の画像を読み込み、DBへ保存する phash 更新行へ変換する。</summary>
    private static async Task<AlpheratzDb.PhotoPhashUpdate> BuildPhashUpdateAsync(PendingPhashItem item)
    {
        try
        {
            var image = await PdqImageReader.ReadLumaAsync(item.PhotoPath).ConfigureAwait(false);
            if (image is null)
            {
                AppLogger.Warn($"PhashService: skip (unreadable) [{item.PhotoFilename}]");
                return new AlpheratzDb.PhotoPhashUpdate(item.PhotoPath, "unreadable");
            }

            var (luma, w, h) = image.Value;
            var combinedHex = PdqHasher.ComputeHashVariantsHex(luma, w, h);
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
    private void UpdateProgress(int done, int total, string? current)
    {
        // フィールドをそのまま publish せず、呼び出し時点の値をスナップショットとして渡す。
        // 連続更新で別の値に差し替わっても、発行済みイベントの内容は変わらない。
        var snapshot = new PhashProgressEvent { done = done, total = total, current = current };
        currentProgress = snapshot;
        _ = eventBus.PublishAsync(EventNames.PhashProgress, snapshot);
    }
}
