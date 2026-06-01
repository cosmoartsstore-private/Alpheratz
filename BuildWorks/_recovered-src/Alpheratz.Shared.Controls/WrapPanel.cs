using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Alpheratz.Shared.Controls;

public class WrapPanel : Panel
{
	public double HorizontalSpacing { get; set; } = 8.0;

	public double VerticalSpacing { get; set; } = 8.0;

	protected override Size MeasureOverride(Size availableSize)
	{
		double num = 0.0;
		double num2 = 0.0;
		double num3 = 0.0;
		double val = 0.0;
		foreach (UIElement child in base.Children)
		{
			child.Measure(new Size(availableSize.Width, availableSize.Height));
			Size desiredSize = child.DesiredSize;
			if (num > 0.0 && num + desiredSize.Width > availableSize.Width)
			{
				val = Math.Max(val, num - HorizontalSpacing);
				num2 += num3 + VerticalSpacing;
				num = 0.0;
				num3 = 0.0;
			}
			num += desiredSize.Width + HorizontalSpacing;
			num3 = Math.Max(num3, desiredSize.Height);
		}
		val = Math.Max(val, (num > 0.0) ? (num - HorizontalSpacing) : 0.0);
		return new Size(val, num2 + num3);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		double num = 0.0;
		double num2 = 0.0;
		double num3 = 0.0;
		foreach (UIElement child in base.Children)
		{
			Size desiredSize = child.DesiredSize;
			if (num > 0.0 && num + desiredSize.Width > finalSize.Width)
			{
				num2 += num3 + VerticalSpacing;
				num = 0.0;
				num3 = 0.0;
			}
			child.Arrange(new Rect(num, num2, desiredSize.Width, desiredSize.Height));
			num += desiredSize.Width + HorizontalSpacing;
			num3 = Math.Max(num3, desiredSize.Height);
		}
		return new Size(finalSize.Width, num2 + num3);
	}
}
