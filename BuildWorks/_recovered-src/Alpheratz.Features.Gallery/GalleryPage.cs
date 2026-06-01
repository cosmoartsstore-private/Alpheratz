using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery.Controls;
using Alpheratz.Shared.Animations;
using Alpheratz.Shared.Controls;
using Alpheratz.Shared.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Gallery;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_WorldResolve_WorldResolvePageWinRTTypeDetails))]
public sealed class GalleryPage : Page, IComponentConnector
{
	private readonly GalleryViewModel viewModel;

	private readonly GalleryFilterPanel FilterPanel;

	private bool bulkOpBarWasVisible;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private GalleryGridStage GridStage;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid BulkOpBar;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Border BulkOpBarShadowHost;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock SelectionCountLabel;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button BulkTagBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ComboBox BulkTagCombo;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public Action? OnResetFilters { get; set; }

	public Action<string>? OnDatePresetSelect { get; set; }

	public Action<PhotoThumbnailItem>? OnSelectPhoto { get; set; }

	public Action<PhotoGridItem>? OnDrillIntoGroup { get; set; }

	public Func<Task<string?>>? OnChooseFolder { get; set; }

	public Action? OnOpenSettings { get; set; }

	public GalleryPage(GalleryViewModel viewModel, GalleryFilterPanel filterPanel)
	{
		GalleryPage galleryPage = this;
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.ctor: InitializeComponent failed: {value}");
			throw;
		}
		this.viewModel = viewModel;
		FilterPanel = filterPanel;
		base.DataContext = viewModel;
		try
		{
			FilterPanel.OnResetFilters = delegate
			{
				galleryPage.OnResetFilters?.Invoke();
			};
			FilterPanel.OnDatePresetSelect = delegate(string preset)
			{
				galleryPage.OnDatePresetSelect?.Invoke(preset);
			};
			FilterPanel.OnOrientationSelect = delegate(string orientation)
			{
				viewModel.filtersState.OrientationFilter = orientation;
			};
			FilterPanel.OnSortSelect = delegate(SortMode sort)
			{
				viewModel.filtersState.SortMode = sort;
			};
			FilterPanel.OnDisplayFolderSelect = delegate(DisplayFolderMode mode)
			{
				viewModel.displayState.prepareDisplayFolderModeChange(viewModel.filtersState.DisplayFolderMode, mode, delegate(DisplayFolderMode m)
				{
					viewModel.filtersState.DisplayFolderMode = m;
				});
			};
			FilterPanel.OnGroupingSelect = delegate(GroupingMode mode)
			{
				if (mode == GroupingMode.none || viewModel.displayState.ViewMode != ViewMode.gallery)
				{
					viewModel.displayState.prepareGroupingModeChange(viewModel.filtersState.GroupingMode, mode, delegate(GroupingMode m)
					{
						viewModel.filtersState.GroupingMode = m;
					});
				}
			};
			FilterPanel.OnWorldFilterAdd = delegate(string worldName)
			{
				if (!viewModel.filtersState.worldFilters.Contains(worldName))
				{
					viewModel.filtersState.worldFilters.Add(worldName);
				}
			};
			FilterPanel.OnWorldFilterRemove = delegate(string worldName)
			{
				viewModel.filtersState.worldFilters.Remove(worldName);
			};
			FilterPanel.OnTagFilterAdd = delegate(string tag)
			{
				if (!viewModel.filtersState.tagFilters.Contains(tag))
				{
					viewModel.filtersState.tagFilters.Add(tag);
				}
			};
			FilterPanel.OnTagFilterRemove = delegate(string tag)
			{
				viewModel.filtersState.tagFilters.Remove(tag);
			};
			FilterPanel.setWorldFilterOptions(viewModel.filtersState.worldFilterOptions);
			FilterPanel.bindFiltersState(viewModel.filtersState);
			GridStage.GridDataContext = viewModel.photosState;
			GridStage.SetGridItemsSource(viewModel.photosState.displayItems);
			PhotoGrid photoGridControlRef = GridStage.PhotoGridControlRef;
			if ((object)photoGridControlRef != null)
			{
				photoGridControlRef.OnRightPanelMeasured = delegate(double width)
				{
					viewModel.displayState.rightPanelRef(width);
				};
				photoGridControlRef.OnGridWrapperMeasured = delegate(double height)
				{
					viewModel.displayState.gridWrapperRef(height);
				};
				photoGridControlRef.OnGridScroll = delegate(double offset)
				{
					viewModel.scrollState.handleGridScroll(offset);
				};
				photoGridControlRef.OnGridWheel = delegate(int delta)
				{
					viewModel.scrollState.handleGridWheel(delta);
				};
				photoGridControlRef.OnFirstVisibleIndexChanged = delegate(int idx)
				{
					galleryPage.SyncMonthNavToIndex(idx);
				};
				photoGridControlRef.OnFavoriteClicked = delegate(PhotoGridItem item)
				{
					viewModel.toggleFavorite(item.Photo.PhotoPath, item.Photo.IsFavorite);
				};
				photoGridControlRef.OnPhotoActivated = delegate(PhotoGridItem item)
				{
					if (viewModel.filtersState.GroupingMode != GroupingMode.none && item.GroupKey != null)
					{
						galleryPage.OnDrillIntoGroup?.Invoke(item);
					}
					else
					{
						viewModel.handlePhotoActivate(item, shiftKey: false, delegate(PhotoThumbnailItem photo)
						{
							galleryPage.OnSelectPhoto?.Invoke(photo);
						});
					}
				};
			}
			else
			{
				AppLogger.Warn("GalleryPage.ctor: PhotoGridControlRef is null - callbacks not wired");
			}
			viewModel.selectionState.PropertyChanged += OnSelectionStateChanged;
			viewModel.selectionState.selectedPhotoPaths.CollectionChanged += OnSelectedPathsChanged;
			viewModel.photosState.PropertyChanged += OnPhotosStateChanged;
			GridStage.EmptyStatePrimaryActionRequested += OnEmptyStatePrimaryAction;
			GridStage.OnMasonryRealized = WireMasonryCallbacks;
			MonthNav monthNavControlRef = GridStage.MonthNavControlRef;
			if ((object)monthNavControlRef != null)
			{
				monthNavControlRef.OnJumpToMonth = delegate(GalleryMonthGroup group)
				{
					if (viewModel.displayState.ViewMode == ViewMode.gallery)
					{
						galleryPage.GridStage.MasonryViewControlRef?.ScrollToPhotoIndex(group.FirstIndex);
					}
					else
					{
						galleryPage.GridStage.PhotoGridControlRef?.ScrollToPhotoIndex(group.FirstIndex);
					}
				};
			}
			GridStage.SetMasonryActive(viewModel.displayState.ViewMode == ViewMode.gallery);
			FilterPanel.SetGroupingEnabled(viewModel.displayState.ViewMode != ViewMode.gallery);
			viewModel.photosState.OnMonthGroupsChanged = delegate(IReadOnlyList<GalleryMonthGroup> groups)
			{
				try
				{
					MonthNav nav = galleryPage.GridStage.MonthNavControlRef;
					if ((object)nav != null)
					{
						DispatcherQueue dispatcherQueue = galleryPage.DispatcherQueue;
						if ((object)dispatcherQueue != null)
						{
							dispatcherQueue.TryEnqueue(delegate
							{
								nav.SetGroups(groups);
							});
						}
						else
						{
							nav.SetGroups(groups);
						}
					}
				}
				catch (Exception value3)
				{
					AppLogger.Error($"GalleryPage.OnMonthGroupsChanged: threw: {value3}");
				}
			};
			viewModel.displayState.PropertyChanged += OnDisplayStateChanged;
			viewModel.photosState.OnPhotosReplaced = delegate
			{
				try
				{
					galleryPage.DispatcherQueue?.TryEnqueue(delegate
					{
						galleryPage.GridStage.MasonryViewControlRef?.ScrollToTop();
						galleryPage.GridStage.PhotoGridControlRef?.ScrollToTop();
					});
				}
				catch (Exception value3)
				{
					AppLogger.Error($"GalleryPage.OnPhotosReplaced: threw: {value3}");
				}
			};
		}
		catch (Exception value2)
		{
			AppLogger.Error($"GalleryPage.ctor: wiring failed: {value2}");
			throw;
		}
	}

	public void SetMasterTags(UiObservableCollection<string> tags)
	{
		try
		{
			FilterPanel.setMasterTagsSource(tags);
			BulkTagCombo.ItemsSource = tags;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.SetMasterTags: threw: {value}");
		}
	}

	private void OnSelectedPathsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		updateBulkOpBar();
	}

	private void OnDisplayStateChanged(object? sender, PropertyChangedEventArgs e)
	{
		try
		{
			if (e.PropertyName == "ViewMode")
			{
				bool flag = viewModel.displayState.ViewMode == ViewMode.gallery;
				GridStage.SetMasonryActive(flag);
				FilterPanel.SetGroupingEnabled(!flag);
				if (flag)
				{
					GridStage.MasonryViewControlRef?.SetPhotos(viewModel.photosState.photos);
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.OnDisplayStateChanged: threw: {value}");
		}
	}

	private void WireMasonryCallbacks(GalleryMasonryView masonry)
	{
		try
		{
			masonry.SetColumnCount(0);
			masonry.SetPhotos(viewModel.photosState.photos);
			masonry.OnPhotoTapped = delegate(PhotoThumbnailItem photo)
			{
				viewModel.handlePhotoActivate(new PhotoGridItem
				{
					Photo = photo
				}, shiftKey: false, delegate(PhotoThumbnailItem p)
				{
					OnSelectPhoto?.Invoke(p);
				});
			};
			masonry.OnThumbnailsNeeded = delegate(IReadOnlyList<PhotoThumbnailItem> items)
			{
				viewModel.photosState.requestVisibleThumbnails(items);
			};
			masonry.OnFirstVisibleIndexChanged = delegate(int idx)
			{
				SyncMonthNavToIndex(idx);
			};
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.WireMasonryCallbacks: threw: {value}");
		}
	}

	private void OnPhotosStateChanged(object? sender, PropertyChangedEventArgs e)
	{
		try
		{
			if (e.PropertyName == "IsLoading" || e.PropertyName == "TotalCount")
			{
				GridStage.UpdateLoadingState(viewModel.photosState.IsLoading, viewModel.photosState.TotalCount, IsFilterActive());
				FilterPanel.setFilteredCount(viewModel.photosState.TotalCount);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.OnPhotosStateChanged: threw: {value}");
		}
	}

	private bool IsFilterActive()
	{
		if (viewModel.filtersState.ActiveFilterCount <= 0)
		{
			return !string.IsNullOrWhiteSpace(viewModel.filtersState.SearchQuery);
		}
		return true;
	}

	private void OnEmptyStatePrimaryAction()
	{
		try
		{
			if (IsFilterActive())
			{
				OnResetFilters?.Invoke();
			}
			else
			{
				OnOpenSettings?.Invoke();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.OnEmptyStatePrimaryAction: threw: {value}");
		}
	}

	private void OnSelectionStateChanged(object? sender, PropertyChangedEventArgs e)
	{
		try
		{
			if (e.PropertyName == "IsMultiSelectMode")
			{
				updateBulkOpBar();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.OnSelectionStateChanged: threw: {value}");
		}
	}

	private void updateBulkOpBar()
	{
		try
		{
			int count = viewModel.selectionState.selectedPhotoPaths.Count;
			bool flag = viewModel.selectionState.IsMultiSelectMode && count > 0;
			if (flag)
			{
				SelectionCountLabel.Text = $"{count} 枚選択";
			}
			if (flag && !bulkOpBarWasVisible)
			{
				BulkOpBar.Visibility = Visibility.Visible;
				AnimationHelper.SlideIn(BulkOpBar, 0f, 20f, 200);
				bulkOpBarWasVisible = true;
			}
			else
			{
				if (flag || !bulkOpBarWasVisible)
				{
					return;
				}
				AnimationHelper.SlideOut(BulkOpBar, 0f, 20f, 150, delegate
				{
					base.DispatcherQueue?.TryEnqueue(delegate
					{
						BulkOpBar.Visibility = Visibility.Collapsed;
						AnimationHelper.ResetVisual(BulkOpBar);
					});
				});
				bulkOpBarWasVisible = false;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.updateBulkOpBar: threw: {value}");
		}
	}

	private void ExitMultiSelect_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			viewModel.selectionState.handleToggleMultiSelectMode();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.ExitMultiSelect_Click: threw: {value}");
		}
	}

	private async void BulkFavorite_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			await viewModel.bulkSetFavorite(isFavorite: true).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.BulkFavorite_Click: threw: {value}");
		}
	}

	private async void BulkUnfavorite_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			await viewModel.bulkSetFavorite(isFavorite: false).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.BulkUnfavorite_Click: threw: {value}");
		}
	}

	private async void BulkTagCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		try
		{
			if (BulkTagCombo.SelectedItem is string tag)
			{
				BulkTagCombo.SelectedIndex = -1;
				await viewModel.bulkAddTag(tag).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.BulkTagCombo_SelectionChanged: threw: {value}");
		}
	}

	private void SyncMonthNavToIndex(int firstVisibleIndex)
	{
		try
		{
			IReadOnlyList<GalleryMonthGroup> monthGroups = viewModel.photosState.monthGroups;
			if (monthGroups.Count == 0)
			{
				return;
			}
			MonthNav monthNavControlRef = GridStage.MonthNavControlRef;
			if ((object)monthNavControlRef == null)
			{
				return;
			}
			int activeIndex = 0;
			for (int num = monthGroups.Count - 1; num >= 0; num--)
			{
				if (firstVisibleIndex >= monthGroups[num].FirstIndex)
				{
					activeIndex = num;
					break;
				}
			}
			monthNavControlRef.SetActiveIndex(activeIndex);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.SyncMonthNavToIndex: threw: {value}");
		}
	}

	private void Page_Loaded(object sender, RoutedEventArgs e)
	{
		bool flag = viewModel.displayState.ViewMode == ViewMode.gallery;
		GridStage.SetMasonryActive(flag);
		FilterPanel.SetGroupingEnabled(!flag);
		GridStage.UpdateLoadingState(viewModel.photosState.IsLoading, viewModel.photosState.TotalCount, IsFilterActive());
		FilterPanel.setFilteredCount(viewModel.photosState.TotalCount);
	}

	private void Page_Unloaded(object sender, RoutedEventArgs e)
	{
		viewModel.selectionState.PropertyChanged -= OnSelectionStateChanged;
		viewModel.selectionState.selectedPhotoPaths.CollectionChanged -= OnSelectedPathsChanged;
		viewModel.displayState.PropertyChanged -= OnDisplayStateChanged;
		viewModel.photosState.PropertyChanged -= OnPhotosStateChanged;
		GridStage.EmptyStatePrimaryActionRequested -= OnEmptyStatePrimaryAction;
		viewModel.photosState.OnMonthGroupsChanged = null;
		viewModel.photosState.OnPhotosReplaced = null;
		GridStage.OnMasonryRealized = null;
		GalleryMasonryView masonryViewControlRef = GridStage.MasonryViewControlRef;
		if ((object)masonryViewControlRef != null)
		{
			masonryViewControlRef.OnPhotoTapped = null;
			masonryViewControlRef.OnThumbnailsNeeded = null;
			masonryViewControlRef.OnFirstVisibleIndexChanged = null;
		}
		MonthNav monthNavControlRef = GridStage.MonthNavControlRef;
		if ((object)monthNavControlRef != null)
		{
			monthNavControlRef.OnJumpToMonth = null;
		}
	}

	private async void BulkCopy_Click(object sender, RoutedEventArgs e)
	{
		_ = 1;
		try
		{
			if (OnChooseFolder != null)
			{
				string text = await OnChooseFolder().ConfigureAwait(continueOnCapturedContext: false);
				if (!string.IsNullOrWhiteSpace(text))
				{
					await viewModel.bulkCopyPhotos(text).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPage.BulkCopy_Click: threw: {value}");
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/Gallery/GalleryPage.xaml");
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
			break;
		}
		case 2:
			GridStage = target.As<GalleryGridStage>();
			break;
		case 3:
			BulkOpBar = target.As<Grid>();
			break;
		case 4:
			BulkOpBarShadowHost = target.As<Border>();
			break;
		case 5:
			SelectionCountLabel = target.As<TextBlock>();
			break;
		case 6:
			target.As<Button>().Click += BulkFavorite_Click;
			break;
		case 7:
			target.As<Button>().Click += BulkUnfavorite_Click;
			break;
		case 8:
			BulkTagBtn = target.As<Button>();
			break;
		case 9:
			target.As<Button>().Click += BulkCopy_Click;
			break;
		case 10:
			target.As<Button>().Click += ExitMultiSelect_Click;
			break;
		case 11:
			BulkTagCombo = target.As<ComboBox>();
			BulkTagCombo.SelectionChanged += BulkTagCombo_SelectionChanged;
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
