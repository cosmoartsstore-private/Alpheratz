using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Alpheratz.Core;

public sealed class LocalEventBus
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<Func<JsonElement, Task>>> _handlers = new();
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

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
            json = payload is null
                ? JsonDocument.Parse("null").RootElement
                : JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
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
                // Continue: legacy alpheratz logs but never lets a single bad
                // subscriber stop the rest from receiving the event.
                AppLogger.Warn($"イベントハンドラーでエラーが発生しました [{eventName}]: {ex.Message}");
            }
        }
        AppLogger.Trace($"LocalEventBus.PublishAsync: exit event={eventName} delivered={snapshot.Count}");
    }

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

    public IAsyncDisposable Subscribe<TPayload>(string eventName, Func<TPayload, Task> handler)
    {
        AppLogger.Trace($"LocalEventBus.Subscribe<T>: enter event={eventName} type={typeof(TPayload).Name}");
        var sub = Subscribe(eventName, payload =>
        {
            var typed = payload.ValueKind == JsonValueKind.Null
                ? default
                : payload.Deserialize<TPayload>(_opts);
            return typed is not null ? handler(typed) : Task.CompletedTask;
        });
        AppLogger.Trace($"LocalEventBus.Subscribe<T>: exit event={eventName}");
        return sub;
    }

    public IAsyncDisposable Subscribe(string eventName, Func<Task> handler)
    {
        AppLogger.Trace($"LocalEventBus.Subscribe(no-payload): enter event={eventName}");
        var sub = Subscribe(eventName, _ => handler());
        AppLogger.Trace($"LocalEventBus.Subscribe(no-payload): exit event={eventName}");
        return sub;
    }

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

    private sealed class Subscription(LocalEventBus bus, string eventName, Func<JsonElement, Task> handler) : IAsyncDisposable
    {
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
