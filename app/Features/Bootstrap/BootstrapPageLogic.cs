namespace Alpheratz.Features.Bootstrap;

/// <summary>
/// BootstrapPage の進捗表示に使うフェーズ変換と補間計算。
/// WinUI の Storyboard や DispatcherTimer を使わず、数値だけを決める。
/// </summary>
internal static class BootstrapPageLogic
{
    public const double TrackWidth = 420.0;
    public const int AnimationFrameIntervalMilliseconds = 16;
    public const double ProgressEasingRatio = 0.12;
    public const double ProgressSnapThreshold = 0.3;
    public const int FadeInDurationMilliseconds = 500;
    public const int FadeOutDurationMilliseconds = 350;

    /// <summary>ライフサイクルフェーズをスプラッシュ上の目標パーセントへ変換する。</summary>
    public static double TargetPercent(AppLifecyclePhase phase)
        => phase switch
        {
            AppLifecyclePhase.booting => 5,
            AppLifecyclePhase.sdkReady => 30,
            AppLifecyclePhase.servicesReady => 60,
            AppLifecyclePhase.dataReady => 100,
            AppLifecyclePhase.uiReady => 100,
            _ => 0,
        };

    /// <summary>現在値を目標値へ 1 フレーム分近付け、停止可能かを返す。</summary>
    public static BootstrapProgressStep NextProgress(double current, double target)
    {
        var diff = target - current;
        if (Math.Abs(diff) < ProgressSnapThreshold)
        {
            return new BootstrapProgressStep(target, ShouldStopTimer: true);
        }

        return new BootstrapProgressStep(current + diff * ProgressEasingRatio, ShouldStopTimer: false);
    }

    /// <summary>パーセント値から progress fill と sparkle の X 位置に使う px 値を返す。</summary>
    public static double ProgressPixels(double percent)
        => TrackWidth * (percent / 100.0);
}

/// <summary>進捗補間 1 フレーム分の計算結果。</summary>
internal sealed record BootstrapProgressStep(double CurrentPercent, bool ShouldStopTimer);
