using System;
using Alpheratz.Core;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Shared.Controls;

public sealed partial class EmptyState : UserControl
{
    public EmptyState()
    {
        AppLogger.Trace("EmptyState.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"EmptyState.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("EmptyState.ctor: exit");
    }
}
