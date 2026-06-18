using Alpheratz.Shared.Controls;

namespace Alpheratz.Tests;

/// <summary>WaveProgressBar の幅計算を検証するテスト。</summary>
public sealed class WaveProgressBarLogicTests
{
    [Fact]
    public void ProgressRatio_ClampsValueToRange()
    {
        Assert.Equal(0, WaveProgressBarLogic.ProgressRatio(0, 100, -10));
        Assert.Equal(0.25, WaveProgressBarLogic.ProgressRatio(0, 100, 25));
        Assert.Equal(1, WaveProgressBarLogic.ProgressRatio(0, 100, 120));
        Assert.Equal(0, WaveProgressBarLogic.ProgressRatio(10, 10, 10));
    }

    [Fact]
    public void FillWidth_UsesTrackWidthAndProgressRatio()
    {
        Assert.Equal(0, WaveProgressBarLogic.FillWidth(0, 0, 100, 50));
        Assert.Equal(80, WaveProgressBarLogic.FillWidth(320, 0, 100, 25));
        Assert.Equal(320, WaveProgressBarLogic.FillWidth(320, 0, 100, 100));
    }
}
