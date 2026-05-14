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
