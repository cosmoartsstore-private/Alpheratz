using System;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Shared.Controls;

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
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
