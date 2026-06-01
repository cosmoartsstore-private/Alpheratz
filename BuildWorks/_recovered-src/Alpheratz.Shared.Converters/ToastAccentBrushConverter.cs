using System;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml.Data;

namespace Alpheratz.Shared.Converters;

public sealed class ToastAccentBrushConverter : IValueConverter
{
	internal static string AccentBrushKey(ToastType type)
	{
		return type switch
		{
			ToastType.success => "ASuccess", 
			ToastType.error => "ADangerSolid", 
			_ => "APrimary", 
		};
	}

	public object? Convert(object value, Type targetType, object parameter, string language)
	{
		return ThemeHelper.BrushForSelectedTheme((value is ToastType type) ? AccentBrushKey(type) : AccentBrushKey(ToastType.info));
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
	{
		throw new NotSupportedException();
	}
}
