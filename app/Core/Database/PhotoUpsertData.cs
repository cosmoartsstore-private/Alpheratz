namespace Alpheratz.Core.Database;

// ---------------------------------------------------------------------------
// Data bag used when upserting a photo from the scanner.
// ---------------------------------------------------------------------------
public sealed class PhotoUpsertData
{
    public string PhotoPath { get; init; } = "";
    public string PhotoFilename { get; init; } = "";
    public string? WorldId { get; init; }
    public string? WorldName { get; init; }
    public string Timestamp { get; init; } = "";
    public string? LastModifiedUtc { get; init; }
    public string? Orientation { get; init; }
    public long? ImageWidth { get; init; }
    public long? ImageHeight { get; init; }
    public long SourceSlot { get; init; } = 1;
    public string? MatchSource { get; init; }
}
