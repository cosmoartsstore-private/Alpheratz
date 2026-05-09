using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Alpheratz.Shared.Controls;

public class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 8;
    public double VerticalSpacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        var x = 0.0;
        var y = 0.0;
        var rowHeight = 0.0;
        var maxWidth = 0.0;

        foreach (UIElement child in Children)
        {
            child.Measure(new Size(availableSize.Width, availableSize.Height));
            var desired = child.DesiredSize;

            if (x > 0 && x + desired.Width > availableSize.Width)
            {
                maxWidth = Math.Max(maxWidth, x - HorizontalSpacing);
                y += rowHeight + VerticalSpacing;
                x = 0;
                rowHeight = 0;
            }

            x += desired.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, desired.Height);
        }

        maxWidth = Math.Max(maxWidth, x > 0 ? x - HorizontalSpacing : 0);
        return new Size(maxWidth, y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var y = 0.0;
        var rowHeight = 0.0;

        foreach (UIElement child in Children)
        {
            var desired = child.DesiredSize;

            if (x > 0 && x + desired.Width > finalSize.Width)
            {
                y += rowHeight + VerticalSpacing;
                x = 0;
                rowHeight = 0;
            }

            child.Arrange(new Rect(x, y, desired.Width, desired.Height));
            x += desired.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, desired.Height);
        }

        return new Size(finalSize.Width, y + rowHeight);
    }
}
