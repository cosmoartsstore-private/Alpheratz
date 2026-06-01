using System;
using System.Collections.Generic;

namespace Alpheratz.Features.Gallery;

public static class GalleryMasonryLayout
{
	public const double Gap = 14.0;

	public const double MinColumnWidth = 320.0;

	public const double MaxCardHeight = 900.0;

	private const double MinAspect = 0.5;

	private const double MaxAspect = 2.0;

	public static MasonryLayoutResult Build(IReadOnlyList<PhotoThumbnailItem> photos, double panelWidth, int requestedColumnCount)
	{
		double num = Math.Max(panelWidth - 8.0, 320.0);
		int val = Math.Max(1, (int)Math.Floor((num + 14.0) / 334.0));
		int num2 = Math.Max(1, Math.Min(requestedColumnCount, val));
		double num3 = Math.Floor((num - 14.0 * (double)(num2 - 1)) / (double)num2);
		if (num3 < 320.0)
		{
			num3 = 320.0;
		}
		double[] array = new double[num2];
		List<MasonryItem> list = new List<MasonryItem>(photos.Count);
		foreach (PhotoThumbnailItem photo in photos)
		{
			int num4 = 0;
			for (int i = 1; i < num2; i++)
			{
				if (array[i] < array[num4])
				{
					num4 = i;
				}
			}
			double cardHeight = GetCardHeight(photo, num3);
			double top = array[num4];
			double left = (double)num4 * (num3 + 14.0);
			array[num4] += cardHeight + 14.0;
			list.Add(new MasonryItem(photo, top, left, num3, cardHeight));
		}
		double num5 = 0.0;
		double[] array2 = array;
		foreach (double num6 in array2)
		{
			if (num6 > num5)
			{
				num5 = num6;
			}
		}
		double totalHeight = Math.Max(0.0, num5 - ((photos.Count > 0) ? 14.0 : 0.0));
		return new MasonryLayoutResult(list, totalHeight, num3, num2, 14.0);
	}

	private static double GetAspectRatio(PhotoThumbnailItem photo)
	{
		long valueOrDefault = photo.ImageWidth.GetValueOrDefault();
		long valueOrDefault2 = photo.ImageHeight.GetValueOrDefault();
		if (valueOrDefault > 0 && valueOrDefault2 > 0)
		{
			return (double)valueOrDefault / (double)valueOrDefault2;
		}
		if (string.Equals(photo.Orientation, "portrait", StringComparison.Ordinal))
		{
			return 0.5625;
		}
		if (string.Equals(photo.Orientation, "landscape", StringComparison.Ordinal))
		{
			return 1.7777777777777777;
		}
		return 1.0;
	}

	private static double GetCardHeight(PhotoThumbnailItem photo, double columnWidth)
	{
		double num = Math.Clamp(GetAspectRatio(photo), 0.5, 2.0);
		return Math.Round(columnWidth / num);
	}
}
