using System.Text.Json.Serialization;

namespace Alpheratz.Models.Events;

// TS: type PhashProgress = { done: number; total: number; current?: string | null; }
public sealed record PhashProgressEvent
{
    [JsonPropertyName("done")]
    public int done { get; init; }

    [JsonPropertyName("total")]
    public int total { get; init; }

    [JsonPropertyName("current")]
    public string? current { get; init; }

    public static PhashProgressEvent Empty { get; } = new()
    {
        done = 0,
        total = 0,
        current = null,
    };
}
