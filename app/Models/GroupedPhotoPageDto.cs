using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Alpheratz.Models;

public sealed record GroupedPhotoPageDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<GroupedPhotoRecordDto> items { get; init; } = [];

    [JsonPropertyName("total")]
    public int total { get; init; }
}
