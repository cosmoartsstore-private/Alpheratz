using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Shared.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class PhotoGrid : UserControl, IComponentConnector
{
	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private PhotoGridItemsView ItemsViewControl;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private CustomScrollbar CustomScrollbarControl;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public Func<Task>? OnGoToPrevPage { get; set; }

	public Func<Task>? OnGoToNextPage { get; set; }

	public Func<Task>? OnLoadMorePhotos { get; set; }

	public Action<double>? OnRightPanelMeasured { get; set; }

	public Action<double>? OnGridWrapperMeasured { get; set; }

	public Action<PhotoGridItem>? OnPhotoActivated { get; set; }

	public Action<PhotoGridItem>? OnFavoriteClicked { get; set; }

	public Action<double>? OnGridScroll { get; set; }

	public Action<int>? OnGridWheel { get; set; }

	public Action<int>? OnFirstVisibleIndexChanged { get; set; }

	public Action<double>? OnScrollbarTrackClick { get; set; }

	public Action<double>? OnScrollbarDrag { get; set; }

	public void SetItemsSource(object? source)
	{
		ItemsViewControl.SetItemsSource(source);
	}

	public PhotoGrid()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGrid.ctor: InitializeComponent failed: {value}");
			throw;
		}
		try
		{
			base.SizeChanged += PhotoGrid_SizeChanged;
			CustomScrollbarControl.OnTrackClick = delegate(double position)
			{
				ScrollToTrackPosition(position);
			};
			CustomScrollbarControl.OnDrag = delegate(double position)
			{
				ScrollToTrackPosition(position);
			};
			ItemsViewControl.OnPhotoActivated = delegate(PhotoGridItem item)
			{
				OnPhotoActivated?.Invoke(item);
			};
			ItemsViewControl.OnFavoriteClicked = delegate(PhotoGridItem item)
			{
				OnFavoriteClicked?.Invoke(item);
			};
			ItemsViewControl.OnGridScroll = delegate(double offset)
			{
				OnGridScroll?.Invoke(offset);
				UpdateScrollbar();
			};
			ItemsViewControl.OnGridWheel = delegate(int delta)
			{
				OnGridWheel?.Invoke(delta);
			};
			ItemsViewControl.OnFirstVisibleIndexChanged = delegate(int idx)
			{
				OnFirstVisibleIndexChanged?.Invoke(idx);
			};
			ItemsViewControl.OnNearBottomReached = delegate
			{
				OnLoadMorePhotos?.Invoke();
			};
		}
		catch (Exception value2)
		{
			AppLogger.Error($"PhotoGrid.ctor: wiring failed: {value2}");
			throw;
		}
	}

	public void ScrollToTop()
	{
		try
		{
			ItemsViewControl.GridScrollViewerRef?.ChangeView(null, 0.0, null);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGrid.ScrollToTop: threw: {value}");
		}
	}

	public void ScrollToPhotoIndex(int index)
	{
		try
		{
			ItemsViewControl.ScrollToItemIndex(index);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGrid.ScrollToPhotoIndex: threw: {value}");
		}
	}

	private void ScrollToTrackPosition(double trackY)
	{
		try
		{
			ScrollViewer gridScrollViewerRef = ItemsViewControl.GridScrollViewerRef;
			if ((object)gridScrollViewerRef != null)
			{
				double extentHeight = gridScrollViewerRef.ExtentHeight;
				double viewportHeight = gridScrollViewerRef.ViewportHeight;
				if (!(extentHeight <= viewportHeight))
				{
					double num = Math.Max(1.0, base.ActualHeight - 48.0);
					double num2 = Math.Clamp(trackY / num, 0.0, 1.0);
					gridScrollViewerRef.ChangeView(null, num2 * (extentHeight - viewportHeight), null, disableAnimation: true);
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGrid.ScrollToTrackPosition: threw: {value}");
		}
	}

	private void UpdateScrollbar()
	{
		try
		{
			ScrollViewer gridScrollViewerRef = ItemsViewControl.GridScrollViewerRef;
			if ((object)gridScrollViewerRef != null)
			{
				double extentHeight = gridScrollViewerRef.ExtentHeight;
				double viewportHeight = gridScrollViewerRef.ViewportHeight;
				if (extentHeight <= 0.0 || viewportHeight <= 0.0 || extentHeight <= viewportHeight)
				{
					CustomScrollbarControl.ThumbTop = 0.0;
					CustomScrollbarControl.ThumbHeight = viewportHeight;
					return;
				}
				double num = Math.Max(1.0, base.ActualHeight - 48.0);
				double num2 = viewportHeight / extentHeight;
				CustomScrollbarControl.ThumbHeight = Math.Max(18.0, num * num2);
				CustomScrollbarControl.ThumbTop = gridScrollViewerRef.VerticalOffset / (extentHeight - viewportHeight) * Math.Max(0.0, num - CustomScrollbarControl.ThumbHeight);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGrid.UpdateScrollbar: threw: {value}");
		}
	}

	private void PhotoGrid_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		try
		{
			OnRightPanelMeasured?.Invoke(e.NewSize.Width);
			OnGridWrapperMeasured?.Invoke(e.NewSize.Height);
			UpdateScrollbar();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGrid.PhotoGrid_SizeChanged: threw: {value}");
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Shared/Controls/PhotoGrid.xaml");
			Application.LoadComponent(this, resourceLocator, ComponentResourceLocation.Application);
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 2:
			ItemsViewControl = target.As<PhotoGridItemsView>();
			break;
		case 3:
			CustomScrollbarControl = target.As<CustomScrollbar>();
			break;
		}
		_contentLoaded = true;
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public IComponentConnector GetBindingConnector(int connectionId, object target)
	{
		return null;
	}
}
