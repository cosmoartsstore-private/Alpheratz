using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Shared.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class EmptyState : UserControl, IComponentConnector
{
	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock TitleText;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock DescriptionText;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button PrimaryActionButton;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private AppIcon IconControl;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public event Action? PrimaryActionRequested;

	public EmptyState()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"EmptyState.ctor: InitializeComponent failed: {value}");
			throw;
		}
	}

	public void SetMode(EmptyStateMode mode)
	{
		try
		{
			if (mode != EmptyStateMode.NoLibrary && mode == EmptyStateMode.NoFilterMatch)
			{
				IconControl.IconName = "search";
				TitleText.Text = "条件に一致する写真がありません";
				DescriptionText.Text = "検索やフィルタの条件を変えるか、条件をクリアしてください。";
				PrimaryActionButton.Content = "フィルタをクリア";
			}
			else
			{
				IconControl.IconName = "photo";
				TitleText.Text = "写真がまだありません";
				DescriptionText.Text = "写真フォルダを設定すると、ここに表示されます。";
				PrimaryActionButton.Content = "写真フォルダを設定";
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"EmptyState.SetMode: threw: {value}");
		}
	}

	private void PrimaryActionButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			this.PrimaryActionRequested?.Invoke();
		}
		catch (Exception value)
		{
			AppLogger.Error($"EmptyState.PrimaryActionButton_Click: threw: {value}");
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Shared/Controls/EmptyState.xaml");
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
			TitleText = target.As<TextBlock>();
			break;
		case 3:
			DescriptionText = target.As<TextBlock>();
			break;
		case 4:
			PrimaryActionButton = target.As<Button>();
			PrimaryActionButton.Click += PrimaryActionButton_Click;
			break;
		case 5:
			IconControl = target.As<AppIcon>();
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
