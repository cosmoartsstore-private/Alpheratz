using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using Alpheratz.Core;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Shared.Animations;
using Alpheratz.Shared.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Shell.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class ShellStage : UserControl, IComponentConnector
{
	private int modalVersion;

	private int topModalVersion;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ContentControl MainContentHost;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid ModalLayerHost;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid TopModalLayerHost;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ScanningOverlay ScanningOverlayControl;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ToastHost ToastHostControl;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ContentControl TopModalContentHost;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ContentControl ModalContentHost;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public object? MainContent
	{
		get
		{
			return MainContentHost.Content;
		}
		set
		{
			MainContentHost.Content = value;
		}
	}

	public object? ModalContent
	{
		get
		{
			return ModalContentHost.Content;
		}
		set
		{
			ModalContentHost.Content = value;
		}
	}

	public Visibility ModalVisibility
	{
		get
		{
			return ModalLayerHost.Visibility;
		}
		set
		{
			if (value == Visibility.Visible)
			{
				modalVersion++;
				ModalLayerHost.Visibility = Visibility.Visible;
				AnimationHelper.FadeIn(ModalLayerHost, 250);
				AnimationHelper.ScaleIn(ModalContentHost);
			}
			else if (ModalLayerHost.Visibility == Visibility.Visible)
			{
				int ourVersion = ++modalVersion;
				AnimationHelper.FadeOut(ModalLayerHost);
				AnimationHelper.ScaleOut(ModalContentHost, 0.92f, 200, delegate
				{
					base.DispatcherQueue?.TryEnqueue(delegate
					{
						if (modalVersion == ourVersion)
						{
							ModalContentHost.Content = null;
							ModalLayerHost.Visibility = Visibility.Collapsed;
							AnimationHelper.ResetVisual(ModalLayerHost);
							AnimationHelper.ResetVisual(ModalContentHost);
						}
					});
				});
			}
			else
			{
				ModalLayerHost.Visibility = value;
			}
		}
	}

	public object? TopModalContent
	{
		get
		{
			return TopModalContentHost.Content;
		}
		set
		{
			TopModalContentHost.Content = value;
		}
	}

	public Visibility TopModalVisibility
	{
		get
		{
			return TopModalLayerHost.Visibility;
		}
		set
		{
			if (value == Visibility.Visible)
			{
				topModalVersion++;
				TopModalLayerHost.Visibility = Visibility.Visible;
				AnimationHelper.FadeIn(TopModalLayerHost, 250);
				AnimationHelper.ScaleIn(TopModalContentHost);
			}
			else if (TopModalLayerHost.Visibility == Visibility.Visible)
			{
				int ourVersion = ++topModalVersion;
				AnimationHelper.FadeOut(TopModalLayerHost);
				AnimationHelper.ScaleOut(TopModalContentHost, 0.92f, 200, delegate
				{
					base.DispatcherQueue?.TryEnqueue(delegate
					{
						if (topModalVersion == ourVersion)
						{
							if (TopModalContentHost.Content is PhotoModalPage photoModalPage)
							{
								photoModalPage.ReleaseImage();
							}
							TopModalContentHost.Content = null;
							TopModalLayerHost.Visibility = Visibility.Collapsed;
							AnimationHelper.ResetVisual(TopModalLayerHost);
							AnimationHelper.ResetVisual(TopModalContentHost);
						}
					});
				});
			}
			else
			{
				TopModalLayerHost.Visibility = value;
			}
		}
	}

	public Visibility ScanningOverlayVisibility
	{
		get
		{
			return ScanningOverlayControl.Visibility;
		}
		set
		{
			if (value == Visibility.Visible && ScanningOverlayControl.Visibility != Visibility.Visible)
			{
				ScanningOverlayControl.Visibility = Visibility.Visible;
				AnimationHelper.FadeIn(ScanningOverlayControl, 300);
			}
			else if (value == Visibility.Collapsed && ScanningOverlayControl.Visibility == Visibility.Visible)
			{
				AnimationHelper.FadeOut(ScanningOverlayControl, 250, 0, delegate
				{
					base.DispatcherQueue?.TryEnqueue(delegate
					{
						ScanningOverlayControl.Visibility = Visibility.Collapsed;
						AnimationHelper.ResetVisual(ScanningOverlayControl);
					});
				});
			}
			else
			{
				ScanningOverlayControl.Visibility = value;
			}
		}
	}

	public object? ScanningOverlayDataContext
	{
		get
		{
			return ScanningOverlayControl.DataContext;
		}
		set
		{
			ScanningOverlayControl.DataContext = value;
		}
	}

	public object? ToastDataContext
	{
		get
		{
			return ToastHostControl.DataContext;
		}
		set
		{
			ToastHostControl.DataContext = value;
		}
	}

	public ScanningOverlay ScanningOverlayControlRef => ScanningOverlayControl;

	public ShellStage()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellStage.ctor: InitializeComponent failed: {value}");
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
			Uri resourceLocator = new Uri("ms-appx:///Features/Shell/Controls/ShellStage.xaml");
			Application.LoadComponent(this, resourceLocator, ComponentResourceLocation.Application);
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 2:
			MainContentHost = target.As<ContentControl>();
			break;
		case 3:
			ModalLayerHost = target.As<Grid>();
			break;
		case 4:
			TopModalLayerHost = target.As<Grid>();
			break;
		case 5:
			ScanningOverlayControl = target.As<ScanningOverlay>();
			break;
		case 6:
			ToastHostControl = target.As<ToastHost>();
			break;
		case 7:
			TopModalContentHost = target.As<ContentControl>();
			break;
		case 8:
			ModalContentHost = target.As<ContentControl>();
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
