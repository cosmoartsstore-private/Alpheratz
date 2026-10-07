namespace Alpheratz.Core.Database;

// scanner が更新対象を判断するために読み込む既存写真行の snapshot。
//
public sealed class ExistingPhotoInfo
{
    public string PhotoFilename { get; init; } = "";
    public string PhotoPath { get; init; } = "";
    public string? WorldId { get; init; }
    public string? WorldName { get; init; }
    public string? LastModifiedUtc { get; init; }
    public string? MatchSource { get; init; }
    public string? Orientation { get; init; }
    public long? ImageWidth { get; init; }
    public long? ImageHeight { get; init; }
    public long SourceSlot { get; init; } = 1;
    public bool IsMissing { get; init; }
}
