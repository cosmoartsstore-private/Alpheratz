namespace Alpheratz.Core.Database;

public sealed class ExistingPhotoInfo
{
	public string PhotoFilename { get; init; } = "";

	public string PhotoPath { get; init; } = "";

	public string? WorldId { get; init; }

	public string? WorldName { get; init; }

	public string? MatchSource { get; init; }

	public string? Orientation { get; init; }

	public long? ImageWidth { get; init; }

	public long? ImageHeight { get; init; }

	public long SourceSlot { get; init; } = 1L;

	public bool IsMissing { get; init; }
}
