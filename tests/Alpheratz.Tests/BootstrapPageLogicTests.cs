using Alpheratz.Features.Bootstrap;

namespace Alpheratz.Tests;

/// <summary>
/// BootstrapPage から分離した起動スプラッシュの進捗計算を検証するテスト。
///
/// BootstrapPage 本体は DispatcherTimer と Storyboard を持つため、通常の単体テストでは
/// WinUI の実行環境に強く依存する。
/// ここではフェーズから目標パーセントへの変換、1 フレーム分の指数補間、表示 px への変換を
/// 数値ロジックとして固定し、起動時の見え方が不用意に変わらないようにする。
/// </summary>
public sealed class BootstrapPageLogicTests
{
    /// <summary>
    /// 起動ライフサイクルフェーズがスプラッシュ上の目標パーセントへ変換されることを確認する。
    ///
    /// 値は実処理の所要時間ではなく、ユーザーに見える進捗停滞を避けるための表示用マイルストーン。
    /// uiReady は dataReady と同じ 100% とし、そこから先の画面切替は Shell 側のアニメーションに任せる。
    /// </summary>
    [Fact]
    public void TargetPercent_ReturnsDisplayMilestonesForLifecyclePhases()
    {
        Assert.Equal(5, BootstrapPageLogic.TargetPercent(AppLifecyclePhase.booting));
        Assert.Equal(30, BootstrapPageLogic.TargetPercent(AppLifecyclePhase.sdkReady));
        Assert.Equal(60, BootstrapPageLogic.TargetPercent(AppLifecyclePhase.servicesReady));
        Assert.Equal(100, BootstrapPageLogic.TargetPercent(AppLifecyclePhase.dataReady));
        Assert.Equal(100, BootstrapPageLogic.TargetPercent(AppLifecyclePhase.uiReady));
        Assert.Equal(0, BootstrapPageLogic.TargetPercent((AppLifecyclePhase)999));
    }

    /// <summary>
    /// 進捗補間が差分の 12% だけ目標に近付き、十分近い場合は目標値へ吸着することを確認する。
    ///
    /// 12% の指数補間は 60fps 前提で約 0.5 秒以内に見た目上ほぼ到達する値。
    /// 差分 0.3% 未満では浮動小数の漸近でタイマーが回り続けないよう、目標値に丸めて停止させる。
    /// </summary>
    [Fact]
    public void NextProgress_EasesByConfiguredRatioAndSnapsNearTarget()
    {
        Assert.Equal(
            new BootstrapProgressStep(CurrentPercent: 12, ShouldStopTimer: false),
            BootstrapPageLogic.NextProgress(current: 0, target: 100));

        Assert.Equal(
            new BootstrapProgressStep(CurrentPercent: 100, ShouldStopTimer: true),
            BootstrapPageLogic.NextProgress(current: 99.8, target: 100));

        Assert.Equal(
            new BootstrapProgressStep(CurrentPercent: 30, ShouldStopTimer: true),
            BootstrapPageLogic.NextProgress(current: 30.2, target: 30));
    }

    /// <summary>
    /// パーセント値から progress fill 幅と sparkle の X 位置に使う px 値を計算することを確認する。
    ///
    /// XAML 上の track 幅は 420px 固定なので、50% では 210px、100% では track 右端の 420px になる。
    /// BootstrapPage はこの同じ値を ProgressFill.Width と StarTranslate.X の両方へ適用する。
    /// </summary>
    [Fact]
    public void ProgressPixels_UsesFixedTrackWidth()
    {
        Assert.Equal(0, BootstrapPageLogic.ProgressPixels(0));
        Assert.Equal(210, BootstrapPageLogic.ProgressPixels(50));
        Assert.Equal(BootstrapPageLogic.TrackWidth, BootstrapPageLogic.ProgressPixels(100));
        Assert.Equal(16, BootstrapPageLogic.AnimationFrameIntervalMilliseconds);
        Assert.Equal(500, BootstrapPageLogic.FadeInDurationMilliseconds);
        Assert.Equal(350, BootstrapPageLogic.FadeOutDurationMilliseconds);
    }
}
