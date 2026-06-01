using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Alpheratz.Shared.Converters;

public sealed class IntToVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
	{
		bool flag = value is int num && num > 0;
		if (parameter is string text && text.Equals("invert", StringComparison.OrdinalIgnoreCase))
		{
			flag = !flag;
		}
		return (!flag) ? Visibility.Collapsed : Visibility.Visible;
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
	{
		throw new NotSupportedException();
	}
}
