using System;
using Microsoft.UI.Dispatching;

namespace Alpheratz.Core;

/// <summary>
/// UI スレッド (XAML の DispatcherQueue) へのマーシャリングを集約するヘルパ。
/// App 起動時に <see cref="Queue"/> がセットされ、以降あらゆるバックグラウンドコードから
/// 安全に UI 更新できる。Queue が未設定（テスト環境など）なら同期実行にフォールバックする。
/// </summary>
public static class UiThread
{
    /// <summary>
    /// アプリのメイン UI スレッドの DispatcherQueue。App.OnLaunched で
    /// <c>DispatcherQueue.GetForCurrentThread()</c> をセットする。
    /// </summary>
    public static DispatcherQueue? Queue { get; set; }

    /// <summary>
    /// action を UI スレッドで実行する。
    ///   - Queue 未設定 → 同期実行 (テスト・デザイン時用フォールバック)
    ///   - 既に UI スレッド上 (HasThreadAccess=true) → 同期実行 (Dispatcher 経由は無駄)
    ///   - 別スレッド → TryEnqueue で非同期マーシャリング
    /// このメソッド自体はブロックしないので、戻った直後に action が完了している保証は無い。
    /// </summary>
    public static void Run(Action action)
    {
        var dq = Queue;
        if (dq is null || dq.HasThreadAccess)
        {
            action();
            return;
        }
        // TryEnqueue は DispatcherQueue が shutdown 後に false を返すことがある。
        // アプリ終了直前にバックグラウンドから UI 更新を要求した場合に起き得る。
        // 静かに失敗すると「最後の状態更新が反映されなかった」バグを生むので警告ログを残す。
        if (!dq.TryEnqueue(() => action()))
        {
            AppLogger.Warn("UiThread.Run: TryEnqueue failed (DispatcherQueue likely shutting down)");
        }
    }
}
