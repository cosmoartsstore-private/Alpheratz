using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Alpheratz.Models;

public sealed record PhotoPageDto
{
	[JsonPropertyName("items")]
	public IReadOnlyList<PhotoRecordDto> items { get; init; } = Array.Empty<PhotoRecordDto>();

	[JsonPropertyName("total")]
	public int total { get; init; }
}
