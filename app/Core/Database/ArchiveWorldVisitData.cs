namespace Alpheratz.Core.Database;

// Polaris の archive ログから解析した1件のワールド訪問記録。
public sealed class ArchiveWorldVisitData
{
    public string SourceLogName { get; init; } = "";
    public string WorldName { get; init; } = "";
    public string JoinTime { get; init; } = "";
    public string? LeaveTime { get; init; }
}
