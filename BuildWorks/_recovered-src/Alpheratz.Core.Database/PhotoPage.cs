using System;
using System.Collections.Generic;
using Alpheratz.Models;

namespace Alpheratz.Core.Database;

public sealed class PhotoPage
{
	public IReadOnlyList<PhotoRecordDto> Items { get; init; } = Array.Empty<PhotoRecordDto>();

	public int Total { get; init; }
}
