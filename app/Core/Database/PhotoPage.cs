using System.Collections.Generic;
using Alpheratz.Models;

namespace Alpheratz.Core.Database;

// ---------------------------------------------------------------------------
// Paginated photo result.
// ---------------------------------------------------------------------------
public sealed class PhotoPage
{
    public IReadOnlyList<PhotoRecordDto> Items { get; init; } = [];
    public int Total { get; init; }
}
