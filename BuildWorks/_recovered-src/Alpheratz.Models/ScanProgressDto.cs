using System.Text.Json.Serialization;

namespace Alpheratz.Models;

public sealed record ScanProgressDto
{
	[JsonPropertyName("processed")]
	public int processed { get; init; }

	[JsonPropertyName("total")]
	public int total { get; init; }

	[JsonPropertyName("current_world")]
	public string current_world { get; init; } = string.Empty;

	[JsonPropertyName("phase")]
	public string phase { get; init; } = "scan";
}
