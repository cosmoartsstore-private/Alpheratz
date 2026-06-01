using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Shared.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Shared.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class PhotoGridItemsView : UserControl, IComponentConnector
{
	private sealed record GridImageSubscription(PhotoThumbnailItem Photo, PropertyChangedEventHandler Handler);

	private const int FIXED_COLUMNS = 5;

	private const double IMAGE_ASPECT_H = 0.75;

	private const double INFO_HEIGHT = 56.0;

	private const double CARD_MARGIN_H = 8.0;

	private const double CARD_MARGIN_V = 12.0;

	private const double GRID_PADDING = 24.0;

	private const int FALLBACK_COLUMN_COUNT = 5;

	private int lastReportedFirstVisible = -1;

	private ItemsWrapGrid? wrapGrid;

	private ScrollViewer? internalScrollViewer;

	private double lastKnownWidth;

	private double currentImageWidth;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private GridView PhotoItems;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public Action<PhotoGridItem>? OnPhotoActivated { get; set; }

	public Action<PhotoGridItem>? OnFavoriteClicked { get; set; }

	public Action<double>? OnGridScroll { get; set; }

	public Action<int>? OnGridWheel { get; set; }

	public Action? OnNearBottomReached { get; set; }

	public Action<int>? OnFirstVisibleIndexChanged { get; set; }

	public ScrollViewer? GridScrollViewerRef
	{
		get
		{
			if ((object)internalScrollViewer != null)
			{
				return internalScrollViewer;
			}
			internalScrollViewer = FindChildScrollViewer(PhotoItems);
			return internalScrollViewer;
		}
	}

	public void SetItemsSource(object? source)
	{
		PhotoItems.ItemsSource = source;
	}

	public PhotoGridItemsView()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.ctor: InitializeComponent failed: {value}");
			throw;
		}
		PhotoItems.Loaded += PhotoItems_Loaded;
		base.Unloaded += PhotoGridItemsView_Unloaded;
		base.ActualThemeChanged += OnActualThemeChanged;
	}

	private void OnActualThemeChanged(FrameworkElement sender, object args)
	{
		try
		{
			ResetAllCardBrushes(PhotoItems);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.OnActualThemeChanged: {value}");
		}
	}

	private static void ResetAllCardBrushes(DependencyObject root)
	{
		Style style = ThemeHelper.AppResource<Style>("PhotoCardBorderStyle");
		if ((object)style == null)
		{
			return;
		}
		Stack<DependencyObject> stack = new Stack<DependencyObject>();
		stack.Push(root);
		while (stack.Count > 0)
		{
			DependencyObject dependencyObject = stack.Pop();
			if (dependencyObject is Border border && border.Style == style)
			{
				Brush brush = ThemeHelper.Brush(border, "ABorder");
				if ((object)brush != null)
				{
					border.BorderBrush = brush;
				}
				Brush brush2 = ThemeHelper.Brush(border, "ASurface");
				if ((object)brush2 != null)
				{
					border.Background = brush2;
				}
			}
			int childrenCount = VisualTreeHelper.GetChildrenCount(dependencyObject);
			for (int i = 0; i < childrenCount; i++)
			{
				stack.Push(VisualTreeHelper.GetChild(dependencyObject, i));
			}
		}
	}

	private void PhotoGridItemsView_Unloaded(object sender, RoutedEventArgs e)
	{
		try
		{
			UnsubscribeAllCards(PhotoItems);
			base.ActualThemeChanged -= OnActualThemeChanged;
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.PhotoGridItemsView_Unloaded: {value}");
		}
	}

	private static void UnsubscribeAllCards(DependencyObject root)
	{
		Stack<DependencyObject> stack = new Stack<DependencyObject>();
		stack.Push(root);
		while (stack.Count > 0)
		{
			DependencyObject dependencyObject = stack.Pop();
			if (dependencyObject is Image { Tag: GridImageSubscription tag } image)
			{
				tag.Photo.PropertyChanged -= tag.Handler;
				image.Tag = null;
				image.Source = null;
			}
			int childrenCount = VisualTreeHelper.GetChildrenCount(dependencyObject);
			for (int i = 0; i < childrenCount; i++)
			{
				stack.Push(VisualTreeHelper.GetChild(dependencyObject, i));
			}
		}
	}

	private void PhotoItems_Loaded(object sender, RoutedEventArgs e)
	{
		try
		{
			ScrollViewer scrollViewer = FindChildScrollViewer(PhotoItems);
			if ((object)scrollViewer != null)
			{
				internalScrollViewer = scrollViewer;
				scrollViewer.ViewChanged += InternalScrollViewer_ViewChanged;
				scrollViewer.PointerWheelChanged += InternalScrollViewer_PointerWheelChanged;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.PhotoItems_Loaded: {value}");
		}
	}

	private static ScrollViewer? FindChildScrollViewer(DependencyObject parent)
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			if (child is ScrollViewer result)
			{
				return result;
			}
			ScrollViewer scrollViewer = FindChildScrollViewer(child);
			if ((object)scrollViewer != null)
			{
				return scrollViewer;
			}
		}
		return null;
	}

	private void RecalculateCardSize(double availableWidth)
	{
		if (availableWidth <= 0.0 || (object)wrapGrid == null)
		{
			return;
		}
		double num = availableWidth - 24.0;
		if (!(num <= 0.0))
		{
			int num2 = 5;
			double num3 = Math.Floor(num / (double)num2 - 8.0);
			if (!(num3 < 100.0))
			{
				currentImageWidth = num3;
				wrapGrid.ItemWidth = num3 + 8.0;
				wrapGrid.ItemHeight = Math.Floor(num3 * 0.75 + 56.0) + 12.0;
				wrapGrid.MaximumRowsOrColumns = num2;
				RefreshActiveShimmerSizes(PhotoItems);
			}
		}
	}

	private void PhotoItemsWrapGrid_Loaded(object sender, RoutedEventArgs e)
	{
		wrapGrid = sender as ItemsWrapGrid;
		double availableWidth = ((PhotoItems.ActualWidth > 0.0) ? PhotoItems.ActualWidth : lastKnownWidth);
		RecalculateCardSize(availableWidth);
	}

	private void GridView_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		lastKnownWidth = e.NewSize.Width;
		if ((object)wrapGrid == null)
		{
			wrapGrid = FindItemsWrapGrid(PhotoItems);
		}
		RecalculateCardSize(e.NewSize.Width);
	}

	private static ItemsWrapGrid? FindItemsWrapGrid(DependencyObject parent)
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			if (child is ItemsWrapGrid result)
			{
				return result;
			}
			ItemsWrapGrid itemsWrapGrid = FindItemsWrapGrid(child);
			if ((object)itemsWrapGrid != null)
			{
				return itemsWrapGrid;
			}
		}
		return null;
	}

	public void ScrollToItemIndex(int index)
	{
		try
		{
			if (index >= 0 && index < PhotoItems.Items.Count)
			{
				object item = PhotoItems.Items[index];
				PhotoItems.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.ScrollToItemIndex: threw: {value}");
		}
	}

	private void PhotoItems_ItemClick(object sender, ItemClickEventArgs e)
	{
		try
		{
			if (e.ClickedItem is PhotoGridItem obj)
			{
				OnPhotoActivated?.Invoke(obj);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.PhotoItems_ItemClick: threw: {value}");
		}
	}

	private void FavoriteStar_Loaded(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!(sender is AnimatedFavoriteStar { DataContext: var dataContext } animatedFavoriteStar))
			{
				return;
			}
			PhotoGridItem item = dataContext as PhotoGridItem;
			if (item != null)
			{
				animatedFavoriteStar.OnClick = delegate
				{
					OnFavoriteClicked?.Invoke(item);
				};
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.FavoriteStar_Loaded: threw: {value}");
		}
	}

	private void InternalScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
	{
		try
		{
			if (sender is ScrollViewer scrollViewer)
			{
				OnGridScroll?.Invoke(scrollViewer.VerticalOffset);
				if (scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset < 600.0)
				{
					OnNearBottomReached?.Invoke();
				}
				ReportFirstVisibleIndex(scrollViewer.VerticalOffset);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.InternalScrollViewer_ViewChanged: {value}");
		}
	}

	private void ReportFirstVisibleIndex(double scrollTop)
	{
		if ((object)wrapGrid == null)
		{
			return;
		}
		double itemHeight = wrapGrid.ItemHeight;
		if (!(itemHeight <= 0.0))
		{
			int num = Math.Max(1, (wrapGrid.MaximumRowsOrColumns > 0) ? wrapGrid.MaximumRowsOrColumns : 5);
			int num2 = (int)(scrollTop / itemHeight) * num;
			if (num2 != lastReportedFirstVisible)
			{
				lastReportedFirstVisible = num2;
				OnFirstVisibleIndexChanged?.Invoke(num2);
			}
		}
	}

	private void InternalScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			OnGridWheel?.Invoke(e.GetCurrentPoint(this).Properties.MouseWheelDelta);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.InternalScrollViewer_PointerWheelChanged: {value}");
		}
	}

	private void CardBorder_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
	{
		try
		{
			if (sender is Border border)
			{
				ElementCompositionPreview.GetElementVisual(border).Offset = Vector3.Zero;
				Brush brush = ThemeHelper.Brush(border, "ABorder");
				if ((object)brush != null)
				{
					border.BorderBrush = brush;
				}
				Brush brush2 = ThemeHelper.Brush(border, "ASurface");
				if ((object)brush2 != null)
				{
					border.Background = brush2;
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.CardBorder_DataContextChanged: {value}");
		}
	}

	private void CardBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if (sender is Border border)
			{
				Visual elementVisual = ElementCompositionPreview.GetElementVisual(border);
				Compositor compositor = elementVisual.Compositor;
				CubicBezierEasingFunction easingFunction = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
				Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
				vector3KeyFrameAnimation.InsertKeyFrame(1f, new Vector3(0f, -2f, 0f), easingFunction);
				vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(180.0);
				elementVisual.StartAnimation("Offset", vector3KeyFrameAnimation);
				Brush brush = ThemeHelper.Brush(border, "ABorderStrong");
				if ((object)brush != null)
				{
					border.BorderBrush = brush;
				}
				Brush brush2 = ThemeHelper.Brush(border, "ASurfaceHover");
				if ((object)brush2 != null)
				{
					border.Background = brush2;
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.CardBorder_PointerEntered: {value}");
		}
	}

	private void CardBorder_PointerExited(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if (sender is Border border)
			{
				Visual elementVisual = ElementCompositionPreview.GetElementVisual(border);
				Compositor compositor = elementVisual.Compositor;
				CubicBezierEasingFunction easingFunction = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
				Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
				vector3KeyFrameAnimation.InsertKeyFrame(1f, Vector3.Zero, easingFunction);
				vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(180.0);
				elementVisual.StartAnimation("Offset", vector3KeyFrameAnimation);
				Brush brush = ThemeHelper.Brush(border, "ABorder");
				if ((object)brush != null)
				{
					border.BorderBrush = brush;
				}
				Brush brush2 = ThemeHelper.Brush(border, "ASurface");
				if ((object)brush2 != null)
				{
					border.Background = brush2;
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.CardBorder_PointerExited: {value}");
		}
	}

	private void ThumbImage_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
	{
		try
		{
			Image img = sender as Image;
			if ((object)img == null)
			{
				return;
			}
			if (img.Tag is GridImageSubscription gridImageSubscription)
			{
				gridImageSubscription.Photo.PropertyChanged -= gridImageSubscription.Handler;
			}
			object newValue = args.NewValue;
			PhotoGridItem item = newValue as PhotoGridItem;
			if (item == null)
			{
				img.Source = null;
				img.Tag = null;
				return;
			}
			ResetCardLoadVisuals(img);
			SetImageSource(img, item.Photo);
			PropertyChangedEventHandler propertyChangedEventHandler = delegate(object? s, PropertyChangedEventArgs e)
			{
				string propertyName = e.PropertyName;
				if ((propertyName == "GridThumbPath" || propertyName == "EffectiveSourcePath") ? true : false)
				{
					base.DispatcherQueue?.TryEnqueue(delegate
					{
						ResetCardLoadVisuals(img);
						SetImageSource(img, item.Photo);
					});
				}
			};
			item.Photo.PropertyChanged += propertyChangedEventHandler;
			img.Tag = new GridImageSubscription(item.Photo, propertyChangedEventHandler);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.ThumbImage_DataContextChanged: {value}");
		}
	}

	private static void SetImageSource(Image img, PhotoThumbnailItem photo)
	{
		string effectiveSourcePath = photo.EffectiveSourcePath;
		if (string.IsNullOrEmpty(effectiveSourcePath))
		{
			img.Source = null;
			return;
		}
		img.Source = new BitmapImage
		{
			CreateOptions = BitmapCreateOptions.IgnoreImageCache,
			DecodePixelWidth = 300,
			DecodePixelType = DecodePixelType.Logical,
			UriSource = new Uri(effectiveSourcePath, UriKind.Absolute)
		};
	}

	private void ThumbImage_Opened(object sender, RoutedEventArgs e)
	{
		try
		{
			if (sender is Image image)
			{
				Visual elementVisual = ElementCompositionPreview.GetElementVisual(image);
				Compositor compositor = elementVisual.Compositor;
				ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
				CubicBezierEasingFunction easingFunction = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
				scalarKeyFrameAnimation.InsertKeyFrame(1f, 1f, easingFunction);
				scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(200.0);
				elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
				StopShimmer(image);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.ThumbImage_Opened: {value}");
		}
	}

	private void ThumbImage_Failed(object sender, ExceptionRoutedEventArgs e)
	{
		AppLogger.Error("PhotoGridItemsView.ThumbImage_Failed: " + e.ErrorMessage);
		try
		{
			if (sender is Image img)
			{
				StopShimmer(img);
				if (FindSibling(img, "ShimmerBase") is Border border)
				{
					border.Opacity = 1.0;
				}
				if (FindSibling(img, "ErrorIcon") is TextBlock textBlock)
				{
					textBlock.Visibility = Visibility.Visible;
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoGridItemsView.ThumbImage_Failed.fallback: {value}");
		}
	}

	private void ResetCardLoadVisuals(Image img)
	{
		ElementCompositionPreview.GetElementVisual(img).Opacity = 0f;
		if (FindSibling(img, "ErrorIcon") is TextBlock textBlock)
		{
			textBlock.Visibility = Visibility.Collapsed;
		}
		StartShimmer(img);
	}

	private void StartShimmer(Image img)
	{
		Border border = FindSibling(img, "ShimmerBase") as Border;
		Border border2 = FindSibling(img, "ShimmerHighlight") as Border;
		if ((object)border != null && (object)border2 != null)
		{
			double num = ((currentImageWidth > 0.0) ? currentImageWidth : img.ActualWidth);
			if (num <= 0.0)
			{
				num = 300.0;
			}
			border.Opacity = 1.0;
			border2.Opacity = 1.0;
			border2.Width = num * 0.4;
			Visual elementVisual = ElementCompositionPreview.GetElementVisual(border2);
			ScalarKeyFrameAnimation scalarKeyFrameAnimation = elementVisual.Compositor.CreateScalarKeyFrameAnimation();
			scalarKeyFrameAnimation.InsertKeyFrame(0f, (float)((0.0 - num) * 0.4));
			scalarKeyFrameAnimation.InsertKeyFrame(1f, (float)num);
			scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(1500.0);
			scalarKeyFrameAnimation.IterationBehavior = AnimationIterationBehavior.Forever;
			elementVisual.StartAnimation("Offset.X", scalarKeyFrameAnimation);
		}
	}

	private void StopShimmer(Image img)
	{
		if (FindSibling(img, "ShimmerHighlight") is Border border)
		{
			try
			{
				ElementCompositionPreview.GetElementVisual(border).StopAnimation("Offset.X");
			}
			catch (Exception value)
			{
				AppLogger.Error($"PhotoGridItemsView.StopShimmer: {value}");
			}
			border.Opacity = 0.0;
		}
		if (FindSibling(img, "ShimmerBase") is Border border2)
		{
			border2.Opacity = 0.0;
		}
	}

	private void RefreshActiveShimmerSizes(DependencyObject root)
	{
		Stack<DependencyObject> stack = new Stack<DependencyObject>();
		stack.Push(root);
		while (stack.Count > 0)
		{
			DependencyObject dependencyObject = stack.Pop();
			if (dependencyObject is Border { Name: "ShimmerHighlight", Opacity: >0.0 } border)
			{
				double num = ((currentImageWidth > 0.0) ? currentImageWidth : border.ActualWidth);
				if (num > 0.0)
				{
					border.Width = num * 0.4;
				}
			}
			int childrenCount = VisualTreeHelper.GetChildrenCount(dependencyObject);
			for (int i = 0; i < childrenCount; i++)
			{
				stack.Push(VisualTreeHelper.GetChild(dependencyObject, i));
			}
		}
	}

	private static FrameworkElement? FindSibling(Image img, string name)
	{
		DependencyObject parent = VisualTreeHelper.GetParent(img);
		if ((object)parent == null)
		{
			return null;
		}
		int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
		for (int i = 0; i < childrenCount; i++)
		{
			if (VisualTreeHelper.GetChild(parent, i) is FrameworkElement frameworkElement && frameworkElement.Name == name)
			{
				return frameworkElement;
			}
		}
		return null;
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Shared/Controls/PhotoGridItemsView.xaml");
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
			PhotoItems = target.As<GridView>();
			PhotoItems.ItemClick += PhotoItems_ItemClick;
			PhotoItems.SizeChanged += GridView_SizeChanged;
			break;
		case 3:
			target.As<ItemsWrapGrid>().Loaded += PhotoItemsWrapGrid_Loaded;
			break;
		case 5:
		{
			Border border = target.As<Border>();
			border.PointerEntered += CardBorder_PointerEntered;
			border.PointerExited += CardBorder_PointerExited;
			border.DataContextChanged += CardBorder_DataContextChanged;
			break;
		}
		case 9:
		{
			Image image = target.As<Image>();
			image.DataContextChanged += ThumbImage_DataContextChanged;
			image.ImageOpened += ThumbImage_Opened;
			image.ImageFailed += ThumbImage_Failed;
			break;
		}
		case 11:
			target.As<AnimatedFavoriteStar>().Loaded += FavoriteStar_Loaded;
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
