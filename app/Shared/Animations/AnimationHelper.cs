using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Alpheratz.Shared.Animations;

/// <summary>
/// Composition API ベースのアニメーションヘルパー。
/// 視覚効果ではなく状態変化の理解を助けるため、次の原則を共通化する。
/// ・操作への応答は原則120～240ms、画面遷移は220～360msで短く完結させる。
/// ・レイアウトを動かさず、TransformとOpacityを中心に使う。
/// ・状態を表す面を主役にし、周辺要素を同時に大きく動かさない。
/// ・行き過ぎ表現は小さく一度だけ使い、必ず静止位置へ戻す。
/// ・進捗表現を除いて常時動かさず、操作や状態変化が起きたときだけ再生する。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public static class AnimationHelper
{
    // Opacity のフェード

    /// <summary>要素の Opacity を 1 へフェードインさせる。</summary>
    public static void FadeIn(UIElement element, int durationMs = 200, int delayMs = 0)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        visual.StopAnimation("Opacity");
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

    // Offset による TranslateX / TranslateY の移動

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

    // モーダルの開閉に使う拡大縮小

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

    // 共通の選択面を移動させ、状態の連続性を伝える。

    /// <summary>
    /// 選択面を現在位置から次の位置へ移動し、進行方向へわずかに行き過ぎて静止させる。
    /// レイアウト位置は変えず、連続する選択肢の関係だけを短い動きで示す。
    /// </summary>
    public static void MoveSelectionSurface(UIElement surface, float fromY, float toY, int durationMs = 220)
    {
        surface.Opacity = 1;
        ElementCompositionPreview.SetIsTranslationEnabled(surface, true);
        var visual = ElementCompositionPreview.GetElementVisual(surface);
        visual.StopAnimation("Translation");

        if (Math.Abs(toY - fromY) < 0.1f)
        {
            visual.Properties.InsertVector3("Translation", new Vector3(0, toY, 0));
            return;
        }

        var compositor = visual.Compositor;
        var travelEase = compositor.CreateCubicBezierEasingFunction(new Vector2(0.22f, 0.72f), new Vector2(0.2f, 1f));
        var settleEase = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.8f), new Vector2(0.25f, 1f));

        var direction = Math.Sign(toY - fromY);
        visual.Properties.InsertVector3("Translation", new Vector3(0, fromY, 0));
        var translationAnimation = compositor.CreateVector3KeyFrameAnimation();
        translationAnimation.InsertKeyFrame(0.8f, new Vector3(0, toY + (1.25f * direction), 0), travelEase);
        translationAnimation.InsertKeyFrame(1f, new Vector3(0, toY, 0), settleEase);
        translationAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
        visual.StartAnimation("Translation", translationAnimation);
    }

    /// <summary>選択面を、アニメーションを行わず現在位置へ同期する。</summary>
    public static void SetSelectionSurfacePosition(UIElement surface, float y)
    {
        surface.Opacity = 1;
        ElementCompositionPreview.SetIsTranslationEnabled(surface, true);
        var visual = ElementCompositionPreview.GetElementVisual(surface);
        visual.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", new Vector3(0, y, 0));
    }

    // 通知と オーバーレイ に使う上方向の移動とフェード

    /// <summary>モーダルを下から上へスライドしながら表示する。</summary>
    public static void ModalSlideUpIn(UIElement element, float fromY = 48f, int durationMs = 300)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        StopTransformAnimations(visual);
        visual.Offset = new Vector3(0, fromY, 0);
        visual.Opacity = 0f;
        visual.Scale = Vector3.One;

        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.34f, 1.1f), new Vector2(0.64f, 1f));

        var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
        offsetAnim.InsertKeyFrame(1f, Vector3.Zero, ease);
        offsetAnim.Duration = TimeSpan.FromMilliseconds(durationMs);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.InsertKeyFrame(1f, 1f, ease);
        opacityAnim.Duration = TimeSpan.FromMilliseconds(durationMs);

        visual.StartAnimation("Offset", offsetAnim);
        visual.StartAnimation("Opacity", opacityAnim);
    }

    // アニメーション完了後の表示状態を即時に初期化する。

    /// <summary>Opacity、Offset、Scale を通常表示状態へ戻す。</summary>
    public static void ResetVisual(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        StopTransformAnimations(visual);
        visual.Opacity = 1f;
        visual.Offset = Vector3.Zero;
        visual.Scale = Vector3.One;
    }

    /// <summary>再表示前に残っている Composition アニメーションを止め、古い縮小状態の再描画を防ぐ。</summary>
    private static void StopTransformAnimations(Visual visual)
    {
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Offset");
        visual.StopAnimation("Scale");
    }

}
