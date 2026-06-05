using Alpheratz.Features.Shell.Controls;
using Microsoft.UI.Xaml;

namespace Alpheratz.Tests;

/// <summary>
/// ShellStage のレイヤ表示遷移と close 完了判定を検証するテスト。
///
/// ShellStage 本体は WinUI のレイヤ要素と Composition アニメーションを直接操作する。
/// ここでは要求 Visibility と現在 Visibility から open/close/直接設定のどれを選ぶか、
/// さらに close アニメ完了時に古い cleanup を捨てる version 判定だけを固定する。
/// </summary>
public sealed class ShellStageLayerLogicTests
{
    /// <summary>
    /// 中位/最上位モーダルで共通の表示遷移判定を確認する。
    ///
    /// Visible 要求は現在値に関係なく open 扱いにして、進行中の close を version bump で無効化する。
    /// Visible から非表示へ変わる場合だけ close アニメを走らせ、それ以外は要求値を直接反映する。
    /// </summary>
    [Fact]
    public void ModalTransition_ReturnsOpenCloseOrDirectSetFromRequestedAndCurrentVisibility()
    {
        Assert.Equal(
            ShellStageLayerTransition.Open,
            ShellStageLayerLogic.ModalTransition(Visibility.Visible, Visibility.Collapsed));
        Assert.Equal(
            ShellStageLayerTransition.Open,
            ShellStageLayerLogic.ModalTransition(Visibility.Visible, Visibility.Visible));
        Assert.Equal(
            ShellStageLayerTransition.Close,
            ShellStageLayerLogic.ModalTransition(Visibility.Collapsed, Visibility.Visible));
        Assert.Equal(
            ShellStageLayerTransition.SetDirectly,
            ShellStageLayerLogic.ModalTransition(Visibility.Collapsed, Visibility.Collapsed));
    }

    /// <summary>
    /// スキャンオーバーレイの表示遷移判定を確認する。
    ///
    /// オーバーレイはモーダルと違い、すでに Visible の状態へ Visible を再設定しても
    /// fade-in を重ねない。Visible への変化と Visible から Collapsed への変化だけを
    /// アニメーション対象にする。
    /// </summary>
    [Fact]
    public void OverlayTransition_AnimatesOnlyVisibilityChanges()
    {
        Assert.Equal(
            ShellStageLayerTransition.Open,
            ShellStageLayerLogic.OverlayTransition(Visibility.Visible, Visibility.Collapsed));
        Assert.Equal(
            ShellStageLayerTransition.SetDirectly,
            ShellStageLayerLogic.OverlayTransition(Visibility.Visible, Visibility.Visible));
        Assert.Equal(
            ShellStageLayerTransition.Close,
            ShellStageLayerLogic.OverlayTransition(Visibility.Collapsed, Visibility.Visible));
        Assert.Equal(
            ShellStageLayerTransition.SetDirectly,
            ShellStageLayerLogic.OverlayTransition(Visibility.Collapsed, Visibility.Collapsed));
    }

    /// <summary>
    /// レイヤ version が操作ごとに進み、close 完了時は同じ version の場合だけ cleanup できることを確認する。
    ///
    /// Close アニメ中に Open が入ると version が進む。
    /// 古い Close の onCompleted は遅れて呼ばれても version 不一致になり、新しいコンテンツを消さない。
    /// </summary>
    [Fact]
    public void VersionHelpers_AllowCleanupOnlyForLatestClose()
    {
        var openVersion = ShellStageLayerLogic.NextVersion(0);
        var closeVersion = ShellStageLayerLogic.NextVersion(openVersion);
        var reopenVersion = ShellStageLayerLogic.NextVersion(closeVersion);

        Assert.Equal(1, openVersion);
        Assert.Equal(2, closeVersion);
        Assert.True(ShellStageLayerLogic.CanCompleteClose(closeVersion, closeVersion));
        Assert.False(ShellStageLayerLogic.CanCompleteClose(reopenVersion, closeVersion));
    }

    /// <summary>
    /// ShellStage のアニメーション時間がレイヤ種別ごとの固定値として公開されていることを確認する。
    ///
    /// code-behind はこれらの値を FadeIn/FadeOut/ScaleIn/ScaleOut に渡すだけにし、
    /// 数値の変更は helper とテストの差分として見えるようにしている。
    /// </summary>
    [Fact]
    public void AnimationDurations_ReturnCurrentLayerTimingValues()
    {
        Assert.Equal(250, ShellStageLayerLogic.ModalFadeInDurationMilliseconds);
        Assert.Equal(350, ShellStageLayerLogic.ModalScaleInDurationMilliseconds);
        Assert.Equal(200, ShellStageLayerLogic.ModalFadeOutDurationMilliseconds);
        Assert.Equal(200, ShellStageLayerLogic.ModalScaleOutDurationMilliseconds);
        Assert.Equal(300, ShellStageLayerLogic.OverlayFadeInDurationMilliseconds);
        Assert.Equal(250, ShellStageLayerLogic.OverlayFadeOutDurationMilliseconds);
    }
}
