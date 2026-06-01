using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Alpheratz.Shared.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
	{
		bool flag = default(bool);
		int num;
		if (value is bool)
		{
			flag = (bool)value;
			num = 1;
		}
		else
		{
			num = 0;
		}
		bool flag2 = (byte)((uint)num & (flag ? 1u : 0u)) != 0;
		if (parameter is string text && text.Equals("invert", StringComparison.OrdinalIgnoreCase))
		{
			flag2 = !flag2;
		}
		return (!flag2) ? Visibility.Collapsed : Visibility.Visible;
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
	{
		throw new NotSupportedException();
	}
}
