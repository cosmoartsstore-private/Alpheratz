using System;
using System.Collections.Generic;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Features.Gallery.Controls;

public sealed partial class MonthNav : UserControl
{
    private IReadOnlyList<GalleryMonthGroup> groups = [];
    private int activeIndex;

    public Action<GalleryMonthGroup>? OnJumpToMonth { get; set; }

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
        AppLogger.Trace("MonthNav.ctor: exit");
    }

    public void SetGroups(IReadOnlyList<GalleryMonthGroup> next)
    {
        AppLogger.Trace($"MonthNav.SetGroups: enter count={next.Count}");
        groups = next;
        Rebuild();
        AppLogger.Trace("MonthNav.SetGroups: exit");
    }

    public void SetActiveIndex(int index)
    {
        if (activeIndex == index) return;
        activeIndex = index;
        Rebuild();
    }

    private void Rebuild()
    {
        try
        {
            MonthList.Children.Clear();
            for (var i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                var isYearStart = i == 0 || groups[i - 1].Year != g.Year;
                var isActive = i == activeIndex;

                if (isYearStart)
                {
                    MonthList.Children.Add(BuildYearHeader(g.Year));
                }

                MonthList.Children.Add(BuildMonthButton(g, isActive));
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"MonthNav.Rebuild: threw: {ex}");
        }
    }

    private static Border BuildYearHeader(int year)
    {
        return new Border
        {
            Padding = new Thickness(8, 10, 8, 2),
            Child = new TextBlock
            {
                Text = year.ToString(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
                Foreground = (Brush)Application.Current.Resources["ATextFaint"],
                HorizontalAlignment = HorizontalAlignment.Center,
            }
        };
    }

    private Button BuildMonthButton(GalleryMonthGroup g, bool isActive)
    {
        var label = new TextBlock
        {
            Text = $"{g.Month}月",
            FontSize = 12,
            FontWeight = isActive
                ? Microsoft.UI.Text.FontWeights.ExtraBold
                : Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources[isActive ? "APrimary" : "ATextDim"],
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var btn = new Button
        {
            MinHeight = 30,
            MinWidth = 48,
            Padding = new Thickness(4, 4, 4, 4),
            Margin = new Thickness(4, 1, 4, 1),
            Background = isActive
                ? (Brush)Application.Current.Resources["APrimarySoft"]
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
