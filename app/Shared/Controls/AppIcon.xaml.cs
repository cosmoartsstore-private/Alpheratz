using System;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core;
using Alpheratz.Shared.Icons;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Shared.Controls;

/// <summary>
/// SVG パスデータを使ったアイコンコントロール。
/// IconName に AppIcons のキーを指定すると対応する Geometry が描画される。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class AppIcon : UserControl
{
    public static readonly DependencyProperty IconNameProperty =
        DependencyProperty.Register("IconName", typeof(string), typeof(AppIcon),
            new PropertyMetadata(string.Empty, OnNameChanged));

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(AppIcon),
            new PropertyMetadata(16.0));

    public new static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(AppIcon),
            new PropertyMetadata(new SolidColorBrush(Colors.Black)));

    public string IconName
    {
        get => (string)GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public new Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public AppIcon()
    {
        AppLogger.Trace("AppIcon.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AppIcon.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        UpdateGeometry();
        AppLogger.Trace("AppIcon.ctor: exit");
    }

    /// <summary>IconName 変更時に AppIcons から Geometry を取得して Path にセットする。</summary>
    private static void OnNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (d is not AppIcon ctrl) return;
            ctrl.UpdateGeometry();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AppIcon.OnNameChanged: threw: {ex}");
        }
    }

    private void UpdateGeometry()
    {
        var iconName = IconName ?? string.Empty;
        IconPath.Data = string.IsNullOrEmpty(iconName)
            ? null
            : AppIcons.GetGeometry(iconName);
    }
}
