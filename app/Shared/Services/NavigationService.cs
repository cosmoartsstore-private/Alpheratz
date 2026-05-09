using Alpheratz.Core;
using Alpheratz.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Shared.Services;

public partial class NavigationService : UiThreadSafeObservableObject
{
    // TS: activeMainScreen
    [ObservableProperty]
    private MainScreen activeMainScreen = MainScreen.gallery;

    public void setActiveMainScreen(MainScreen screen)
    {
        AppLogger.Trace($"NavigationService.setActiveMainScreen: enter screen={screen}");
        ActiveMainScreen = screen;
        AppLogger.Trace("NavigationService.setActiveMainScreen: exit");
    }
}
