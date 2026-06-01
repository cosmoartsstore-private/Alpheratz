namespace Alpheratz.Shared.Models;

public static class SourceSlotExtensions
{
	public static long ToBackendValue(this SourceSlot sourceSlot)
	{
		return (sourceSlot != SourceSlot.Secondary) ? 1 : 2;
	}

	public static SourceSlot FromBackendValue(long sourceSlot)
	{
		if (sourceSlot != 2)
		{
			return SourceSlot.Primary;
		}
		return SourceSlot.Secondary;
	}

	public static string ToTsDisplayFolderMode(this SourceSlot sourceSlot)
	{
		if (sourceSlot != SourceSlot.Secondary)
		{
			return "primary";
		}
		return "secondary";
	}
}
