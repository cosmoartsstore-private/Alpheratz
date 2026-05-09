using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;

namespace Alpheratz.Features.Bootstrap;

// Tracks the app's startup pipeline phase. Subscribers can react to phase
// transitions or await a particular phase before doing data-bound work.
// Lower-case method names match the existing migration style (see
// ShellViewModel.ToastState).
//
// Tracing convention used here (and to be applied to every new method going
// forward): each non-trivial method emits an `enter` trace with its key
// arguments, an `exit` trace, and one trace per meaningful branch. Pure hot
// readers (CurrentPhase / isAtLeast) deliberately omit tracing because they
// are polled from gating code and would drown out the log.
public sealed class AppLifecycleService : INotifyPropertyChanged
{
    private readonly object gate = new();
    private AppLifecyclePhase currentPhase = AppLifecyclePhase.booting;

    public AppLifecyclePhase CurrentPhase
    {
        get { lock (gate) return currentPhase; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<AppLifecyclePhase>? PhaseAdvanced;

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

    public bool isAtLeast(AppLifecyclePhase phase)
    {
        lock (gate) return currentPhase >= phase;
    }

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

        // Re-check after subscribing to close the race with a transition that
        // fired between the initial check and the event hookup.
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
