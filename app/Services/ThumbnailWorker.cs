using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Imaging;

namespace Alpheratz.Services;

public sealed record ThumbnailResult(string PhotoPath, long SourceSlot, string ThumbPath);

/// <summary>
/// グリッド/表示サムネイルをバックグラウンドで一括生成するワーカー。
/// ThumbnailService.EnsureGridThumbAsync はキャッシュ済みなら即返するため、
/// 重複バッチを投げてもコストは低い。
/// </summary>
public sealed class ThumbnailWorker
{
    /// <summary>同時生成数の上限。CPU 負荷とディスク I/O のバランスで調整する。</summary>
    private const int MaxConcurrency = 2;

    private readonly ThumbnailService thumbnailService;

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
        => RunAsync("Grid", targets, thumbnailService.EnsureGridThumbAsync, onReady, ct);

    /// <summary>表示用（高解像度）サムネイルを並列生成する。</summary>
    public Task GenerateDisplayAsync(
        IReadOnlyList<(string path, long slot)> targets,
        Action<ThumbnailResult> onReady,
        CancellationToken ct = default)
        => RunAsync("Display", targets, thumbnailService.EnsureDisplayThumbAsync, onReady, ct);

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
