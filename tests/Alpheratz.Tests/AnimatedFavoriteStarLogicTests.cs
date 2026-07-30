using System.Numerics;
using Alpheratz.Shared.Controls;

namespace Alpheratz.Tests;

/// <summary>
/// AnimatedFavoriteStar から分離した状態表示とアニメーション定義を検証するテスト。
///
/// AnimatedFavoriteStar 本体は WinUI の Brush、DependencyProperty、Composition Visual を直接操作するため、
/// 通常の単体テストでは生成しない。ここでは UI に依存しない helper が返すテーマキー、アクセシブル名、
/// hit-test 判定、アニメーション keyframe を固定し、code-behind がそれらを適用するだけで済む状態を保つ。
/// </summary>
public sealed class AnimatedFavoriteStarLogicTests
{
    /// <summary>
    /// Liked 状態から UI Automation 名と Brush 解決方針が選ばれることを確認する。
    ///
    /// liked では星の塗りと線の両方にお気に入り色を使う。
    /// unliked では塗りを透明にし、線だけ薄いテキスト色を使うため、FillResourceKey は null になる。
    /// どちらの場合もテーマ辞書にキーがない起動直後に備えて、塗りは透明、線は gray のフォールバックを保持する。
    /// </summary>
    [Fact]
    public void TextAndBrushPlan_ReturnLikedAndUnlikedDisplayValues()
    {
        Assert.Equal("お気に入りに追加", AnimatedFavoriteStarLogic.AutomationName(false));
        Assert.Equal("お気に入りから削除", AnimatedFavoriteStarLogic.AutomationName(true));

        Assert.Equal(
            new FavoriteStarBrushPlan(
                AnimatedFavoriteStarLogic.FavoriteBrushKey,
                AnimatedFavoriteStarLogic.FavoriteBrushKey,
                FavoriteStarBrushFallback.Transparent,
                FavoriteStarBrushFallback.Gray),
            AnimatedFavoriteStarLogic.BrushPlan(true));

        Assert.Equal(
            new FavoriteStarBrushPlan(
                null,
                AnimatedFavoriteStarLogic.FaintTextBrushKey,
                FavoriteStarBrushFallback.Transparent,
                FavoriteStarBrushFallback.Gray),
            AnimatedFavoriteStarLogic.BrushPlan(false));
    }

    /// <summary>
    /// Interactive はクリックコールバック実行と hit-test 可否の両方を同じ規則で制御することを確認する。
    ///
    /// PhotoModal では表示専用の星として使うため、Interactive=false のときは Button がクリック対象にならず、
    /// 何らかの理由で click handler が呼ばれても外部コールバックを実行しない。
    /// </summary>
    [Fact]
    public void InteractiveHelpers_ReturnSameGateForHitTestAndCallback()
    {
        Assert.False(AnimatedFavoriteStarLogic.HitTestVisible(false));
        Assert.False(AnimatedFavoriteStarLogic.ShouldInvokeClick(false));
        Assert.True(AnimatedFavoriteStarLogic.HitTestVisible(true));
        Assert.True(AnimatedFavoriteStarLogic.ShouldInvokeClick(true));
    }

    /// <summary>
    /// アニメーションなしで反映する光彩状態と中心点フォールバックを確認する。
    ///
    /// 初期表示やテーマ変更時はアニメーションを走らせず、光彩は非表示のまま現在状態へ即時同期する。
    /// Liked 状態も星本体の塗りで示し、背景に丸い装飾を残さない。
    /// ActualWidth/ActualHeight が 0 の初期計測前でも Composition の CenterPoint が左上に寄らないよう、
    /// 30px の既定サイズから中央座標を求める。
    /// </summary>
    [Fact]
    public void ImmediateVisualAndCenter_ReturnStableNonAnimatedValues()
    {
        Assert.Equal(new FavoriteStarImmediateVisual(0f, new Vector3(0.78f, 0.78f, 1f)),
            AnimatedFavoriteStarLogic.ImmediateVisual(false));
        Assert.Equal(new FavoriteStarImmediateVisual(0f, new Vector3(1f, 1f, 1f)),
            AnimatedFavoriteStarLogic.ImmediateVisual(true));

        Assert.Equal(new Vector3(15f, 15f, 0f), AnimatedFavoriteStarLogic.Center(0, 0));
        Assert.Equal(new Vector3(20f, 10f, 0f), AnimatedFavoriteStarLogic.Center(40, 20));
    }

    /// <summary>
    /// animate=false または状態未変更ではアニメーションしないことを確認する。
    ///
    /// 初期 Loaded とテーマ変更では現在状態への同期だけを行い、前回値と同じ Liked 変更でも
    /// 不要な Composition animation を開始しない。状態が false から true へ変わると FadeIn、
    /// true から false へ変わると FadeOut になる。
    /// </summary>
    [Fact]
    public void AnimationKind_ReturnsOnlyActualStateTransitions()
    {
        Assert.Equal(FavoriteStarAnimationKind.None, AnimatedFavoriteStarLogic.AnimationKind(false, liked: true, previousLiked: false));
        Assert.Equal(FavoriteStarAnimationKind.None, AnimatedFavoriteStarLogic.AnimationKind(true, liked: false, previousLiked: false));
        Assert.Equal(FavoriteStarAnimationKind.FadeIn, AnimatedFavoriteStarLogic.AnimationKind(true, liked: true, previousLiked: false));
        Assert.Equal(FavoriteStarAnimationKind.FadeOut, AnimatedFavoriteStarLogic.AnimationKind(true, liked: false, previousLiked: true));
        Assert.Null(AnimatedFavoriteStarLogic.AnimationPlan(FavoriteStarAnimationKind.None));
    }

