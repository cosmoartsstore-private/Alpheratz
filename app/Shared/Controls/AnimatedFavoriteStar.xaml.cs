using System;
using System.Numerics;
using Alpheratz.Core;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Shared.Controls;

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
    }

    /// <summary>UI Automation 用に、現在の Liked 状態に応じたアクセシブル名をセット。</summary>
    private void UpdateAutomationName()
    {
        var name = Liked ? "お気に入り解除" : "お気に入りに追加";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, name);
    }

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

    private static void OnInteractiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (d is AnimatedFavoriteStar control) control.InteractiveButton.IsHitTestVisible = control.Interactive;
        }
        catch (Exception ex) { AppLogger.Error($"AnimatedFavoriteStar.OnInteractiveChanged: {ex}"); }
    }

    private void ApplyLiked(bool animate)
    {
        try
        {
            var liked = Liked;

            if (liked)
            {
                StarFill = (Brush)Application.Current.Resources["AFavorite"];
                StarStroke = (Brush)Application.Current.Resources["AFavorite"];
            }
            else
            {
                StarFill = new SolidColorBrush(Colors.Transparent);
                StarStroke = (Brush)Application.Current.Resources["ATextFaint"];
            }

            if (animate && liked != previousLiked)
            {
                if (liked)
                    PlayFadeInAnimation();
                else
                    PlayFadeOutAnimation();
            }
            else
            {
                var glowVisual = ElementCompositionPreview.GetElementVisual(Glow);
                glowVisual.Opacity = liked ? 0.95f : 0f;
                glowVisual.Scale = liked ? new Vector3(1f, 1f, 1f) : new Vector3(0.78f, 0.78f, 1f);
            }

            previousLiked = liked;
            InteractiveButton.IsHitTestVisible = Interactive;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AnimatedFavoriteStar.ApplyLiked: {ex}");
        }
    }

    private Vector3 GetCenter()
    {
        var w = (float)ActualWidth;
        var h = (float)ActualHeight;
        if (w <= 0) w = 30f;
        if (h <= 0) h = 30f;
        return new Vector3(w / 2f, h / 2f, 0f);
    }

    private void PlayFadeInAnimation()
    {
        var starVisual = ElementCompositionPreview.GetElementVisual(InteractiveButton);
        var glowVisual = ElementCompositionPreview.GetElementVisual(Glow);
        var compositor = starVisual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        var center = GetCenter();

        // Star bounce scale
        var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
        scaleAnim.InsertKeyFrame(0f, new Vector3(0.9f, 0.9f, 1f));
        scaleAnim.InsertKeyFrame(0.6f, new Vector3(1.08f, 1.08f, 1f), ease);
        scaleAnim.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), ease);
        scaleAnim.Duration = TimeSpan.FromMilliseconds(240);

        // Star opacity
        var starOpacity = compositor.CreateScalarKeyFrameAnimation();
        starOpacity.InsertKeyFrame(0f, 0.55f);
        starOpacity.InsertKeyFrame(1f, 1f, ease);
        starOpacity.Duration = TimeSpan.FromMilliseconds(240);

        // Glow opacity
        var glowOpacity = compositor.CreateScalarKeyFrameAnimation();
        glowOpacity.InsertKeyFrame(0f, 0f);
        glowOpacity.InsertKeyFrame(1f, 0.95f, ease);
        glowOpacity.Duration = TimeSpan.FromMilliseconds(240);

        // Glow scale (expand from small)
        var glowScale = compositor.CreateVector3KeyFrameAnimation();
        glowScale.InsertKeyFrame(0f, new Vector3(0.72f, 0.72f, 1f));
        glowScale.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), ease);
        glowScale.Duration = TimeSpan.FromMilliseconds(240);

        starVisual.CenterPoint = center;
        glowVisual.CenterPoint = center;

        starVisual.StartAnimation("Scale", scaleAnim);
        starVisual.StartAnimation("Opacity", starOpacity);
        glowVisual.StartAnimation("Opacity", glowOpacity);
        glowVisual.StartAnimation("Scale", glowScale);
    }

    private void PlayFadeOutAnimation()
    {
        var starVisual = ElementCompositionPreview.GetElementVisual(InteractiveButton);
        var glowVisual = ElementCompositionPreview.GetElementVisual(Glow);
        var compositor = starVisual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        var center = GetCenter();

        // Star fade out
        var starOpacity = compositor.CreateScalarKeyFrameAnimation();
        starOpacity.InsertKeyFrame(0f, 1f);
        starOpacity.InsertKeyFrame(1f, 0.75f, ease);
        starOpacity.Duration = TimeSpan.FromMilliseconds(200);

        // Star shrink
        var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
        scaleAnim.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
        scaleAnim.InsertKeyFrame(1f, new Vector3(0.92f, 0.92f, 1f), ease);
        scaleAnim.Duration = TimeSpan.FromMilliseconds(200);

        // Glow fade out
        var glowOpacity = compositor.CreateScalarKeyFrameAnimation();
        glowOpacity.InsertKeyFrame(0f, 0.95f);
        glowOpacity.InsertKeyFrame(1f, 0f, ease);
        glowOpacity.Duration = TimeSpan.FromMilliseconds(200);

        // Glow shrink
        var glowScale = compositor.CreateVector3KeyFrameAnimation();
        glowScale.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
        glowScale.InsertKeyFrame(1f, new Vector3(0.82f, 0.82f, 1f), ease);
        glowScale.Duration = TimeSpan.FromMilliseconds(200);

        starVisual.CenterPoint = center;
        glowVisual.CenterPoint = center;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        starVisual.StartAnimation("Scale", scaleAnim);
        starVisual.StartAnimation("Opacity", starOpacity);
        glowVisual.StartAnimation("Opacity", glowOpacity);
        glowVisual.StartAnimation("Scale", glowScale);

        batch.End();
        batch.Completed += (_, _) =>
        {
            DispatcherQueue?.TryEnqueue(() =>
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
            if (Interactive) OnClick?.Invoke();
        }
        catch (Exception ex) { AppLogger.Error($"AnimatedFavoriteStar.InteractiveButton_Click: {ex}"); }
    }
}
