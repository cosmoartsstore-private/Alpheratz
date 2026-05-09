using System.Text.Json.Serialization;

namespace Alpheratz.Models;

public sealed record SimilarWorldCandidateDto
{
    [JsonPropertyName("photo")]
    public PhotoRecordDto photo { get; init; } = new();

    [JsonPropertyName("distance")]
    public int distance { get; init; }

    [JsonPropertyName("similarity")]
    public double similarity { get; init; }
}
