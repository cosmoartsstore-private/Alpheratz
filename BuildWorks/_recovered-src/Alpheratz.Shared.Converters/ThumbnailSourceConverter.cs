using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Alpheratz.Shared.Converters;

public sealed class ThumbnailSourceConverter : IValueConverter
{
	public int DecodePixelWidth { get; set; } = 260;

	public object? Convert(object value, Type targetType, object parameter, string language)
	{
		if (!(value is string text) || string.IsNullOrEmpty(text))
		{
			return null;
		}
		try
		{
			BitmapImage bitmapImage = new BitmapImage
			{
				CreateOptions = BitmapCreateOptions.IgnoreImageCache,
				UriSource = new Uri(text, UriKind.Absolute)
			};
			if (DecodePixelWidth > 0)
			{
				bitmapImage.DecodePixelWidth = DecodePixelWidth;
				bitmapImage.DecodePixelType = DecodePixelType.Logical;
			}
			return bitmapImage;
		}
		catch
		{
			return null;
		}
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
	{
		throw new NotSupportedException();
	}
}
