using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using Alpheratz.Core;
using Alpheratz.Shared.Icons;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Shared.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class AppIcon : UserControl, IComponentConnector
{
	public static readonly DependencyProperty IconNameProperty = DependencyProperty.Register("IconName", typeof(string), typeof(AppIcon), new PropertyMetadata(string.Empty, OnNameChanged));

	public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register("IconSize", typeof(double), typeof(AppIcon), new PropertyMetadata(16.0));

	public new static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register("Foreground", typeof(Brush), typeof(AppIcon), new PropertyMetadata(new SolidColorBrush(Colors.Black)));

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private UserControl Root;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Path IconPath;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public string IconName
	{
		get
		{
			return (string)GetValue(IconNameProperty);
		}
		set
		{
			SetValue(IconNameProperty, value);
		}
	}

	public double IconSize
	{
		get
		{
			return (double)GetValue(IconSizeProperty);
		}
		set
		{
			SetValue(IconSizeProperty, value);
		}
	}

	public new Brush Foreground
	{
		get
		{
			return (Brush)GetValue(ForegroundProperty);
		}
		set
		{
			SetValue(ForegroundProperty, value);
		}
	}

	public AppIcon()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"AppIcon.ctor: InitializeComponent failed: {value}");
			throw;
		}
	}

	private static void OnNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		try
		{
			if (d is AppIcon appIcon)
			{
				string text = ((string)e.NewValue) ?? string.Empty;
				if (string.IsNullOrEmpty(text))
				{
					appIcon.IconPath.Data = null;
				}
				else
				{
					appIcon.IconPath.Data = AppIcons.GetGeometry(text);
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"AppIcon.OnNameChanged: threw: {value}");
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Shared/Controls/AppIcon.xaml");
			Application.LoadComponent(this, resourceLocator, ComponentResourceLocation.Application);
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			Root = target.As<UserControl>();
			break;
		case 2:
			IconPath = target.As<Path>();
			break;
		}
		_contentLoaded = true;
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public IComponentConnector GetBindingConnector(int connectionId, object target)
	{
		return null;
	}
}
