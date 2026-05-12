using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Models;

namespace Alpheratz.Shared.Services;

public sealed class ToastService
{
    // TS: toasts
    public UiObservableCollection<ToastMessage> toasts { get; } = [];

    // R2-A-17: 旧実装は id に UtcNow.ToUnixTimeMilliseconds() を使っており、
    //          同じ ms 内に複数 addToast を呼ぶと衝突しうえ、ToastMessage が record の場合
    //          値等価のため別 toast を誤って Remove する事故が起きうる。
    //          Interlocked.Increment による単調増加 id を採用し、衝突を物理的に排除する。
    //          開始値は現在時刻ベースで、再起動間でも近傍 id が出にくくする。
    private static long _idCounter = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;

    // TS: addToast(msg, type = "info", duration = 3000)
    public void addToast(string msg, ToastType type = ToastType.info, int duration = 3000)
    {
        AppLogger.Trace($"ToastService.addToast: enter type={type} duration={duration} msg={msg}");
        try
        {
            var id = Interlocked.Increment(ref _idCounter);
            var toast = new ToastMessage(id, msg, type);
            toasts.Add(toast);

            _ = Task.Run(async () =>
            {
                AppLogger.Trace($"ToastService.addToast.removalTask: enter id={id}");
                try
                {
                    await Task.Delay(duration).ConfigureAwait(false);
                    var dq = App.MainWindowInstance?.DispatcherQueue;
                    if (dq is not null)
                    {
                        _ = dq.TryEnqueue(() =>
                        {
                            try { toasts.Remove(toast); }
                            catch (Exception ex) { AppLogger.Error($"ToastService.addToast.remove: threw: {ex}"); }
                        });
                    }
                    else
                    {
                        // MainWindow がまだ初期化されていない / すでに閉じられている場合は
                        // toasts コレクション自体が UI スレッドにマーシャリングする実装なので
                        // ここで直接 Remove を呼んでも安全。Remove を完全にスキップすると
                        // 古い Toast が永遠に表示されたままになるバグになる。
                        try { toasts.Remove(toast); }
                        catch (Exception ex) { AppLogger.Error($"ToastService.addToast.remove (no dq): threw: {ex}"); }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ToastService.addToast.removalTask: threw: {ex}");
                }
                AppLogger.Trace($"ToastService.addToast.removalTask: exit id={id}");
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ToastService.addToast: threw: {ex}");
        }
        AppLogger.Trace("ToastService.addToast: exit");
    }
}