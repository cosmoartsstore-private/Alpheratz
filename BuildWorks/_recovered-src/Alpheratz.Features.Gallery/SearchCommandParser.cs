using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Alpheratz.Features.Gallery;

public static class SearchCommandParser
{
	private static readonly Regex CommandPattern = new Regex("(?<key>[a-zA-Z]+):(?:\"(?<qval>[^\"]*)\"|(?<val>\\S+))", RegexOptions.Compiled);

	public static SearchCommandResult Parse(string rawQuery)
	{
		if (string.IsNullOrWhiteSpace(rawQuery))
		{
			return new SearchCommandResult(string.Empty);
		}
		SearchCommandResult searchCommandResult = new SearchCommandResult();
		List<string> list = new List<string>();
		int num = 0;
		foreach (Match item in CommandPattern.Matches(rawQuery))
		{
			if (item.Index > num)
			{
				string text = rawQuery.Substring(num, item.Index - num);
				if (!string.IsNullOrWhiteSpace(text))
				{
					list.Add(text.Trim());
				}
			}
			string key = item.Groups["key"].Value.ToLowerInvariant();
			string value = (item.Groups["qval"].Success ? item.Groups["qval"].Value : item.Groups["val"].Value);
			if (!tryApplyCommand(key, value, searchCommandResult))
			{
				list.Add(item.Value);
			}
			num = item.Index + item.Length;
		}
		if (num < rawQuery.Length)
		{
			string text2 = rawQuery.Substring(num);
			if (!string.IsNullOrWhiteSpace(text2))
			{
				list.Add(text2.Trim());
			}
		}
		searchCommandResult.PlainText = string.Join(" ", list);
		return searchCommandResult;
	}

	private static bool tryApplyCommand(string key, string value, SearchCommandResult result)
	{
		switch (key)
		{
		case "since":
			if (isValidDate(value))
			{
				result.DateFrom = value;
				return true;
			}
			return false;
		case "until":
			if (isValidDate(value))
			{
				result.DateTo = value;
				return true;
			}
			return false;
		case "orientation":
		{
			string text2 = value.ToLowerInvariant();
			if ((text2 == "portrait" || text2 == "landscape") ? true : false)
			{
				result.OrientationFilter = text2;
				return true;
			}
			return false;
		}
		case "is":
		{
			string text4 = value.ToLowerInvariant();
			if ((text4 == "favorite" || text4 == "fav") ? true : false)
			{
				result.FavoritesOnly = true;
				return true;
			}
			return false;
		}
		case "tag":
			if (!string.IsNullOrWhiteSpace(value))
			{
				result.Tags.Add(value);
				return true;
			}
			return false;
		case "folder":
		{
			string text3 = value.ToLowerInvariant();
			if ((text3 == "primary" || text3 == "secondary") ? true : false)
			{
				result.FolderMode = text3;
				return true;
			}
			return false;
		}
		case "sort":
		{
			string text = value.ToLowerInvariant();
			if ((text == "date" || text == "world") ? true : false)
			{
				result.SortMode = text;
				return true;
			}
			return false;
		}
		default:
			return false;
		}
	}

	private static bool isValidDate(string value)
	{
		DateTime result;
		return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
	}
}
