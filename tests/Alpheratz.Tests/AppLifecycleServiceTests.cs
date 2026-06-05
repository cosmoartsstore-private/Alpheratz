using Alpheratz.Features.Bootstrap;

namespace Alpheratz.Tests;

/// <summary>
/// AppLifecycleService の起動フェーズ管理を検証するテスト。
///
/// 起動中の画面や ViewModel は、DB 初期化や初回レイアウトが終わるまで待ってから処理を始める。
/// このサービスの順序判定や待機処理がずれると、未初期化の visual tree や DB へ触れてしまうため、
/// フェーズ前進、巻き戻し無視、待機完了、キャンセルを個別に固定する。
/// </summary>
public sealed class AppLifecycleServiceTests
{
    /// <summary>
    /// advanceTo がフェーズを前進させ、PropertyChanged と PhaseAdvanced を通知することを確認する。
    ///
    /// CurrentPhase は単調に進む前提で使われるため、一度進んだフェーズが戻らないことも同時に検証する。
    /// </summary>
    [Fact]
    public void AdvanceTo_MovesForwardAndIgnoresRollback()
    {
        var service = new AppLifecycleService();
        var propertyChanged = 0;
        var advanced = new List<AppLifecyclePhase>();
        service.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppLifecycleService.CurrentPhase))
                propertyChanged++;
        };
        service.PhaseAdvanced += (_, phase) => advanced.Add(phase);

        service.advanceTo(AppLifecyclePhase.servicesReady);
        service.advanceTo(AppLifecyclePhase.sdkReady);

        Assert.Equal(AppLifecyclePhase.servicesReady, service.CurrentPhase);
        Assert.True(service.isAtLeast(AppLifecyclePhase.sdkReady));
        Assert.False(service.isAtLeast(AppLifecyclePhase.dataReady));
        Assert.Equal(1, propertyChanged);
        Assert.Equal([AppLifecyclePhase.servicesReady], advanced);
    }

    /// <summary>
    /// waitForAsync がすでに到達済みのフェーズでは即完了することを確認する。
    ///
    /// 起動後に遅れて購読するコンポーネントは、過去の通知を受け取れない。
    /// その場合でも CurrentPhase を見て即完了する必要があるため、到達済みフェーズの待機を検証する。
    /// </summary>
    [Fact]
    public async Task WaitForAsync_CompletesImmediatelyWhenPhaseIsAlreadyReached()
    {
        var service = new AppLifecycleService();
        service.advanceTo(AppLifecyclePhase.dataReady);

        await service.waitForAsync(AppLifecyclePhase.servicesReady);

        Assert.Equal(AppLifecyclePhase.dataReady, service.CurrentPhase);
    }

    /// <summary>
    /// waitForAsync が将来フェーズの到達まで待ち、advanceTo 後に完了することを確認する。
    ///
    /// 初期化の途中で UI 側が dataReady や uiReady を待つ経路では、
    /// PhaseAdvanced イベントで待機 Task が解放される必要がある。
    /// </summary>
    [Fact]
    public async Task WaitForAsync_CompletesAfterFuturePhaseAdvances()
    {
        var service = new AppLifecycleService();
        var waitTask = service.waitForAsync(AppLifecyclePhase.uiReady);

        Assert.False(waitTask.IsCompleted);
        service.advanceTo(AppLifecyclePhase.dataReady);
        Assert.False(waitTask.IsCompleted);
        service.advanceTo(AppLifecyclePhase.uiReady);

        await waitTask;
        Assert.True(waitTask.IsCompletedSuccessfully);
    }

    /// <summary>
    /// waitForAsync が CancellationToken のキャンセルで取り消されることを確認する。
    ///
    /// ページ破棄や起動中断時に待機 Task が残ると、後続フェーズ通知で不要な処理が走る。
    /// キャンセル時に購読を外して Task を Canceled にする現在仕様を固定する。
    /// </summary>
    [Fact]
    public async Task WaitForAsync_CancelsWhenTokenIsCanceled()
    {
        var service = new AppLifecycleService();
        using var cts = new CancellationTokenSource();
        var waitTask = service.waitForAsync(AppLifecyclePhase.uiReady, cts.Token);

        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => waitTask);
        service.advanceTo(AppLifecyclePhase.uiReady);
        Assert.Equal(AppLifecyclePhase.uiReady, service.CurrentPhase);
    }
}
