using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using Alpheratz.Core;
using Alpheratz.Shared.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;
using Windows.Foundation;
using Windows.UI;

namespace Alpheratz.Features.Gallery.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class GalleryMasonryView : UserControl, IComponentConnector
{
	private sealed class CardEntry
	{
		public required int Index;

		public required Border Container;

		public required Image Image;

		public required PhotoThumbnailItem Photo;

		public string LoadedPath = string.Empty;

		public bool IsLoaded;

		public DispatcherQueueTimer? PendingReleaseTimer;

		public PropertyChangedEventHandler? PhotoSubscription;

		public Action? StopShimmer;
	}

	private const double OverscanPx = 400.0;

	private const double ReleaseMarginPx = 1200.0;

	private const int ReleaseDelayMs = 250;

	private UiObservableCollection<PhotoThumbnailItem>? photos;

	private int? requestedColumnCount;

	private MasonryLayoutResult? currentLayout;

	private List<PhotoThumbnailItem>? layoutPhotos;

	private readonly Dictionary<int, CardEntry> activeCards = new Dictionary<int, CardEntry>();

	private readonly HashSet<string> requestedThumbs = new HashSet<string>();

	private readonly DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread();

	private bool updateVisibilityPending;

	private int[]? sortedByTopIndices;

	private readonly HashSet<int> visibleSet = new HashSet<int>();

	private readonly List<PhotoThumbnailItem> thumbsNeededBuf = new List<PhotoThumbnailItem>();

	private readonly List<int> toRemoveBuf = new List<int>();

	private int lastReportedFirstVisible = -1;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ScrollViewer ScrollHost;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Canvas MasonryCanvas;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public Action<PhotoThumbnailItem>? OnPhotoTapped { get; set; }

	public Action<IReadOnlyList<PhotoThumbnailItem>>? OnThumbnailsNeeded { get; set; }

	public Action<int>? OnFirstVisibleIndexChanged { get; set; }

