using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using Alpheratz.Core;
using Alpheratz.Shared.Animations;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Shared.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class CustomScrollbar : UserControl, IComponentConnector
{
	private bool isDragging;

	public static readonly DependencyProperty ThumbTopProperty = DependencyProperty.Register("ThumbTop", typeof(double), typeof(CustomScrollbar), new PropertyMetadata(0.0, OnChanged));

	public static readonly DependencyProperty ThumbHeightProperty = DependencyProperty.Register("ThumbHeight", typeof(double), typeof(CustomScrollbar), new PropertyMetadata(48.0, OnChanged));

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid Track;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border TrackRail;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border Thumb;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TranslateTransform ThumbTransform;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public double ThumbTop
	{
		get
		{
			return (double)GetValue(ThumbTopProperty);
		}
		set
		{
			SetValue(ThumbTopProperty, value);
		}
	}

	public double ThumbHeight
	{
		get
		{
			return (double)GetValue(ThumbHeightProperty);
		}
		set
		{
			SetValue(ThumbHeightProperty, value);
		}
	}

	public Action<double>? OnTrackClick { get; set; }

	public Action<double>? OnDrag { get; set; }

	public CustomScrollbar()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"CustomScrollbar.ctor: InitializeComponent failed: {value}");
			throw;
		}
		Apply();
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
			if (!isDragging)
			{
				Thumb.ClearValue(Border.BackgroundProperty);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"CustomScrollbar.OnActualThemeChanged: threw: {value}");
		}
	}

	private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		try
		{
			if (d is CustomScrollbar customScrollbar)
			{
				customScrollbar.Apply();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"CustomScrollbar.OnChanged: threw: {value}");
		}
	}

	private void Apply()
	{
		try
		{
			Thumb.Height = Math.Max(18.0, ThumbHeight);
			ThumbTransform.Y = ThumbTop;
		}
		catch (Exception value)
		{
			AppLogger.Error($"CustomScrollbar.Apply: threw: {value}");
		}
	}

	private void Track_PointerPressed(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			isDragging = true;
			CapturePointer(e.Pointer);
			OnTrackClick?.Invoke(e.GetCurrentPoint(Track).Position.Y);
		}
		catch (Exception value)
		{
			AppLogger.Error($"CustomScrollbar.Track_PointerPressed: threw: {value}");
		}
	}

	private void Track_PointerMoved(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if (isDragging)
			{
				OnDrag?.Invoke(e.GetCurrentPoint(Track).Position.Y);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"CustomScrollbar.Track_PointerMoved: threw: {value}");
		}
	}

	private void Track_PointerReleased(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			isDragging = false;
			ReleasePointerCapture(e.Pointer);
		}
		catch (Exception value)
		{
			AppLogger.Error($"CustomScrollbar.Track_PointerReleased: threw: {value}");
		}
	}

	private void Track_PointerEntered(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			AnimationHelper.FadeTo(TrackRail, 1f);
			Thumb.Width = 8.0;
			Thumb.Background = ThemeHelper.Brush(this, "AScrollbarThumbHover");
		}
		catch (Exception value)
		{
			AppLogger.Error($"CustomScrollbar.Track_PointerEntered: threw: {value}");
		}
	}

	private void Track_PointerExited(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if (!isDragging)
			{
				AnimationHelper.FadeTo(TrackRail, 0f);
				Thumb.Width = 6.0;
				Thumb.ClearValue(Border.BackgroundProperty);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"CustomScrollbar.Track_PointerExited: threw: {value}");
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Shared/Controls/CustomScrollbar.xaml");
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
			Track = target.As<Grid>();
			Track.PointerEntered += Track_PointerEntered;
			Track.PointerExited += Track_PointerExited;
			Track.PointerPressed += Track_PointerPressed;
			Track.PointerMoved += Track_PointerMoved;
			Track.PointerReleased += Track_PointerReleased;
			break;
		case 3:
			TrackRail = target.As<Border>();
			break;
		case 4:
			Thumb = target.As<Border>();
			break;
		case 5:
			ThumbTransform = target.As<TranslateTransform>();
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
