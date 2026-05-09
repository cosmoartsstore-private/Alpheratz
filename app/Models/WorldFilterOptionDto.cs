using System.Text.Json.Serialization;

namespace Alpheratz.Models;

public sealed record WorldFilterOptionDto
{
    [JsonPropertyName("world_name")]
    public string? world_name { get; init; }

    [JsonPropertyName("count")]
    public long count { get; init; }
}
