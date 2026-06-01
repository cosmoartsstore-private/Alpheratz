using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Animations;
using Alpheratz.Shared.Controls;
using Alpheratz.Shared.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Alpheratz.Features.PhotoModal;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_WorldResolve_WorldResolvePageWinRTTypeDetails))]
public sealed class PhotoModalPage : Page, IComponentConnector
{
	private PhotoModalViewModel viewModel;

	private UiObservableCollection<string>? masterTags;

	private readonly HashSet<string> selectedAvailableTags = new HashSet<string>();

	private bool tagMultiSelectMode;

	private const int MaxTagsPerPhoto = 10;

	private bool navHovered;

	private const byte navEdgeAlphaDisabled = 34;

	private const byte navEdgeAlphaIdle = 85;

	private const byte navEdgeAlphaHover = 153;

	private bool prevEdgeHovered;

	private bool nextEdgeHovered;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid ClipboardOverlay;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid ImagePanel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button OpenTagMasterButton;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock MultiSelectHint;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock EmptyTagNote;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private WrapPanel AvailableTagsPanel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock AppliedTagsEmptyNote;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ItemsControl AppliedTagsList;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock TagLimitHint;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button CloseButtonOverlay;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock WorldNameText;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private StackPanel MatchSourcePanel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private WrapPanel PhotoMetaPanel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock PhotoTimestampText;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock MatchSourceLabel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ScrollViewer ImageScroll;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button PrevPhotoButton;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button NextPhotoButton;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border PositionIndicator;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock PositionIndicatorText;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private GradientStop NextEdgeGradientStop;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private AppIcon NextPhotoIcon;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private GradientStop PrevEdgeGradientStop;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private AppIcon PrevPhotoIcon;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Image ModalImage;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public Action? OnClose { get; set; }

	public Action? OnGoBack { get; set; }

	public Action? OnGoPrev { get; set; }

	public Action? OnGoNext { get; set; }

	public Func<Task>? OnOpenWorld { get; set; }

	public Func<Task>? OnOpenExplorer { get; set; }

	public Func<Task>? OnTweet { get; set; }

	public Func<Task>? OnToggleFavorite { get; set; }

	public Func<string, string, Task>? OnAddTag { get; set; }

	public Func<string, string, Task>? OnRemoveTag { get; set; }

	public Action? OnOpenTagMaster { get; set; }

	public void SetMasterTags(UiObservableCollection<string> tags)
	{
		try
		{
			if (masterTags != null)
			{
				masterTags.CollectionChanged -= OnMasterTagsChanged;
			}
			masterTags = tags;
			masterTags.CollectionChanged += OnMasterTagsChanged;
			rebuildAvailableTags();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.SetMasterTags: threw: {value}");
		}
	}

