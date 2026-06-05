using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using Alpheratz.Core;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Features.Gallery.Controls;

/// <summary>ギャラリー右端に表示する年月ジャンプ用の縦ナビゲーション。</summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class MonthNav : UserControl
{
    private IReadOnlyList<GalleryMonthGroup> groups = [];
    private int activeIndex;

    public Action<GalleryMonthGroup>? OnJumpToMonth { get; set; }

    // 月ナビゲーションを初期化し、テーマ変更時に動的ボタンを再描画する。
    public MonthNav()
    {
        AppLogger.Trace("MonthNav.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"MonthNav.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        ActualThemeChanged += OnActualThemeChanged;
        Unloaded += (_, _) => ActualThemeChanged -= OnActualThemeChanged;
        AppLogger.Trace("MonthNav.ctor: exit");
    }

    /// <summary>テーマ切替時に code-behind で構築した月ボタンの色を更新する。</summary>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        try { Rebuild(); }
        catch (Exception ex) { AppLogger.Error($"MonthNav.OnActualThemeChanged: {ex}"); }
    }

    // 表示対象の月グループを差し替え、月ボタンを再構築する。
    public void SetGroups(IReadOnlyList<GalleryMonthGroup> next)
    {
        AppLogger.Trace($"MonthNav.SetGroups: enter count={next.Count}");
        groups = next;
        Rebuild();
        AppLogger.Trace("MonthNav.SetGroups: exit");
    }

    // 現在表示中の月インデックスを反映し、アクティブ表示を更新する。
    public void SetActiveIndex(int index)
    {
        if (activeIndex == index) return;
        activeIndex = index;
        Rebuild();
    }

    // 年見出しと月ボタンを現在のグループ一覧から作り直す。
    private void Rebuild()
    {
        try
        {
            MonthList.Children.Clear();
            foreach (var item in MonthNavLogic.BuildRenderItems(groups, activeIndex))
            {
                if (item.Kind == MonthNavRenderKind.YearHeader && item.Year is { } year)
                {
                    MonthList.Children.Add(BuildYearHeader(year));
                }
                else if (item.Group is { } group && item.Button is { } display)
                {
                    MonthList.Children.Add(BuildMonthButton(group, display));
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"MonthNav.Rebuild: threw: {ex}");
        }
    }

    // 年の切れ目に表示する見出し要素を作る。
    private Border BuildYearHeader(int year)
    {
        return new Border
        {
            Padding = new Thickness(8, 10, 8, 2),
            Child = new TextBlock
            {
                Text = year.ToString(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
                Foreground = ThemeHelper.Brush(this, MonthNavLogic.YearHeaderForegroundKey),
                HorizontalAlignment = HorizontalAlignment.Center,
            }
        };
    }

    // 月ジャンプ用ボタンを作り、アクティブ月には強調色を付ける。
    private Button BuildMonthButton(GalleryMonthGroup g, MonthNavButtonDisplay display)
    {
        var label = new TextBlock
        {
            Text = display.Label,
            FontSize = 12,
            FontWeight = display.FontWeight == MonthNavFontWeight.ExtraBold
                ? Microsoft.UI.Text.FontWeights.ExtraBold
                : Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeHelper.Brush(this, display.ForegroundKey),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var btn = new Button
        {
            MinHeight = 30,
            MinWidth = 48,
            Padding = new Thickness(4, 4, 4, 4),
            Margin = new Thickness(4, 1, 4, 1),
            Background = display.BackgroundKey is not null
                ? ThemeHelper.Brush(this, display.BackgroundKey)
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = label,
            Tag = g,
        };
        btn.Click += MonthButton_Click;
        return btn;
    }

    // 月ボタンの Tag から対象グループを取り出し、ジャンプ要求を通知する。
    private void MonthButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.Tag is GalleryMonthGroup group)
            {
                OnJumpToMonth?.Invoke(group);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"MonthNav.MonthButton_Click: threw: {ex}");
        }
    }
}
