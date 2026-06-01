namespace Alpheratz.Core.Database;

public sealed class ArchiveWorldVisitData
{
	public string SourceLogName { get; init; } = "";

	public string WorldName { get; init; } = "";

	public string JoinTime { get; init; } = "";

	public string? LeaveTime { get; init; }
}
