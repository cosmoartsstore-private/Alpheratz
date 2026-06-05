namespace Alpheratz.Shared.Controls;

/// <summary>
/// CustomScrollbar の寸法とポインタ状態から決まる表示値。
/// WinUI 要素を触らず、code-behind が適用する値だけを計算する。
/// </summary>
internal static class CustomScrollbarLogic
{
    public const double MinimumThumbHeight = 18;
    public const double RestThumbWidth = 6;
    public const double HoverThumbWidth = 8;
    public const float HiddenRailOpacity = 0f;
    public const float VisibleRailOpacity = 1f;
    public const int HoverAnimationDurationMilliseconds = 200;
    public const string RestThumbBrushKey = "AScrollbarThumb";
    public const string HoverThumbBrushKey = "AScrollbarThumbHover";

    /// <summary>依存プロパティ値から実際に表示する Thumb の高さと位置を返す。</summary>
    public static ScrollbarThumbLayout ThumbLayout(double thumbTop, double thumbHeight)
        => new(Math.Max(MinimumThumbHeight, thumbHeight), thumbTop);

    /// <summary>ドラッグ開始時に通知するトラック上の Y 位置を返す。</summary>
    public static double TrackClickPosition(double trackY) => trackY;

    /// <summary>ドラッグ中だけ親へ渡す Y 位置を返し、非ドラッグ時は通知しない。</summary>
    public static double? DragPosition(bool isDragging, double trackY)
        => isDragging ? trackY : null;

    /// <summary>ポインタ解放後のドラッグ状態を返す。</summary>
    public static bool DraggingAfterRelease() => false;

    /// <summary>ポインタ出入りとドラッグ状態から、適用すべき hover 表示を返す。</summary>
    public static ScrollbarHoverVisual? HoverVisual(ScrollbarPointerState state, bool isDragging)
        => state switch
        {
            ScrollbarPointerState.Entered => new(
                VisibleRailOpacity,
                HoverThumbWidth,
                HoverThumbBrushKey,
                HoverAnimationDurationMilliseconds),
            ScrollbarPointerState.Exited when isDragging => null,
            ScrollbarPointerState.Exited => new(
                HiddenRailOpacity,
                RestThumbWidth,
                RestThumbBrushKey,
                HoverAnimationDurationMilliseconds),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
}

/// <summary>CustomScrollbar の Thumb に適用する高さと Y 位置。</summary>
internal sealed record ScrollbarThumbLayout(double Height, double Top);

/// <summary>CustomScrollbar のポインタ出入り状態。</summary>
internal enum ScrollbarPointerState
{
    Entered,
    Exited,
}

/// <summary>hover 状態で track rail と Thumb に適用する表示値。</summary>
internal sealed record ScrollbarHoverVisual(
    float TrackRailOpacity,
    double ThumbWidth,
    string ThumbBrushKey,
    int AnimationDurationMilliseconds);
