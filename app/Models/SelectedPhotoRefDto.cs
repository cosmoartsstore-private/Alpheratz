using System.Text.Json.Serialization;

namespace Alpheratz.Models;

public sealed record SelectedPhotoRefDto
{
    [JsonPropertyName("photo_path")]
    public string photo_path { get; init; } = string.Empty;

    [JsonPropertyName("source_slot")]
    public long source_slot { get; init; } = 1;
}
