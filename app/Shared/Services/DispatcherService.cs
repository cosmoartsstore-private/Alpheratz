using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Dispatching;

namespace Alpheratz.Shared.Services;

/// <summary>
/// UI スレッドへのディスパッチを提供するサービス。
/// RunOnUiThread は呼び出し元が UI スレッドなら同期実行、そうでなければキューイングする。
/// </summary>
public sealed class DispatcherService
{
    private readonly DispatcherQueue dispatcherQueue;

    public DispatcherService(DispatcherQueue dispatcherQueue)
    {
        AppLogger.Trace("DispatcherService.ctor: enter");
        this.dispatcherQueue = dispatcherQueue;
        AppLogger.Trace("DispatcherService.ctor: exit");
    }

    /// <summary>次のフレームで action を UI スレッド上で実行する（Fire-and-forget）。</summary>
    public void requestAnimationFrame(Action action)
    {
        try
        {
            dispatcherQueue.TryEnqueue(() =>
            {
                try { action(); }
                catch (Exception ex) { AppLogger.Error($"DispatcherService.requestAnimationFrame.action: threw: {ex}"); }
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"DispatcherService.requestAnimationFrame: threw: {ex}");
        }
    }

    /// <summary>
    /// action を UI スレッド上で実行し、完了を待つ。
    /// 既に UI スレッド上にいる場合は同期実行して Task.CompletedTask を返す。
    /// ConfigureAwait(false) と組み合わせることで、非同期メソッド内から安全に UI を更新できる。
    /// </summary>
    public Task RunOnUiThread(Action action)
    {
        if (dispatcherQueue.HasThreadAccess)
        {
            try { action(); }
            catch (Exception ex)
            {
                AppLogger.Error($"DispatcherService.RunOnUiThread.inline: threw: {ex}");
                return Task.FromException(ex);
            }
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = dispatcherQueue.TryEnqueue(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex)
            {
                AppLogger.Error($"DispatcherService.RunOnUiThread.action: threw: {ex}");
                tcs.SetException(ex);
            }
        });
        if (!queued)
        {
            AppLogger.Error("DispatcherService.RunOnUiThread: TryEnqueue failed");
            tcs.SetException(new InvalidOperationException("DispatcherQueue が利用できません"));
        }
        return tcs.Task;
    }

    /// <summary>指定ミリ秒後に action を UI スレッド上で実行する（簡易 setTimeout）。</summary>
    public async Task setTimeout(Action action, int milliseconds)
    {
        AppLogger.Trace($"DispatcherService.setTimeout: enter ms={milliseconds}");
        try
        {
            await Task.Delay(milliseconds).ConfigureAwait(false);
            dispatcherQueue.TryEnqueue(() =>
            {
                try { action(); }
                catch (Exception ex) { AppLogger.Error($"DispatcherService.setTimeout.action: threw: {ex}"); }
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"DispatcherService.setTimeout: threw: {ex}");
        }
        AppLogger.Trace("DispatcherService.setTimeout: exit");
    }
}
