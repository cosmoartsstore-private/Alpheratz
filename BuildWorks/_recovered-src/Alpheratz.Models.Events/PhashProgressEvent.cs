using System.Text.Json.Serialization;

namespace Alpheratz.Models.Events;

public sealed record PhashProgressEvent
{
	[JsonPropertyName("done")]
	public int done { get; init; }

	[JsonPropertyName("total")]
	public int total { get; init; }

	[JsonPropertyName("current")]
	public string? current { get; init; }

	public static PhashProgressEvent Empty { get; } = new PhashProgressEvent
	{
		done = 0,
		total = 0,
		current = null
	};
}
