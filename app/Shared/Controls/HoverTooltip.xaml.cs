using System;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Shared.Controls;

public sealed partial class HoverTooltip : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(HoverTooltip), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty DisabledProperty =
        DependencyProperty.Register(nameof(Disabled), typeof(bool), typeof(HoverTooltip), new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register(nameof(Child), typeof(object), typeof(HoverTooltip), new PropertyMetadata(null));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool Disabled { get => (bool)GetValue(DisabledProperty); set => SetValue(DisabledProperty, value); }
    public object? Child { get => GetValue(ChildProperty); set => SetValue(ChildProperty, value); }

    public HoverTooltip()
    {
        AppLogger.Trace("HoverTooltip.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"HoverTooltip.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        Apply();
        AppLogger.Trace("HoverTooltip.ctor: exit");
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (d is HoverTooltip control) control.Apply();
        }
        catch (Exception ex) { AppLogger.Error($"HoverTooltip.OnChanged: threw: {ex}"); }
    }

    private void Apply()
    {
        // Hot path during binding; trace only on errors.
        try
        {
            ToolTipService.SetToolTip(Presenter, Disabled ? null : Label);
        }
        catch (Exception ex) { AppLogger.Error($"HoverTooltip.Apply: threw: {ex}"); }
    }
}
