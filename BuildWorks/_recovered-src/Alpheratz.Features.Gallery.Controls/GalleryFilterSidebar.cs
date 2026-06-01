using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Gallery.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class GalleryFilterSidebar : UserControl, IComponentConnector
{
	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public Action? OnResetFilters { get; set; }

	public Action<string>? OnDatePresetSelect { get; set; }

	public GalleryFilterSidebar()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterSidebar.ctor: InitializeComponent failed: {value}");
			throw;
		}
	}

	private void ResetFilters_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnResetFilters?.Invoke();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterSidebar.ResetFilters_Click: threw: {value}");
		}
	}

	private void PresetToday_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnDatePresetSelect?.Invoke("today");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterSidebar.PresetToday_Click: threw: {value}");
		}
	}

	private void PresetLast7Days_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnDatePresetSelect?.Invoke("last7days");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterSidebar.PresetLast7Days_Click: threw: {value}");
		}
	}

	private void PresetThisMonth_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnDatePresetSelect?.Invoke("thisMonth");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterSidebar.PresetThisMonth_Click: threw: {value}");
		}
	}

	private void PresetLastMonth_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnDatePresetSelect?.Invoke("lastMonth");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterSidebar.PresetLastMonth_Click: threw: {value}");
		}
	}

	private void PresetHalfYear_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnDatePresetSelect?.Invoke("halfYear");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterSidebar.PresetHalfYear_Click: threw: {value}");
		}
	}

	private void PresetOneYear_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnDatePresetSelect?.Invoke("oneYear");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryFilterSidebar.PresetOneYear_Click: threw: {value}");
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/Gallery/Controls/GalleryFilterSidebar.xaml");
			Application.LoadComponent(this, resourceLocator, ComponentResourceLocation.Application);
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void Connect(int connectionId, object target)
	{
		_contentLoaded = true;
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public IComponentConnector GetBindingConnector(int connectionId, object target)
	{
		return null;
	}
}
