using System.Collections.Generic;

namespace Alpheratz.Features.Gallery;

public sealed record MasonryLayoutResult(IReadOnlyList<MasonryItem> Items, double TotalHeight, double ColumnWidth, int ColumnCount, double Gap);
