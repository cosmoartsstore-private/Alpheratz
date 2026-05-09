using System.Text.Json.Serialization;

namespace Alpheratz.Models;

public sealed record GroupedPhotoRecordDto
{
    [JsonPropertyName("photo")]
    public PhotoRecordDto photo { get; init; } = new();

    [JsonPropertyName("group_count")]
    public int group_count { get; init; }

    [JsonPropertyName("group_key")]
    public string group_key { get; init; } = string.Empty;
}
