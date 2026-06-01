using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using Alpheratz.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;
using WinRT.Interop;

namespace Alpheratz;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Markup.IComponentConnector")]
[WinRTExposedType(typeof(Alpheratz_MainWindowWinRTTypeDetails))]
public class MainWindow : Window, IComponentConnector
{
	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid RootHost;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public MainWindow()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception ex)
		{
			AppLogger.Error($"MainWindow.ctor: InitializeComponent failed: {ex}");
			for (Exception innerException = ex.InnerException; innerException != null; innerException = innerException.InnerException)
			{
				AppLogger.Error($"MainWindow.ctor: InnerException: {innerException}");
			}
			throw;
		}
		base.Title = "Alpheratz";
		base.ExtendsContentIntoTitleBar = false;
		AppWindow fromWindowId = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this)));
		fromWindowId.SetIcon("Assets\\icon.ico");
		if (fromWindowId.Presenter is OverlappedPresenter overlappedPresenter)
		{
			overlappedPresenter.Maximize();
		}
	}

	public MainWindow(Page rootPage)
		: this()
	{
		try
		{
			SetRoot(rootPage);
		}
		catch (Exception value)
		{
			AppLogger.Error($"MainWindow.ctor(Page): SetRoot failed: {value}");
			throw;
		}
	}

	public void SetTheme(ElementTheme theme)
	{
		try
		{
			RootHost.RequestedTheme = theme;
		}
		catch (Exception value)
		{
			AppLogger.Error($"MainWindow.SetTheme: threw: {value}");
		}
	}

	public void SetRoot(Page rootPage)
	{
		try
		{
			RootHost.Children.Clear();
			RootHost.Children.Add(rootPage);
		}
		catch (Exception value)
		{
			AppLogger.Error($"MainWindow.SetRoot: failed: {value}");
			throw;
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///MainWindow.xaml");
			Application.LoadComponent(this, resourceLocator, ComponentResourceLocation.Application);
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void Connect(int connectionId, object target)
	{
		if (connectionId == 2)
		{
			RootHost = target.As<Grid>();
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
