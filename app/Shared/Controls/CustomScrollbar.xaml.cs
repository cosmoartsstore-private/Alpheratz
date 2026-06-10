using System;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core;
using Alpheratz.Shared.Animations;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Alpheratz.Shared.Controls;

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
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

    // カスタムスクロールバーの表示部品を初期化し、現在の Thumb 値を反映する。
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

    // ThumbTop / ThumbHeight の変更を実際の表示位置へ反映する。
    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (d is CustomScrollbar control) control.Apply();
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.OnChanged: threw: {ex}"); }
    }

    // Thumb の高さと Y 位置を依存プロパティから更新する。
    private void Apply()
    {
        // スクロール中に高頻度で呼ばれるため、通常ログは出さずエラーだけ記録する。
        try
        {
            var layout = CustomScrollbarLogic.ThumbLayout(ThumbTop, ThumbHeight);
            Thumb.Height = layout.Height;
            ThumbTransform.Y = layout.Top;
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Apply: threw: {ex}"); }
    }

    // トラック押下でドラッグを開始し、押下位置へスクロール要求を出す。
    private void Track_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        AppLogger.Trace("CustomScrollbar.Track_PointerPressed: enter");
        try
        {
            if (!e.GetCurrentPoint(Track).Properties.IsLeftButtonPressed) return;
            isDragging = true;
            Track.CapturePointer(e.Pointer);
            OnTrackClick?.Invoke(CustomScrollbarLogic.TrackClickPosition(e.GetCurrentPoint(Track).Position.Y));
            e.Handled = true;
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Track_PointerPressed: threw: {ex}"); }
        AppLogger.Trace("CustomScrollbar.Track_PointerPressed: exit");
    }

    // ドラッグ中のポインタ位置をスクロール位置へ変換するため親へ通知する。
    private void Track_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        // ドラッグ中に高頻度で呼ばれるため、通常ログは出さずエラーだけ記録する。
        try
        {
            var point = e.GetCurrentPoint(Track);
            if (!CustomScrollbarLogic.ShouldContinueDragging(isDragging, point.Properties.IsLeftButtonPressed))
            {
                EndDrag(e.Pointer);
                return;
            }

            var dragPosition = CustomScrollbarLogic.DragPosition(isDragging, point.Properties.IsLeftButtonPressed, point.Position.Y);
            if (dragPosition.HasValue) OnDrag?.Invoke(dragPosition.Value);
            e.Handled = true;
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Track_PointerMoved: threw: {ex}"); }
    }

    // ポインタ解放でドラッグ状態とキャプチャを解除する。
    private void Track_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        AppLogger.Trace("CustomScrollbar.Track_PointerReleased: enter");
        try
        {
            EndDrag(e.Pointer);
            e.Handled = true;
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Track_PointerReleased: threw: {ex}"); }
        AppLogger.Trace("CustomScrollbar.Track_PointerReleased: exit");
    }

    // ポインタキャンセル時もドラッグ状態を解除する。
    private void Track_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        try { EndDrag(e.Pointer); }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Track_PointerCanceled: threw: {ex}"); }
    }

    // CapturePointer が外部要因で失われた場合もスクロール追従を止める。
    private void Track_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        try { EndDrag(null); }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Track_PointerCaptureLost: threw: {ex}"); }
    }

    // ホバー中はトラックと Thumb を強調して操作対象を見やすくする。
    private void Track_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            ApplyHoverVisual(CustomScrollbarLogic.HoverVisual(ScrollbarPointerState.Entered, isDragging));
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Track_PointerEntered: threw: {ex}"); }
    }

    // ホバー解除時は、ドラッグ中でなければ通常の細い Thumb に戻す。
    private void Track_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            ApplyHoverVisual(CustomScrollbarLogic.HoverVisual(ScrollbarPointerState.Exited, isDragging));
        }
        catch (Exception ex) { AppLogger.Error($"CustomScrollbar.Track_PointerExited: threw: {ex}"); }
    }

    // hover 表示の計算結果を WinUI 要素へ反映する。ドラッグ中の exit は表示維持のため何もしない。
    private void ApplyHoverVisual(ScrollbarHoverVisual? visual)
    {
        if (visual is null) return;
        AnimationHelper.FadeTo(TrackRail, visual.TrackRailOpacity, visual.AnimationDurationMilliseconds);
        Thumb.Width = visual.ThumbWidth;
        Thumb.Background = ThemeHelper.Brush(this, visual.ThumbBrushKey);
    }

    /// <summary>ドラッグ状態を終了し、保持していればポインタキャプチャを解除する。</summary>
    private void EndDrag(Pointer? pointer)
    {
        isDragging = CustomScrollbarLogic.DraggingAfterRelease();
        if (pointer is not null && Track.PointerCaptures?.Contains(pointer) == true)
            Track.ReleasePointerCapture(pointer);
        ApplyHoverVisual(CustomScrollbarLogic.HoverVisual(ScrollbarPointerState.Exited, isDragging));
    }
}
