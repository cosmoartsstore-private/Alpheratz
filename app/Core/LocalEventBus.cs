using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Alpheratz.Core;

/// <summary>
/// プロセス内イベントバス。スキャナ完了・スキャナ進捗・PDQ 完了などの「複数モジュールに
/// 通知したいが直接参照させたくない」イベントを名前空間付きで配信する。
/// payload は JsonElement で受け渡し、型付き Subscribe&lt;T&gt; が逆シリアライズして渡す
/// （Publish 側と Subscribe 側で型を共有しなくてよく、モジュール境界を緩く保てる）。
/// 同期発火する Pub/Sub ではなく、各 handler は await されるため副作用順序は決定的。
/// </summary>
public sealed class LocalEventBus
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<Func<JsonElement, Task>>> _handlers = new();
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 指定イベント名の購読者全員に payload を配信する。
    /// 配信中の購読者変更で例外が出ないよう、_handlers を先に snapshot へコピーしてから配信する。
    /// 個別 handler の例外は警告ログのみで握りつぶし、残りの handler の配信を止めない。
    ///
    /// 配信は**意図的に直列**にしている。並列化 (Task.WhenAll) は一見高速化に見えるが、
    /// 本アプリの主要イベント (scan:completed 等) は複数の購読者がいずれも DB write を伴うため、
    /// 並列発火させると同一 photos テーブルへの UPDATE 衝突で SQLITE_BUSY が頻発し、
    /// orientation / phash の部分書込が発生する (ShellViewModel.postScanGate のコメント参照)。
    /// 直列発火のままにしておくのが安全。
    /// </summary>
    public async Task PublishAsync(string eventName, object? payload = null)
    {
        AppLogger.Trace($"LocalEventBus.PublishAsync: enter event={eventName}");
        List<Func<JsonElement, Task>> snapshot;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(eventName, out var list))
            {
                AppLogger.Trace($"LocalEventBus.PublishAsync: skip (no handlers) event={eventName}");
                return;
            }
            snapshot = new List<Func<JsonElement, Task>>(list);
        }

        JsonElement json;
        try
        {
            // JsonDocument.Parse(...).RootElement を返すと、所有する JsonDocument を解放できない。
            // SerializeToElement は独立した JsonElement を返すため、配信中に保持するリソースが残らない。
            json = JsonSerializer.SerializeToElement(
                payload,
                payload?.GetType() ?? typeof(object),
                _opts);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"LocalEventBus.PublishAsync: serialize threw event={eventName}: {ex}");
            return;
        }

        foreach (var handler in snapshot)
        {
            try { await handler(json).ConfigureAwait(false); }
            catch (Exception ex)
            {
                // 1つの購読者の失敗で、同じイベントを待つ別機能まで止めない。
                AppLogger.Warn($"イベントハンドラーでエラーが発生しました [{eventName}]: {ex.Message}");
            }
        }
        AppLogger.Trace($"LocalEventBus.PublishAsync: exit event={eventName} delivered={snapshot.Count}");
    }

    /// <summary>
    /// 生の JsonElement を受ける汎用購読。返却された IAsyncDisposable を DisposeAsync すると
    /// 同じデリゲート参照を Remove して購読を解除する（解除は参照同一性ベース）。
    /// </summary>
    public IAsyncDisposable Subscribe(string eventName, Func<JsonElement, Task> handler)
    {
        AppLogger.Trace($"LocalEventBus.Subscribe: enter event={eventName}");
        lock (_lock)
        {
            if (!_handlers.TryGetValue(eventName, out var list))
            {
                list = [];
                _handlers[eventName] = list;
            }
            list.Add(handler);
        }
        AppLogger.Trace($"LocalEventBus.Subscribe: exit event={eventName}");
        return new Subscription(this, eventName, handler);
    }

    /// <summary>
    /// 型付き購読。内部でラッパ関数を作って Subscribe(JsonElement) に渡すため、
    /// 返却される IAsyncDisposable はそのラッパ参照を覚えていて Dispose 時に Remove する。
    /// payload が JSON null か逆シリアライズ失敗時は handler を呼ばずスキップする。
    /// </summary>
    public IAsyncDisposable Subscribe<TPayload>(string eventName, Func<TPayload, Task> handler)
    {
        AppLogger.Trace($"LocalEventBus.Subscribe<T>: enter event={eventName} type={typeof(TPayload).Name}");
        var sub = Subscribe(eventName, payload =>
        {
            TPayload? typed = default;
            if (payload.ValueKind != JsonValueKind.Null)
            {
                try
                {
                    typed = payload.Deserialize<TPayload>(_opts);
                }
                catch (Exception ex)
                {
                    // payload スキーマが publisher 側で変わった場合などは、購読側で診断できるよう警告を残す。
                    AppLogger.Warn($"LocalEventBus.Subscribe<{typeof(TPayload).Name}>: deserialize failed for event={eventName}: {ex.Message}");
                    return Task.CompletedTask;
                }
            }
            return typed is not null ? handler(typed) : Task.CompletedTask;
        });
        AppLogger.Trace($"LocalEventBus.Subscribe<T>: exit event={eventName}");
        return sub;
    }

    /// <summary>payload を見ない購読のショートハンド (scan:completed のような完了通知用)。</summary>
    public IAsyncDisposable Subscribe(string eventName, Func<Task> handler)
    {
        AppLogger.Trace($"LocalEventBus.Subscribe(no-payload): enter event={eventName}");
        var sub = Subscribe(eventName, _ => handler());
        AppLogger.Trace($"LocalEventBus.Subscribe(no-payload): exit event={eventName}");
        return sub;
    }

    /// <summary>指定イベントから、Subscribe 時に登録した同じ handler 参照を解除する。</summary>
    private void Unsubscribe(string eventName, Func<JsonElement, Task> handler)
    {
        AppLogger.Trace($"LocalEventBus.Unsubscribe: enter event={eventName}");
        lock (_lock)
        {
            if (_handlers.TryGetValue(eventName, out var list))
                list.Remove(handler);
        }
        AppLogger.Trace($"LocalEventBus.Unsubscribe: exit event={eventName}");
    }

    /// <summary>
    /// 購読トークン。DisposeAsync で同じ (eventName, handler 参照) を _handlers から削除する。
    /// delegate キャプチャの参照同一性に依存しているため、Subscribe&lt;T&gt; 等のラッパ経由でも
    /// ラッパ参照が保持されている限り正しく解除できる。
    /// </summary>
    private sealed class Subscription(LocalEventBus bus, string eventName, Func<JsonElement, Task> handler) : IAsyncDisposable
    {
        /// <summary>購読時に登録した handler をイベントバスから解除する。</summary>
        public ValueTask DisposeAsync()
        {
            AppLogger.Trace($"LocalEventBus.Subscription.DisposeAsync: enter event={eventName}");
            try { bus.Unsubscribe(eventName, handler); }
            catch (Exception ex) { AppLogger.Error($"LocalEventBus.Subscription.DisposeAsync: threw: {ex}"); }
            AppLogger.Trace($"LocalEventBus.Subscription.DisposeAsync: exit event={eventName}");
            return ValueTask.CompletedTask;
        }
    }
}
