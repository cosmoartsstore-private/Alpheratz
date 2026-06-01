namespace Alpheratz.Models.Events;

public static class EventNames
{
	public const string ScanProgress = "scan:progress";

	public const string ScanCompleted = "scan:completed";

	public const string ScanEnrichCompleted = "scan:enrich_completed";

	public const string ScanError = "scan:error";

	public const string PhashProgress = "phash_progress";

	public const string PhashComplete = "phash_complete";

	public const string PhashError = "phash_error";

	public const string OrientationProgress = "orientation_progress";

	public const string OrientationComplete = "orientation_complete";
}
