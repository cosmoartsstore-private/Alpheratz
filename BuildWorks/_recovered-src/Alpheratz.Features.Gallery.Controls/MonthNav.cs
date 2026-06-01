using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using Alpheratz.Core;
using Alpheratz.Shared.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;
using Windows.Foundation;

namespace Alpheratz.Features.Gallery.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class MonthNav : UserControl, IComponentConnector
{
	private IReadOnlyList<GalleryMonthGroup> groups = Array.Empty<GalleryMonthGroup>();

	private int activeIndex;

	private bool isScrubbing;

	private int lastScrubbedIndex = -1;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ScrollViewer NavScroll;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private StackPanel MonthList;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public Action<GalleryMonthGroup>? OnJumpToMonth { get; set; }

	public MonthNav()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"MonthNav.ctor: InitializeComponent failed: {value}");
			throw;
		}
		MonthList.PointerPressed += MonthList_PointerPressed;
		MonthList.PointerMoved += MonthList_PointerMoved;
		MonthList.PointerReleased += MonthList_PointerReleased;
		MonthList.PointerCaptureLost += MonthList_PointerCaptureLost;
		MonthList.PointerCanceled += MonthList_PointerCaptureLost;
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
			Rebuild();
		}
		catch (Exception value)
		{
			AppLogger.Error($"MonthNav.OnActualThemeChanged: {value}");
		}
	}

	public void SetGroups(IReadOnlyList<GalleryMonthGroup> next)
	{
		groups = next;
		Rebuild();
	}

	public void SetActiveIndex(int index)
	{
		if (activeIndex != index)
		{
			activeIndex = index;
			Rebuild();
		}
	}

	private void Rebuild()
	{
		try
		{
			MonthList.Children.Clear();
			for (int i = 0; i < groups.Count; i++)
			{
				GalleryMonthGroup galleryMonthGroup = groups[i];
				bool num = i == 0 || groups[i - 1].Year != galleryMonthGroup.Year;
				bool isActive = i == activeIndex;
				if (num)
				{
					MonthList.Children.Add(BuildYearHeader(galleryMonthGroup.Year));
				}
				MonthList.Children.Add(BuildMonthButton(galleryMonthGroup, isActive));
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"MonthNav.Rebuild: threw: {value}");
		}
	}

	private Border BuildYearHeader(int year)
	{
		return new Border
		{
			Padding = new Thickness(8.0, 10.0, 8.0, 2.0),
			Child = new TextBlock
			{
				Text = year.ToString(),
				FontSize = 11.0,
				FontWeight = FontWeights.ExtraBold,
				Foreground = ThemeHelper.Brush(this, "ATextFaint"),
				HorizontalAlignment = HorizontalAlignment.Center
			}
		};
	}

	private Button BuildMonthButton(GalleryMonthGroup g, bool isActive)
	{
		TextBlock content = new TextBlock
		{
			Text = $"{g.Month}月",
			FontSize = 12.0,
			FontWeight = (isActive ? FontWeights.ExtraBold : FontWeights.SemiBold),
			Foreground = ThemeHelper.Brush(this, isActive ? "APrimaryText" : "ATextDim"),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		Button button = new Button();
		button.MinHeight = 30.0;
		button.MinWidth = 48.0;
		button.Padding = new Thickness(4.0, 4.0, 4.0, 4.0);
		button.Margin = new Thickness(4.0, 1.0, 4.0, 1.0);
		button.Background = (isActive ? ThemeHelper.Brush(this, "APrimarySoft") : new SolidColorBrush(Colors.Transparent));
		button.BorderThickness = new Thickness(0.0);
		button.CornerRadius = new CornerRadius(8.0);
		button.HorizontalAlignment = HorizontalAlignment.Stretch;
		button.HorizontalContentAlignment = HorizontalAlignment.Center;
		button.Content = content;
		button.Tag = g;
		button.Click += MonthButton_Click;
		button.PointerEntered += MonthButton_PointerEntered;
		button.PointerExited += MonthButton_PointerExited;
		return button;
	}

	private void MonthButton_PointerEntered(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if (!isScrubbing && sender is Button button && !IsActive(button))
			{
				button.Background = ThemeHelper.Brush(this, "ASurfaceHover");
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"MonthNav.MonthButton_PointerEntered: threw: {value}");
		}
	}

	private void MonthButton_PointerExited(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if (sender is Button button && !IsActive(button))
			{
				button.Background = new SolidColorBrush(Colors.Transparent);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"MonthNav.MonthButton_PointerExited: threw: {value}");
		}
	}

	private bool IsActive(Button btn)
	{
		if (!(btn.Tag is GalleryMonthGroup galleryMonthGroup))
		{
			return false;
		}
		if (activeIndex < 0 || activeIndex >= groups.Count)
		{
			return false;
		}
		return (object)groups[activeIndex] == galleryMonthGroup;
	}

	private void MonthButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (sender is Button { Tag: GalleryMonthGroup tag })
			{
				OnJumpToMonth?.Invoke(tag);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"MonthNav.MonthButton_Click: threw: {value}");
		}
	}

	private void MonthList_PointerPressed(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			PointerPointProperties properties = e.GetCurrentPoint(MonthList).Properties;
			if ((e.Pointer.PointerDeviceType != PointerDeviceType.Mouse || properties.IsLeftButtonPressed) && MonthList.CapturePointer(e.Pointer))
			{
				isScrubbing = true;
				lastScrubbedIndex = -1;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"MonthNav.MonthList_PointerPressed: threw: {value}");
		}
	}

	private void MonthList_PointerMoved(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if (isScrubbing)
			{
				double y = e.GetCurrentPoint(MonthList).Position.Y;
				int num = HitTestMonthIndex(y);
				if (num >= 0 && num != lastScrubbedIndex)
				{
					lastScrubbedIndex = num;
					OnJumpToMonth?.Invoke(groups[num]);
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"MonthNav.MonthList_PointerMoved: threw: {value}");
		}
	}

	private void MonthList_PointerReleased(object sender, PointerRoutedEventArgs e)
	{
		EndScrub(e.Pointer);
	}

	private void MonthList_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
	{
		EndScrub(null);
	}

	private void EndScrub(Pointer? pointer)
	{
		try
		{
			if (isScrubbing)
			{
				isScrubbing = false;
				lastScrubbedIndex = -1;
				if (pointer != null)
				{
					MonthList.ReleasePointerCapture(pointer);
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"MonthNav.EndScrub: threw: {value}");
		}
	}

	private int HitTestMonthIndex(double y)
	{
		foreach (UIElement child in MonthList.Children)
		{
			if (!(child is Button { Tag: GalleryMonthGroup tag } button))
			{
				continue;
			}
			double y2 = button.TransformToVisual(MonthList).TransformPoint(new Point(0f, 0f)).Y;
			double num = y2 + button.ActualHeight;
			if (y >= y2 && y < num)
			{
				int num2 = IndexOfGroup(tag);
				if (num2 >= 0)
				{
					return num2;
				}
			}
		}
		return -1;
	}

	private int IndexOfGroup(GalleryMonthGroup g)
	{
		for (int i = 0; i < groups.Count; i++)
		{
			if ((object)groups[i] == g)
			{
				return i;
			}
		}
		return -1;
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/Gallery/Controls/MonthNav.xaml");
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
			NavScroll = target.As<ScrollViewer>();
			break;
		case 3:
			MonthList = target.As<StackPanel>();
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
