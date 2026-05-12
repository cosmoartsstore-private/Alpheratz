using System.Collections.Generic;
using Alpheratz.Shared.Models;

namespace Alpheratz.Models;

public sealed record PhotoQueryPayload(
    string? startDate,
    string? endDate,
    string? worldQuery,
    IReadOnlyList<string>? worldExacts,
    string? orientation,
    bool? favoritesOnly,
    IReadOnlyList<string>? tagFilters,
    long? sourceSlot,
    int? limit,
    int? offset,
    bool? includePhash = null,
    SortMode sortMode = SortMode.dateDesc);