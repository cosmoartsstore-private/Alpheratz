using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Alpheratz.Shared.Animations;

public static class AnimationHelper
{
	public static void FadeIn(UIElement element, int durationMs = 200, int delayMs = 0)
	{
		Visual elementVisual = ElementCompositionPreview.GetElementVisual(element);
		Compositor compositor = elementVisual.Compositor;
		ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f)));
		scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
		scalarKeyFrameAnimation.DelayTime = TimeSpan.FromMilliseconds(delayMs);
		elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
	}

	public static void FadeOut(UIElement element, int durationMs = 200, int delayMs = 0, Action? onCompleted = null)
	{
		Visual elementVisual = ElementCompositionPreview.GetElementVisual(element);
		Compositor compositor = elementVisual.Compositor;
		ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation.InsertKeyFrame(1f, 0f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f)));
		scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
		scalarKeyFrameAnimation.DelayTime = TimeSpan.FromMilliseconds(delayMs);
		if (onCompleted != null)
		{
			CompositionScopedBatch compositionScopedBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
			elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
			compositionScopedBatch.End();
			compositionScopedBatch.Completed += delegate
			{
				onCompleted();
			};
		}
		else
		{
			elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
		}
	}

	public static void FadeTo(UIElement element, float targetOpacity, int durationMs = 200)
	{
		Visual elementVisual = ElementCompositionPreview.GetElementVisual(element);
		Compositor compositor = elementVisual.Compositor;
		ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation.InsertKeyFrame(1f, targetOpacity, compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f)));
		scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
		elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
	}

	public static void FadeInOut(UIElement element, int fadeInMs = 200, int holdMs = 1200, int fadeOutMs = 400)
	{
		Visual visual = ElementCompositionPreview.GetElementVisual(element);
		Compositor compositor = visual.Compositor;
		CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
		visual.Opacity = 0f;
		CompositionScopedBatch compositionScopedBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
		ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation.InsertKeyFrame(1f, 1f, ease);
		scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(fadeInMs);
		visual.StartAnimation("Opacity", scalarKeyFrameAnimation);
		compositionScopedBatch.End();
		compositionScopedBatch.Completed += delegate
		{
			CompositionScopedBatch compositionScopedBatch2 = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
			ScalarKeyFrameAnimation scalarKeyFrameAnimation2 = compositor.CreateScalarKeyFrameAnimation();
			scalarKeyFrameAnimation2.InsertKeyFrame(1f, 0f, ease);
			scalarKeyFrameAnimation2.Duration = TimeSpan.FromMilliseconds(fadeOutMs);
			scalarKeyFrameAnimation2.DelayTime = TimeSpan.FromMilliseconds(holdMs);
			visual.StartAnimation("Opacity", scalarKeyFrameAnimation2);
			compositionScopedBatch2.End();
		};
	}

	public static void SlideIn(UIElement element, float fromX = 0f, float fromY = 0f, int durationMs = 180)
	{
		Visual elementVisual = ElementCompositionPreview.GetElementVisual(element);
		Compositor compositor = elementVisual.Compositor;
		elementVisual.Offset = new Vector3(fromX, fromY, 0f);
		elementVisual.Opacity = 0f;
		CubicBezierEasingFunction easingFunction = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
		Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
		vector3KeyFrameAnimation.InsertKeyFrame(1f, Vector3.Zero, easingFunction);
		vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
		ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation.InsertKeyFrame(1f, 1f, easingFunction);
		scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
		elementVisual.StartAnimation("Offset", vector3KeyFrameAnimation);
		elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
	}

	public static void SlideOut(UIElement element, float toX = 0f, float toY = 0f, int durationMs = 180, Action? onCompleted = null)
	{
		Visual elementVisual = ElementCompositionPreview.GetElementVisual(element);
		Compositor compositor = elementVisual.Compositor;
		CubicBezierEasingFunction easingFunction = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
		CompositionScopedBatch compositionScopedBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
		Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
		vector3KeyFrameAnimation.InsertKeyFrame(1f, new Vector3(toX, toY, 0f), easingFunction);
		vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
		ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation.InsertKeyFrame(1f, 0f, easingFunction);
		scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
		elementVisual.StartAnimation("Offset", vector3KeyFrameAnimation);
		elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
		compositionScopedBatch.End();
		if (onCompleted != null)
		{
			compositionScopedBatch.Completed += delegate
			{
				onCompleted();
			};
		}
	}

	public static void ScaleIn(UIElement element, float fromScale = 0.88f, int durationMs = 350)
	{
		Visual elementVisual = ElementCompositionPreview.GetElementVisual(element);
		Compositor compositor = elementVisual.Compositor;
		elementVisual.CenterPoint = new Vector3(elementVisual.Size.X / 2f, elementVisual.Size.Y / 2f, 0f);
		elementVisual.Scale = new Vector3(fromScale, fromScale, 1f);
		elementVisual.Opacity = 0f;
		CubicBezierEasingFunction easingFunction = compositor.CreateCubicBezierEasingFunction(new Vector2(0.34f, 1.56f), new Vector2(0.64f, 1f));
		Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
		vector3KeyFrameAnimation.InsertKeyFrame(1f, Vector3.One, easingFunction);
		vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
		ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f)));
		scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds((int)((double)durationMs * 0.6));
		elementVisual.StartAnimation("Scale", vector3KeyFrameAnimation);
		elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
	}

	public static void ScaleOut(UIElement element, float toScale = 0.92f, int durationMs = 200, Action? onCompleted = null)
	{
		Visual elementVisual = ElementCompositionPreview.GetElementVisual(element);
		Compositor compositor = elementVisual.Compositor;
		elementVisual.CenterPoint = new Vector3(elementVisual.Size.X / 2f, elementVisual.Size.Y / 2f, 0f);
		CubicBezierEasingFunction easingFunction = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));
		CompositionScopedBatch compositionScopedBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
		Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
		vector3KeyFrameAnimation.InsertKeyFrame(1f, new Vector3(toScale, toScale, 1f), easingFunction);
		vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
		ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
		scalarKeyFrameAnimation.InsertKeyFrame(1f, 0f, easingFunction);
		scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
		elementVisual.StartAnimation("Scale", vector3KeyFrameAnimation);
		elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
		compositionScopedBatch.End();
		if (onCompleted != null)
		{
			compositionScopedBatch.Completed += delegate
			{
				onCompleted();
			};
		}
	}

	public static void SlideUpFadeIn(UIElement element, float fromY = 20f, int durationMs = 250)
	{
		SlideIn(element, 0f, fromY, durationMs);
	}

	public static void SlideUpFadeOut(UIElement element, float toY = -10f, int durationMs = 200, Action? onCompleted = null)
	{
		SlideOut(element, 0f, toY, durationMs, onCompleted);
	}

	public static void SetOpacity(UIElement element, float opacity)
	{
		ElementCompositionPreview.GetElementVisual(element).Opacity = opacity;
	}

	public static void SetOffset(UIElement element, float x, float y)
	{
		ElementCompositionPreview.GetElementVisual(element).Offset = new Vector3(x, y, 0f);
	}

	public static void ResetVisual(UIElement element)
	{
		Visual elementVisual = ElementCompositionPreview.GetElementVisual(element);
		elementVisual.Opacity = 1f;
		elementVisual.Offset = Vector3.Zero;
		elementVisual.Scale = Vector3.One;
	}
}
