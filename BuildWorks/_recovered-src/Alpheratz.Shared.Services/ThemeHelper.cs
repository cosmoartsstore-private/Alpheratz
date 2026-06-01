using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Shared.Services;

public static class ThemeHelper
{
	public static ElementTheme SelectedTheme { get; private set; }

	public static void NotifySelectedThemeChanged(ElementTheme theme)
	{
		SelectedTheme = theme;
	}

	public static Brush? Brush(FrameworkElement element, string key)
	{
		return Resolve<Brush>(element, key);
	}

	public static Brush? BrushForSelectedTheme(string key)
	{
		string themeKey = ((SelectedTheme == ElementTheme.Dark) ? "Default" : ((SelectedTheme == ElementTheme.Light) ? "Light" : ((Application.Current.RequestedTheme == ApplicationTheme.Dark) ? "Default" : "Light")));
		if (TryFind<Brush>(Application.Current.Resources, themeKey, key, out Brush value))
		{
			return value;
		}
		return null;
	}

	public static T? Resource<T>(FrameworkElement element, string key) where T : class
	{
		return Resolve<T>(element, key);
	}

	public static T? AppResource<T>(string key) where T : class
	{
		ResourceDictionary resources = Application.Current.Resources;
		if (resources.TryGetValue(key, out var value) && value is T result)
		{
			return result;
		}
		for (int num = resources.MergedDictionaries.Count - 1; num >= 0; num--)
		{
			if (resources.MergedDictionaries[num].TryGetValue(key, out var value2) && value2 is T result2)
			{
				return result2;
			}
		}
		return null;
	}

	private static T? Resolve<T>(FrameworkElement element, string key) where T : class
	{
		string themeKey = element.ActualTheme switch
		{
			ElementTheme.Dark => "Default", 
			ElementTheme.Light => "Light", 
			_ => (Application.Current.RequestedTheme == ApplicationTheme.Dark) ? "Default" : "Light", 
		};
		FrameworkElement frameworkElement = element;
		while ((object)frameworkElement != null)
		{
			if (TryFind<T>(frameworkElement.Resources, themeKey, key, out T value))
			{
				return value;
			}
			frameworkElement = frameworkElement.Parent as FrameworkElement;
		}
		if (TryFind<T>(Application.Current.Resources, themeKey, key, out T value2))
		{
			return value2;
		}
		return null;
	}

	private static bool TryFind<T>(ResourceDictionary dict, string themeKey, string key, out T? value) where T : class
	{
		if (dict.ThemeDictionaries.TryGetValue(themeKey, out var value2) && value2 is ResourceDictionary resourceDictionary && resourceDictionary.TryGetValue(key, out var value3) && value3 is T val)
		{
			value = val;
			return true;
		}
		for (int num = dict.MergedDictionaries.Count - 1; num >= 0; num--)
		{
			if (TryFind<T>(dict.MergedDictionaries[num], themeKey, key, out value))
			{
				return true;
			}
		}
		value = null;
		return false;
	}
}
