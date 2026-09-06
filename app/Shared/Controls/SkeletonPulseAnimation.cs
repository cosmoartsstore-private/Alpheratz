using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Alpheratz.Shared.Controls;

/// <summary>
/// 写真カードの読み込み面へMaterial UI型のPulseを適用する。
/// 面全体を1.5秒周期で1.0から0.48まで明滅させ、横切るハイライトは生成しない。
/// </summary>
internal static class SkeletonPulseAnimation
{
    private const int DurationMilliseconds = 1500;
    private const float MinimumOpacity = 0.48f;

    public static void Start(FrameworkElement target)
    {
        target.Opacity = 1;

        var visual = ElementCompositionPreview.GetElementVisual(target);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1f;

        var compositor = visual.Compositor;
        var easeInOut = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.42f, 0f),
            new Vector2(0.58f, 1f));
        var pulse = compositor.CreateScalarKeyFrameAnimation();
        pulse.InsertKeyFrame(0f, 1f);
        pulse.InsertKeyFrame(0.5f, MinimumOpacity, easeInOut);
        pulse.InsertKeyFrame(1f, 1f, easeInOut);
        pulse.Duration = TimeSpan.FromMilliseconds(DurationMilliseconds);
        pulse.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", pulse);
    }

    public static void Stop(FrameworkElement target)
    {
        var visual = ElementCompositionPreview.GetElementVisual(target);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1f;
        target.Opacity = 0;
    }
}
