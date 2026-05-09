using System;
using Alpheratz.Core;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Shared.Controls;

public sealed partial class ToastHost : UserControl
{
    public ToastHost()
    {
        AppLogger.Trace("ToastHost.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ToastHost.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("ToastHost.ctor: exit");
    }
}