	private void OnMasterTagsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		try
		{
			rebuildAvailableTags();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.OnMasterTagsChanged: {value}");
		}
	}

	public PhotoModalPage(PhotoModalViewModel viewModel)
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.ctor: InitializeComponent failed: {value}");
			throw;
		}
		this.viewModel = viewModel;
		base.DataContext = viewModel;
		viewModel.state.PropertyChanged += OnStatePropertyChanged;
	}

	public void UpdateViewModel(PhotoModalViewModel next)
	{
		viewModel.state.PropertyChanged -= OnStatePropertyChanged;
		viewModel = next;
		base.DataContext = next;
		next.state.PropertyChanged += OnStatePropertyChanged;
		syncWorldName();
		syncMatchSource();
		syncPhotoMeta();
		resetTagMultiSelect();
		rebuildAvailableTags();
		syncModalImage();
		resetZoom();
		syncNavButtons();
		syncPositionIndicator();
	}

	private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "SelectedPhoto")
		{
			syncWorldName();
			syncMatchSource();
			syncPhotoMeta();
			resetTagMultiSelect();
			rebuildAvailableTags();
			syncModalImage();
			resetZoom();
			syncNavButtons();
			syncPositionIndicator();
			return;
		}
		string propertyName = e.PropertyName;
		if ((propertyName == "CanGoPrev" || propertyName == "CanGoNext") ? true : false)
		{
			syncNavButtons();
			return;
		}
		propertyName = e.PropertyName;
		if ((propertyName == "CurrentIndex" || propertyName == "TotalInList") ? true : false)
		{
			syncPositionIndicator();
		}
	}

	private void syncModalImage()
	{
		try
		{
			string text = viewModel.state.SelectedPhoto?.EffectiveDisplayPath;
			if (string.IsNullOrEmpty(text))
			{
				ModalImage.Source = null;
				return;
			}
			text = text.Replace('/', Path.DirectorySeparatorChar);
			ModalImage.Source = new BitmapImage
			{
				CreateOptions = BitmapCreateOptions.IgnoreImageCache,
				DecodePixelWidth = 1920,
				DecodePixelType = DecodePixelType.Logical,
				UriSource = new Uri(text, UriKind.Absolute)
			};
			applyImageViewport();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.syncModalImage: threw: {value}");
		}
	}

	private void applyImageViewport()
	{
		try
		{
			if ((object)ImageScroll != null && (object)ModalImage != null)
			{
				ModalImage.ClearValue(FrameworkElement.WidthProperty);
				ModalImage.ClearValue(FrameworkElement.HeightProperty);
				double num = ImageScroll.ViewportWidth;
				if (num <= 0.0)
				{
					num = ImageScroll.ActualWidth;
				}
				double num2 = ImageScroll.ViewportHeight;
				if (num2 <= 0.0)
				{
					num2 = ImageScroll.ActualHeight;
				}
				if (num > 0.0)
				{
					ModalImage.MaxWidth = num;
				}
				if (num2 > 0.0)
				{
					ModalImage.MaxHeight = num2;
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.applyImageViewport: threw: {value}");
		}
	}

	private void ImageScroll_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		applyImageViewport();
	}

	private void syncWorldName()
	{
		string text = viewModel.state.SelectedPhoto?.WorldName;
		WorldNameText.Text = (string.IsNullOrEmpty(text) ? "ワールド不明" : text);
	}

	private void syncMatchSource()
	{
		string text = viewModel.state.SelectedPhoto?.MatchSource;
		string text2 = ((text == "polaris_archive") ? "archive ログから補完" : ((!(text == "phash")) ? null : "類似写真から推測"));
		string text3 = text2;
		if (text3 != null)
		{
			MatchSourcePanel.Visibility = Visibility.Visible;
			MatchSourceLabel.Text = text3;
		}
		else
		{
			MatchSourcePanel.Visibility = Visibility.Collapsed;
		}
	}

	private void rebuildAvailableTags()
	{
		try
		{
			AvailableTagsPanel.Children.Clear();
			IReadOnlyList<string> readOnlyList = viewModel.state.SelectedPhoto?.Tags;
			List<string> list = new List<string>();
			if (masterTags != null)
			{
				foreach (string masterTag in masterTags)
				{
					if (!string.IsNullOrEmpty(masterTag) && (readOnlyList == null || !readOnlyList.Contains(masterTag)))
					{
						list.Add(masterTag);
					}
				}
			}
			selectedAvailableTags.IntersectWith(list);
			if (selectedAvailableTags.Count == 0)
			{
				tagMultiSelectMode = false;
			}
			bool flag = list.Count > 0;
			AvailableTagsPanel.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
			EmptyTagNote.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
			foreach (string item in list)
			{
				Button button = new Button
				{
					Style = (Style)Application.Current.Resources["ChipButtonStyle"],
					Tag = item,
					Content = new TextBlock
					{
						Text = item,
						FontSize = 12.0,
						FontWeight = FontWeights.SemiBold,
						VerticalAlignment = VerticalAlignment.Center
					}
				};
				button.Click += AvailableTag_Click;
				applyChipVisual(button, selectedAvailableTags.Contains(item));
				AvailableTagsPanel.Children.Add(button);
			}
			syncMultiSelectHint();
			syncAppliedTagsEmpty();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.rebuildAvailableTags: threw: {value}");
		}
	}

	private void syncAppliedTagsEmpty()
	{
		int valueOrDefault = (viewModel.state.SelectedPhoto?.Tags?.Count).GetValueOrDefault();
		bool flag = valueOrDefault > 0;
		AppliedTagsList.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		AppliedTagsEmptyNote.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		if (valueOrDefault < 10)
		{
			TagLimitHint.Visibility = Visibility.Collapsed;
		}
	}

	private void showTagLimitHint()
	{
		TagLimitHint.Text = $"タグは最大{10}個までです";
		TagLimitHint.Visibility = Visibility.Visible;
	}

	private int currentTagCount()
	{
		return (viewModel.state.SelectedPhoto?.Tags?.Count).GetValueOrDefault();
	}

	private void applyChipVisual(Button chip, bool selected)
	{
		if (chip.Content is TextBlock textBlock)
		{
			if (selected)
			{
				chip.Background = ThemeHelper.Brush(chip, "APrimary") ?? chip.Background;
				chip.BorderBrush = ThemeHelper.Brush(chip, "APrimary") ?? chip.BorderBrush;
				chip.Foreground = new SolidColorBrush(Colors.White);
				textBlock.Foreground = new SolidColorBrush(Colors.White);
			}
			else
			{
				chip.ClearValue(Control.BackgroundProperty);
				chip.ClearValue(Control.BorderBrushProperty);
				chip.ClearValue(Control.ForegroundProperty);
				textBlock.Foreground = ThemeHelper.Brush(chip, "APrimaryText") ?? textBlock.Foreground;
			}
		}
	}

	private void syncMultiSelectHint()
	{
		if (tagMultiSelectMode && selectedAvailableTags.Count > 0)
		{
			MultiSelectHint.Text = $"Enter で {selectedAvailableTags.Count}件追加";
			MultiSelectHint.Visibility = Visibility.Visible;
		}
		else
		{
			MultiSelectHint.Visibility = Visibility.Collapsed;
		}
	}

	private void resetTagMultiSelect()
	{
		selectedAvailableTags.Clear();
		tagMultiSelectMode = false;
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnClose?.Invoke();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.CloseButton_Click: {value}");
		}
	}

	private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e)
	{
		try
		{
			OnClose?.Invoke();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.Backdrop_Tapped: {value}");
		}
	}

	private void ModalContent_Tapped(object sender, TappedRoutedEventArgs e)
	{
		e.Handled = true;
	}

	private void Page_Unloaded(object sender, RoutedEventArgs e)
	{
		viewModel.state.PropertyChanged -= OnStatePropertyChanged;
		if (masterTags != null)
		{
			masterTags.CollectionChanged -= OnMasterTagsChanged;
		}
		base.ActualThemeChanged -= OnPhotoModalThemeChanged;
	}

	private void Page_Loaded(object sender, RoutedEventArgs e)
	{
		try
		{
			base.ActualThemeChanged -= OnPhotoModalThemeChanged;
			base.ActualThemeChanged += OnPhotoModalThemeChanged;
			Focus(FocusState.Programmatic);
			syncWorldName();
			syncMatchSource();
			syncPhotoMeta();
			rebuildAvailableTags();
			syncModalImage();
			applyImageViewport();
			resetZoom();
			syncNavButtons();
			syncPositionIndicator();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.Page_Loaded: {value}");
		}
	}

	private void OnPhotoModalThemeChanged(FrameworkElement sender, object args)
	{
		try
		{
			rebuildAvailableTags();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.OnPhotoModalThemeChanged: {value}");
		}
	}

	private void Page_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
	{
		try
		{
			if (FocusManager.GetFocusedElement(base.XamlRoot) is TextBox)
			{
				return;
			}
			switch (e.Key)
			{
			case VirtualKey.Enter:
				if (tagMultiSelectMode && selectedAvailableTags.Count > 0)
				{
					e.Handled = true;
					addSelectedTagsAsync();
				}
				break;
			case VirtualKey.Escape:
				e.Handled = true;
				OnClose?.Invoke();
				break;
			case VirtualKey.Left:
				e.Handled = true;
				OnGoPrev?.Invoke();
				break;
			case VirtualKey.Right:
				e.Handled = true;
				OnGoNext?.Invoke();
				break;
			case VirtualKey.Back:
				e.Handled = true;
				OnGoBack?.Invoke();
				break;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.Page_KeyDown: {value}");
		}
	}

	private async void OpenWorld_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (OnOpenWorld != null)
			{
				await OnOpenWorld().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.OpenWorld_Click: {value}");
		}
	}

	private async void OpenExplorer_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (OnOpenExplorer != null)
			{
				await OnOpenExplorer().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.OpenExplorer_Click: {value}");
		}
	}

	private async void Tweet_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (OnTweet != null)
			{
				await OnTweet().ConfigureAwait(continueOnCapturedContext: false);
				base.DispatcherQueue?.TryEnqueue(ShowClipboardOverlay);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.Tweet_Click: {value}");
		}
	}

	private void ShowClipboardOverlay()
	{
		try
		{
			AnimationHelper.FadeInOut(ClipboardOverlay);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.ShowClipboardOverlay: {value}");
		}
	}

	private async void Favorite_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (OnToggleFavorite != null)
			{
				await OnToggleFavorite().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.Favorite_Click: {value}");
		}
	}

	private async void AvailableTag_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!((sender as Button)?.Tag is string text) || string.IsNullOrEmpty(text))
			{
				return;
			}
			bool flag = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
			if (tagMultiSelectMode)
			{
				if (!selectedAvailableTags.Add(text))
				{
					selectedAvailableTags.Remove(text);
				}
				if (selectedAvailableTags.Count == 0)
				{
					tagMultiSelectMode = false;
				}
				rebuildAvailableTags();
			}
			else if (flag)
			{
				tagMultiSelectMode = true;
				selectedAvailableTags.Add(text);
				rebuildAvailableTags();
			}
			else if (currentTagCount() >= 10)
			{
				showTagLimitHint();
			}
			else
			{
				string text2 = viewModel.state.SelectedPhoto?.PhotoPath;
				if (text2 != null && OnAddTag != null)
				{
					await OnAddTag(text2, text);
					rebuildAvailableTags();
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.AvailableTag_Click: {value}");
		}
	}

	private async Task addSelectedTagsAsync()
	{
		try
		{
			string photoPath = viewModel.state.SelectedPhoto?.PhotoPath;
			if (photoPath == null || OnAddTag == null)
			{
				return;
			}
			List<string> list = selectedAvailableTags.ToList();
			resetTagMultiSelect();
			int num = 10 - currentTagCount();
			bool overflow = list.Count > num;
			if (num < 0)
			{
				num = 0;
			}
			foreach (string item in list.Take(num))
			{
				await OnAddTag(photoPath, item);
			}
			rebuildAvailableTags();
			if (overflow)
			{
				showTagLimitHint();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.addSelectedTagsAsync: {value}");
		}
	}

	private void OpenTagMaster_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnOpenTagMaster?.Invoke();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.OpenTagMaster_Click: {value}");
		}
	}

	public void ReleaseImage()
	{
		try
		{
			ModalImage.Source = null;
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.ReleaseImage: {value}");
		}
	}

	private void ModalImage_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
	{
		try
		{
			if (ImageScroll.ZoomFactor > 1.01f)
			{
				ImageScroll.ChangeView(0.0, 0.0, 1f);
				return;
			}
			Point position = e.GetPosition(ImageScroll);
			double value = (ImageScroll.HorizontalOffset + position.X) * 2.0 - ImageScroll.ViewportWidth / 2.0;
			double value2 = (ImageScroll.VerticalOffset + position.Y) * 2.0 - ImageScroll.ViewportHeight / 2.0;
			ImageScroll.ChangeView(value, value2, 2f);
		}
		catch (Exception value3)
		{
			AppLogger.Error($"PhotoModalPage.ModalImage_DoubleTapped: {value3}");
		}
	}

	private void resetZoom()
	{
		try
		{
			ImageScroll?.ChangeView(0.0, 0.0, 1f, disableAnimation: true);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.resetZoom: {value}");
		}
	}

	private void syncPositionIndicator()
	{
		try
		{
			int currentIndex = viewModel.state.CurrentIndex;
			int totalInList = viewModel.state.TotalInList;
			if (totalInList > 0 && currentIndex > 0)
			{
				PositionIndicatorText.Text = $"{currentIndex} / {totalInList}";
				PositionIndicator.Visibility = Visibility.Visible;
			}
			else
			{
				PositionIndicator.Visibility = Visibility.Collapsed;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.syncPositionIndicator: {value}");
		}
	}

	private void syncPhotoMeta()
	{
		try
		{
			string text = viewModel.state.SelectedPhoto?.Timestamp;
			if (string.IsNullOrEmpty(text))
			{
				PhotoTimestampText.Visibility = Visibility.Collapsed;
				return;
			}
			PhotoTimestampText.Visibility = Visibility.Visible;
			PhotoTimestampText.Text = text;
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.syncPhotoMeta: {value}");
		}
	}

	private async void RemoveTag_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			string text = (sender as FrameworkElement)?.Tag as string;
			string text2 = viewModel.state.SelectedPhoto?.PhotoPath;
			if (!string.IsNullOrEmpty(text) && text2 != null && OnRemoveTag != null)
			{
				await OnRemoveTag(text2, text);
				rebuildAvailableTags();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.RemoveTag_Click: {value}");
		}
	}

	private void PrevPhoto_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnGoPrev?.Invoke();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.PrevPhoto_Click: {value}");
		}
	}

	private void NextPhoto_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OnGoNext?.Invoke();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.NextPhoto_Click: {value}");
		}
	}

	private void syncNavButtons()
	{
		try
		{
			bool canGoPrev = viewModel.state.CanGoPrev;
			bool canGoNext = viewModel.state.CanGoNext;
			PrevPhotoButton.IsEnabled = canGoPrev;
			NextPhotoButton.IsEnabled = canGoNext;
			applyNavEdge(PrevEdgeGradientStop, PrevPhotoIcon, canGoPrev, navHovered || prevEdgeHovered);
			applyNavEdge(NextEdgeGradientStop, NextPhotoIcon, canGoNext, navHovered || nextEdgeHovered);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.syncNavButtons: {value}");
		}
	}

	private static void applyNavEdge(GradientStop edgeStop, UIElement icon, bool canGo, bool hovered)
	{
		byte a = (byte)((!canGo) ? 34 : (hovered ? 153 : 85));
		edgeStop.Color = Color.FromArgb(a, 0, 0, 0);
		icon.Opacity = (canGo ? 1.0 : 0.3);
	}

	private void ImagePanel_PointerEntered(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			navHovered = true;
			syncNavButtons();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.ImagePanel_PointerEntered: {value}");
		}
	}

	private void ImagePanel_PointerExited(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			navHovered = false;
			syncNavButtons();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.ImagePanel_PointerExited: {value}");
		}
	}

	private void NavEdge_PointerEntered(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if (sender == PrevPhotoButton)
			{
				prevEdgeHovered = true;
			}
			else if (sender == NextPhotoButton)
			{
				nextEdgeHovered = true;
			}
			syncNavButtons();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.NavEdge_PointerEntered: {value}");
		}
	}

	private void NavEdge_PointerExited(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if (sender == PrevPhotoButton)
			{
				prevEdgeHovered = false;
			}
			else if (sender == NextPhotoButton)
			{
				nextEdgeHovered = false;
			}
			syncNavButtons();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.NavEdge_PointerExited: {value}");
		}
	}

	private void BottomAction_PointerEntered(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if (sender is Button button)
			{
				Brush brush = ThemeHelper.Brush(button, "ASurfaceHover");
				if ((object)brush != null)
				{
					button.Background = brush;
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.BottomAction_PointerEntered: {value}");
		}
	}

	private void BottomAction_PointerExited(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if (sender is Button button)
			{
				button.Background = new SolidColorBrush(Colors.Transparent);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalPage.BottomAction_PointerExited: {value}");
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/PhotoModal/PhotoModalPage.xaml");
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
		{
			Page page = target.As<Page>();
			page.Loaded += Page_Loaded;
			page.Unloaded += Page_Unloaded;
			page.PreviewKeyDown += Page_PreviewKeyDown;
			break;
		}
		case 2:
			target.As<Grid>().Tapped += Backdrop_Tapped;
			break;
		case 3:
			target.As<Border>().Tapped += ModalContent_Tapped;
			break;
		case 4:
			ClipboardOverlay = target.As<Grid>();
			break;
		case 5:
			ImagePanel = target.As<Grid>();
			ImagePanel.PointerEntered += ImagePanel_PointerEntered;
			ImagePanel.PointerExited += ImagePanel_PointerExited;
			break;
		case 6:
		{
			Button button4 = target.As<Button>();
			button4.Click += Favorite_Click;
			button4.PointerEntered += BottomAction_PointerEntered;
			button4.PointerExited += BottomAction_PointerExited;
			break;
		}
		case 7:
		{
			Button button3 = target.As<Button>();
			button3.Click += Tweet_Click;
			button3.PointerEntered += BottomAction_PointerEntered;
			button3.PointerExited += BottomAction_PointerExited;
			break;
		}
		case 8:
		{
			Button button2 = target.As<Button>();
			button2.Click += OpenWorld_Click;
			button2.PointerEntered += BottomAction_PointerEntered;
			button2.PointerExited += BottomAction_PointerExited;
			break;
		}
		case 9:
		{
			Button button = target.As<Button>();
			button.Click += OpenExplorer_Click;
			button.PointerEntered += BottomAction_PointerEntered;
			button.PointerExited += BottomAction_PointerExited;
			break;
		}
		case 10:
			OpenTagMasterButton = target.As<Button>();
			OpenTagMasterButton.Click += OpenTagMaster_Click;
			break;
		case 11:
			MultiSelectHint = target.As<TextBlock>();
			break;
		case 12:
			EmptyTagNote = target.As<TextBlock>();
			break;
		case 13:
			AvailableTagsPanel = target.As<WrapPanel>();
			break;
		case 14:
			AppliedTagsEmptyNote = target.As<TextBlock>();
			break;
		case 15:
			AppliedTagsList = target.As<ItemsControl>();
			break;
		case 16:
			TagLimitHint = target.As<TextBlock>();
			break;
		case 17:
			target.As<Button>().Click += RemoveTag_Click;
			break;
		case 18:
			CloseButtonOverlay = target.As<Button>();
			CloseButtonOverlay.Click += CloseButton_Click;
			break;
		case 19:
			WorldNameText = target.As<TextBlock>();
			break;
		case 20:
			MatchSourcePanel = target.As<StackPanel>();
			break;
		case 21:
			PhotoMetaPanel = target.As<WrapPanel>();
			break;
		case 22:
			PhotoTimestampText = target.As<TextBlock>();
			break;
		case 23:
			MatchSourceLabel = target.As<TextBlock>();
			break;
		case 24:
			ImageScroll = target.As<ScrollViewer>();
			ImageScroll.SizeChanged += ImageScroll_SizeChanged;
			ImageScroll.DoubleTapped += ModalImage_DoubleTapped;
			break;
		case 25:
			PrevPhotoButton = target.As<Button>();
			PrevPhotoButton.Click += PrevPhoto_Click;
			PrevPhotoButton.PointerEntered += NavEdge_PointerEntered;
			PrevPhotoButton.PointerExited += NavEdge_PointerExited;
			break;
		case 26:
			NextPhotoButton = target.As<Button>();
			NextPhotoButton.Click += NextPhoto_Click;
			NextPhotoButton.PointerEntered += NavEdge_PointerEntered;
			NextPhotoButton.PointerExited += NavEdge_PointerExited;
			break;
		case 27:
			PositionIndicator = target.As<Border>();
			break;
		case 28:
			PositionIndicatorText = target.As<TextBlock>();
			break;
		case 29:
			NextEdgeGradientStop = target.As<GradientStop>();
			break;
		case 30:
			NextPhotoIcon = target.As<AppIcon>();
			break;
		case 31:
			PrevEdgeGradientStop = target.As<GradientStop>();
			break;
		case 32:
			PrevPhotoIcon = target.As<AppIcon>();
			break;
		case 33:
			ModalImage = target.As<Image>();
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
