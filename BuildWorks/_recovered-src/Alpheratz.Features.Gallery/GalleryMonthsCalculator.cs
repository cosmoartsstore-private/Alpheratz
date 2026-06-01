using System;
using System.Collections.Generic;
using System.Globalization;

namespace Alpheratz.Features.Gallery;

public static class GalleryMonthsCalculator
{
	public static IReadOnlyList<GalleryMonthGroup> Build(IReadOnlyList<PhotoThumbnailItem> photos)
	{
		if (photos.Count == 0)
		{
			return Array.Empty<GalleryMonthGroup>();
		}
		List<GalleryMonthGroup> list = new List<GalleryMonthGroup>();
		string text = string.Empty;
		int year = 0;
		int num = 0;
		int firstIndex = 0;
		int num2 = 0;
		for (int i = 0; i < photos.Count; i++)
		{
			(int year, int month) tuple = ParseYearMonth(photos[i].Timestamp);
			int item = tuple.year;
			int item2 = tuple.month;
			string text2 = $"{item:D4}-{item2:D2}";
			if (text2 != text)
			{
				if (text.Length > 0)
				{
					list.Add(new GalleryMonthGroup(text, year, num, $"{num}月", firstIndex, num2));
				}
				text = text2;
				year = item;
				num = item2;
				firstIndex = i;
				num2 = 1;
			}
			else
			{
				num2++;
			}
		}
		if (text.Length > 0)
		{
			list.Add(new GalleryMonthGroup(text, year, num, $"{num}月", firstIndex, num2));
		}
		return list;
	}

	private static (int year, int month) ParseYearMonth(string timestamp)
	{
		if (string.IsNullOrEmpty(timestamp))
		{
			return (year: 0, month: 1);
		}
		if (DateTime.TryParse(timestamp.Replace(' ', 'T'), CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
		{
			return (year: result.Year, month: result.Month);
		}
		return (year: 0, month: 1);
	}
}
