using System;

namespace Alpheratz.Shared.Controls;

/// <summary>WaveProgressBar の表示幅計算を UI 要素なしで扱う補助ロジック。</summary>
internal static class WaveProgressBarLogic
{
    public static double ProgressRatio(double minimum, double maximum, double value)
    {
        var range = maximum - minimum;
        if (range <= 0) return 0;
        return Math.Clamp((value - minimum) / range, 0, 1);
    }

    public static double FillWidth(double trackWidth, double minimum, double maximum, double value)
    {
        if (trackWidth <= 0) return 0;
        return trackWidth * ProgressRatio(minimum, maximum, value);
    }
}
