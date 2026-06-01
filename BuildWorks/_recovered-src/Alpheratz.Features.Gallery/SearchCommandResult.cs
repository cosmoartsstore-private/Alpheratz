using System.Collections.Generic;

namespace Alpheratz.Features.Gallery;

public class SearchCommandResult
{
	public string PlainText { get; set; } = string.Empty;

	public string? DateFrom { get; set; }

	public string? DateTo { get; set; }

	public string? OrientationFilter { get; set; }

	public bool? FavoritesOnly { get; set; }

	public List<string> Tags { get; } = new List<string>();

	public string? FolderMode { get; set; }

	public string? SortMode { get; set; }

	public bool HasCommands
	{
		get
		{
			if (DateFrom == null && DateTo == null && OrientationFilter == null && !FavoritesOnly.HasValue && Tags.Count <= 0 && FolderMode == null)
			{
				return SortMode != null;
			}
			return true;
		}
	}

	public SearchCommandResult()
	{
	}

	public SearchCommandResult(string plainText)
	{
		PlainText = plainText;
	}
}
