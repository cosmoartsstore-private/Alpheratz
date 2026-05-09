using System;
using Alpheratz.Core;
using Alpheratz.Shared.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Alpheratz.Shared.Controls;

public sealed partial class CustomScrollbar : UserControl
{
    private bool isDragging;

    public static readonly DependencyProperty ThumbTopProperty =
        DependencyProperty.Register(nameof(ThumbTop), typeof(double), typeof(CustomScrollbar), new PropertyMetadata(0d, OnChanged));

    public static readonly DependencyProperty ThumbHeightProperty =
        DependencyProperty.Register(nameof(ThumbHeight), typeof(double), typeof(CustomScrollbar), new PropertyMetadata(48d, OnChanged));

    public double ThumbTop { get => (double)GetValue(ThumbTopProperty); set => SetValue(ThumbTopProperty, value); }
    public double ThumbHeight { get => (double)GetValue(ThumbHeightProperty); set => SetValue(ThumbHeightProperty, value); }

    public Action<double>? OnTrackClick { get; set; }
    public Action<double>? OnDrag { get; set; }

    public CustomScrollbar()
    {
        AppLogger.Trace("CustomScrollbar.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"CustomScrollbar.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        Apply();
        AppLogger.Trace("CustomScrollbar.ctor: exit");
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (d is CustomScrollbar control) control.Apply();
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.OnChanged: threw: {ex}"); }
    }

    private void Apply()
    {
        // Hot path during scroll; only error-log on throw.
        try
        {
            Thumb.Height = Math.Max(18, ThumbHeight);
            ThumbTransform.Y = ThumbTop;
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Apply: threw: {ex}"); }
    }

    private void Track_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        AppLogger.Trace("CustomScrollbar.Track_PointerPressed: enter");
        try
        {
            isDragging = true;
            CapturePointer(e.Pointer);
            OnTrackClick?.Invoke(e.GetCurrentPoint(Track).Position.Y);
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Track_PointerPressed: threw: {ex}"); }
        AppLogger.Trace("CustomScrollbar.Track_PointerPressed: exit");
    }

    private void Track_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        // Hot path during drag; only error log on throw.
        try
        {
            if (isDragging) OnDrag?.Invoke(e.GetCurrentPoint(Track).Position.Y);
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Track_PointerMoved: threw: {ex}"); }
    }

    private void Track_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        AppLogger.Trace("CustomScrollbar.Track_PointerReleased: enter");
        try
        {
            isDragging = false;
            ReleasePointerCapture(e.Pointer);
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Track_PointerReleased: threw: {ex}"); }
        AppLogger.Trace("CustomScrollbar.Track_PointerReleased: exit");
    }

    private void Track_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            AnimationHelper.FadeTo(TrackRail, 1f, 200);
            Thumb.Width = 8;
            Thumb.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AScrollbarThumbHover"];
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Track_PointerEntered: threw: {ex}"); }
    }

    private void Track_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (isDragging) return;
            AnimationHelper.FadeTo(TrackRail, 0f, 200);
            Thumb.Width = 6;
            Thumb.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AScrollbarThumb"];
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Track_PointerExited: threw: {ex}"); }
    }
}
