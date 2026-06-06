using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Models;

namespace Alpheratz.Shared.Services;

/// <summary>
/// 画面右上に重ねて出る Toast 通知を管理するサービス。
/// 表示は <see cref="toasts"/> コレクションを ToastHost コントロールが GalleryViewModel 等から
/// 間接的にバインドして自動更新する。各 toast は duration ms 経過後に自動削除される。
/// </summary>
public sealed class ToastService
{
    /// <summary>UI にバインドされる toast コレクション。変更は必ず dispatcherService 経由で行う。</summary>
    public UiObservableCollection<ToastMessage> toasts { get; } = [];

    private readonly DispatcherService dispatcherService;

    // 単調増加 id カウンタ。
    // 以前は UtcNow.ToUnixTimeMilliseconds() を id にしていたが、
    //   - 同 ms 内に複数 addToast が呼ばれると id が衝突する
    //   - ToastMessage は record なので値等価比較になり、別 toast を誤って Remove する事故が起きうる
    // という問題があった。Interlocked.Increment による単調増加 id で物理的に衝突を排除する。
    // 初期値を現在時刻 * 1000 にしてあるのは、再起動間でも id が近傍にならないようにするため。
    private static long _idCounter = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;

    public ToastService(DispatcherService? dispatcherService = null)
    {
        this.dispatcherService = dispatcherService ?? new DispatcherService();
    }

    /// <summary>
    /// 通知を表示する。duration ms 後にバックグラウンドタスクで自動削除される。
    /// Add/Remove は dispatcherService 経由で UI スレッドへ寄せる。
    /// </summary>
    public void addToast(string msg, ToastType type = ToastType.info, int duration = 3000)
    {
        AppLogger.Trace($"ToastService.addToast: enter type={type} duration={duration} msg={msg}");
        try
        {
            var id = Interlocked.Increment(ref _idCounter);
            var toast = new ToastMessage(id, msg, type);
            _ = dispatcherService.RunOnUiThread(() => toasts.Add(toast));

            _ = Task.Run(async () =>
            {
                AppLogger.Trace($"ToastService.addToast.removalTask: enter id={id}");
                try
                {
                    await Task.Delay(duration).ConfigureAwait(false);
                    await dispatcherService.RunOnUiThread(() => toasts.Remove(toast)).ConfigureAwait(false);
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
