using System.Collections.Generic;
using System.Numerics;

namespace Alpheratz.Shared.Controls;

/// <summary>
/// AnimatedFavoriteStar の状態表示とアニメーション値を UI 非依存で決める補助ロジック。
/// Brush や Composition は生成せず、code-behind が適用するためのキーと数値だけを返す。
/// </summary>
internal static class AnimatedFavoriteStarLogic
{
    public const string FavoriteBrushKey = "AFavorite";
    public const string FaintTextBrushKey = "ATextFaint";

    private const float DefaultSize = 30f;

    /// <summary>Liked 状態から UI Automation に公開する名前を返す。</summary>
    public static string AutomationName(bool liked)
        => liked ? "お気に入り解除" : "お気に入りに追加";

    /// <summary>Liked 状態から星の塗りと線に使うテーマキーとフォールバックを返す。</summary>
    public static FavoriteStarBrushPlan BrushPlan(bool liked)
        => liked
            ? new FavoriteStarBrushPlan(FavoriteBrushKey, FavoriteBrushKey, FavoriteStarBrushFallback.Transparent, FavoriteStarBrushFallback.Gray)
            : new FavoriteStarBrushPlan(null, FaintTextBrushKey, FavoriteStarBrushFallback.Transparent, FavoriteStarBrushFallback.Gray);

    /// <summary>クリックイベントで外部コールバックを呼ぶべきかを返す。</summary>
    public static bool ShouldInvokeClick(bool interactive) => interactive;

    /// <summary>ボタンの hit-test 可否を Interactive 状態から返す。</summary>
    public static bool HitTestVisible(bool interactive) => interactive;

    /// <summary>アニメーションしない場合に即時反映する光彩の状態を返す。通常表示では円を残さない。</summary>
    public static FavoriteStarImmediateVisual ImmediateVisual(bool liked)
        => liked
            ? new FavoriteStarImmediateVisual(0f, new Vector3(1f, 1f, 1f))
            : new FavoriteStarImmediateVisual(0f, new Vector3(0.78f, 0.78f, 1f));

    /// <summary>Composition の中心点に使う座標を、未計測時は 30px の既定サイズで補って返す。</summary>
    public static Vector3 Center(double actualWidth, double actualHeight)
    {
        var width = actualWidth > 0 ? (float)actualWidth : DefaultSize;
        var height = actualHeight > 0 ? (float)actualHeight : DefaultSize;
        return new Vector3(width / 2f, height / 2f, 0f);
    }

    /// <summary>現在の変更で再生するアニメーション種別を返す。</summary>
    public static FavoriteStarAnimationKind AnimationKind(bool animate, bool liked, bool previousLiked)
    {
        if (!animate || liked == previousLiked) return FavoriteStarAnimationKind.None;
        return liked ? FavoriteStarAnimationKind.FadeIn : FavoriteStarAnimationKind.FadeOut;
    }

    /// <summary>指定アニメーション種別に対応する keyframe 設定を返す。</summary>
    public static FavoriteStarAnimationPlan? AnimationPlan(FavoriteStarAnimationKind kind)
        => kind switch
        {
            FavoriteStarAnimationKind.FadeIn => FadeInAnimation(),
            FavoriteStarAnimationKind.FadeOut => FadeOutAnimation(),
            _ => null,
        };

    /// <summary>お気に入り解除アニメーション完了後に残す最終状態を返す。</summary>
    public static FavoriteStarCompletionVisual FadeOutCompletionVisual()
        => new FavoriteStarCompletionVisual(
            StarOpacity: 1f,
            StarScale: new Vector3(1f, 1f, 1f),
            GlowOpacity: 0f,
            GlowScale: new Vector3(0.78f, 0.78f, 1f));

    /// <summary>お気に入り追加時の星と光彩の出現 keyframe を返す。</summary>
    private static FavoriteStarAnimationPlan FadeInAnimation()
        => new FavoriteStarAnimationPlan(
            DurationMilliseconds: 240,
            EaseControlPoint1: new Vector2(0.16f, 1f),
            EaseControlPoint2: new Vector2(0.3f, 1f),
            StarScaleFrames:
            [
                new FavoriteStarVectorFrame(0f, new Vector3(0.9f, 0.9f, 1f), FavoriteStarEaseKind.None),
                new FavoriteStarVectorFrame(0.6f, new Vector3(1.08f, 1.08f, 1f), FavoriteStarEaseKind.Main),
                new FavoriteStarVectorFrame(1f, new Vector3(1f, 1f, 1f), FavoriteStarEaseKind.Main),
            ],
            StarOpacityFrames:
            [
                new FavoriteStarScalarFrame(0f, 0.55f, FavoriteStarEaseKind.None),
                new FavoriteStarScalarFrame(1f, 1f, FavoriteStarEaseKind.Main),
            ],
            GlowOpacityFrames:
            [
                new FavoriteStarScalarFrame(0f, 0f, FavoriteStarEaseKind.None),
                new FavoriteStarScalarFrame(1f, 0f, FavoriteStarEaseKind.Main),
            ],
            GlowScaleFrames:
            [
                new FavoriteStarVectorFrame(0f, new Vector3(0.72f, 0.72f, 1f), FavoriteStarEaseKind.None),
                new FavoriteStarVectorFrame(1f, new Vector3(1f, 1f, 1f), FavoriteStarEaseKind.Main),
            ]);

