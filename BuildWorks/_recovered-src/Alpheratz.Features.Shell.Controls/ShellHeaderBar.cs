using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Controls;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;
using Windows.System;

namespace Alpheratz.Features.Shell.Controls;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class ShellHeaderBar : UserControl, IComponentConnector
{
	private bool isMultiSelectActive;

	private GroupingMode currentGroupingMode;

	private ViewMode currentViewMode;

	private bool searchBoxFocused;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid ContentRoot;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button FilterPillBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border PdqProgressChip;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button GroupingBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button MultiSelectBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button SettingsGearBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private AppIcon MultiSelectIcon;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private AppIcon GroupingIcon;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button ViewModeStandardSeg;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button ViewModeGallerySeg;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private AppIcon ViewModeGalleryIcon;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private AppIcon ViewModeStandardIcon;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock PdqProgressLabel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock PdqProgressDone;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock PdqProgressTotal;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border SearchBoxBorder;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button SearchCommandsBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Flyout SearchCommandsFlyout;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBox SearchTextBox;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public Action? OnToggleFilter { get; set; }

	public Action? OnShowSettings { get; set; }

	public Action? OnToggleMultiSelect { get; set; }

	public Action<GroupingMode>? OnGroupingChange { get; set; }

	public Func<string, Task>? OnViewModeChange { get; set; }

	public Action? OnSearchSubmit { get; set; }

