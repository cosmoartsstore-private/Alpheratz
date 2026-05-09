using System;
using Microsoft.UI.Dispatching;

namespace Alpheratz.Core;

public static class UiThread
{
    public static DispatcherQueue? Queue { get; set; }

    public static void Run(Action action)
    {
        var dq = Queue;
        if (dq is null || dq.HasThreadAccess)
        {
            action();
        }
        else
        {
            dq.TryEnqueue(() => action());
        }
    }
}
