using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Numerics;
using Alpheratz.Core;
using Alpheratz.Shared.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Shared.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class AnimatedFavoriteStar : UserControl, IComponentConnector
{
	private bool previousLiked;

	public static readonly DependencyProperty LikedProperty = DependencyProperty.Register("Liked", typeof(bool), typeof(AnimatedFavoriteStar), new PropertyMetadata(false, OnLikedChanged));

	public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register("Interactive", typeof(bool), typeof(AnimatedFavoriteStar), new PropertyMetadata(false, OnInteractiveChanged));

	public static readonly DependencyProperty StarFillProperty = DependencyProperty.Register("StarFill", typeof(Brush), typeof(AnimatedFavoriteStar), new PropertyMetadata(new SolidColorBrush(Colors.Transparent)));

	public static readonly DependencyProperty StarStrokeProperty = DependencyProperty.Register("StarStroke", typeof(Brush), typeof(AnimatedFavoriteStar), new PropertyMetadata(new SolidColorBrush(Colors.Gray)));

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private UserControl Root;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Ellipse Glow;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button InteractiveButton;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Path Star;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public bool Liked
	{
		get
		{
			return (bool)GetValue(LikedProperty);
		}
		set
		{
			SetValue(LikedProperty, value);
		}
	}

	public bool Interactive
	{
		get
		{
			return (bool)GetValue(InteractiveProperty);
		}
		set
		{
			SetValue(InteractiveProperty, value);
		}
	}

	public Brush StarFill
	{
		get
		{
			return (Brush)GetValue(StarFillProperty);
		}
		set
		{
			SetValue(StarFillProperty, value);
		}
	}

	public Brush StarStroke
	{
		get
		{
			return (Brush)GetValue(StarStrokeProperty);
		}
		set
		{
			SetValue(StarStrokeProperty, value);
		}
	}

	public Action? OnClick { get; set; }

	public AnimatedFavoriteStar()
	{
		InitializeComponent();
		base.Loaded += delegate
		{
			try
			{
				ApplyLiked(animate: false);
				UpdateAutomationName();
			}
			catch (Exception value)
			{
				AppLogger.Error($"AnimatedFavoriteStar.Loaded: {value}");
			}
		};
		base.ActualThemeChanged += delegate
		{
			try
			{
				ApplyLiked(animate: false);
			}
			catch (Exception value)
			{
				AppLogger.Error($"AnimatedFavoriteStar.ActualThemeChanged: {value}");
			}
		};
	}

	private void UpdateAutomationName()
	{
		string value = (Liked ? "お気に入り解除" : "お気に入りに追加");
		AutomationProperties.SetName(this, value);
	}

	private static void OnLikedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		try
		{
			if (d is AnimatedFavoriteStar animatedFavoriteStar)
			{
				animatedFavoriteStar.ApplyLiked(animate: true);
				animatedFavoriteStar.UpdateAutomationName();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"AnimatedFavoriteStar.OnLikedChanged: {value}");
		}
	}

	private static void OnInteractiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		try
		{
			if (d is AnimatedFavoriteStar animatedFavoriteStar)
			{
				animatedFavoriteStar.InteractiveButton.IsHitTestVisible = animatedFavoriteStar.Interactive;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"AnimatedFavoriteStar.OnInteractiveChanged: {value}");
		}
	}

	private void ApplyLiked(bool animate)
	{
		try
		{
			bool liked = Liked;
			if (liked)
			{
				StarFill = ThemeHelper.Brush(this, "AFavorite") ?? new SolidColorBrush(Colors.Transparent);
				StarStroke = ThemeHelper.Brush(this, "AFavorite") ?? new SolidColorBrush(Colors.Gray);
			}
			else
			{
				StarFill = new SolidColorBrush(Colors.Transparent);
				StarStroke = ThemeHelper.Brush(this, "ATextFaint") ?? new SolidColorBrush(Colors.Gray);
			}
			if (animate && liked != previousLiked)
			{
				if (liked)
				{
					PlayFadeInAnimation();
				}
				else
				{
					PlayFadeOutAnimation();
				}
			}
			else
			{
				Visual elementVisual = ElementCompositionPreview.GetElementVisual(Glow);
				elementVisual.Opacity = (liked ? 0.95f : 0f);
				elementVisual.Scale = (liked ? new Vector3(1f, 1f, 1f) : new Vector3(0.78f, 0.78f, 1f));
			}
			previousLiked = liked;
			InteractiveButton.IsHitTestVisible = Interactive;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AnimatedFavoriteStar.ApplyLiked: {value}");
		}
	}

	private Vector3 GetCenter()
	{
		float num = (float)base.ActualWidth;
		float num2 = (float)base.ActualHeight;
		if (num <= 0f)
		{
			num = 30f;
		}
		if (num2 <= 0f)
		{
			num2 = 30f;
		}
		return new Vector3(num / 2f, num2 / 2f, 0f);
	}

	private void PlayFadeInAnimation()
	{
		Visual elementVisual = ElementCompositionPreview.GetElementVisual(InteractiveButton);
		Visual elementVisual2 = ElementCompositionPreview.GetElementVisual(Glow);
		Compositor compositor = elementVisual.Compositor;
		CubicBezierEasingFunction easingFunction = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
		Vector3 center = GetCenter();
		Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
		vector3KeyFrameAnimation.InsertKeyFrame(0f, new Vector3(0.9f, 0.9f, 1f));
		vector3KeyFrameAnimation.InsertKeyFrame(0.6f, new Vector3(1.08f, 1.08f, 1f), easingFunction);
		vector3KeyFrameAnimation.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), easingFunction);
		vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(240.0);
		ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation.InsertKeyFrame(0f, 0.55f);
		scalarKeyFrameAnimation.InsertKeyFrame(1f, 1f, easingFunction);
		scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(240.0);
		ScalarKeyFrameAnimation scalarKeyFrameAnimation2 = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation2.InsertKeyFrame(0f, 0f);
		scalarKeyFrameAnimation2.InsertKeyFrame(1f, 0.95f, easingFunction);
		scalarKeyFrameAnimation2.Duration = TimeSpan.FromMilliseconds(240.0);
		Vector3KeyFrameAnimation vector3KeyFrameAnimation2 = compositor.CreateVector3KeyFrameAnimation();
		vector3KeyFrameAnimation2.InsertKeyFrame(0f, new Vector3(0.72f, 0.72f, 1f));
		vector3KeyFrameAnimation2.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), easingFunction);
		vector3KeyFrameAnimation2.Duration = TimeSpan.FromMilliseconds(240.0);
		elementVisual.CenterPoint = center;
		elementVisual2.CenterPoint = center;
		elementVisual.StartAnimation("Scale", vector3KeyFrameAnimation);
		elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
		elementVisual2.StartAnimation("Opacity", scalarKeyFrameAnimation2);
		elementVisual2.StartAnimation("Scale", vector3KeyFrameAnimation2);
	}

	private void PlayFadeOutAnimation()
	{
		Visual starVisual = ElementCompositionPreview.GetElementVisual(InteractiveButton);
		Visual glowVisual = ElementCompositionPreview.GetElementVisual(Glow);
		Compositor compositor = starVisual.Compositor;
		CubicBezierEasingFunction easingFunction = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
		Vector3 center = GetCenter();
		ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation.InsertKeyFrame(0f, 1f);
		scalarKeyFrameAnimation.InsertKeyFrame(1f, 0.75f, easingFunction);
		scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(200.0);
		Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
		vector3KeyFrameAnimation.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
		vector3KeyFrameAnimation.InsertKeyFrame(1f, new Vector3(0.92f, 0.92f, 1f), easingFunction);
		vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(200.0);
		ScalarKeyFrameAnimation scalarKeyFrameAnimation2 = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation2.InsertKeyFrame(0f, 0.95f);
		scalarKeyFrameAnimation2.InsertKeyFrame(1f, 0f, easingFunction);
		scalarKeyFrameAnimation2.Duration = TimeSpan.FromMilliseconds(200.0);
		Vector3KeyFrameAnimation vector3KeyFrameAnimation2 = compositor.CreateVector3KeyFrameAnimation();
		vector3KeyFrameAnimation2.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
		vector3KeyFrameAnimation2.InsertKeyFrame(1f, new Vector3(0.82f, 0.82f, 1f), easingFunction);
		vector3KeyFrameAnimation2.Duration = TimeSpan.FromMilliseconds(200.0);
		starVisual.CenterPoint = center;
		glowVisual.CenterPoint = center;
		CompositionScopedBatch compositionScopedBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
		starVisual.StartAnimation("Scale", vector3KeyFrameAnimation);
		starVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
		glowVisual.StartAnimation("Opacity", scalarKeyFrameAnimation2);
		glowVisual.StartAnimation("Scale", vector3KeyFrameAnimation2);
		compositionScopedBatch.End();
		compositionScopedBatch.Completed += delegate
		{
			base.DispatcherQueue?.TryEnqueue(delegate
			{
				starVisual.Opacity = 1f;
				starVisual.Scale = new Vector3(1f, 1f, 1f);
				glowVisual.Opacity = 0f;
				glowVisual.Scale = new Vector3(0.78f, 0.78f, 1f);
			});
		};
	}

	private void InteractiveButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (Interactive)
			{
				OnClick?.Invoke();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"AnimatedFavoriteStar.InteractiveButton_Click: {value}");
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Shared/Controls/AnimatedFavoriteStar.xaml");
			Application.LoadComponent(this, resourceLocator, ComponentResourceLocation.Application);
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			Root = target.As<UserControl>();
			break;
		case 2:
			Glow = target.As<Ellipse>();
			break;
		case 3:
			InteractiveButton = target.As<Button>();
			InteractiveButton.Click += InteractiveButton_Click;
			break;
		case 4:
			Star = target.As<Path>();
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