    /// <summary>
    /// お気に入り追加時の keyframe が既存の弾む表示を表していることを確認する。
    ///
    /// FadeIn は 240ms で星を 0.9 倍から 1.08 倍へ一度膨らませ、最後に 1.0 倍へ戻す。
    /// 同時に星の opacity を濃くする。背面の光彩は通常表示で円が残らないよう透明のままにする。
    /// これらの数値は見た目のタイミングを決める仕様なので、UI テストではなく plan の値として固定する。
    /// </summary>
    [Fact]
    public void FadeInPlan_ReturnsBounceAndGlowKeyframes()
    {
        var plan = Assert.IsType<FavoriteStarAnimationPlan>(
            AnimatedFavoriteStarLogic.AnimationPlan(FavoriteStarAnimationKind.FadeIn));

        Assert.Equal(240, plan.DurationMilliseconds);
        Assert.Equal(new Vector2(0.16f, 1f), plan.EaseControlPoint1);
        Assert.Equal(new Vector2(0.3f, 1f), plan.EaseControlPoint2);

        Assert.Collection(
            plan.StarScaleFrames,
            frame => Assert.Equal(new FavoriteStarVectorFrame(0f, new Vector3(0.9f, 0.9f, 1f), FavoriteStarEaseKind.None), frame),
            frame => Assert.Equal(new FavoriteStarVectorFrame(0.6f, new Vector3(1.08f, 1.08f, 1f), FavoriteStarEaseKind.Main), frame),
            frame => Assert.Equal(new FavoriteStarVectorFrame(1f, new Vector3(1f, 1f, 1f), FavoriteStarEaseKind.Main), frame));
        Assert.Collection(
            plan.StarOpacityFrames,
            frame => Assert.Equal(new FavoriteStarScalarFrame(0f, 0.55f, FavoriteStarEaseKind.None), frame),
            frame => Assert.Equal(new FavoriteStarScalarFrame(1f, 1f, FavoriteStarEaseKind.Main), frame));
        Assert.Collection(
            plan.GlowOpacityFrames,
            frame => Assert.Equal(new FavoriteStarScalarFrame(0f, 0f, FavoriteStarEaseKind.None), frame),
            frame => Assert.Equal(new FavoriteStarScalarFrame(1f, 0f, FavoriteStarEaseKind.Main), frame));
        Assert.Collection(
            plan.GlowScaleFrames,
            frame => Assert.Equal(new FavoriteStarVectorFrame(0f, new Vector3(0.72f, 0.72f, 1f), FavoriteStarEaseKind.None), frame),
            frame => Assert.Equal(new FavoriteStarVectorFrame(1f, new Vector3(1f, 1f, 1f), FavoriteStarEaseKind.Main), frame));
    }

    /// <summary>
    /// お気に入り解除時の keyframe と完了後の明示状態を確認する。
    ///
    /// FadeOut は 200ms で星を少し薄く小さくする。光彩は透明のまま縮める。
    /// Composition animation の終了値だけに任せると次回表示時に星側の縮小が残るため、
    /// 完了後は星を通常 opacity/scale に戻し、光彩だけを非表示の縮小状態へ固定する。
    /// </summary>
    [Fact]
    public void FadeOutPlan_ReturnsShrinkKeyframesAndCompletionVisual()
    {
        var plan = Assert.IsType<FavoriteStarAnimationPlan>(
            AnimatedFavoriteStarLogic.AnimationPlan(FavoriteStarAnimationKind.FadeOut));

        Assert.Equal(200, plan.DurationMilliseconds);
        Assert.Collection(
            plan.StarScaleFrames,
            frame => Assert.Equal(new FavoriteStarVectorFrame(0f, new Vector3(1f, 1f, 1f), FavoriteStarEaseKind.None), frame),
            frame => Assert.Equal(new FavoriteStarVectorFrame(1f, new Vector3(0.92f, 0.92f, 1f), FavoriteStarEaseKind.Main), frame));
        Assert.Collection(
            plan.StarOpacityFrames,
            frame => Assert.Equal(new FavoriteStarScalarFrame(0f, 1f, FavoriteStarEaseKind.None), frame),
            frame => Assert.Equal(new FavoriteStarScalarFrame(1f, 0.75f, FavoriteStarEaseKind.Main), frame));
        Assert.Collection(
            plan.GlowOpacityFrames,
            frame => Assert.Equal(new FavoriteStarScalarFrame(0f, 0f, FavoriteStarEaseKind.None), frame),
            frame => Assert.Equal(new FavoriteStarScalarFrame(1f, 0f, FavoriteStarEaseKind.Main), frame));
        Assert.Collection(
            plan.GlowScaleFrames,
            frame => Assert.Equal(new FavoriteStarVectorFrame(0f, new Vector3(1f, 1f, 1f), FavoriteStarEaseKind.None), frame),
            frame => Assert.Equal(new FavoriteStarVectorFrame(1f, new Vector3(0.82f, 0.82f, 1f), FavoriteStarEaseKind.Main), frame));

        Assert.Equal(
            new FavoriteStarCompletionVisual(
                StarOpacity: 1f,
                StarScale: new Vector3(1f, 1f, 1f),
                GlowOpacity: 0f,
                GlowScale: new Vector3(0.78f, 0.78f, 1f)),
            AnimatedFavoriteStarLogic.FadeOutCompletionVisual());
    }
}
