using System.Collections.Generic;
using Alpheratz.Shared.Models;

namespace Alpheratz.Features.Gallery;

public sealed record PhotoQueryFilters(
    string searchQuery,
    IReadOnlyList<string> worldFilters,
    string dateFrom,
    string dateTo,
    string orientationFilter,
    bool favoritesOnly,
    IReadOnlyList<string> tagFilters,
    bool includePhash,
    bool pagingEnabled,
    ViewMode viewMode,
    long? sourceSlot,
    GroupingMode groupingMode,
    SortMode sortMode = SortMode.dateDesc);