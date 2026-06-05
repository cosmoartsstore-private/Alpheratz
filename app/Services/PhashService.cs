using System;
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
    private const int BatchSize = 50;

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
                            await MarkPhashUnreadableAsync(item.PhotoPath, ct).ConfigureAwait(false);
                            done++;
                            UpdateProgress(done, total, item.PhotoFilename);
                            continue;
                        }

                        var (luma, w, h) = image.Value;
                        var combinedHex = PdqHasher.ComputeHashVariantsHex(luma, w, h);
                        if (string.IsNullOrEmpty(combinedHex))
                        {
                            AppLogger.Warn($"PhashService: skip (no hash) [{item.PhotoFilename}]");
                            await MarkPhashUnreadableAsync(item.PhotoPath, ct).ConfigureAwait(false);
                            done++;
                            UpdateProgress(done, total, item.PhotoFilename);
                            continue;
                        }
                        await db.UpdatePhotoPhashAsync(item.PhotoPath, combinedHex, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"PhashService: skip [{item.PhotoFilename}]: {ex.Message}");
                        try { await MarkPhashUnreadableAsync(item.PhotoPath, ct).ConfigureAwait(false); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception markEx) { AppLogger.Warn($"PhashService: failed to mark unreadable [{item.PhotoFilename}]: {markEx.Message}"); }
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
                await eventBus.PublishAsync(EventNames.PhashComplete, new object()).ConfigureAwait(false);
            else
                await eventBus.PublishAsync(EventNames.PhashError, errorMessage ?? "phash analysis failed").ConfigureAwait(false);
        }

        AppLogger.Trace("PhashService.StartPdqAnalysisAsync: exit");
    }

    /// <summary>読めない画像を終端状態として保存し、以後の pending 対象から外す。</summary>
    private Task MarkPhashUnreadableAsync(string photoPath, CancellationToken ct)
        => db.UpdatePhotoPhashAsync(photoPath, "unreadable", ct);

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
