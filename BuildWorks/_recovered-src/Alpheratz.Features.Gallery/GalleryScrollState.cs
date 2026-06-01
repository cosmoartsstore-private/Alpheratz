using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Gallery;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class GalleryScrollState : UiThreadSafeObservableObject
{
	[ObservableProperty]
	private double scrollTop;

	private double pendingScrollTop;

	public double totalRows { get; private set; }

	public double totalHeight { get; private set; }

	public double maxScrollTop { get; private set; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public double ScrollTop
	{
		get
		{
			return scrollTop;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(scrollTop, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ScrollTop);
				scrollTop = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ScrollTop);
			}
		}
	}

	public void Recalculate(int photosLength, int columnCount, double gridHeight, double ROW_HEIGHT, bool disableProgrammaticBounds = false)
	{
		try
		{
			totalRows = Math.Ceiling((double)photosLength / (double)Math.Max(1, columnCount));
			totalHeight = totalRows * ROW_HEIGHT;
			maxScrollTop = Math.Max(0.0, totalHeight - gridHeight);
			if (!disableProgrammaticBounds)
			{
				ScrollTop = (pendingScrollTop = Math.Max(0.0, Math.Min(maxScrollTop, pendingScrollTop)));
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryScrollState.Recalculate: threw: {value}");
		}
	}

	public void handleGridScroll(double currentScrollTop)
	{
		try
		{
			pendingScrollTop = currentScrollTop;
			ScrollTop = pendingScrollTop;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryScrollState.handleGridScroll: threw: {value}");
		}
	}

	public void handleGridWheel(double deltaY, bool disableProgrammaticBounds = false)
	{
		try
		{
			if (!disableProgrammaticBounds && !(maxScrollTop <= 0.0))
			{
				ScrollTop = (pendingScrollTop = Math.Max(0.0, Math.Min(maxScrollTop, ScrollTop + deltaY)));
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryScrollState.handleGridWheel: threw: {value}");
		}
	}

	public void onGridRef(double currentScrollTop, bool disableProgrammaticBounds = false)
	{
		try
		{
			if (disableProgrammaticBounds)
			{
				ScrollTop = currentScrollTop;
			}
			else
			{
				ScrollTop = (pendingScrollTop = Math.Max(0.0, Math.Min(maxScrollTop, pendingScrollTop)));
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryScrollState.onGridRef: threw: {value}");
		}
	}
}
