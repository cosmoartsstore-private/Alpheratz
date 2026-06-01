using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Alpheratz.Core;

public sealed class LocalEventBus
{
	private sealed class Subscription(LocalEventBus bus, string eventName, Func<JsonElement, Task> handler) : IAsyncDisposable
	{
		public ValueTask DisposeAsync()
		{
			try
			{
				bus.Unsubscribe(eventName, handler);
			}
			catch (Exception value)
			{
				AppLogger.Error($"LocalEventBus.Subscription.DisposeAsync: threw: {value}");
			}
			return ValueTask.CompletedTask;
		}
	}

	private readonly object _lock = new object();

	private readonly Dictionary<string, List<Func<JsonElement, Task>>> _handlers = new Dictionary<string, List<Func<JsonElement, Task>>>();

	private static readonly JsonSerializerOptions _opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	public async Task PublishAsync(string eventName, object? payload = null)
	{
		List<Func<JsonElement, Task>> list;
		lock (_lock)
		{
			if (!_handlers.TryGetValue(eventName, out List<Func<JsonElement, Task>> value))
			{
				return;
			}
			list = new List<Func<JsonElement, Task>>(value);
		}
		JsonElement json;
		try
		{
			json = ((payload == null) ? JsonDocument.Parse("null").RootElement : JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
		}
		catch (Exception value2)
		{
			AppLogger.Error($"LocalEventBus.PublishAsync: serialize threw event={eventName}: {value2}");
			return;
		}
		foreach (Func<JsonElement, Task> item in list)
		{
			try
			{
				await item(json).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex)
			{
				AppLogger.Warn("イベントハンドラーでエラーが発生しました [" + eventName + "]: " + ex.Message);
			}
		}
	}

	public IAsyncDisposable Subscribe(string eventName, Func<JsonElement, Task> handler)
	{
		lock (_lock)
		{
			if (!_handlers.TryGetValue(eventName, out List<Func<JsonElement, Task>> value))
			{
				value = new List<Func<JsonElement, Task>>();
				_handlers[eventName] = value;
			}
			value.Add(handler);
		}
		return new Subscription(this, eventName, handler);
	}

	public IAsyncDisposable Subscribe<TPayload>(string eventName, Func<TPayload, Task> handler)
	{
		return Subscribe(eventName, delegate(JsonElement payload)
		{
			TPayload val = default(TPayload);
			if (payload.ValueKind != JsonValueKind.Null)
			{
				try
				{
					val = payload.Deserialize<TPayload>(_opts);
				}
				catch (Exception ex)
				{
					AppLogger.Warn($"LocalEventBus.Subscribe<{typeof(TPayload).Name}>: deserialize failed for event={eventName}: {ex.Message}");
					return Task.CompletedTask;
				}
			}
			return (val == null) ? Task.CompletedTask : handler(val);
		});
	}

	public IAsyncDisposable Subscribe(string eventName, Func<Task> handler)
	{
		return Subscribe(eventName, (JsonElement _) => handler());
	}

	private void Unsubscribe(string eventName, Func<JsonElement, Task> handler)
	{
		lock (_lock)
		{
			if (_handlers.TryGetValue(eventName, out List<Func<JsonElement, Task>> value))
			{
				value.Remove(handler);
			}
		}
	}
}