	public ShellHeaderBar()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.ctor: InitializeComponent failed: {value}");
			throw;
		}
		SyncMultiSelectStyle();
		SyncGroupingStyle();
		syncViewModeSegment();
		base.ActualThemeChanged += OnActualThemeChanged;
		base.Unloaded += delegate
		{
			base.ActualThemeChanged -= OnActualThemeChanged;
		};
	}

	private void OnActualThemeChanged(FrameworkElement sender, object args)
	{
		try
		{
			SyncMultiSelectStyle();
			SyncGroupingStyle();
			syncViewModeSegment();
			syncSearchBoxBorder(searchBoxFocused);
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.OnActualThemeChanged: {value}");
		}
	}

	public void SetControlsInteractive(bool interactive)
	{
		try
		{
			if ((object)ContentRoot != null)
			{
				ContentRoot.IsHitTestVisible = interactive;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.SetControlsInteractive: threw: {value}");
		}
	}

	public void SetMultiSelectActive(bool active)
	{
		try
		{
			isMultiSelectActive = active;
			SyncMultiSelectStyle();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.SetMultiSelectActive: threw: {value}");
		}
	}

	public void SetGroupingMode(GroupingMode mode)
	{
		try
		{
			currentGroupingMode = mode;
			SyncGroupingStyle();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.SetGroupingMode: threw: {value}");
		}
	}

	public void SetViewMode(ViewMode mode)
	{
		try
		{
			currentViewMode = mode;
			syncViewModeSegment();
			SyncGroupingStyle();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.SetViewMode: threw: {value}");
		}
	}

	public void SetPdqProgress(bool running, int done, int total)
	{
		try
		{
			if (!running)
			{
				PdqProgressChip.Visibility = Visibility.Collapsed;
				return;
			}
			PdqProgressChip.Visibility = Visibility.Visible;
			if (total > 0)
			{
				PdqProgressDone.Text = done.ToString();
				PdqProgressTotal.Text = $"/ {total}";
				PdqProgressDone.Visibility = Visibility.Visible;
				PdqProgressTotal.Visibility = Visibility.Visible;
			}
			else
			{
				PdqProgressDone.Visibility = Visibility.Collapsed;
				PdqProgressTotal.Visibility = Visibility.Collapsed;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.SetPdqProgress: threw: {value}");
		}
	}

	private void FilterButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnToggleFilter?.Invoke();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.FilterButton_Click: threw: {value}");
		}
	}

	private void SettingsGearBtn_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnShowSettings?.Invoke();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.SettingsGearBtn_Click: threw: {value}");
		}
	}

	private void MultiSelectBtn_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnToggleMultiSelect?.Invoke();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.MultiSelectBtn_Click: threw: {value}");
		}
	}

	private void GroupingBtn_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			GroupingMode obj = ((currentGroupingMode != GroupingMode.world) ? GroupingMode.world : GroupingMode.none);
			OnGroupingChange?.Invoke(obj);
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.GroupingBtn_Click: threw: {value}");
		}
	}

	private async void ViewModeStandardSeg_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (currentViewMode != ViewMode.standard && OnViewModeChange != null)
			{
				await OnViewModeChange("standard").ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.ViewModeStandardSeg_Click: threw: {value}");
		}
	}

	private async void ViewModeGallerySeg_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (currentViewMode != ViewMode.gallery && OnViewModeChange != null)
			{
				await OnViewModeChange("gallery").ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.ViewModeGallerySeg_Click: threw: {value}");
		}
	}

	private void SyncMultiSelectStyle()
	{
		ApplyActiveStyle(MultiSelectBtn, MultiSelectIcon, isMultiSelectActive);
	}

	private void SyncGroupingStyle()
	{
		bool active = currentGroupingMode == GroupingMode.world;
		ApplyActiveStyle(GroupingBtn, GroupingIcon, active);
		bool flag = currentViewMode == ViewMode.gallery;
		GroupingBtn.IsEnabled = !flag;
		GroupingBtn.Opacity = (flag ? 0.4 : 1.0);
	}

	private void syncViewModeSegment()
	{
		try
		{
			if ((object)ViewModeStandardSeg != null && (object)ViewModeGallerySeg != null)
			{
				bool flag = currentViewMode == ViewMode.gallery;
				Style style = ThemeHelper.AppResource<Style>("ThemeSegmentSelectedStyle");
				Style style2 = ThemeHelper.AppResource<Style>("ThemeSegmentStyle");
				if ((object)style != null && (object)style2 != null)
				{
					ViewModeStandardSeg.Style = (flag ? style2 : style);
					ViewModeGallerySeg.Style = (flag ? style : style2);
					Brush brush = ResolveThemeBrush("ATextDim");
					SolidColorBrush solidColorBrush = new SolidColorBrush(Colors.White);
					ViewModeStandardIcon.Foreground = (flag ? (brush ?? solidColorBrush) : solidColorBrush);
					ViewModeGalleryIcon.Foreground = (flag ? solidColorBrush : (brush ?? solidColorBrush));
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.syncViewModeSegment: threw: {value}");
		}
	}

	private void ApplyActiveStyle(Button button, AppIcon icon, bool active)
	{
		try
		{
			if (active)
			{
				Brush brush = ResolveThemeBrush("APrimarySoft");
				if ((object)brush != null)
				{
					button.Background = brush;
				}
				Brush brush2 = ResolveThemeBrush("ABorderStrong");
				if ((object)brush2 != null)
				{
					button.BorderBrush = brush2;
				}
				Brush brush3 = ResolveThemeBrush("APrimary");
				if ((object)brush3 != null)
				{
					button.Foreground = brush3;
					icon.Foreground = brush3;
				}
			}
			else
			{
				button.Background = new SolidColorBrush(Colors.Transparent);
				button.BorderBrush = new SolidColorBrush(Colors.Transparent);
				Brush brush4 = ResolveThemeBrush("ATextDim");
				if ((object)brush4 != null)
				{
					button.Foreground = brush4;
					icon.Foreground = brush4;
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.ApplyActiveStyle: threw: {value}");
		}
	}

	private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
	{
		try
		{
			searchBoxFocused = true;
			syncSearchBoxBorder(focused: true);
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.SearchTextBox_GotFocus: threw: {value}");
		}
	}

	private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
	{
		try
		{
			searchBoxFocused = false;
			syncSearchBoxBorder(focused: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.SearchTextBox_LostFocus: threw: {value}");
		}
	}

	private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		try
		{
			if (e.Key == VirtualKey.Enter)
			{
				e.Handled = true;
				SearchTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
				OnSearchSubmit?.Invoke();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.SearchTextBox_KeyDown: threw: {value}");
		}
	}

	private void SearchCommandItem_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (sender is Button { Tag: string tag } && !string.IsNullOrEmpty(tag))
			{
				string text = SearchTextBox.Text ?? string.Empty;
				string text2 = ((text.Length > 0 && !text.EndsWith(' ')) ? " " : string.Empty);
				SearchTextBox.Text = text + text2 + tag;
				SearchCommandsFlyout.Hide();
				SearchTextBox.Focus(FocusState.Programmatic);
				SearchTextBox.SelectionStart = SearchTextBox.Text.Length;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.SearchCommandItem_Click: threw: {value}");
		}
	}

	private void syncSearchBoxBorder(bool focused)
	{
		try
		{
			if (focused)
			{
				Brush brush = ResolveThemeBrush("ABorderStrong");
				if ((object)brush != null)
				{
					SearchBoxBorder.BorderBrush = brush;
				}
				Brush brush2 = ResolveThemeBrush("ASurface");
				if ((object)brush2 != null)
				{
					SearchBoxBorder.Background = brush2;
				}
			}
			else
			{
				SearchBoxBorder.ClearValue(Border.BorderBrushProperty);
				SearchBoxBorder.ClearValue(Border.BackgroundProperty);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellHeaderBar.syncSearchBoxBorder: threw: {value}");
		}
	}

	private Brush? ResolveThemeBrush(string key)
	{
		return ThemeHelper.Brush(this, key);
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/Shell/Controls/ShellHeaderBar.xaml");
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
			ContentRoot = target.As<Grid>();
			break;
		case 3:
			FilterPillBtn = target.As<Button>();
			FilterPillBtn.Click += FilterButton_Click;
			break;
		case 4:
			PdqProgressChip = target.As<Border>();
			break;
		case 5:
			GroupingBtn = target.As<Button>();
			GroupingBtn.Click += GroupingBtn_Click;
			break;
		case 6:
			MultiSelectBtn = target.As<Button>();
			MultiSelectBtn.Click += MultiSelectBtn_Click;
			break;
		case 7:
			SettingsGearBtn = target.As<Button>();
			SettingsGearBtn.Click += SettingsGearBtn_Click;
			break;
		case 8:
			MultiSelectIcon = target.As<AppIcon>();
			break;
		case 9:
			GroupingIcon = target.As<AppIcon>();
			break;
		case 10:
			ViewModeStandardSeg = target.As<Button>();
			ViewModeStandardSeg.Click += ViewModeStandardSeg_Click;
			break;
		case 11:
			ViewModeGallerySeg = target.As<Button>();
			ViewModeGallerySeg.Click += ViewModeGallerySeg_Click;
			break;
		case 12:
			ViewModeGalleryIcon = target.As<AppIcon>();
			break;
		case 13:
			ViewModeStandardIcon = target.As<AppIcon>();
			break;
		case 14:
			PdqProgressLabel = target.As<TextBlock>();
			break;
		case 15:
			PdqProgressDone = target.As<TextBlock>();
			break;
		case 16:
			PdqProgressTotal = target.As<TextBlock>();
			break;
		case 17:
			SearchBoxBorder = target.As<Border>();
			break;
		case 18:
			SearchCommandsBtn = target.As<Button>();
			break;
		case 19:
			SearchCommandsFlyout = target.As<Flyout>();
			break;
		case 20:
			target.As<Button>().Click += SearchCommandItem_Click;
			break;
		case 21:
			target.As<Button>().Click += SearchCommandItem_Click;
			break;
		case 22:
			target.As<Button>().Click += SearchCommandItem_Click;
			break;
		case 23:
			target.As<Button>().Click += SearchCommandItem_Click;
			break;
		case 24:
			target.As<Button>().Click += SearchCommandItem_Click;
			break;
		case 25:
			target.As<Button>().Click += SearchCommandItem_Click;
			break;
		case 26:
			target.As<Button>().Click += SearchCommandItem_Click;
			break;
		case 27:
			SearchTextBox = target.As<TextBox>();
			SearchTextBox.GotFocus += SearchTextBox_GotFocus;
			SearchTextBox.LostFocus += SearchTextBox_LostFocus;
			SearchTextBox.KeyDown += SearchTextBox_KeyDown;
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
