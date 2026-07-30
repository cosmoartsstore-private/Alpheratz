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
    // 進捗イベントが逆順で届くと、進捗バーが一時的に巻き戻って見える。
    // 直前の publish の後ろへ次の publish をつなぎ、発行順を保つ。
    private Task _publishTail = Task.CompletedTask;
    private readonly object _publishLock = new();

    /// <summary>補完対象 DB と進捗通知先を受け取ってワーカーを作成する。</summary>
    public OrientationService(AlpheratzDb db, LocalEventBus eventBus)
    {
        AppLogger.Trace("OrientationService.ctor: enter");
        this.db = db;
        this.eventBus = eventBus;
        AppLogger.Trace("OrientationService.ctor: exit");
    }

    /// <summary>現在の orientation 補完進捗を返す。</summary>
    public Task<OrientationProgressEvent> GetOrientationProgressAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("OrientationService.GetOrientationProgressAsync: enter");
        ct.ThrowIfCancellationRequested();
        lock (_publishLock)
            return Task.FromResult(currentProgress);
    }

    /// <summary>orientation または寸法が欠落している写真を新しい順に補完する。</summary>
    public async Task StartOrientationCalculationAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("OrientationService.StartOrientationCalculationAsync: enter");

        // 同じ DB 行を並列更新しないよう、実行中の二重起動は無視する。
        if (Interlocked.Exchange(ref isRunning, 1) != 0)
        {
            AppLogger.Trace("OrientationService.StartOrientationCalculationAsync: skip (already running)");
            return;
        }

        var succeeded = false;
        try
        {
            var total = await db.GetPendingOrientationCountAsync(ct).ConfigureAwait(false);
            UpdateProgress(0, total, true);
            if (total == 0)
            {
                AppLogger.Trace("OrientationService.StartOrientationCalculationAsync: nothing pending");
                succeeded = true;
                return;
            }

            var done = 0;
            while (!ct.IsCancellationRequested)
            {
                var batch = await db.GetPendingOrientationBatchAsync(BatchSize, ct).ConfigureAwait(false);
                if (batch.Count == 0) break;

                foreach (var item in batch)
                {
                    ct.ThrowIfCancellationRequested();

                    var (orientation, w, h) = PhotoScanner.ProbeImageDimensions(item.PhotoPath);
                    if (orientation is null or "" or "unknown" || w is null || h is null)
                    {
                        orientation = "unreadable";
                        w = null;
                        h = null;
                    }
                    await db.UpdatePhotoOrientationAndDimensionsAsync(
                        item.PhotoPath, orientation, w, h, ct).ConfigureAwait(false);

                    done++;
                    UpdateProgress(done, total, true);
                }
            }

            ct.ThrowIfCancellationRequested();
            succeeded = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            AppLogger.Trace("OrientationService.StartOrientationCalculationAsync: cancelled");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"OrientationService.StartOrientationCalculationAsync: threw: {ex}");
        }
        finally
        {
            try
            {
                OrientationProgressEvent snapshot;
                lock (_publishLock)
                    snapshot = currentProgress;
                UpdateProgress(snapshot.processed, snapshot.total, false);
                await WaitForProgressPublicationsAsync().ConfigureAwait(false);
                if (succeeded)
                    await eventBus.PublishAsync(EventNames.OrientationComplete, new object()).ConfigureAwait(false);
            }
            finally
            {
                // 最終進捗と完了通知までを 1 回の実行として扱い、次回起動との通知混在を防ぐ。
                Interlocked.Exchange(ref isRunning, 0);
            }
        }

        AppLogger.Trace("OrientationService.StartOrientationCalculationAsync: exit");
    }

    /// <summary>進捗スナップショットを更新し、イベントバスへ順序を保って通知する。</summary>
    private void UpdateProgress(int processed, int total, bool running)
    {
        // フィールドをそのまま publish せず、呼び出し時点の値をスナップショットとして渡す。
        // _publishTail で直列化し、連続更新時も handler の実行順が逆転しないようにする。
        var snapshot = new OrientationProgressEvent { processed = processed, total = total, running = running };
        lock (_publishLock)
        {
            currentProgress = snapshot;
            _publishTail = _publishTail.ContinueWith(
                _ => eventBus.PublishAsync(EventNames.OrientationProgress, snapshot),
                TaskScheduler.Default).Unwrap();
        }
    }

    /// <summary>完了通知の前に、それまでの進捗通知がすべて配信されるのを待つ。</summary>
    private Task WaitForProgressPublicationsAsync()
    {
        lock (_publishLock)
            return _publishTail;
    }
}
