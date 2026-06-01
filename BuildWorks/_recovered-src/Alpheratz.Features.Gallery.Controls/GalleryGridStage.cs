using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using Alpheratz.Core;
using Alpheratz.Shared.Controls;
using Alpheratz.Shared.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Gallery.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class GalleryGridStage : UserControl, IComponentConnector
{
	private bool masonryRealized;

	private const int SkeletonTileCount = 16;

	private const double SkeletonTileWidth = 290.0;

	private const double SkeletonThumbHeight = 200.0;

	private const double SkeletonShimmerWidthRatio = 0.4;

	private readonly List<Border> skeletonHighlights = new List<Border>();

	private bool skeletonBuilt;

	private bool skeletonShimmerRunning;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ColumnDefinition MonthNavColumn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private MonthNav MonthNavControl;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private PhotoGrid PhotoGridControl;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private GalleryMasonryView MasonryViewControl;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private EmptyState EmptyStateControl;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid LoadingVeil;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private WrapPanel SkeletonHost;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public Action<GalleryMasonryView>? OnMasonryRealized { get; set; }

	public object? GridDataContext
	{
		get
		{
			return PhotoGridControl.DataContext;
		}
		set
		{
			PhotoGridControl.DataContext = value;
		}
	}

	public PhotoGrid? PhotoGridControlRef => PhotoGridControl;

	public GalleryMasonryView? MasonryViewControlRef
	{
		get
		{
			if (!masonryRealized)
			{
				return null;
			}
			return MasonryViewControl;
		}
	}

	public MonthNav? MonthNavControlRef => MonthNavControl;

	public Visibility EmptyStateVisibility
	{
		get
		{
			return EmptyStateControl.Visibility;
		}
		set
		{
			EmptyStateControl.Visibility = value;
		}
	}

	public event Action? EmptyStatePrimaryActionRequested;

	public GalleryGridStage()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryGridStage.ctor: InitializeComponent failed: {value}");
			throw;
		}
		EmptyStateControl.PrimaryActionRequested += delegate
		{
			this.EmptyStatePrimaryActionRequested?.Invoke();
		};
	}

	public void SetGridItemsSource(object? source)
	{
		PhotoGridControl.SetItemsSource(source);
	}

	public void SetEmptyStateMode(EmptyStateMode mode)
	{
		EmptyStateControl.SetMode(mode);
	}

	private GalleryMasonryView? EnsureMasonryRealized()
	{
		if (masonryRealized)
		{
			return MasonryViewControl;
		}
		try
		{
			FindName("MasonryViewControl");
			masonryRealized = true;
			GalleryMasonryView masonryViewControl = MasonryViewControl;
			if ((object)masonryViewControl != null)
			{
				OnMasonryRealized?.Invoke(masonryViewControl);
			}
			return MasonryViewControl;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryGridStage.EnsureMasonryRealized: threw: {value}");
			return null;
		}
	}

	public void SetMasonryActive(bool active)
	{
		try
		{
			if (active)
			{
				GalleryMasonryView galleryMasonryView = EnsureMasonryRealized();
				PhotoGridControl.Visibility = Visibility.Collapsed;
				if ((object)galleryMasonryView != null)
				{
					galleryMasonryView.Visibility = Visibility.Visible;
				}
			}
			else
			{
				PhotoGridControl.Visibility = Visibility.Visible;
				if (masonryRealized)
				{
					MasonryViewControl.Visibility = Visibility.Collapsed;
				}
			}
			MonthNavControl.Visibility = Visibility.Visible;
			MonthNavColumn.Width = new GridLength(56.0);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryGridStage.SetMasonryActive: threw: {value}");
		}
	}

	public void UpdateLoadingState(bool isLoading, int totalCount, bool filterActive)
	{
		try
		{
			LoadingVeil.Visibility = ((!isLoading) ? Visibility.Collapsed : Visibility.Visible);
			if (isLoading)
			{
				EnsureSkeletonTiles();
				StartSkeletonShimmer();
			}
			else
			{
				StopSkeletonShimmer();
			}
			bool flag = !isLoading && totalCount == 0;
			if (flag)
			{
				EmptyStateControl.SetMode(filterActive ? EmptyStateMode.NoFilterMatch : EmptyStateMode.NoLibrary);
			}
			EmptyStateControl.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
			MonthNavControl.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
			MonthNavColumn.Width = (flag ? new GridLength(0.0) : new GridLength(56.0));
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryGridStage.UpdateLoadingState: threw: {value}");
		}
	}

	private void EnsureSkeletonTiles()
	{
		if (skeletonBuilt)
		{
			return;
		}
		try
		{
			for (int i = 0; i < 16; i++)
			{
				SkeletonHost.Children.Add(BuildSkeletonTile());
			}
			skeletonBuilt = true;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryGridStage.EnsureSkeletonTiles: threw: {value}");
		}
	}

	private Border BuildSkeletonTile()
	{
		Border obj = new Border
		{
			Width = 290.0,
			CornerRadius = new CornerRadius(12.0),
			BorderThickness = new Thickness(1.0),
			BorderBrush = ThemeHelper.Brush(this, "ABorder"),
			Background = ThemeHelper.Brush(this, "ASurfaceSoft")
		};
		Grid grid = new Grid
		{
			RowDefinitions = 
			{
				new RowDefinition
				{
					Height = new GridLength(200.0)
				},
				new RowDefinition
				{
					Height = GridLength.Auto
				}
			}
		};
		Grid grid2 = new Grid
		{
			Background = ThemeHelper.Brush(this, "ASurfaceSoft")
		};
		Border item = new Border
		{
			Background = ThemeHelper.Brush(this, "ASurfaceSoft")
		};
		Border item2 = new Border
		{
			HorizontalAlignment = HorizontalAlignment.Left,
			Width = 116.0,
			Background = ThemeHelper.Brush(this, "ASurfaceHover")
		};
		grid2.Children.Add(item);
		grid2.Children.Add(item2);
		Grid.SetRow(grid2, 0);
		grid.Children.Add(grid2);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(11.0, 10.0, 11.0, 12.0),
			Spacing = 8.0
		};
		stackPanel.Children.Add(new Border
		{
			Height = 12.0,
			Width = 174.0,
			HorizontalAlignment = HorizontalAlignment.Left,
			CornerRadius = new CornerRadius(6.0),
			Background = ThemeHelper.Brush(this, "ASurfaceHover")
		});
		stackPanel.Children.Add(new Border
		{
			Height = 10.0,
			Width = 101.5,
			HorizontalAlignment = HorizontalAlignment.Left,
			CornerRadius = new CornerRadius(6.0),
			Background = ThemeHelper.Brush(this, "ASurfaceHover")
		});
		Grid.SetRow(stackPanel, 1);
		grid.Children.Add(stackPanel);
		obj.Child = grid;
		skeletonHighlights.Add(item2);
		return obj;
	}

	private void StartSkeletonShimmer()
	{
		if (skeletonShimmerRunning)
		{
			return;
		}
		try
		{
			double num = 116.0;
			foreach (Border skeletonHighlight in skeletonHighlights)
			{
				skeletonHighlight.Opacity = 1.0;
				Visual elementVisual = ElementCompositionPreview.GetElementVisual(skeletonHighlight);
				ScalarKeyFrameAnimation scalarKeyFrameAnimation = elementVisual.Compositor.CreateScalarKeyFrameAnimation();
				scalarKeyFrameAnimation.InsertKeyFrame(0f, (float)(0.0 - num));
				scalarKeyFrameAnimation.InsertKeyFrame(1f, 290f);
				scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(1500.0);
				scalarKeyFrameAnimation.IterationBehavior = AnimationIterationBehavior.Forever;
				elementVisual.StartAnimation("Offset.X", scalarKeyFrameAnimation);
			}
			skeletonShimmerRunning = true;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryGridStage.StartSkeletonShimmer: threw: {value}");
		}
	}

	private void StopSkeletonShimmer()
	{
		if (!skeletonShimmerRunning)
		{
			return;
		}
		try
		{
			foreach (Border skeletonHighlight in skeletonHighlights)
			{
				try
				{
					ElementCompositionPreview.GetElementVisual(skeletonHighlight).StopAnimation("Offset.X");
				}
				catch (Exception value)
				{
					AppLogger.Error($"GalleryGridStage.StopSkeletonShimmer.item: {value}");
				}
			}
		}
		catch (Exception value2)
		{
			AppLogger.Error($"GalleryGridStage.StopSkeletonShimmer: threw: {value2}");
		}
		finally
		{
			skeletonShimmerRunning = false;
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/Gallery/Controls/GalleryGridStage.xaml");
			Application.LoadComponent(this, resourceLocator, ComponentResourceLocation.Application);
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	private void UnloadObject(DependencyObject unloadableObject)
	{
		if (unloadableObject != null)
		{
			if (unloadableObject == MasonryViewControl)
			{
				DisconnectUnloadedObject(5);
			}
			XamlMarkupHelper.UnloadObject(unloadableObject);
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 2:
			MonthNavColumn = target.As<ColumnDefinition>();
			break;
		case 3:
			MonthNavControl = target.As<MonthNav>();
			break;
		case 4:
			PhotoGridControl = target.As<PhotoGrid>();
			break;
		case 5:
			MasonryViewControl = target.As<GalleryMasonryView>();
			break;
		case 6:
			EmptyStateControl = target.As<EmptyState>();
			break;
		case 7:
			LoadingVeil = target.As<Grid>();
			break;
		case 8:
			SkeletonHost = target.As<WrapPanel>();
			break;
		}
		_contentLoaded = true;
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	private void DisconnectUnloadedObject(int connectionId)
	{
		if (connectionId == 5)
		{
			MasonryViewControl = null;
			return;
		}
		throw new ArgumentException("Invalid connectionId.");
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public IComponentConnector GetBindingConnector(int connectionId, object target)
	{
		return null;
	}
}
