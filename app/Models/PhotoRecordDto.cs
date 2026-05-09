using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Alpheratz.Models;

public sealed record PhotoRecordDto
{
    [JsonPropertyName("photo_filename")]
    public string photo_filename { get; init; } = string.Empty;

    [JsonPropertyName("photo_path")]
    public string photo_path { get; init; } = string.Empty;

    [JsonPropertyName("resolved_photo_path")]
    public string? resolved_photo_path { get; init; }

    [JsonPropertyName("grid_thumb_path")]
    public string? grid_thumb_path { get; init; }

    [JsonPropertyName("display_thumb_path")]
    public string? display_thumb_path { get; init; }

    [JsonPropertyName("world_id")]
    public string? world_id { get; init; }

    [JsonPropertyName("world_name")]
    public string? world_name { get; init; }

    [JsonPropertyName("timestamp")]
    public string timestamp { get; init; } = string.Empty;

    [JsonPropertyName("memo")]
    public string memo { get; init; } = string.Empty;

    [JsonPropertyName("phash")]
    public string? phash { get; init; }

    [JsonPropertyName("orientation")]
    public string? orientation { get; init; }

    [JsonPropertyName("image_width")]
    public long? image_width { get; init; }

    [JsonPropertyName("image_height")]
    public long? image_height { get; init; }

    [JsonPropertyName("source_slot")]
    public long source_slot { get; init; } = 1;

    [JsonPropertyName("is_favorite")]
    public bool is_favorite { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> tags { get; init; } = [];

    [JsonPropertyName("match_source")]
    public string? match_source { get; init; }

    [JsonPropertyName("is_missing")]
    public bool is_missing { get; init; }
}
