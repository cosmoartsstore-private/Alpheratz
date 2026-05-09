using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Models;

namespace Alpheratz.Shared.Services;

public sealed class ToastService
{
    // TS: toasts
    public UiObservableCollection<ToastMessage> toasts { get; } = [];

    // TS: addToast(msg, type = "info", duration = 3000)
    public void addToast(string msg, ToastType type = ToastType.info, int duration = 3000)
    {
        AppLogger.Trace($"ToastService.addToast: enter type={type} duration={duration} msg={msg}");
        try
        {
            var id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var toast = new ToastMessage(id, msg, type);
            toasts.Add(toast);

            _ = Task.Run(async () =>
            {
                AppLogger.Trace($"ToastService.addToast.removalTask: enter id={id}");
                try
                {
                    await Task.Delay(duration).ConfigureAwait(false);
                    _ = App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                    {
                        try { toasts.Remove(toast); }
                        catch (Exception ex) { AppLogger.Error($"ToastService.addToast.remove: threw: {ex}"); }
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ToastService.addToast.removalTask: threw: {ex}");
                }
                AppLogger.Trace($"ToastService.addToast.removalTask: exit id={id}");
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ToastService.addToast: threw: {ex}");
        }
        AppLogger.Trace("ToastService.addToast: exit");
    }
}
