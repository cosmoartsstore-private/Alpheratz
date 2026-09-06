using System;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Alpheratz.Shared.Controls;

/// <summary>
/// 既知の進捗は滑らかに伸びる塗り、未確定の進捗は単色セグメントの移動で示す共通直線バー。
/// 既存 XAML との互換性を保つためコントロール名は維持している。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class WaveProgressBar : UserControl
{
    private const int FillAnimationDurationMilliseconds = 180;
    private const int IndeterminateAnimationDurationMilliseconds = 1100;
    private Storyboard? fillStoryboard;
    private Storyboard? indeterminateStoryboard;
    private bool isLoaded;

    public WaveProgressBar()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(WaveProgressBar),
        new PropertyMetadata(0.0, OnProgressPropertyChanged));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(WaveProgressBar),
        new PropertyMetadata(100.0, OnProgressPropertyChanged));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(WaveProgressBar),
        new PropertyMetadata(0.0, OnProgressPropertyChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate),
        typeof(bool),
        typeof(WaveProgressBar),
        new PropertyMetadata(false, OnProgressPropertyChanged));

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    private static void OnProgressPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveProgressBar bar)
            bar.UpdateFill();
    }

    private void WaveProgressBar_Loaded(object sender, RoutedEventArgs e)
    {
        isLoaded = true;
        UpdateFill();
    }

    private void WaveProgressBar_Unloaded(object sender, RoutedEventArgs e)
    {
        isLoaded = false;
        fillStoryboard?.Stop();
        indeterminateStoryboard?.Stop();
        fillStoryboard = null;
        indeterminateStoryboard = null;
    }

    private void WaveProgressBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFill();
    }

    private void UpdateFill()
    {
        try
        {
            var width = Math.Max(0, TrackRoot.ActualWidth);
            var rawHeight = ActualHeight > 0 ? ActualHeight : Height;
            var height = double.IsFinite(rawHeight) && rawHeight > 0
                ? rawHeight
                : Math.Max(3, MinHeight);
            TrackRoot.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };

            if (IsIndeterminate)
            {
                fillStoryboard?.Stop();
                FillBorder.Opacity = 0;
                StartIndeterminate(width);
                return;
            }

            indeterminateStoryboard?.Stop();
            indeterminateStoryboard = null;
            IndeterminateSegment.Opacity = 0;

            var targetWidth = WaveProgressBarLogic.FillWidth(width, Minimum, Maximum, Value);
            var currentWidth = Math.Clamp(FillBorder.ActualWidth, 0, width);
            FillBorder.Opacity = targetWidth > 0 ? 1.0 : 0.0;
            AnimateFill(currentWidth, targetWidth);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WaveProgressBar.UpdateFill: threw: {ex}");
        }
    }

    private void AnimateFill(double currentWidth, double targetWidth)
    {
        fillStoryboard?.Stop();
        fillStoryboard = null;
        FillBorder.Width = currentWidth;

        if (!isLoaded || Math.Abs(targetWidth - currentWidth) < 0.5)
        {
            FillBorder.Width = targetWidth;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = currentWidth,
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(FillAnimationDurationMilliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, FillBorder);
        Storyboard.SetTargetProperty(animation, "Width");
        fillStoryboard = new Storyboard();
        fillStoryboard.Children.Add(animation);
        fillStoryboard.Begin();
    }

    private void StartIndeterminate(double width)
    {
        try
        {
            if (width <= 0) return;

            indeterminateStoryboard?.Stop();
            var segmentWidth = Math.Min(width, Math.Clamp(width * 0.28, 32, 120));
            IndeterminateSegment.Width = segmentWidth;
            IndeterminateSegment.Opacity = 1;
            IndeterminateTransform.X = -segmentWidth;

            var animation = new DoubleAnimation
            {
                From = -segmentWidth,
                To = width,
                Duration = TimeSpan.FromMilliseconds(IndeterminateAnimationDurationMilliseconds),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Storyboard.SetTarget(animation, IndeterminateTransform);
            Storyboard.SetTargetProperty(animation, "X");
            indeterminateStoryboard = new Storyboard();
            indeterminateStoryboard.Children.Add(animation);
            indeterminateStoryboard.Begin();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WaveProgressBar.StartIndeterminate: threw: {ex}");
        }
    }
}
