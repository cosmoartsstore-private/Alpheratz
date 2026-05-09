namespace Alpheratz.Core.Database;

// ---------------------------------------------------------------------------
// A single parsed world-visit entry from the Polaris archive log.
// ---------------------------------------------------------------------------
public sealed class ArchiveWorldVisitData
{
    public string SourceLogName { get; init; } = "";
    public string WorldName { get; init; } = "";
    public string JoinTime { get; init; } = "";
    public string? LeaveTime { get; init; }
}
