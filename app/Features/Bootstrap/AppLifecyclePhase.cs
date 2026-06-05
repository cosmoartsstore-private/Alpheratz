namespace Alpheratz.Features.Bootstrap;

/// <summary>
/// アプリ起動パイプラインの段階。
/// 数値の大小が進行順を表すため、>= 比較で到達判定できる。
/// </summary>
public enum AppLifecyclePhase
{
    booting = 0,
    sdkReady = 1,
    servicesReady = 2,
    dataReady = 3,
    uiReady = 4,
}
