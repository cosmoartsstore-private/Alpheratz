using System.Collections.Generic;
using Alpheratz.Models;

namespace Alpheratz.Core.Database;

// ページ単位で取得した写真の結果。
public sealed class PhotoPage
{
    public IReadOnlyList<PhotoRecordDto> Items { get; init; } = [];
    public int Total { get; init; }
}
