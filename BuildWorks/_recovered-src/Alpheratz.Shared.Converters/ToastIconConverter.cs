using System;
using Alpheratz.Shared.Models;
using Microsoft.UI.Xaml.Data;

namespace Alpheratz.Shared.Converters;

public sealed class ToastIconConverter : IValueConverter
{
	internal static string Glyph(ToastType type)
	{
		return type switch
		{
			ToastType.success => "\ue930", 
			ToastType.error => "\ue783", 
			_ => "\ue946", 
		};
	}

	public object Convert(object value, Type targetType, object parameter, string language)
	{
		if (!(value is ToastType type))
		{
			return Glyph(ToastType.info);
		}
		return Glyph(type);
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
	{
		throw new NotSupportedException();
	}
}