	public GalleryMasonryView()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryMasonryView.ctor: InitializeComponent failed: {value}");
			throw;
		}
		base.SizeChanged += delegate
		{
			Rebuild();
		};
		base.ActualThemeChanged += OnActualThemeChanged;
		base.Unloaded += delegate
		{
			base.ActualThemeChanged -= OnActualThemeChanged;
		};
	}

	private void OnActualThemeChanged(FrameworkElement sender, object args)
	{
		try
		{
			foreach (CardEntry value2 in activeCards.Values)
			{
				Brush brush = ThemeHelper.Brush(value2.Container, "ASurface");
				if ((object)brush != null)
				{
					value2.Container.Background = brush;
				}
				Brush brush2 = ThemeHelper.Brush(value2.Container, "ABorder");
				if ((object)brush2 != null)
				{
					value2.Container.BorderBrush = brush2;
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryMasonryView.OnActualThemeChanged: {value}");
		}
	}

	public void SetPhotos(UiObservableCollection<PhotoThumbnailItem> next)
	{
		if (photos != null)
		{
			photos.CollectionChanged -= OnPhotosChanged;
		}
		photos = next;
		photos.CollectionChanged += OnPhotosChanged;
		requestedThumbs.Clear();
		Rebuild();
	}

	public void SetColumnCount(int count)
	{
		int? num = ((count > 0) ? new int?(count) : ((int?)null));
		if (requestedColumnCount != num)
		{
			requestedColumnCount = num;
			Rebuild();
		}
	}

	public void ScrollToTop()
	{
		try
		{
			ScrollHost.ChangeView(null, 0.0, null);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryMasonryView.ScrollToTop: threw: {value}");
		}
	}

	public void ScrollToPhotoIndex(int index)
	{
		try
		{
			if ((object)currentLayout != null && index >= 0 && index < currentLayout.Items.Count)
			{
				double top = currentLayout.Items[index].Top;
				ScrollHost.ChangeView(null, top, null);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryMasonryView.ScrollToPhotoIndex: threw: {value}");
		}
	}

	private void OnPhotosChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Add)
		{
			RebuildLayout();
			return;
		}
		requestedThumbs.Clear();
		Rebuild();
	}

	private void ScrollHost_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		Rebuild();
	}

	private void ScrollHost_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
	{
		RequestUpdateVisibility();
	}

	private void RequestUpdateVisibility()
	{
		if (!updateVisibilityPending)
		{
			updateVisibilityPending = true;
			dispatcherQueue.TryEnqueue(delegate
			{
				updateVisibilityPending = false;
				UpdateVisibility();
			});
		}
	}

	private (double inner, int effectiveCols) ComputeColumns()
	{
		double actualWidth = base.ActualWidth;
		if (actualWidth <= 0.0)
		{
			actualWidth = ScrollHost.ActualWidth;
		}
		double num = Math.Max(0.0, actualWidth - 28.0);
		int num2 = ((!(num > 0.0)) ? 1 : Math.Max(1, (int)Math.Floor((num + 14.0) / 334.0)));
		int item = (requestedColumnCount.HasValue ? Math.Max(1, Math.Min(requestedColumnCount.Value, num2)) : num2);
		return (inner: num, effectiveCols: item);
	}

	private void Rebuild()
	{
		try
		{
			foreach (var (_, cardEntry2) in activeCards)
			{
				cardEntry2.PendingReleaseTimer?.Stop();
				cardEntry2.PendingReleaseTimer = null;
				if (cardEntry2.PhotoSubscription != null)
				{
					cardEntry2.Photo.PropertyChanged -= cardEntry2.PhotoSubscription;
					cardEntry2.PhotoSubscription = null;
				}
			}
			activeCards.Clear();
			MasonryCanvas.Children.Clear();
			var (num2, num3) = ComputeColumns();
			if (num2 <= 0.0 || photos == null)
			{
				currentLayout = null;
				layoutPhotos = null;
				sortedByTopIndices = null;
				MasonryCanvas.Width = 0.0;
				MasonryCanvas.Height = 0.0;
			}
			else
			{
				layoutPhotos = new List<PhotoThumbnailItem>(photos);
				currentLayout = GalleryMasonryLayout.Build(layoutPhotos, num2, num3);
				MasonryCanvas.Width = (double)currentLayout.ColumnCount * currentLayout.ColumnWidth + (double)(currentLayout.ColumnCount - 1) * currentLayout.Gap;
				MasonryCanvas.Height = currentLayout.TotalHeight;
				BuildSortedIndex();
				dispatcherQueue.TryEnqueue(UpdateVisibility);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryMasonryView.Rebuild: threw: {value}");
		}
	}

	private void RebuildLayout()
	{
		try
		{
			if (photos != null)
			{
				var (num, num2) = ComputeColumns();
				if (!(num <= 0.0))
				{
					layoutPhotos = new List<PhotoThumbnailItem>(photos);
					currentLayout = GalleryMasonryLayout.Build(layoutPhotos, num, num2);
					MasonryCanvas.Width = (double)currentLayout.ColumnCount * currentLayout.ColumnWidth + (double)(currentLayout.ColumnCount - 1) * currentLayout.Gap;
					MasonryCanvas.Height = currentLayout.TotalHeight;
					BuildSortedIndex();
					dispatcherQueue.TryEnqueue(UpdateVisibility);
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryMasonryView.RebuildLayout: threw: {value}");
		}
	}

	private void BuildSortedIndex()
	{
		if ((object)currentLayout == null || currentLayout.Items.Count == 0)
		{
			sortedByTopIndices = null;
			return;
		}
		IReadOnlyList<MasonryItem> items = currentLayout.Items;
		int[] array = new int[items.Count];
		for (int i = 0; i < items.Count; i++)
		{
			array[i] = i;
		}
		Array.Sort(array, (int a, int b) => items[a].Top.CompareTo(items[b].Top));
		sortedByTopIndices = array;
	}

	private void UpdateVisibility()
	{
		try
		{
			if ((object)currentLayout == null || layoutPhotos == null || sortedByTopIndices == null || currentLayout.Items.Count == 0)
			{
				return;
			}
			double verticalOffset = ScrollHost.VerticalOffset;
			double num = verticalOffset + ScrollHost.ViewportHeight;
			double num2 = verticalOffset - 400.0;
			double num3 = num + 400.0;
			double num4 = verticalOffset - 1200.0;
			double num5 = num + 1200.0;
			visibleSet.Clear();
			thumbsNeededBuf.Clear();
			IReadOnlyList<MasonryItem> items = currentLayout.Items;
			int[] array = sortedByTopIndices;
			int num6 = array.Length;
			double num7 = num2 - 900.0;
			int num8 = 0;
			int num9 = num6 - 1;
			int num10 = num6;
			while (num8 <= num9)
			{
				int num11 = num8 + num9 >> 1;
				if (items[array[num11]].Top >= num7)
				{
					num10 = num11;
					num9 = num11 - 1;
				}
				else
				{
					num8 = num11 + 1;
				}
			}
			for (int i = num10; i < num6; i++)
			{
				int num12 = array[i];
				MasonryItem masonryItem = items[num12];
				if (masonryItem.Top > num3)
				{
					break;
				}
				if (!(masonryItem.Top + masonryItem.Height < num2))
				{
					visibleSet.Add(num12);
					if (!activeCards.TryGetValue(num12, out CardEntry value))
					{
						value = CreateCard(num12, masonryItem);
						activeCards[num12] = value;
						MasonryCanvas.Children.Add(value.Container);
					}
					value.PendingReleaseTimer?.Stop();
					value.PendingReleaseTimer = null;
					if (!value.IsLoaded)
					{
						LoadImage(value);
					}
					if (requestedThumbs.Add(layoutPhotos[num12].PhotoPath) && string.IsNullOrEmpty(layoutPhotos[num12].GridThumbPath) && !string.IsNullOrEmpty(layoutPhotos[num12].PhotoPath))
					{
						thumbsNeededBuf.Add(layoutPhotos[num12]);
					}
				}
			}
			toRemoveBuf.Clear();
			foreach (var (num14, cardEntry2) in activeCards)
			{
				if (visibleSet.Contains(num14))
				{
					continue;
				}
				MasonryItem masonryItem2 = items[num14];
				if (masonryItem2.Top + masonryItem2.Height < num4 || masonryItem2.Top > num5)
				{
					cardEntry2.PendingReleaseTimer?.Stop();
					cardEntry2.PendingReleaseTimer = null;
					if (cardEntry2.PhotoSubscription != null)
					{
						cardEntry2.Photo.PropertyChanged -= cardEntry2.PhotoSubscription;
						cardEntry2.PhotoSubscription = null;
					}
					cardEntry2.Image.Source = null;
					MasonryCanvas.Children.Remove(cardEntry2.Container);
					toRemoveBuf.Add(num14);
				}
				else if (cardEntry2.IsLoaded && (object)cardEntry2.PendingReleaseTimer == null)
				{
					ScheduleRelease(cardEntry2);
				}
			}
			foreach (int item in toRemoveBuf)
			{
				activeCards.Remove(item);
			}
			if (thumbsNeededBuf.Count > 0)
			{
				OnThumbnailsNeeded?.Invoke(thumbsNeededBuf);
			}
			ReportFirstVisibleIndex(verticalOffset);
		}
		catch (Exception value2)
		{
			AppLogger.Error($"GalleryMasonryView.UpdateVisibility: threw: {value2}");
		}
	}

	private void ReportFirstVisibleIndex(double scrollTop)
	{
		if ((object)currentLayout == null || sortedByTopIndices == null)
		{
			return;
		}
		IReadOnlyList<MasonryItem> items = currentLayout.Items;
		int[] array = sortedByTopIndices;
		int num = array.Length;
		if (num == 0)
		{
			return;
		}
		int num2 = 0;
		int num3 = num - 1;
		int num4 = num;
		while (num2 <= num3)
		{
			int num5 = num2 + num3 >> 1;
			if (items[array[num5]].Top >= scrollTop)
			{
				num4 = num5;
				num3 = num5 - 1;
			}
			else
			{
				num2 = num5 + 1;
			}
		}
		int num6;
		if (num4 < num)
		{
			num6 = array[num4];
		}
		else
		{
			if (num <= 0)
			{
				return;
			}
			num6 = array[num - 1];
		}
		if (num6 != lastReportedFirstVisible)
		{
			lastReportedFirstVisible = num6;
			OnFirstVisibleIndexChanged?.Invoke(num6);
		}
	}

	private void LoadImage(CardEntry entry)
	{
		string text = entry.Photo.EffectiveSourcePath ?? string.Empty;
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		try
		{
			BitmapImage source = new BitmapImage
			{
				CreateOptions = BitmapCreateOptions.IgnoreImageCache,
				DecodePixelWidth = Math.Max(1, (int)entry.Container.Width),
				DecodePixelType = DecodePixelType.Logical,
				UriSource = new Uri(text, UriKind.Absolute)
			};
			entry.Image.Source = source;
			entry.LoadedPath = text;
			entry.IsLoaded = true;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryMasonryView.LoadImage: failed for {text}: {value}");
		}
	}

	private void ScheduleRelease(CardEntry entry)
	{
		DispatcherQueueTimer dispatcherQueueTimer = dispatcherQueue.CreateTimer();
		dispatcherQueueTimer.Interval = TimeSpan.FromMilliseconds(250.0);
		dispatcherQueueTimer.IsRepeating = false;
		dispatcherQueueTimer.Tick += delegate
		{
			try
			{
				entry.Image.Source = null;
				entry.LoadedPath = string.Empty;
				entry.IsLoaded = false;
			}
			catch (Exception value)
			{
				AppLogger.Error($"GalleryMasonryView.ReleaseTick: threw: {value}");
			}
			entry.PendingReleaseTimer = null;
		};
		dispatcherQueueTimer.Start();
		entry.PendingReleaseTimer = dispatcherQueueTimer;
	}

	private CardEntry CreateCard(int index, MasonryItem item)
	{
		Border border = new Border
		{
			Width = item.Width,
			Height = item.Height,
			CornerRadius = new CornerRadius(12.0),
			Background = ThemeHelper.Brush(this, "ASurface"),
			BorderBrush = ThemeHelper.Brush(this, "ABorder"),
			BorderThickness = new Thickness(1.0)
		};
		Canvas.SetLeft(border, item.Left);
		Canvas.SetTop(border, item.Top);
		Grid cardGrid = new Grid
		{
			Width = item.Width,
			Height = item.Height
		};
		cardGrid.Clip = new RectangleGeometry
		{
			Rect = new Rect(0.0, 0.0, item.Width, item.Height)
		};
		border.Child = cardGrid;
		Border shimmerBase = new Border
		{
			Background = ThemeHelper.Brush(this, "ASurfaceSoft"),
			Width = item.Width,
			Height = item.Height
		};
		cardGrid.Children.Add(shimmerBase);
		Border shimmerHighlight = new Border
		{
			Background = ThemeHelper.Brush(this, "ASurfaceHover"),
			Width = item.Width * 0.4,
			Height = item.Height,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		cardGrid.Children.Add(shimmerHighlight);
		Visual shimmerVisual = ElementCompositionPreview.GetElementVisual(shimmerHighlight);
		Compositor compositor = shimmerVisual.Compositor;
		ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation.InsertKeyFrame(0f, (float)((0.0 - item.Width) * 0.4));
		scalarKeyFrameAnimation.InsertKeyFrame(1f, (float)item.Width);
		scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(1500.0);
		scalarKeyFrameAnimation.IterationBehavior = AnimationIterationBehavior.Forever;
		shimmerVisual.StartAnimation("Offset.X", scalarKeyFrameAnimation);
		Image image = new Image
		{
			Stretch = Stretch.UniformToFill,
			Width = item.Width,
			Height = item.Height
		};
		cardGrid.Children.Add(image);
		Visual imageVisual = ElementCompositionPreview.GetElementVisual(image);
		imageVisual.Opacity = 0f;
		image.ImageOpened += delegate
		{
			ScalarKeyFrameAnimation scalarKeyFrameAnimation2 = compositor.CreateScalarKeyFrameAnimation();
			CubicBezierEasingFunction easingFunction = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
			scalarKeyFrameAnimation2.InsertKeyFrame(1f, 1f, easingFunction);
			scalarKeyFrameAnimation2.Duration = TimeSpan.FromMilliseconds(200.0);
			imageVisual.StartAnimation("Opacity", scalarKeyFrameAnimation2);
			StopShimmer();
		};
		image.ImageFailed += delegate(object _, ExceptionRoutedEventArgs args)
		{
			AppLogger.Warn("GalleryMasonryView.ImageFailed: " + args.ErrorMessage);
			StopShimmer();
			shimmerBase.Background = ThemeHelper.Brush(this, "ASurfaceSoft");
			shimmerBase.Opacity = 1.0;
			TextBlock item4 = new TextBlock
			{
				Text = "\ue7ba",
				FontFamily = new FontFamily("Segoe MDL2 Assets"),
				FontSize = 24.0,
				Foreground = ThemeHelper.Brush(this, "ATextDisabled"),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			cardGrid.Children.Add(item4);
		};
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Bottom,
			Padding = new Thickness(10.0, 20.0, 10.0, 10.0),
			Background = new LinearGradientBrush
			{
				StartPoint = new Point(0.5, 0.0),
				EndPoint = new Point(0.5, 1.0),
				GradientStops = 
				{
					new GradientStop
					{
						Color = Color.FromArgb(0, 0, 0, 0),
						Offset = 0.0
					},
					new GradientStop
					{
						Color = Color.FromArgb(200, 0, 0, 0),
						Offset = 1.0
					}
				}
			}
		};
		TextBlock item2 = new TextBlock
		{
			Text = (item.Photo.WorldName ?? string.Empty),
			FontSize = 12.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			TextTrimming = TextTrimming.CharacterEllipsis,
			MaxLines = 1
		};
		stackPanel.Children.Add(item2);
		TextBlock item3 = new TextBlock
		{
			Text = (item.Photo.Timestamp ?? string.Empty),
			FontSize = 10.0,
			Foreground = new SolidColorBrush(Color.FromArgb(187, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			FontFamily = ThemeHelper.AppResource<FontFamily>("AFontMono"),
			TextTrimming = TextTrimming.CharacterEllipsis,
			MaxLines = 1
		};
		stackPanel.Children.Add(item3);
		cardGrid.Children.Add(stackPanel);
		Visual overlayVisual = ElementCompositionPreview.GetElementVisual(stackPanel);
		overlayVisual.Opacity = 0f;
		Border selectionRing = new Border
		{
			Background = new SolidColorBrush(Colors.Transparent),
			BorderBrush = ThemeHelper.Brush(this, "APrimary"),
			BorderThickness = new Thickness(3.0),
			CornerRadius = new CornerRadius(12.0),
			Visibility = ((!item.Photo.IsSelected) ? Visibility.Collapsed : Visibility.Visible)
		};
		cardGrid.Children.Add(selectionRing);
		Border selectionBadge = new Border
		{
			Width = 26.0,
			Height = 26.0,
			CornerRadius = new CornerRadius(13.0),
			Background = ThemeHelper.Brush(this, "APrimary"),
			BorderBrush = new SolidColorBrush(Color.FromArgb(102, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			BorderThickness = new Thickness(1.0),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 9.0, 9.0, 0.0),
			Visibility = ((!item.Photo.IsSelected) ? Visibility.Collapsed : Visibility.Visible),
			Child = new TextBlock
			{
				Text = "✓",
				Foreground = new SolidColorBrush(Colors.White),
				FontSize = 13.0,
				FontWeight = FontWeights.Bold,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			}
		};
		cardGrid.Children.Add(selectionBadge);
		Visual borderVisual = ElementCompositionPreview.GetElementVisual(border);
		ElementCompositionPreview.SetIsTranslationEnabled(border, value: true);
		borderVisual.Properties.InsertVector3("Translation", Vector3.Zero);
		CubicBezierEasingFunction hoverEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
		border.PointerEntered += delegate
		{
			Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
			vector3KeyFrameAnimation.InsertKeyFrame(1f, new Vector3(0f, -2f, 0f), hoverEasing);
			vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(200.0);
			borderVisual.StartAnimation("Translation", vector3KeyFrameAnimation);
			Vector3KeyFrameAnimation vector3KeyFrameAnimation2 = compositor.CreateVector3KeyFrameAnimation();
			vector3KeyFrameAnimation2.InsertKeyFrame(1f, new Vector3(1.04f, 1.04f, 1f), hoverEasing);
			vector3KeyFrameAnimation2.Duration = TimeSpan.FromMilliseconds(300.0);
			imageVisual.CenterPoint = new Vector3((float)(item.Width / 2.0), (float)(item.Height / 2.0), 0f);
			imageVisual.StartAnimation("Scale", vector3KeyFrameAnimation2);
			ScalarKeyFrameAnimation scalarKeyFrameAnimation2 = compositor.CreateScalarKeyFrameAnimation();
			scalarKeyFrameAnimation2.InsertKeyFrame(1f, 1f, hoverEasing);
			scalarKeyFrameAnimation2.Duration = TimeSpan.FromMilliseconds(180.0);
			overlayVisual.StartAnimation("Opacity", scalarKeyFrameAnimation2);
		};
		border.PointerExited += delegate
		{
			Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
			vector3KeyFrameAnimation.InsertKeyFrame(1f, Vector3.Zero, hoverEasing);
			vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(200.0);
			borderVisual.StartAnimation("Translation", vector3KeyFrameAnimation);
			Vector3KeyFrameAnimation vector3KeyFrameAnimation2 = compositor.CreateVector3KeyFrameAnimation();
			vector3KeyFrameAnimation2.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), hoverEasing);
			vector3KeyFrameAnimation2.Duration = TimeSpan.FromMilliseconds(300.0);
			imageVisual.StartAnimation("Scale", vector3KeyFrameAnimation2);
			ScalarKeyFrameAnimation scalarKeyFrameAnimation2 = compositor.CreateScalarKeyFrameAnimation();
			scalarKeyFrameAnimation2.InsertKeyFrame(1f, 0f, hoverEasing);
			scalarKeyFrameAnimation2.Duration = TimeSpan.FromMilliseconds(180.0);
			overlayVisual.StartAnimation("Opacity", scalarKeyFrameAnimation2);
		};
		border.Tapped += delegate
		{
			try
			{
				OnPhotoTapped?.Invoke(item.Photo);
			}
			catch (Exception value)
			{
				AppLogger.Error($"GalleryMasonryView.PhotoTapped: threw: {value}");
			}
		};
		CardEntry entry = new CardEntry
		{
			Index = index,
			Container = border,
			Image = image,
			Photo = item.Photo,
			StopShimmer = StopShimmer
		};
		entry.PhotoSubscription = delegate(object? s, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "IsSelected")
			{
				Visibility visibility = ((!entry.Photo.IsSelected) ? Visibility.Collapsed : Visibility.Visible);
				selectionRing.Visibility = visibility;
				selectionBadge.Visibility = visibility;
			}
			else if ((!(e.PropertyName != "EffectiveSourcePath") || !(e.PropertyName != "GridThumbPath") || !(e.PropertyName != "ResolvedPhotoPath")) && !((entry.Photo.EffectiveSourcePath ?? string.Empty) == entry.LoadedPath))
			{
				if (entry.IsLoaded)
				{
					LoadImage(entry);
				}
				else
				{
					double top = Canvas.GetTop(entry.Container);
					double num = top + entry.Container.Height;
					double num2 = ScrollHost.VerticalOffset - 400.0;
					double num3 = ScrollHost.VerticalOffset + ScrollHost.ViewportHeight + 400.0;
					if (num >= num2 && top <= num3)
					{
						LoadImage(entry);
					}
				}
			}
		};
		item.Photo.PropertyChanged += entry.PhotoSubscription;
		return entry;
		void StopShimmer()
		{
			try
			{
				shimmerVisual.StopAnimation("Offset.X");
				shimmerBase.Opacity = 0.0;
				shimmerHighlight.Opacity = 0.0;
			}
			catch (Exception value)
			{
				AppLogger.Error($"GalleryMasonryView.StopShimmer: threw: {value}");
			}
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/Gallery/Controls/GalleryMasonryView.xaml");
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
			ScrollHost = target.As<ScrollViewer>();
			ScrollHost.SizeChanged += ScrollHost_SizeChanged;
			ScrollHost.ViewChanged += ScrollHost_ViewChanged;
			break;
		case 3:
			MasonryCanvas = target.As<Canvas>();
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
