using System.Text.Json.Serialization;

namespace Alpheratz.Models.Events;

public sealed record OrientationProgressEvent
{
	[JsonPropertyName("processed")]
	public int processed { get; init; }

	[JsonPropertyName("total")]
	public int total { get; init; }

	[JsonPropertyName("running")]
	public bool running { get; init; }
}
