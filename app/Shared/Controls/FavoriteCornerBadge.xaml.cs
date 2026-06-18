using System;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Shared.Controls;

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class FavoriteCornerBadge : UserControl
{
    public static readonly DependencyProperty LikedProperty =
        DependencyProperty.Register(nameof(Liked), typeof(bool), typeof(FavoriteCornerBadge),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty InteractiveProperty =
        DependencyProperty.Register(nameof(Interactive), typeof(bool), typeof(FavoriteCornerBadge),
            new PropertyMetadata(false, OnInteractiveChanged));

    public static readonly DependencyProperty BadgeSizeProperty =
        DependencyProperty.Register(nameof(BadgeSize), typeof(double), typeof(FavoriteCornerBadge),
            new PropertyMetadata(36.0));

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(FavoriteCornerBadge),
            new PropertyMetadata(12.0));

    public bool Liked
    {
        get => (bool)GetValue(LikedProperty);
        set => SetValue(LikedProperty, value);
    }

    public bool Interactive
    {
        get => (bool)GetValue(InteractiveProperty);
        set => SetValue(InteractiveProperty, value);
    }

    public double BadgeSize
    {
        get => (double)GetValue(BadgeSizeProperty);
        set => SetValue(BadgeSizeProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public Action? OnClick { get; set; }

    public FavoriteCornerBadge()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            try { ApplyVisualState(); }
            catch (Exception ex) { AppLogger.Error($"FavoriteCornerBadge.Loaded: {ex}"); }
        };
        ActualThemeChanged += (_, _) =>
        {
            try { ApplyVisualState(); }
            catch (Exception ex) { AppLogger.Error($"FavoriteCornerBadge.ActualThemeChanged: {ex}"); }
        };
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (d is FavoriteCornerBadge badge)
                badge.ApplyVisualState();
        }
        catch (Exception ex) { AppLogger.Error($"FavoriteCornerBadge.OnVisualPropertyChanged: {ex}"); }
    }

    private static void OnInteractiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (d is FavoriteCornerBadge badge)
                badge.InteractiveButton.IsHitTestVisible = badge.Interactive;
        }
        catch (Exception ex) { AppLogger.Error($"FavoriteCornerBadge.OnInteractiveChanged: {ex}"); }
    }

    private void ApplyVisualState()
    {
        var liked = Liked;
        Triangle.Fill = liked ? ActiveFill() : InactiveFill();
        Triangle.Opacity = liked ? 0.96 : 0.88;
        StarIcon.IconName = liked ? "starFill" : "star";
        StarIcon.Foreground = new SolidColorBrush(Colors.White);
        StarIcon.Opacity = 1.0;
        InteractiveButton.IsHitTestVisible = Interactive;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            this,
            liked ? "お気に入り解除" : "お気に入りに追加");
    }

    private static Brush ActiveFill()
        => new SolidColorBrush(Windows.UI.Color.FromArgb(0xF4, 0xF5, 0x9E, 0x0B));

    private static Brush InactiveFill()
        => new SolidColorBrush(Windows.UI.Color.FromArgb(0xD9, 0x6B, 0x72, 0x80));

    private void InteractiveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Interactive) OnClick?.Invoke();
        }
        catch (Exception ex) { AppLogger.Error($"FavoriteCornerBadge.InteractiveButton_Click: {ex}"); }
    }

    private void InteractiveButton_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }
}
