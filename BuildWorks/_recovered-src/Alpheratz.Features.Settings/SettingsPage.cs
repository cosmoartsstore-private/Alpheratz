using System;
using System.CodeDom.Compiler;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Services;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;
using Windows.System;

namespace Alpheratz.Features.Settings;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_WorldResolve_WorldResolvePageWinRTTypeDetails))]
public sealed class SettingsPage : Page, IComponentConnector
{
	private readonly SettingsCompositeViewModel viewModel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ItemsControl TemplateList;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock EditorModeLabel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button CancelEditButton;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button SaveButton;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border TagEmptyState;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ItemsControl TagRepeater;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBox TagInputBox;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button ThemeSegLight;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button ThemeSegDark;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button CloseButtonOverlay;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public Action? OnClose { get; set; }

	public Func<int, Task>? OnChooseFolder { get; set; }

	public Action<int>? OnResetFolder { get; set; }

	public Func<bool, Task>? OnStartupPreferenceChanged { get; set; }

	public Func<bool, Task>? OnThemeChanged { get; set; }

	public Func<Task>? OnStartWorldAnalysis { get; set; }

	public Func<Task>? OnCreateTag { get; set; }

	public Func<string, Task>? OnDeleteTag { get; set; }

	public Action? OnCancelEdit { get; set; }

	public Func<Task>? OnSaveTemplate { get; set; }

	public Action<string>? OnStartEdit { get; set; }

	public Func<string, Task>? OnDeleteTemplate { get; set; }

	public Func<string, Task>? OnSelectTemplate { get; set; }

