using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace Alpheratz.Shared.Converters;

public sealed class MatchPercentConverter : IValueConverter
{
	private const double PdqMaxDistance = 256.0;

	public object Convert(object value, Type targetType, object parameter, string language)
	{
		if (value is int num)
		{
			int num2 = (int)Math.Round((1.0 - (double)num / 256.0) * 100.0);
			if (num2 < 0)
			{
				num2 = 0;
			}
			if (num2 > 100)
			{
				num2 = 100;
			}
			return "一致率: " + num2.ToString(CultureInfo.InvariantCulture) + "%";
		}
		return "一致率: —";
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
	{
		throw new NotSupportedException();
	}
}
