using System.Collections.Generic;
using Alpheratz.Shared.Models;

namespace Alpheratz.Core.Database;

// ---------------------------------------------------------------------------
// Query parameter bag for paginated photo queries.
// ---------------------------------------------------------------------------
public sealed class PhotoQueryParams
{
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
    public string? WorldQuery { get; init; }
    public IReadOnlyList<string>? WorldExacts { get; init; }
    public string? Orientation { get; init; }
    public bool? FavoritesOnly { get; init; }
    public IReadOnlyList<string>? TagFilters { get; init; }
    public long? SourceSlot { get; init; }
    public string? PhotoPathExact { get; init; }
    public int? Limit { get; init; }
    public int? Offset { get; init; }
    public bool IncludePhash { get; init; }
    public SortMode Sort { get; init; } = SortMode.dateDesc;
}