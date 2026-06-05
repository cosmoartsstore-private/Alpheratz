using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Alpheratz.Shared.Animations;

/// <summary>
/// Composition API ベースのアニメーションヘルパー。
/// CSS transition 相当の軽量アニメーションを WinUI で再現する。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public static class AnimationHelper
{
    // -----------------------------------------------------------------------
    // Opacity fade
    // -----------------------------------------------------------------------

    /// <summary>要素の Opacity を 1 へフェードインさせる。</summary>
    public static void FadeIn(UIElement element, int durationMs = 200, int delayMs = 0)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var anim = compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f)));
        anim.Duration = TimeSpan.FromMilliseconds(durationMs);
        anim.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        visual.StartAnimation("Opacity", anim);
    }

    /// <summary>要素の Opacity を 0 へフェードアウトさせ、完了時に任意の処理を呼ぶ。</summary>
    public static void FadeOut(UIElement element, int durationMs = 200, int delayMs = 0, Action? onCompleted = null)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var anim = compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(1f, 0f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f)));
        anim.Duration = TimeSpan.FromMilliseconds(durationMs);
        anim.DelayTime = TimeSpan.FromMilliseconds(delayMs);

        if (onCompleted is not null)
        {
            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation("Opacity", anim);
            batch.End();
            batch.Completed += (_, _) => onCompleted();
        }
        else
        {
            visual.StartAnimation("Opacity", anim);
        }
    }

    /// <summary>要素の Opacity を指定値へアニメーションさせる。</summary>
    public static void FadeTo(UIElement element, float targetOpacity, int durationMs = 200)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var anim = compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(1f, targetOpacity, compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f)));
        anim.Duration = TimeSpan.FromMilliseconds(durationMs);
        visual.StartAnimation("Opacity", anim);
    }

    /// <summary>フェードイン後に指定時間保持し、その後フェードアウトする。</summary>
    public static void FadeInOut(UIElement element, int fadeInMs = 200, int holdMs = 1200, int fadeOutMs = 400)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));

        visual.Opacity = 0f;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var fadeIn = compositor.CreateScalarKeyFrameAnimation();
        fadeIn.InsertKeyFrame(1f, 1f, ease);
        fadeIn.Duration = TimeSpan.FromMilliseconds(fadeInMs);
        visual.StartAnimation("Opacity", fadeIn);
        batch.End();

        batch.Completed += (_, _) =>
        {
            var batch2 = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            var fadeOut = compositor.CreateScalarKeyFrameAnimation();
            fadeOut.InsertKeyFrame(1f, 0f, ease);
            fadeOut.Duration = TimeSpan.FromMilliseconds(fadeOutMs);
            fadeOut.DelayTime = TimeSpan.FromMilliseconds(holdMs);
            visual.StartAnimation("Opacity", fadeOut);
            batch2.End();
        };
    }

    // -----------------------------------------------------------------------
    // Slide (TranslateX / TranslateY via Offset)
    // -----------------------------------------------------------------------

    /// <summary>指定オフセットから現在位置へスライドしながら表示する。</summary>
    public static void SlideIn(UIElement element, float fromX = 0, float fromY = 0, int durationMs = 180)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        visual.Offset = new Vector3(fromX, fromY, 0);
        visual.Opacity = 0f;

        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));

        var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
        offsetAnim.InsertKeyFrame(1f, Vector3.Zero, ease);
        offsetAnim.Duration = TimeSpan.FromMilliseconds(durationMs);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.InsertKeyFrame(1f, 1f, ease);
        opacityAnim.Duration = TimeSpan.FromMilliseconds(durationMs);

        visual.StartAnimation("Offset", offsetAnim);
        visual.StartAnimation("Opacity", opacityAnim);
    }

    /// <summary>指定オフセットへスライドしながら非表示にし、完了時に任意の処理を呼ぶ。</summary>
    public static void SlideOut(UIElement element, float toX = 0, float toY = 0, int durationMs = 180, Action? onCompleted = null)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
        offsetAnim.InsertKeyFrame(1f, new Vector3(toX, toY, 0), ease);
        offsetAnim.Duration = TimeSpan.FromMilliseconds(durationMs);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.InsertKeyFrame(1f, 0f, ease);
        opacityAnim.Duration = TimeSpan.FromMilliseconds(durationMs);

        visual.StartAnimation("Offset", offsetAnim);
        visual.StartAnimation("Opacity", opacityAnim);

        batch.End();
        if (onCompleted is not null)
            batch.Completed += (_, _) => onCompleted();
    }

    // -----------------------------------------------------------------------
    // Scale (bounce / pop) — modal open/close style
    // -----------------------------------------------------------------------

    /// <summary>小さめのスケールから拡大しながら表示する。</summary>
    public static void ScaleIn(UIElement element, float fromScale = 0.88f, int durationMs = 350)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        visual.CenterPoint = new Vector3(visual.Size.X / 2f, visual.Size.Y / 2f, 0f);
        visual.Scale = new Vector3(fromScale, fromScale, 1f);
        visual.Opacity = 0f;

        // 少し行き過ぎる easing にして、モーダル表示に軽い弾みを出す。
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.34f, 1.56f), new Vector2(0.64f, 1f));

        var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
        scaleAnim.InsertKeyFrame(1f, Vector3.One, ease);
        scaleAnim.Duration = TimeSpan.FromMilliseconds(durationMs);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f)));
        opacityAnim.Duration = TimeSpan.FromMilliseconds((int)(durationMs * 0.6));

        visual.StartAnimation("Scale", scaleAnim);
        visual.StartAnimation("Opacity", opacityAnim);
    }

    /// <summary>少し縮小しながら非表示にし、完了時に任意の処理を呼ぶ。</summary>
    public static void ScaleOut(UIElement element, float toScale = 0.92f, int durationMs = 200, Action? onCompleted = null)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        visual.CenterPoint = new Vector3(visual.Size.X / 2f, visual.Size.Y / 2f, 0f);
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f));

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
        scaleAnim.InsertKeyFrame(1f, new Vector3(toScale, toScale, 1f), ease);
        scaleAnim.Duration = TimeSpan.FromMilliseconds(durationMs);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.InsertKeyFrame(1f, 0f, ease);
        opacityAnim.Duration = TimeSpan.FromMilliseconds(durationMs);

        visual.StartAnimation("Scale", scaleAnim);
        visual.StartAnimation("Opacity", opacityAnim);

        batch.End();
        if (onCompleted is not null)
            batch.Completed += (_, _) => onCompleted();
    }

    // -----------------------------------------------------------------------
    // Combined: slide-up + fade (for toasts, overlays)
    // -----------------------------------------------------------------------

    /// <summary>下方向から上へスライドしながらフェードインする。</summary>
    public static void SlideUpFadeIn(UIElement element, float fromY = 20f, int durationMs = 250)
    {
        SlideIn(element, 0, fromY, durationMs);
    }

    /// <summary>上方向へスライドしながらフェードアウトする。</summary>
    public static void SlideUpFadeOut(UIElement element, float toY = -10f, int durationMs = 200, Action? onCompleted = null)
    {
        SlideOut(element, 0, toY, durationMs, onCompleted);
    }

    // -----------------------------------------------------------------------
    // Utility: set visual properties instantly (no animation)
    // -----------------------------------------------------------------------

    /// <summary>アニメーションなしで Opacity を直接設定する。</summary>
    public static void SetOpacity(UIElement element, float opacity)
    {
        ElementCompositionPreview.GetElementVisual(element).Opacity = opacity;
    }

    /// <summary>アニメーションなしで Offset を直接設定する。</summary>
    public static void SetOffset(UIElement element, float x, float y)
    {
        ElementCompositionPreview.GetElementVisual(element).Offset = new Vector3(x, y, 0);
    }

    /// <summary>Opacity、Offset、Scale を通常表示状態へ戻す。</summary>
    public static void ResetVisual(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.Opacity = 1f;
        visual.Offset = Vector3.Zero;
        visual.Scale = Vector3.One;
    }
}
