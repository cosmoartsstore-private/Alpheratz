namespace Alpheratz.Shared.Models;

public enum SourceSlot
{
    Primary = 1,
    Secondary = 2,
}

public static class SourceSlotExtensions
{
    public static long ToBackendValue(this SourceSlot sourceSlot) => sourceSlot == SourceSlot.Secondary ? 2 : 1;

    public static SourceSlot FromBackendValue(long sourceSlot) => sourceSlot == 2 ? SourceSlot.Secondary : SourceSlot.Primary;

    public static string ToTsDisplayFolderMode(this SourceSlot sourceSlot) => sourceSlot == SourceSlot.Secondary ? "secondary" : "primary";
}
