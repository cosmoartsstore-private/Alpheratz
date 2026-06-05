namespace Alpheratz.Shared.Models;

public enum SourceSlot
{
    Primary = 1,
    Secondary = 2,
}

public static class SourceSlotExtensions
{
    /// <summary>DB の source_slot 列に保存する数値へ変換する。</summary>
    public static long ToBackendValue(this SourceSlot sourceSlot) => sourceSlot == SourceSlot.Secondary ? 2 : 1;

    /// <summary>DB の source_slot 値から画面側の列挙値へ変換する。</summary>
    public static SourceSlot FromBackendValue(long sourceSlot) => sourceSlot == 2 ? SourceSlot.Secondary : SourceSlot.Primary;

    /// <summary>既存の表示フォルダモード文字列へ変換する。</summary>
    public static string ToTsDisplayFolderMode(this SourceSlot sourceSlot) => sourceSlot == SourceSlot.Secondary ? "secondary" : "primary";
}
