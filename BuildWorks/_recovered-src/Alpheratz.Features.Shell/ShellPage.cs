using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Bootstrap;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.Gallery.Controls;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Features.Settings;
using Alpheratz.Features.Shell.Controls;
using Alpheratz.Features.WorldResolve;
using Alpheratz.Shared.Animations;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;
using Windows.System;
using Windows.UI.Core;

namespace Alpheratz.Features.Shell;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_WorldResolve_WorldResolvePageWinRTTypeDetails))]
public sealed class ShellPage : Page, IComponentConnector
{
	private bool runtimeStateWired;

	private AppLifecycleService? lifecycleService;

	private readonly ShellViewModel viewModel;

	private GalleryPage? galleryPage;

	private SettingsPage? settingsPage;

	private PhotoModalPage? activePhotoModalPage;

	private bool isModalOpen;

	private long lastModalOpenTick;

	private bool isFilterOpen;

	private TaskCompletionSource<bool?>? confirmTcs;

	private GroupDrillDownPage? drillDownPage;

	private IReadOnlyList<PhotoThumbnailItem>? drillDownPhotos;

	private bool isMiddleModalOpen;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ShellHeaderBar HeaderBar;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ShellStage Stage;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid FilterOverlay;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid ConfirmOverlay;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock ConfirmTitle;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock ConfirmMessage;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button ConfirmNoButton;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button ConfirmYesButton;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border FilterBackdrop;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border FilterPanelContainer;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private GalleryFilterPanel FilterPanel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	private void ShellPage_Loaded(object sender, RoutedEventArgs e)
	{
		if (!runtimeStateWired)
		{
			runtimeStateWired = true;
			lifecycleService = App.Services?.GetService<AppLifecycleService>();
			if (lifecycleService == null)
			{
				AppLogger.Warn("ShellPage.ShellPage_Loaded: AppLifecycleService not resolved");
			}
			ApplyTheme(viewModel.ThemeMode);
			SyncFilterPanelMinWidth();
			base.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, delegate
			{
				lifecycleService?.advanceTo(AppLifecyclePhase.uiReady);
			});
		}
	}

	private void ApplyTheme(ThemeMode mode)
	{
		try
		{
			ElementTheme elementTheme = (base.RequestedTheme = ((mode != ThemeMode.dark) ? ElementTheme.Light : ElementTheme.Dark));
			if (App.MainWindowInstance is MainWindow mainWindow)
			{
				mainWindow.SetTheme(elementTheme);
			}
			if ((object)settingsPage != null)
			{
				settingsPage.RequestedTheme = elementTheme;
			}
			if ((object)galleryPage != null)
			{
				galleryPage.RequestedTheme = elementTheme;
			}
			if ((object)drillDownPage != null)
			{
				drillDownPage.RequestedTheme = elementTheme;
			}
			if (Stage.TopModalContent is FrameworkElement frameworkElement)
			{
				frameworkElement.RequestedTheme = elementTheme;
			}
			if (Stage.ModalContent is FrameworkElement frameworkElement2)
			{
				frameworkElement2.RequestedTheme = elementTheme;
			}
			FilterPanel.RequestedTheme = elementTheme;
			ThemeHelper.NotifySelectedThemeChanged(elementTheme);
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.ApplyTheme: threw: {value}");
		}
	}

	private void ShellPage_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		SyncFilterPanelMinWidth();
	}

	private void SyncFilterPanelMinWidth()
	{
		try
		{
			double actualWidth = base.ActualWidth;
			if (!(actualWidth <= 0.0))
			{
				FilterPanelContainer.MinWidth = actualWidth * 0.3;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.SyncFilterPanelMinWidth: {value}");
		}
	}

	private void ShellPage_Unloaded(object sender, RoutedEventArgs e)
	{
		try
		{
			viewModel.galleryViewModel.drillDownPhotosProvider = null;
			viewModel.galleryViewModel.selectionState.PropertyChanged -= OnSelectionStateChanged;
			viewModel.PropertyChanged -= OnShellViewModelChanged;
			drillDownPhotos = null;
			if ((object)drillDownPage != null)
			{
				drillDownPage.OnBack = null;
				drillDownPage.OnPhotoActivated = null;
				drillDownPage.OnFavoriteClicked = null;
				drillDownPage.OnThumbnailsNeeded = null;
				drillDownPage = null;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.ShellPage_Unloaded: threw: {value}");
		}
	}

	private void UpdateScanningOverlayVisibility()
	{
		Stage.ScanningOverlayVisibility = ((!(viewModel.ScanStatus == "scanning")) ? Visibility.Collapsed : Visibility.Visible);
	}

	public ShellPage(ShellViewModel viewModel)
	{
		ShellPage shellPage = this;
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.ctor: InitializeComponent failed: {value}");
			throw;
		}
		this.viewModel = viewModel;
		base.DataContext = viewModel;
		try
		{
			HeaderBar.OnToggleFilter = ToggleFilter;
			HeaderBar.OnShowSettings = ShowSettings;
			HeaderBar.OnToggleMultiSelect = delegate
			{
				viewModel.galleryViewModel.selectionState.handleToggleMultiSelectMode();
				shellPage.HeaderBar.SetMultiSelectActive(viewModel.galleryViewModel.selectionState.IsMultiSelectMode);
			};
			HeaderBar.OnGroupingChange = async delegate(GroupingMode mode)
			{
				if (mode != GroupingMode.none && viewModel.ViewMode == ViewMode.gallery)
				{
					await viewModel.handleSetViewMode(ViewMode.standard).ConfigureAwait(continueOnCapturedContext: true);
				}
				viewModel.galleryViewModel.displayState.prepareGroupingModeChange(viewModel.galleryViewModel.filtersState.GroupingMode, mode, delegate(GroupingMode m)
				{
					viewModel.galleryViewModel.filtersState.GroupingMode = m;
				});
				shellPage.HeaderBar.SetGroupingMode(mode);
			};
			HeaderBar.OnViewModeChange = async delegate(string modeStr)
			{
				ViewMode nextMode = ((modeStr == "gallery") ? ViewMode.gallery : ViewMode.standard);
				await viewModel.handleSetViewMode(nextMode).ConfigureAwait(continueOnCapturedContext: false);
			};
			HeaderBar.OnSearchSubmit = delegate
			{
				viewModel.galleryViewModel.applySearchNow();
			};
			viewModel.galleryViewModel.selectionState.PropertyChanged += OnSelectionStateChanged;
			viewModel.PropertyChanged += OnShellViewModelChanged;
			viewModel.galleryViewModel.drillDownPhotosProvider = () => shellPage.drillDownPhotos;
			Stage.ScanningOverlayControlRef.OnCancelScan = viewModel.cancelScan;
		}
		catch (Exception value2)
		{
			AppLogger.Error($"ShellPage.ctor: wiring failed: {value2}");
			throw;
		}
		ShowGallery();
		HeaderBar.SetViewMode(viewModel.ViewMode);
		HeaderBar.SetGroupingMode(viewModel.galleryViewModel.filtersState.GroupingMode);
		HeaderBar.SetPdqProgress(viewModel.IsPdqRunning, viewModel.PdqProgress?.done ?? 0, viewModel.PdqProgress?.total ?? 0);
	}

	private void OnSelectionStateChanged(object? sender, PropertyChangedEventArgs e)
	{
		try
		{
			if (e.PropertyName == "IsMultiSelectMode")
			{
				HeaderBar.SetMultiSelectActive(viewModel.galleryViewModel.selectionState.IsMultiSelectMode);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.OnSelectionStateChanged: threw: {value}");
		}
	}

	private void OnShellViewModelChanged(object? sender, PropertyChangedEventArgs e)
	{
		try
		{
			if (e.PropertyName == "ViewMode")
			{
				HeaderBar.SetViewMode(viewModel.ViewMode);
				HeaderBar.SetGroupingMode(viewModel.galleryViewModel.filtersState.GroupingMode);
			}
			else if (e.PropertyName == "ScanStatus")
			{
				UpdateScanningOverlayVisibility();
			}
			else if (e.PropertyName == "PendingFolderPath" && viewModel.PendingFolderPath != null)
			{
				ShowFolderChangeConfirmAsync();
			}
			else if (e.PropertyName == "PendingResetRequest" && (object)viewModel.PendingResetRequest != null)
			{
				ShowResetConfirmAsync();
			}
			else if (e.PropertyName == "ThemeMode")
			{
				ApplyTheme(viewModel.ThemeMode);
			}
			else if (e.PropertyName == "IsPdqRunning" || e.PropertyName == "PdqProgress")
			{
				HeaderBar.SetPdqProgress(viewModel.IsPdqRunning, viewModel.PdqProgress?.done ?? 0, viewModel.PdqProgress?.total ?? 0);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.OnShellViewModelChanged: threw: {value}");
		}
	}

	private Task<bool?> ShowConfirmDialogAsync(string title, string message, string yesText, string noText, string? cancelText = null)
	{
		try
		{
			ConfirmTitle.Text = title;
			ConfirmMessage.Text = message;
			ConfirmYesButton.Content = yesText;
			ConfirmNoButton.Content = noText;
			confirmTcs?.TrySetResult(null);
			confirmTcs = new TaskCompletionSource<bool?>();
			ConfirmOverlay.Visibility = Visibility.Visible;
			return confirmTcs.Task;
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.ShowConfirmDialogAsync: threw: {value}");
			return Task.FromResult<bool?>(null);
		}
	}

	private void CloseConfirmDialog(bool? result)
	{
		ConfirmOverlay.Visibility = Visibility.Collapsed;
		TaskCompletionSource<bool?>? taskCompletionSource = confirmTcs;
		confirmTcs = null;
		taskCompletionSource?.TrySetResult(result);
	}

	private void ConfirmYes_Click(object sender, RoutedEventArgs e)
	{
		CloseConfirmDialog(true);
	}

	private void ConfirmNo_Click(object sender, RoutedEventArgs e)
	{
		CloseConfirmDialog(false);
	}

	private void ConfirmBackdrop_Tapped(object sender, TappedRoutedEventArgs e)
	{
		CloseConfirmDialog(null);
	}

	private void ConfirmContent_Tapped(object sender, TappedRoutedEventArgs e)
	{
		e.Handled = true;
	}

	private async Task ShowFolderChangeConfirmAsync()
	{
		string newPath = viewModel.PendingFolderPath;
		if (!string.IsNullOrEmpty(newPath))
		{
			if (await ShowConfirmDialogAsync("写真フォルダの変更", "新しい写真フォルダ:\n" + newPath + "\n\n既存のキャッシュをリセットして変更しますか？\n（タグは失われる可能性があります）", "変更する", "キャンセル").ConfigureAwait(continueOnCapturedContext: true) != true)
			{
				viewModel.PendingFolderPath = null;
			}
			else
			{
				await viewModel.applyFolderChange(newPath).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}

	private async Task ShowResetConfirmAsync()
	{
		if ((object)viewModel.PendingResetRequest != null)
		{
			if (await ShowConfirmDialogAsync("写真フォルダのリセット", "現在の写真フォルダ設定とキャッシュをリセットします。", "リセットする", "キャンセル").ConfigureAwait(continueOnCapturedContext: true) != true)
			{
				viewModel.PendingResetRequest = null;
			}
			else
			{
				await viewModel.executeResetFolder(viewModel.PendingResetRequest.slot).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}

	public void ShowGallery()
	{
		try
		{
			if ((object)galleryPage == null)
			{
				galleryPage = new GalleryPage(viewModel.galleryViewModel, FilterPanel)
				{
					OnResetFilters = viewModel.galleryViewModel.resetFilters,
					OnDatePresetSelect = delegate(string preset)
					{
						viewModel.galleryViewModel.filtersState.handleDatePresetSelect(preset);
					},
					OnSelectPhoto = delegate(PhotoThumbnailItem photo)
					{
						PhotoModalViewModel photoModalViewModel = viewModel.createPhotoModalViewModel(photo);
						if (photoModalViewModel != null)
						{
							ShowPhotoModal(photoModalViewModel);
						}
					},
					OnDrillIntoGroup = delegate(PhotoGridItem item)
					{
						ShowGroupDrillDown(item);
					},
					OnChooseFolder = () => viewModel.settingsViewModel.handleChooseFolderPathOnly(),
					OnOpenSettings = ShowSettings
				};
				galleryPage.SetMasterTags(viewModel.tagMasterViewModel.masterTags);
			}
			Stage.MainContent = galleryPage;
			HeaderBar.SetViewMode(viewModel.ViewMode);
			HeaderBar.SetGroupingMode(viewModel.galleryViewModel.filtersState.GroupingMode);
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.ShowGallery: threw: {value}");
		}
	}

	private void syncHeaderInteractivity()
	{
		try
		{
			bool flag = isModalOpen || isMiddleModalOpen;
			bool flag2 = flag || isFilterOpen;
			HeaderBar.SetControlsInteractive(!flag2);
			HeaderBar.Opacity = (flag ? 0.4 : (isFilterOpen ? 0.6 : 1.0));
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.syncHeaderInteractivity: threw: {value}");
		}
	}

	private async Task ShowGroupDrillDown(PhotoGridItem groupItem)
	{
		try
		{
			string groupKey = groupItem.GroupKey;
			if (string.IsNullOrEmpty(groupKey))
			{
				return;
			}
			IReadOnlyList<PhotoThumbnailItem> items = (drillDownPhotos = await viewModel.galleryViewModel.getGroupPhotosAsync(groupKey).ConfigureAwait(continueOnCapturedContext: true));
			drillDownPage = new GroupDrillDownPage
			{
				OnBack = CloseMiddleModal,
				OnPhotoActivated = delegate(PhotoThumbnailItem photo)
				{
					if (drillDownPhotos != null)
					{
						PhotoModalViewModel photoModalViewModel = viewModel.createPhotoModalViewModelFromList(photo, drillDownPhotos);
						if (photoModalViewModel != null)
						{
							ShowPhotoModal(photoModalViewModel);
						}
					}
				},
				OnFavoriteClicked = delegate(PhotoThumbnailItem photo)
				{
					viewModel.galleryViewModel.toggleFavorite(photo.PhotoPath, photo.IsFavorite);
				},
				OnThumbnailsNeeded = delegate(IReadOnlyList<PhotoThumbnailItem> items2)
				{
					viewModel.galleryViewModel.photosState.kickThumbnailsForExternal(items2);
				}
			};
			string groupName = (string.IsNullOrWhiteSpace(groupItem.Photo?.WorldName) ? "ワールド不明" : groupItem.Photo.WorldName);
			drillDownPage.SetGroupInfo(groupName, items);
			Stage.ModalContent = drillDownPage;
			Stage.ModalVisibility = Visibility.Visible;
			isMiddleModalOpen = true;
			lastModalOpenTick = Environment.TickCount64;
			syncHeaderInteractivity();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.ShowGroupDrillDown: threw: {value}");
		}
	}

	private void CloseMiddleModal()
	{
		try
		{
			isMiddleModalOpen = false;
			drillDownPhotos = null;
			Stage.ModalVisibility = Visibility.Collapsed;
			syncHeaderInteractivity();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.CloseMiddleModal: threw: {value}");
		}
	}

	public void ShowSettings()
	{
		if (isModalOpen || isMiddleModalOpen || isFilterOpen)
		{
			return;
		}
		try
		{
			if ((object)settingsPage == null)
			{
				SettingsCompositeViewModel settingsCompositeViewModel = new SettingsCompositeViewModel(viewModel.settingsViewModel, viewModel.tagMasterViewModel, viewModel.templatePageViewModel);
				settingsPage = new SettingsPage(settingsCompositeViewModel)
				{
					OnClose = CloseMiddleModal,
					OnChooseFolder = async delegate(int slot)
					{
						string text = await viewModel.settingsViewModel.handleChooseFolderPathOnly().ConfigureAwait(continueOnCapturedContext: false);
						if (!string.IsNullOrWhiteSpace(text))
						{
							string text2 = ((slot == 1) ? viewModel.PhotoFolderPath : viewModel.SecondaryPhotoFolderPath);
							if (!string.Equals(text, text2, StringComparison.Ordinal))
							{
								if (slot == 1 && !string.IsNullOrEmpty(text2))
								{
									viewModel.promptFolderChange(slot, text);
								}
								else
								{
									viewModel.PendingFolderSlot = slot;
									await viewModel.applyFolderChange(text).ConfigureAwait(continueOnCapturedContext: false);
								}
							}
						}
					},
					OnResetFolder = viewModel.handleResetFolder,
					OnStartupPreferenceChanged = viewModel.handleStartupPreference,
					OnThemeChanged = (bool isDark) => viewModel.handleThemeChange(isDark ? ThemeMode.dark : ThemeMode.light),
					OnStartWorldAnalysis = ShowWorldResolveModalAsync,
					OnCreateTag = viewModel.tagMasterViewModel.createTag,
					OnDeleteTag = viewModel.tagMasterViewModel.deleteTag,
					OnCancelEdit = viewModel.templatePageViewModel.cancelEdit,
					OnStartEdit = viewModel.templatePageViewModel.startEdit,
					OnDeleteTemplate = (string template) => viewModel.templatePageViewModel.deleteTemplate(template, viewModel.buildSettingPayload()),
					OnSaveTemplate = () => viewModel.templatePageViewModel.saveTemplate(viewModel.buildSettingPayload()),
					OnSelectTemplate = async delegate(string template)
					{
						viewModel.templatePageViewModel.ActiveTweetTemplate = template;
						await viewModel.templatePageViewModel.saveTemplates(viewModel.buildSettingPayload()).ConfigureAwait(continueOnCapturedContext: false);
					}
				};
			}
			Stage.ModalContent = settingsPage;
			Stage.ModalVisibility = Visibility.Visible;
			isMiddleModalOpen = true;
			lastModalOpenTick = Environment.TickCount64;
			syncHeaderInteractivity();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.ShowSettings: threw: {value}");
		}
	}

	public void ShowPhotoModal(PhotoModalViewModel modalViewModel)
	{
		try
		{
			PhotoModalPage photoModalPage = new PhotoModalPage(modalViewModel);
			photoModalPage.RequestedTheme = base.RequestedTheme;
			photoModalPage.OnAddTag = (string photoPath, string tag) => viewModel.galleryViewModel.addTag(photoPath, tag);
			photoModalPage.OnRemoveTag = (string photoPath, string tag) => viewModel.galleryViewModel.removeTag(photoPath, tag);
			photoModalPage.OnClose = delegate
			{
				modalViewModel.closePhotoModal();
				CloseModal();
			};
			photoModalPage.OnOpenWorld = modalViewModel.handleOpenWorld;
			photoModalPage.OnOpenExplorer = modalViewModel.handleOpenExplorer;
			photoModalPage.OnOpenTagMaster = delegate
			{
				modalViewModel.closePhotoModal();
				CloseModal();
				ShowSettings();
			};
			photoModalPage.OnToggleFavorite = async delegate
			{
				PhotoThumbnailItem selectedPhoto = modalViewModel.state.SelectedPhoto;
				if (selectedPhoto != null)
				{
					bool newValue = !selectedPhoto.IsFavorite;
					await viewModel.galleryViewModel.toggleFavorite(selectedPhoto.PhotoPath, selectedPhoto.IsFavorite).ConfigureAwait(continueOnCapturedContext: false);
					base.DispatcherQueue?.TryEnqueue(delegate
					{
						selectedPhoto.IsFavorite = newValue;
					});
				}
			};
			photoModalPage.OnTweet = async delegate
			{
				PhotoThumbnailItem selectedPhoto = modalViewModel.state.SelectedPhoto;
				if (selectedPhoto != null)
				{
					await viewModel.templatePageViewModel.openTweetIntent(selectedPhoto).ConfigureAwait(continueOnCapturedContext: false);
				}
			};
			photoModalPage.OnGoBack = delegate
			{
				modalViewModel.goBackPhoto();
			};
			photoModalPage.OnGoPrev = delegate
			{
				modalViewModel.state.goPrevPhoto();
			};
			photoModalPage.OnGoNext = delegate
			{
				modalViewModel.state.goNextPhoto();
			};
			photoModalPage.SetMasterTags(viewModel.tagMasterViewModel.masterTags);
			Stage.TopModalContent = photoModalPage;
			Stage.TopModalVisibility = Visibility.Visible;
			activePhotoModalPage = photoModalPage;
			isModalOpen = true;
			lastModalOpenTick = Environment.TickCount64;
			syncHeaderInteractivity();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.ShowPhotoModal: threw: {value}");
		}
	}

	private async Task ShowWorldResolveModalAsync()
	{
		try
		{
			WorldResolveViewModel worldResolveViewModel = viewModel.CreateWorldResolveViewModel();
			WorldResolvePage modalContent = new WorldResolvePage(worldResolveViewModel)
			{
				OnClose = CloseMiddleModal,
				OnApplied = async delegate
				{
					await viewModel.galleryViewModel.photosState.loadPhotos().ConfigureAwait(continueOnCapturedContext: false);
					viewModel.toastService.addToast("ワールド情報を適用しました");
				}
			};
			Stage.ModalContent = modalContent;
			Stage.ModalVisibility = Visibility.Visible;
			isMiddleModalOpen = true;
			lastModalOpenTick = Environment.TickCount64;
			syncHeaderInteractivity();
			await worldResolveViewModel.InitializeAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.ShowWorldResolveModalAsync: threw: {value}");
		}
	}

	public void CloseModal()
	{
		try
		{
			isModalOpen = false;
			activePhotoModalPage = null;
			Stage.TopModalVisibility = Visibility.Collapsed;
			syncHeaderInteractivity();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.CloseModal: threw: {value}");
		}
	}

	private void ToggleFilter()
	{
		try
		{
			if (isFilterOpen || (!isModalOpen && !isMiddleModalOpen))
			{
				isFilterOpen = !isFilterOpen;
				SetFilterOverlayOpen(isFilterOpen);
				syncHeaderInteractivity();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.ToggleFilter: threw: {value}");
		}
	}

	private void SetFilterOverlayOpen(bool isOpen)
	{
		if (isOpen)
		{
			FilterOverlay.Visibility = Visibility.Visible;
			AnimationHelper.SlideIn(FilterPanelContainer, -16f);
			return;
		}
		AnimationHelper.SlideOut(FilterPanelContainer, -16f, 0f, 180, delegate
		{
			base.DispatcherQueue?.TryEnqueue(delegate
			{
				FilterOverlay.Visibility = Visibility.Collapsed;
			});
		});
	}

	private void FilterBackdrop_Tapped(object sender, TappedRoutedEventArgs e)
	{
		if (isFilterOpen)
		{
			ToggleFilter();
		}
	}

	private void FilterPanelContainer_Tapped(object sender, TappedRoutedEventArgs e)
	{
		e.Handled = true;
	}

	private void ShellPage_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
	{
		try
		{
			if (isModalOpen && (object)activePhotoModalPage != null && !(FocusManager.GetFocusedElement(base.XamlRoot) is TextBox))
			{
				switch (e.Key)
				{
				case VirtualKey.Left:
					e.Handled = true;
					activePhotoModalPage.OnGoPrev?.Invoke();
					break;
				case VirtualKey.Right:
					e.Handled = true;
					activePhotoModalPage.OnGoNext?.Invoke();
					break;
				case VirtualKey.Escape:
					e.Handled = true;
					activePhotoModalPage.OnClose?.Invoke();
					break;
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.ShellPage_PreviewKeyDown: threw: {value}");
		}
	}

	private void ShellPage_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		try
		{
			bool flag = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
			if (e.Key == VirtualKey.Escape)
			{
				if (ConfirmOverlay.Visibility == Visibility.Visible)
				{
					CloseConfirmDialog(null);
					e.Handled = true;
					return;
				}
				if (isFilterOpen)
				{
					ToggleFilter();
					e.Handled = true;
					return;
				}
				if (viewModel.galleryViewModel.selectionState.IsMultiSelectMode)
				{
					viewModel.galleryViewModel.selectionState.handleToggleMultiSelectMode();
					e.Handled = true;
					return;
				}
			}
			if (isModalOpen || isMiddleModalOpen)
			{
				return;
			}
			if (flag && e.Key == VirtualKey.F)
			{
				if (!isFilterOpen)
				{
					ToggleFilter();
				}
				e.Handled = true;
			}
			else if (flag && e.Key == (VirtualKey)188)
			{
				ShowSettings();
				e.Handled = true;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellPage.ShellPage_KeyDown: threw: {value}");
		}
	}

	private void ModalDismissArea_Tapped(object sender, TappedRoutedEventArgs e)
	{
		if (Environment.TickCount64 - lastModalOpenTick >= 400)
		{
			if (isModalOpen)
			{
				e.Handled = true;
				(Stage.TopModalContent as PhotoModalPage)?.OnClose?.Invoke();
			}
			else if (isMiddleModalOpen)
			{
				e.Handled = true;
				CloseMiddleModal();
			}
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/Shell/ShellPage.xaml");
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
			page.Loaded += ShellPage_Loaded;
			page.Unloaded += ShellPage_Unloaded;
			page.PreviewKeyDown += ShellPage_PreviewKeyDown;
			page.KeyDown += ShellPage_KeyDown;
			page.SizeChanged += ShellPage_SizeChanged;
			break;
		}
		case 2:
			HeaderBar = target.As<ShellHeaderBar>();
			HeaderBar.Tapped += ModalDismissArea_Tapped;
			break;
		case 3:
			Stage = target.As<ShellStage>();
			break;
		case 4:
			FilterOverlay = target.As<Grid>();
			break;
		case 5:
			ConfirmOverlay = target.As<Grid>();
			break;
		case 6:
			target.As<Border>().Tapped += ConfirmBackdrop_Tapped;
			break;
		case 7:
			target.As<Border>().Tapped += ConfirmContent_Tapped;
			break;
		case 8:
			ConfirmTitle = target.As<TextBlock>();
			break;
		case 9:
			ConfirmMessage = target.As<TextBlock>();
			break;
		case 10:
			ConfirmNoButton = target.As<Button>();
			ConfirmNoButton.Click += ConfirmNo_Click;
			break;
		case 11:
			ConfirmYesButton = target.As<Button>();
			ConfirmYesButton.Click += ConfirmYes_Click;
			break;
		case 12:
			FilterBackdrop = target.As<Border>();
			FilterBackdrop.Tapped += FilterBackdrop_Tapped;
			break;
		case 13:
			FilterPanelContainer = target.As<Border>();
			FilterPanelContainer.Tapped += FilterPanelContainer_Tapped;
			break;
		case 14:
			FilterPanel = target.As<GalleryFilterPanel>();
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
