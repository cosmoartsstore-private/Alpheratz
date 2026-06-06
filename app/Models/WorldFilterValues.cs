namespace Alpheratz.Models;

public static class WorldFilterValues
{
    public const string Unknown = "__ALPHERATZ_UNKNOWN_WORLD__";

    public static bool IsUnknown(string? value) => value == Unknown;
}
