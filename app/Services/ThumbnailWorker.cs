using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Imaging;

namespace Alpheratz.Services;

/// <summary>サムネイル生成 1 件の完了を表す DTO（呼出側コールバックに渡す）。</summary>
public sealed record ThumbnailResult(string PhotoPath, long SourceSlot, string ThumbPath);

/// <summary>
/// グリッド/表示サムネイルをバックグラウンドで一括生成するワーカー。
/// ThumbnailService.EnsureGridThumbAsync はキャッシュ済みなら即返するため、
/// 重複バッチ (ビューポート遷移時の同じ写真への再リクエスト) を投げてもコストは低い。
/// </summary>
public sealed class ThumbnailWorker
{
    /// <summary>
    /// 同時生成数の上限。2 にしている理由：
    ///   - サムネイル生成は CPU (デコード+リサイズ) と I/O (元画像読み込み・JPEG 書き込み) の両方が重い
    ///   - 並列度を上げすぎると HDD ではシーク多発で逆に遅くなる
    ///   - 2 は SSD/HDD どちらでも安定する経験値
    /// CPU コア数に合わせて自動調整する余地もあるが、UI スレッドを圧迫しないことを優先。
    /// </summary>
    private const int MaxConcurrency = 2;

    private readonly ThumbnailService thumbnailService;
    private readonly object operationGate = new();
    private readonly Dictionary<Task, CancellationTokenSource> activeOperations = [];
    private bool operationsSuspended;

    public ThumbnailWorker(ThumbnailService thumbnailService)
    {
        AppLogger.Trace("ThumbnailWorker.ctor: enter");
        this.thumbnailService = thumbnailService;
        AppLogger.Trace("ThumbnailWorker.ctor: exit");
    }

    /// <summary>
    /// グリッド用サムネイルを並列生成する。
    /// 各生成完了時に onReady をワーカースレッドから呼ぶ。
    /// UI 更新は呼び出し元が UiThread.Run 経由で行うこと。
    /// </summary>
    public Task GenerateGridAsync(
        IReadOnlyList<(string path, long slot)> targets,
        Action<ThumbnailResult> onReady,
        CancellationToken ct = default)
    {
        CancellationTokenSource operationCts;
        Task operation;
        lock (operationGate)
        {
            if (operationsSuspended || ct.IsCancellationRequested)
                return Task.CompletedTask;

            operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // 実処理を別タスクへ送ることで、外部コールバックを gate の内側で実行しない。
            // gate を解放する前に登録するため、停止処理は開始直後の書込みも必ず捕捉できる。
            operation = Task.Run(
                () => RunAsync("Grid", targets, thumbnailService.EnsureGridThumbAsync, onReady, operationCts.Token),
                CancellationToken.None);
            activeOperations.Add(operation, operationCts);
        }

        _ = operation.ContinueWith(
            completeOperation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return operation;
    }

    /// <summary>
    /// すべてのサムネイル生成を停止し、キャッシュへの書込みが終了するまで待つ。
    /// ResumeOperations を呼ぶまでは新しい生成要求を受け付けない。
    /// </summary>
    public async Task SuspendOperationsAndWaitAsync()
    {
        Task[] operations;
        CancellationTokenSource[] sources;
        lock (operationGate)
        {
            operationsSuspended = true;
            operations = activeOperations.Keys.ToArray();
            sources = activeOperations.Values.Distinct().ToArray();
        }

        foreach (var source in sources)
        {
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        if (operations.Length == 0)
            return;

        try
        {
            await Task.WhenAll(operations).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLogger.Warn($"ThumbnailWorker.SuspendOperationsAndWaitAsync: completion failed: {ex.Message}");
        }
    }

    /// <summary>フォルダ整理完了後、新しいサムネイル生成要求を許可する。</summary>
    public void ResumeOperations()
    {
        lock (operationGate)
            operationsSuspended = false;
    }

    private void completeOperation(Task operation)
    {
        CancellationTokenSource? source = null;
        lock (operationGate)
            activeOperations.Remove(operation, out source);
        source?.Dispose();
    }

    /// <summary>
    /// SemaphoreSlim で同時実行数を制限しつつ全ターゲットを並列処理する。
    /// 個別の失敗はログに記録してスキップし、バッチ全体をキャンセルしない。
    /// </summary>
    private async Task RunAsync(
        string kindLabel,
        IReadOnlyList<(string path, long slot)> targets,
        Func<string, long, CancellationToken, Task<string>> ensure,
        Action<ThumbnailResult> onReady,
        CancellationToken ct)
    {
        AppLogger.Trace($"ThumbnailWorker.Run({kindLabel}): enter count={targets.Count}");
        if (targets.Count == 0)
        {
            AppLogger.Trace($"ThumbnailWorker.Run({kindLabel}): exit (empty)");
            return;
        }

        try
        {
            using var sem = new SemaphoreSlim(MaxConcurrency);
            var tasks = targets.Select(async target =>
            {
                await sem.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (ct.IsCancellationRequested) return;
                    var thumbPath = await ensure(target.path, target.slot, ct).ConfigureAwait(false);
                    try
                    {
                        onReady(new ThumbnailResult(target.path, target.slot, thumbPath));
                    }
                    catch (Exception cbEx)
                    {
                        AppLogger.Warn($"ThumbnailWorker.onReady threw: {cbEx.Message}");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLogger.Warn($"{kindLabel} thumb skip [{target.path}]: {ex.Message}");
                }
                finally
                {
                    sem.Release();
                }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AppLogger.Trace($"ThumbnailWorker.Run({kindLabel}): cancelled");
        }
        finally
        {
            AppLogger.Trace($"ThumbnailWorker.Run({kindLabel}): exit");
        }
    }
}