	public SettingsPage(SettingsCompositeViewModel viewModel)
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.ctor: InitializeComponent failed: {value}");
			throw;
		}
		this.viewModel = viewModel;
		base.DataContext = viewModel;
		base.Loaded += SettingsPage_Loaded;
		base.Unloaded += SettingsPage_Unloaded;
	}

	private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
	{
		try
		{
			viewModel.TagMaster.masterTags.CollectionChanged += MasterTags_CollectionChanged;
			viewModel.Template.PropertyChanged += TemplateViewModel_PropertyChanged;
			base.ActualThemeChanged += OnActualThemeChanged;
			UpdateTagEmptyState();
			UpdateEditorState();
			syncThemeSegment();
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.SettingsPage_Loaded: threw: {value}");
		}
	}

	private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
	{
		try
		{
			viewModel.TagMaster.masterTags.CollectionChanged -= MasterTags_CollectionChanged;
			viewModel.Template.PropertyChanged -= TemplateViewModel_PropertyChanged;
			base.ActualThemeChanged -= OnActualThemeChanged;
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.SettingsPage_Unloaded: threw: {value}");
		}
	}

	private void OnActualThemeChanged(FrameworkElement sender, object args)
	{
		try
		{
			RefreshTemplateCardVisuals();
			syncThemeSegment();
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.OnActualThemeChanged: {value}");
		}
	}

	private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e)
	{
		OnClose?.Invoke();
	}

	private void ModalContent_Tapped(object sender, TappedRoutedEventArgs e)
	{
		e.Handled = true;
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		OnClose?.Invoke();
	}

	private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key == VirtualKey.Escape)
		{
			OnClose?.Invoke();
			e.Handled = true;
		}
	}

	private async void ChoosePrimaryFolder_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (OnChooseFolder != null)
			{
				await OnChooseFolder(1).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.ChoosePrimaryFolder_Click: threw: {value}");
		}
	}

	private async void ChooseSecondaryFolder_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (OnChooseFolder != null)
			{
				await OnChooseFolder(2).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.ChooseSecondaryFolder_Click: threw: {value}");
		}
	}

	private void ResetPrimaryFolder_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnResetFolder?.Invoke(1);
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.ResetPrimaryFolder_Click: threw: {value}");
		}
	}

	private void ResetSecondaryFolder_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnResetFolder?.Invoke(2);
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.ResetSecondaryFolder_Click: threw: {value}");
		}
	}

	private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
	{
		try
		{
			if (sender is ToggleSwitch toggleSwitch && OnStartupPreferenceChanged != null)
			{
				await OnStartupPreferenceChanged(toggleSwitch.IsOn).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.StartupToggle_Toggled: threw: {value}");
		}
	}

	private async void ThemeSegLight_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			syncThemeSegment(ElementTheme.Light);
			if (OnThemeChanged != null)
			{
				await OnThemeChanged(arg: false).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.ThemeSegLight_Click: threw: {value}");
		}
	}

	private async void ThemeSegDark_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			syncThemeSegment(ElementTheme.Dark);
			if (OnThemeChanged != null)
			{
				await OnThemeChanged(arg: true).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.ThemeSegDark_Click: threw: {value}");
		}
	}

	private void syncThemeSegment(ElementTheme? overrideTheme = null)
	{
		if ((object)ThemeSegLight != null && (object)ThemeSegDark != null)
		{
			bool flag = (overrideTheme ?? base.ActualTheme) == ElementTheme.Dark;
			Style style = ThemeHelper.AppResource<Style>("ThemeSegmentSelectedStyle");
			Style style2 = ThemeHelper.AppResource<Style>("ThemeSegmentStyle");
			if ((object)style != null && (object)style2 != null)
			{
				ThemeSegLight.Style = (flag ? style2 : style);
				ThemeSegDark.Style = (flag ? style : style2);
			}
		}
	}

	private void RegisterStellaRecord_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (StellaRecordRegistration.IsStellaRecordAvailable())
			{
				string? exePath = Environment.ProcessPath ?? string.Empty;
				string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
				StellaRecordRegistration.Register(exePath, iconPath);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.RegisterStellaRecord_Click: threw: {value}");
		}
	}

	private async void StartWorldAnalysis_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (OnStartWorldAnalysis != null)
			{
				await OnStartWorldAnalysis().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.StartWorldAnalysis_Click: threw: {value}");
		}
	}

	private async void CreditLink_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if ((sender as FrameworkElement)?.Tag is string text && !string.IsNullOrWhiteSpace(text))
			{
				await Launcher.LaunchUriAsync(new Uri(text));
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.CreditLink_Click: threw: {value}");
		}
	}

	private void MasterTags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		base.DispatcherQueue.TryEnqueue(UpdateTagEmptyState);
	}

	private void UpdateTagEmptyState()
	{
		bool flag = viewModel.TagMaster.masterTags.Count == 0;
		TagEmptyState.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void TagInput_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key == VirtualKey.Enter)
		{
			e.Handled = true;
			AddTag_Click(sender, e);
		}
	}

	private async void AddTag_Click(object sender, RoutedEventArgs e)
	{
		_ = 1;
		try
		{
			if (OnCreateTag != null)
			{
				await OnCreateTag().ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				await viewModel.TagMaster.createTag().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.AddTag_Click: threw: {value}");
		}
	}

	private async void DeleteTag_Click(object sender, RoutedEventArgs e)
	{
		_ = 1;
		try
		{
			if ((sender as FrameworkElement)?.DataContext is string text)
			{
				if (OnDeleteTag != null)
				{
					await OnDeleteTag(text).ConfigureAwait(continueOnCapturedContext: false);
				}
				else
				{
					await viewModel.TagMaster.deleteTag(text).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.DeleteTag_Click: threw: {value}");
		}
	}

	private void TemplateViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "EditingTweetTemplate")
		{
			base.DispatcherQueue.TryEnqueue(UpdateEditorState);
		}
		else if (e.PropertyName == "ActiveTweetTemplate")
		{
			base.DispatcherQueue.TryEnqueue(RefreshTemplateCardVisuals);
		}
	}

	private void UpdateEditorState()
	{
		bool flag = viewModel.Template.EditingTweetTemplate != null;
		CancelEditButton.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		SaveButton.Content = (flag ? "更新" : "登録");
		EditorModeLabel.Text = (flag ? "テンプレート編集" : "新規テンプレート");
	}

	private void CancelEdit_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (OnCancelEdit != null)
			{
				OnCancelEdit();
			}
			else
			{
				viewModel.Template.cancelEdit();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.CancelEdit_Click: threw: {value}");
		}
	}

	private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (OnSaveTemplate != null)
			{
				await OnSaveTemplate().ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				viewModel.Template.saveTemplateDraft();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.SaveTemplate_Click: threw: {value}");
		}
	}

	private void EditTemplate_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if ((sender as FrameworkElement)?.DataContext is string text)
			{
				if (OnStartEdit != null)
				{
					OnStartEdit(text);
				}
				else
				{
					viewModel.Template.startEdit(text);
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.EditTemplate_Click: threw: {value}");
		}
	}

	private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!((sender as FrameworkElement)?.DataContext is string text))
			{
				return;
			}
			if (OnDeleteTemplate != null)
			{
				await OnDeleteTemplate(text).ConfigureAwait(continueOnCapturedContext: false);
				return;
			}
			AppLogger.Warn("SettingsPage.DeleteTemplate_Click: OnDeleteTemplate not wired; collection-only delete");
			if (viewModel.Template.tweetTemplates.Contains(text))
			{
				viewModel.Template.tweetTemplates.Remove(text);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.DeleteTemplate_Click: threw: {value}");
		}
	}

	private async void TemplateCard_PointerPressed(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if ((sender as FrameworkElement)?.DataContext is string text)
			{
				if (OnSelectTemplate != null)
				{
					await OnSelectTemplate(text).ConfigureAwait(continueOnCapturedContext: false);
				}
				else
				{
					viewModel.Template.ActiveTweetTemplate = text;
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.TemplateCard_PointerPressed: threw: {value}");
		}
	}

	private void TemplateCard_Loaded(object sender, RoutedEventArgs e)
	{
		try
		{
			if (sender is Border { DataContext: string dataContext } border)
			{
				ApplyTemplateCardStyle(border, dataContext == viewModel.Template.ActiveTweetTemplate);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.TemplateCard_Loaded: threw: {value}");
		}
	}

	private void RefreshTemplateCardVisuals()
	{
		try
		{
			for (int i = 0; i < TemplateList.Items.Count; i++)
			{
				DependencyObject dependencyObject = TemplateList.ContainerFromIndex(i);
				if ((object)dependencyObject != null)
				{
					Border border = FindChildBorder(dependencyObject);
					if (border?.DataContext is string text)
					{
						ApplyTemplateCardStyle(border, text == viewModel.Template.ActiveTweetTemplate);
					}
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsPage.RefreshTemplateCardVisuals: threw: {value}");
		}
	}

	private static Border? FindChildBorder(DependencyObject parent)
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			if (child is Border result)
			{
				return result;
			}
			Border border = FindChildBorder(child);
			if ((object)border != null)
			{
				return border;
			}
		}
		return null;
	}

	private static void ApplyTemplateCardStyle(Border card, bool isActive)
	{
		card.BorderBrush = ThemeHelper.Brush(card, isActive ? "APrimary" : "ABorder");
		card.BorderThickness = new Thickness((!isActive) ? 1 : 2);
		card.Background = ThemeHelper.Brush(card, "ASurfaceSoft");
		if (((card.Child as Grid)?.Children[0] as StackPanel)?.Children[0] is TextBlock textBlock)
		{
			textBlock.Text = (isActive ? "使用中" : "テンプレート");
			textBlock.Foreground = ThemeHelper.Brush(card, isActive ? "APrimaryText" : "ATextDim");
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/Settings/SettingsPage.xaml");
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
			target.As<Page>().KeyDown += Page_KeyDown;
			break;
		case 2:
			target.As<Grid>().Tapped += Backdrop_Tapped;
			break;
		case 3:
			target.As<Border>().Tapped += ModalContent_Tapped;
			break;
		case 4:
			TemplateList = target.As<ItemsControl>();
			break;
		case 5:
		{
			Border border = target.As<Border>();
			border.Loaded += TemplateCard_Loaded;
			border.PointerPressed += TemplateCard_PointerPressed;
			break;
		}
		case 6:
			target.As<Button>().Click += EditTemplate_Click;
			break;
		case 7:
			target.As<Button>().Click += DeleteTemplate_Click;
			break;
		case 9:
			EditorModeLabel = target.As<TextBlock>();
			break;
		case 10:
			CancelEditButton = target.As<Button>();
			CancelEditButton.Click += CancelEdit_Click;
			break;
		case 11:
			SaveButton = target.As<Button>();
			SaveButton.Click += SaveTemplate_Click;
			break;
		case 12:
			TagEmptyState = target.As<Border>();
			break;
		case 13:
			TagRepeater = target.As<ItemsControl>();
			break;
		case 15:
			target.As<Button>().Click += DeleteTag_Click;
			break;
		case 16:
			TagInputBox = target.As<TextBox>();
			TagInputBox.KeyDown += TagInput_KeyDown;
			break;
		case 17:
			target.As<Button>().Click += AddTag_Click;
			break;
		case 18:
			target.As<Button>().Click += CreditLink_Click;
			break;
		case 19:
			target.As<Button>().Click += CreditLink_Click;
			break;
		case 20:
			target.As<Button>().Click += CreditLink_Click;
			break;
		case 21:
			target.As<Button>().Click += StartWorldAnalysis_Click;
			break;
		case 22:
			target.As<Button>().Click += RegisterStellaRecord_Click;
			break;
		case 23:
			ThemeSegLight = target.As<Button>();
			ThemeSegLight.Click += ThemeSegLight_Click;
			break;
		case 24:
			ThemeSegDark = target.As<Button>();
			ThemeSegDark.Click += ThemeSegDark_Click;
			break;
		case 25:
			target.As<ToggleSwitch>().Toggled += StartupToggle_Toggled;
			break;
		case 26:
			target.As<Button>().Click += ChooseSecondaryFolder_Click;
			break;
		case 27:
			target.As<Button>().Click += ResetSecondaryFolder_Click;
			break;
		case 28:
			target.As<Button>().Click += ChoosePrimaryFolder_Click;
			break;
		case 29:
			target.As<Button>().Click += ResetPrimaryFolder_Click;
			break;
		case 30:
			CloseButtonOverlay = target.As<Button>();
			CloseButtonOverlay.Click += CloseButton_Click;
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
