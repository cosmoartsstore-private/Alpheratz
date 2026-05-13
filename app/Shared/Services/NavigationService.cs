using Alpheratz.Core;
using Alpheratz.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Shared.Services;

/// <summary>
/// 現在表示中のメインスクリーン (Gallery / TagMaster / Template / Settings) を保持するサービス。
/// 現状は ShellPage のみが使用しているが、サービス化することでテスト・別 UI からの遷移にも対応できる。
/// </summary>
public partial class NavigationService : UiThreadSafeObservableObject
{
    [ObservableProperty]
    private MainScreen activeMainScreen = MainScreen.gallery;

    /// <summary>アクティブスクリーンを切り替える。LeftRail のハイライト更新に使う。</summary>
    public void setActiveMainScreen(MainScreen screen)
    {
        AppLogger.Trace($"NavigationService.setActiveMainScreen: enter screen={screen}");
        ActiveMainScreen = screen;
        AppLogger.Trace("NavigationService.setActiveMainScreen: exit");
    }
}