    /// <summary>お気に入り解除時の星と光彩の退場 keyframe を返す。</summary>
    private static FavoriteStarAnimationPlan FadeOutAnimation()
        => new FavoriteStarAnimationPlan(
            DurationMilliseconds: 200,
            EaseControlPoint1: new Vector2(0.16f, 1f),
            EaseControlPoint2: new Vector2(0.3f, 1f),
            StarScaleFrames:
            [
                new FavoriteStarVectorFrame(0f, new Vector3(1f, 1f, 1f), FavoriteStarEaseKind.None),
                new FavoriteStarVectorFrame(1f, new Vector3(0.92f, 0.92f, 1f), FavoriteStarEaseKind.Main),
            ],
            StarOpacityFrames:
            [
                new FavoriteStarScalarFrame(0f, 1f, FavoriteStarEaseKind.None),
                new FavoriteStarScalarFrame(1f, 0.75f, FavoriteStarEaseKind.Main),
            ],
            GlowOpacityFrames:
            [
                new FavoriteStarScalarFrame(0f, 0f, FavoriteStarEaseKind.None),
                new FavoriteStarScalarFrame(1f, 0f, FavoriteStarEaseKind.Main),
            ],
            GlowScaleFrames:
            [
                new FavoriteStarVectorFrame(0f, new Vector3(1f, 1f, 1f), FavoriteStarEaseKind.None),
                new FavoriteStarVectorFrame(1f, new Vector3(0.82f, 0.82f, 1f), FavoriteStarEaseKind.Main),
            ]);
}

/// <summary>星の Brush 解決に必要なテーマキーとフォールバック。</summary>
internal sealed record FavoriteStarBrushPlan(
    string? FillResourceKey,
    string? StrokeResourceKey,
    FavoriteStarBrushFallback FillFallback,
    FavoriteStarBrushFallback StrokeFallback);

/// <summary>テーマリソースが見つからない場合に使う既定 Brush の種類。</summary>
internal enum FavoriteStarBrushFallback
{
    Transparent,
    Gray,
}

/// <summary>アニメーションなしで反映する光彩の表示状態。</summary>
internal sealed record FavoriteStarImmediateVisual(float GlowOpacity, Vector3 GlowScale);

/// <summary>お気に入り状態変更時に再生するアニメーション種別。</summary>
internal enum FavoriteStarAnimationKind
{
    None,
    FadeIn,
    FadeOut,
}

/// <summary>keyframe に適用する easing 種別。</summary>
internal enum FavoriteStarEaseKind
{
    None,
    Main,
}

/// <summary>Scalar keyframe の位置、値、easing 種別。</summary>
internal sealed record FavoriteStarScalarFrame(float Progress, float Value, FavoriteStarEaseKind Ease);

/// <summary>Vector3 keyframe の位置、値、easing 種別。</summary>
internal sealed record FavoriteStarVectorFrame(float Progress, Vector3 Value, FavoriteStarEaseKind Ease);

/// <summary>星と光彩に同時適用するアニメーション設定。</summary>
internal sealed record FavoriteStarAnimationPlan(
    int DurationMilliseconds,
    Vector2 EaseControlPoint1,
    Vector2 EaseControlPoint2,
    IReadOnlyList<FavoriteStarVectorFrame> StarScaleFrames,
    IReadOnlyList<FavoriteStarScalarFrame> StarOpacityFrames,
    IReadOnlyList<FavoriteStarScalarFrame> GlowOpacityFrames,
    IReadOnlyList<FavoriteStarVectorFrame> GlowScaleFrames);

/// <summary>お気に入り解除アニメーション完了後の明示的な最終状態。</summary>
internal sealed record FavoriteStarCompletionVisual(
    float StarOpacity,
    Vector3 StarScale,
    float GlowOpacity,
    Vector3 GlowScale);
