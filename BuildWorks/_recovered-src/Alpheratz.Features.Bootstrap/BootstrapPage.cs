using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Bootstrap;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_WorldResolve_WorldResolvePageWinRTTypeDetails))]
public sealed class BootstrapPage : Page, IComponentConnector
{
	private const double TrackWidth = 420.0;

	private double _current;

	private double _target;

	private readonly DispatcherTimer _anim;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private StackPanel SplashContent;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border ProgressFill;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private AppIcon StarIcon;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TranslateTransform StarTranslate;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public BootstrapPage()
	{
		try
		{
			InitializeComponent();
			_anim = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(16.0)
			};
			_anim.Tick += OnAnimTick;
		}
		catch (Exception value)
		{
			AppLogger.Error($"BootstrapPage.ctor: InitializeComponent failed: {value}");
			throw;
		}
	}

	private void Page_Unloaded(object sender, RoutedEventArgs e)
	{
		try
		{
			if ((object)_anim != null)
			{
				_anim.Stop();
				_anim.Tick -= OnAnimTick;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"BootstrapPage.Page_Unloaded: threw: {value}");
		}
	}

	public void SetPhase(AppLifecyclePhase phase)
	{
		try
		{
			_target = phase switch
			{
				AppLifecyclePhase.booting => 5, 
				AppLifecyclePhase.sdkReady => 30, 
				AppLifecyclePhase.servicesReady => 60, 
				AppLifecyclePhase.dataReady => 100, 
				AppLifecyclePhase.uiReady => 100, 
				_ => 0, 
			};
			if (!_anim.IsEnabled)
			{
				_anim.Start();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"BootstrapPage.SetPhase: threw: {value}");
		}
	}

	private void OnAnimTick(object? sender, object e)
	{
		double num = _target - _current;
		if (Math.Abs(num) < 0.3)
		{
			_current = _target;
			_anim.Stop();
		}
		else
		{
			_current += num * 0.12;
		}
		double num2 = 420.0 * (_current / 100.0);
		ProgressFill.Width = num2;
		StarTranslate.X = num2;
	}

	public Task FadeInAsync()
	{
		_current = 0.0;
		ProgressFill.Width = 0.0;
		StarTranslate.X = 0.0;
		TaskCompletionSource tcs = new TaskCompletionSource();
		try
		{
			Storyboard storyboard = new Storyboard();
			DoubleAnimation doubleAnimation = new DoubleAnimation
			{
				From = 0.0,
				To = 1.0,
				Duration = new Duration(TimeSpan.FromMilliseconds(500.0)),
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			Storyboard.SetTarget(doubleAnimation, SplashContent);
			Storyboard.SetTargetProperty(doubleAnimation, "Opacity");
			storyboard.Children.Add(doubleAnimation);
			storyboard.Completed += delegate
			{
				tcs.TrySetResult();
			};
			storyboard.Begin();
		}
		catch (Exception value)
		{
			AppLogger.Error($"BootstrapPage.FadeInAsync: threw: {value}");
			tcs.TrySetResult();
		}
		return tcs.Task;
	}

	public Task FadeOutAsync()
	{
		_anim.Stop();
		TaskCompletionSource tcs = new TaskCompletionSource();
		try
		{
			Storyboard storyboard = new Storyboard();
			DoubleAnimation doubleAnimation = new DoubleAnimation
			{
				From = 1.0,
				To = 0.0,
				Duration = new Duration(TimeSpan.FromMilliseconds(350.0)),
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseIn
				}
			};
			Storyboard.SetTarget(doubleAnimation, SplashContent);
			Storyboard.SetTargetProperty(doubleAnimation, "Opacity");
			storyboard.Children.Add(doubleAnimation);
			storyboard.Completed += delegate
			{
				tcs.TrySetResult();
			};
			storyboard.Begin();
		}
		catch (Exception value)
		{
			AppLogger.Error($"BootstrapPage.FadeOutAsync: threw: {value}");
			tcs.TrySetResult();
		}
		return tcs.Task;
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/Bootstrap/BootstrapPage.xaml");
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
			target.As<Page>().Unloaded += Page_Unloaded;
			break;
		case 2:
			SplashContent = target.As<StackPanel>();
			break;
		case 3:
			ProgressFill = target.As<Border>();
			break;
		case 4:
			StarIcon = target.As<AppIcon>();
			break;
		case 5:
			StarTranslate = target.As<TranslateTransform>();
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
