using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;

namespace Alpheratz.Features.Bootstrap;

/// <summary>
/// 起動パイプラインの現在フェーズを管理するサービス。
/// 画面や ViewModel は必要なフェーズまで待ってから、データバインドを開始できる。
/// </summary>
public sealed class AppLifecycleService : INotifyPropertyChanged
{
    private readonly object gate = new();
    private AppLifecyclePhase currentPhase = AppLifecyclePhase.booting;

    /// <summary>現在到達している起動フェーズ。</summary>
    public AppLifecyclePhase CurrentPhase
    {
        get { lock (gate) return currentPhase; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<AppLifecyclePhase>? PhaseAdvanced;

    /// <summary>起動フェーズを前進させ、購読者へ通知する。過去フェーズへの巻き戻しは無視する。</summary>
    public void advanceTo(AppLifecyclePhase next)
    {
        AppLogger.Trace($"AppLifecycleService.advanceTo: enter next={next}");
        AppLifecyclePhase previous;
        lock (gate)
        {
            if (next <= currentPhase)
            {
                AppLogger.Trace($"AppLifecycleService.advanceTo: skip (current={currentPhase}, next={next})");
                return;
            }
            previous = currentPhase;
            currentPhase = next;
        }

        AppLogger.Trace($"AppLifecycleService.advanceTo: transition {previous} -> {next}");

        try
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPhase)));
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AppLifecycleService.advanceTo: PropertyChanged subscriber threw: {ex}");
        }

        try
        {
            PhaseAdvanced?.Invoke(this, next);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AppLifecycleService.advanceTo: PhaseAdvanced subscriber threw: {ex}");
        }

        AppLogger.Trace($"AppLifecycleService.advanceTo: exit next={next}");
    }

    /// <summary>現在フェーズが指定フェーズ以上かを返す。</summary>
    public bool isAtLeast(AppLifecyclePhase phase)
    {
        lock (gate) return currentPhase >= phase;
    }

    /// <summary>指定フェーズへ到達するまで待つ。すでに到達済みなら即完了する。</summary>
    public Task waitForAsync(AppLifecyclePhase phase, CancellationToken ct = default)
    {
        AppLogger.Trace($"AppLifecycleService.waitForAsync: enter phase={phase}");

        if (isAtLeast(phase))
        {
            AppLogger.Trace($"AppLifecycleService.waitForAsync: already-satisfied phase={phase}");
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<AppLifecyclePhase>? handler = null;
        handler = (_, p) =>
        {
            if (p < phase) return;
            AppLogger.Trace($"AppLifecycleService.waitForAsync: resolved phase={phase} via {p}");
            PhaseAdvanced -= handler!;
            tcs.TrySetResult();
        };
        PhaseAdvanced += handler;

        if (ct.CanBeCanceled)
        {
            ct.Register(() =>
            {
                AppLogger.Trace($"AppLifecycleService.waitForAsync: cancelled phase={phase}");
                PhaseAdvanced -= handler;
                tcs.TrySetCanceled(ct);
            });
        }

        // 初回判定とイベント購読の間にフェーズが進む可能性があるため、購読後に再確認する。
        if (isAtLeast(phase))
        {
            AppLogger.Trace($"AppLifecycleService.waitForAsync: race-resolved phase={phase}");
            PhaseAdvanced -= handler;
            tcs.TrySetResult();
        }

        AppLogger.Trace($"AppLifecycleService.waitForAsync: pending phase={phase}");
        return tcs.Task;
    }
}
