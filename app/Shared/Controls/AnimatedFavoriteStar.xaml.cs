using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Numerics;
using Alpheratz.Core;
using Alpheratz.Shared.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Shared.Controls;

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class AnimatedFavoriteStar : UserControl
{
    private bool previousLiked;

    public static readonly DependencyProperty LikedProperty =
        DependencyProperty.Register(nameof(Liked), typeof(bool), typeof(AnimatedFavoriteStar), new PropertyMetadata(false, OnLikedChanged));

    public static readonly DependencyProperty InteractiveProperty =
        DependencyProperty.Register(nameof(Interactive), typeof(bool), typeof(AnimatedFavoriteStar), new PropertyMetadata(false, OnInteractiveChanged));

    public static readonly DependencyProperty StarFillProperty =
        DependencyProperty.Register(nameof(StarFill), typeof(Brush), typeof(AnimatedFavoriteStar), new PropertyMetadata(new SolidColorBrush(Colors.Transparent)));

    public static readonly DependencyProperty StarStrokeProperty =
        DependencyProperty.Register(nameof(StarStroke), typeof(Brush), typeof(AnimatedFavoriteStar), new PropertyMetadata(new SolidColorBrush(Colors.Gray)));

    public bool Liked { get => (bool)GetValue(LikedProperty); set => SetValue(LikedProperty, value); }
    public bool Interactive { get => (bool)GetValue(InteractiveProperty); set => SetValue(InteractiveProperty, value); }
    public Brush StarFill { get => (Brush)GetValue(StarFillProperty); set => SetValue(StarFillProperty, value); }
    public Brush StarStroke { get => (Brush)GetValue(StarStrokeProperty); set => SetValue(StarStrokeProperty, value); }

    public Action? OnClick { get; set; }

    // 初期表示とテーマ変更時に、Liked 状態に合った色とアクセシブル名へ同期する。
    public AnimatedFavoriteStar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            try
            {
                ApplyLiked(false);
                UpdateAutomationName();
            }
            catch (Exception ex) { AppLogger.Error($"AnimatedFavoriteStar.Loaded: {ex}"); }
        };
        ActualThemeChanged += (_, _) =>
        {
            try { ApplyLiked(false); }
            catch (Exception ex) { AppLogger.Error($"AnimatedFavoriteStar.ActualThemeChanged: {ex}"); }
        };
    }

    /// <summary>UI Automation 用に、現在の Liked 状態に応じたアクセシブル名をセット。</summary>
    private void UpdateAutomationName()
    {
        var name = AnimatedFavoriteStarLogic.AutomationName(Liked);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, name);
    }

    // Liked 変更時に見た目とアクセシブル名を更新し、必要ならアニメーションする。
    private static void OnLikedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (d is AnimatedFavoriteStar control)
            {
                control.ApplyLiked(true);
                control.UpdateAutomationName();
            }
        }
        catch (Exception ex) { AppLogger.Error($"AnimatedFavoriteStar.OnLikedChanged: {ex}"); }
    }

    // Interactive 変更時にクリック領域の hit-test 可否を切り替える。
    private static void OnInteractiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (d is AnimatedFavoriteStar control)
            {
                control.InteractiveButton.IsHitTestVisible = AnimatedFavoriteStarLogic.HitTestVisible(control.Interactive);
            }
        }
        catch (Exception ex) { AppLogger.Error($"AnimatedFavoriteStar.OnInteractiveChanged: {ex}"); }
    }

    // 現在の Liked 状態を塗りと光彩に反映する。
    private void ApplyLiked(bool animate)
    {
        try
        {
            var liked = Liked;
            var brushPlan = AnimatedFavoriteStarLogic.BrushPlan(liked);

            StarFill = ResolveBrush(brushPlan.FillResourceKey, brushPlan.FillFallback);
            StarStroke = ResolveBrush(brushPlan.StrokeResourceKey, brushPlan.StrokeFallback);

            var animationKind = AnimatedFavoriteStarLogic.AnimationKind(animate, liked, previousLiked);
            if (animationKind != FavoriteStarAnimationKind.None)
            {
                PlayFavoriteAnimation(animationKind);
            }
            else
            {
                var immediate = AnimatedFavoriteStarLogic.ImmediateVisual(liked);
                var glowVisual = ElementCompositionPreview.GetElementVisual(Glow);
                glowVisual.Opacity = immediate.GlowOpacity;
                glowVisual.Scale = immediate.GlowScale;
            }

            previousLiked = liked;
            InteractiveButton.IsHitTestVisible = AnimatedFavoriteStarLogic.HitTestVisible(Interactive);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AnimatedFavoriteStar.ApplyLiked: {ex}");
        }
    }

    // テーマリソースキーとフォールバック種別から実際の Brush を解決する。
    private Brush ResolveBrush(string? resourceKey, FavoriteStarBrushFallback fallback)
    {
        if (resourceKey is not null)
        {
            var themeBrush = ThemeHelper.Brush(this, resourceKey);
            if (themeBrush is not null) return themeBrush;
        }
        return FallbackBrush(fallback);
    }

    // テーマリソースが取れないときの既定 Brush を返す。
    private static Brush FallbackBrush(FavoriteStarBrushFallback fallback)
        => fallback == FavoriteStarBrushFallback.Gray
            ? new SolidColorBrush(Colors.Gray)
            : new SolidColorBrush(Colors.Transparent);

    // Composition の拡大縮小中心として使うコントロール中央座標を返す。
    private Vector3 GetCenter()
        => AnimatedFavoriteStarLogic.Center(ActualWidth, ActualHeight);

    // お気に入り状態変更時の星と光彩のアニメーションを再生する。
    private void PlayFavoriteAnimation(FavoriteStarAnimationKind kind)
    {
        var plan = AnimatedFavoriteStarLogic.AnimationPlan(kind);
        if (plan is null) return;

        var starVisual = ElementCompositionPreview.GetElementVisual(InteractiveButton);
        var glowVisual = ElementCompositionPreview.GetElementVisual(Glow);
        var compositor = starVisual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(plan.EaseControlPoint1, plan.EaseControlPoint2);
        var center = GetCenter();

        starVisual.CenterPoint = center;
        glowVisual.CenterPoint = center;

        var starScale = CreateVectorAnimation(compositor, plan.DurationMilliseconds, plan.StarScaleFrames, ease);
        var starOpacity = CreateScalarAnimation(compositor, plan.DurationMilliseconds, plan.StarOpacityFrames, ease);
        var glowOpacity = CreateScalarAnimation(compositor, plan.DurationMilliseconds, plan.GlowOpacityFrames, ease);
        var glowScale = CreateVectorAnimation(compositor, plan.DurationMilliseconds, plan.GlowScaleFrames, ease);

        if (kind == FavoriteStarAnimationKind.FadeOut)
        {
            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            StartFavoriteAnimations(starVisual, glowVisual, starScale, starOpacity, glowOpacity, glowScale);
            batch.End();
            batch.Completed += (_, _) => ResetAfterFadeOut(starVisual, glowVisual);
            return;
        }

        StartFavoriteAnimations(starVisual, glowVisual, starScale, starOpacity, glowOpacity, glowScale);
    }

    // plan の scalar keyframe から Composition アニメーションを作る。
    private static ScalarKeyFrameAnimation CreateScalarAnimation(
        Compositor compositor,
        int durationMilliseconds,
        IReadOnlyList<FavoriteStarScalarFrame> frames,
        CompositionEasingFunction ease)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        foreach (var frame in frames)
        {
            if (frame.Ease == FavoriteStarEaseKind.Main)
                animation.InsertKeyFrame(frame.Progress, frame.Value, ease);
            else
                animation.InsertKeyFrame(frame.Progress, frame.Value);
        }
        animation.Duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        return animation;
    }

    // plan の Vector3 keyframe から Composition アニメーションを作る。
    private static Vector3KeyFrameAnimation CreateVectorAnimation(
        Compositor compositor,
        int durationMilliseconds,
        IReadOnlyList<FavoriteStarVectorFrame> frames,
        CompositionEasingFunction ease)
    {
        var animation = compositor.CreateVector3KeyFrameAnimation();
        foreach (var frame in frames)
        {
            if (frame.Ease == FavoriteStarEaseKind.Main)
                animation.InsertKeyFrame(frame.Progress, frame.Value, ease);
            else
                animation.InsertKeyFrame(frame.Progress, frame.Value);
        }
        animation.Duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        return animation;
    }

    // 作成済みの星と光彩アニメーションを対象 Visual に開始する。
    private static void StartFavoriteAnimations(
        Visual starVisual,
        Visual glowVisual,
        Vector3KeyFrameAnimation starScale,
        ScalarKeyFrameAnimation starOpacity,
        ScalarKeyFrameAnimation glowOpacity,
        Vector3KeyFrameAnimation glowScale)
    {
        starVisual.StartAnimation("Scale", starScale);
        starVisual.StartAnimation("Opacity", starOpacity);
        glowVisual.StartAnimation("Opacity", glowOpacity);
        glowVisual.StartAnimation("Scale", glowScale);
    }

    // FadeOut の終了後に一時的な縮小・透明度を通常状態へ戻す。
    private void ResetAfterFadeOut(Visual starVisual, Visual glowVisual)
    {
        var completion = AnimatedFavoriteStarLogic.FadeOutCompletionVisual();
        DispatcherQueue?.TryEnqueue(() =>
        {
            starVisual.Opacity = completion.StarOpacity;
            starVisual.Scale = completion.StarScale;
            glowVisual.Opacity = completion.GlowOpacity;
            glowVisual.Scale = completion.GlowScale;
        });
    }

    // クリック可能な状態のときだけ外部コールバックを実行する。
    private void InteractiveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AnimatedFavoriteStarLogic.ShouldInvokeClick(Interactive)) OnClick?.Invoke();
        }
        catch (Exception ex) { AppLogger.Error($"AnimatedFavoriteStar.InteractiveButton_Click: {ex}"); }
    }
}
