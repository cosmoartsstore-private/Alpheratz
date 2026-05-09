namespace Alpheratz.Features.Bootstrap;

// Ordered phases of the app's startup pipeline. Each value is strictly
// greater than the previous so >= comparisons reflect progress order.
public enum AppLifecyclePhase
{
    booting = 0,
    sdkReady = 1,
    servicesReady = 2,
    dataReady = 3,
    uiReady = 4,
}
