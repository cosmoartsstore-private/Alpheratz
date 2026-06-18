using System;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Alpheratz.Shared.Controls;

/// <summary>青い進捗バーへ水色のハイライトを周期的に流す共通コントロール。</summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class WaveProgressBar : UserControl
{
    private Storyboard? waveStoryboard;

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
        UpdateFill();
        StartWave();
    }

    private void WaveProgressBar_Unloaded(object sender, RoutedEventArgs e)
    {
        waveStoryboard?.Stop();
        waveStoryboard = null;
    }

    private void WaveProgressBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFill();
        StartWave();
    }

    private void UpdateFill()
    {
        try
        {
            var width = Math.Max(0, TrackRoot.ActualWidth);
            var rawHeight = ActualHeight > 0 ? ActualHeight : Height;
            var height = double.IsFinite(rawHeight) && rawHeight > 0
                ? rawHeight
                : Math.Max(6, MinHeight);
            var fillWidth = IsIndeterminate
                ? width
                : WaveProgressBarLogic.FillWidth(width, Minimum, Maximum, Value);

            FillBorder.Width = fillWidth;
            FillBorder.Opacity = fillWidth > 0 ? 1.0 : 0.0;
            FillBorder.Clip = new RectangleGeometry { Rect = new Rect(0, 0, fillWidth, height) };
            TrackRoot.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WaveProgressBar.UpdateFill: threw: {ex}");
        }
    }

    private void StartWave()
    {
        try
        {
            var width = TrackRoot.ActualWidth;
            if (width <= 0) return;

            waveStoryboard?.Stop();
            var highlightWidth = HighlightBand.Width;
            HighlightTransform.X = -highlightWidth;

            var animation = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                Value = -highlightWidth,
            });
            animation.KeyFrames.Add(new SplineDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.6)),
                Value = width + highlightWidth,
                KeySpline = new KeySpline
                {
                    ControlPoint1 = new Point(0.4, 0),
                    ControlPoint2 = new Point(0.6, 1),
                },
            });
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.4)),
                Value = width + highlightWidth,
            });

            Storyboard.SetTarget(animation, HighlightTransform);
            Storyboard.SetTargetProperty(animation, "X");
            waveStoryboard = new Storyboard();
            waveStoryboard.Children.Add(animation);
            waveStoryboard.Begin();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WaveProgressBar.StartWave: threw: {ex}");
        }
    }
}
