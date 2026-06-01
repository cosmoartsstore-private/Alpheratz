using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Gallery;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class GalleryViewModel : UiThreadSafeObservableObject
{
	private const int MAX_TAG_LENGTH = 20;

	private readonly PhotoService photoService;

	private readonly WorldService worldService;

	private readonly PhashService phashService;

	private readonly ToastService toastService;

	public GalleryPhotosState photosState { get; }

	public GalleryFiltersState filtersState { get; }

	public GallerySelectionState selectionState { get; }

	public GalleryDisplayState displayState { get; }

	public GalleryScrollState scrollState { get; }

	public Func<IReadOnlyList<PhotoThumbnailItem>?>? drillDownPhotosProvider { get; set; }

	public GalleryViewModel(PhotoService photoService, WorldService worldService, PhashService phashService, ToastService toastService, GalleryPhotosState photosState, GalleryFiltersState filtersState, GallerySelectionState selectionState, GalleryDisplayState displayState, GalleryScrollState scrollState)
	{
		this.photoService = photoService;
		this.worldService = worldService;
		this.phashService = phashService;
		this.toastService = toastService;
		this.photosState = photosState;
		this.filtersState = filtersState;
		this.selectionState = selectionState;
		this.displayState = displayState;
		this.scrollState = scrollState;
		filtersState.PropertyChanged += onFiltersChanged;
		filtersState.worldFilters.CollectionChanged += onFiltersCollectionChanged;
		filtersState.tagFilters.CollectionChanged += onFiltersCollectionChanged;
		selectionState.selectedPhotoPaths.CollectionChanged += onSelectedPathsChanged;
	}

	public void Cleanup()
	{
		filtersState.PropertyChanged -= onFiltersChanged;
		filtersState.worldFilters.CollectionChanged -= onFiltersCollectionChanged;
		filtersState.tagFilters.CollectionChanged -= onFiltersCollectionChanged;
		selectionState.selectedPhotoPaths.CollectionChanged -= onSelectedPathsChanged;
	}

	private void onSelectedPathsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		try
		{
			switch (e.Action)
			{
			case NotifyCollectionChangedAction.Add:
				if (e.NewItems == null)
				{
					break;
				}
				{
					foreach (object newItem in e.NewItems)
					{
						if (newItem is string photoPath2)
						{
							setIsSelectedByPath(photoPath2, value: true);
						}
					}
					break;
				}
			case NotifyCollectionChangedAction.Remove:
				if (e.OldItems == null)
				{
					break;
				}
				{
					foreach (object oldItem in e.OldItems)
					{
						if (oldItem is string photoPath)
						{
							setIsSelectedByPath(photoPath, value: false);
						}
					}
					break;
				}
			case NotifyCollectionChangedAction.Reset:
			{
				foreach (PhotoThumbnailItem photo in photosState.photos)
				{
					if (photo.IsSelected)
					{
						photo.IsSelected = false;
					}
				}
				break;
			}
			case NotifyCollectionChangedAction.Replace:
			case NotifyCollectionChangedAction.Move:
				break;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.onSelectedPathsChanged: {value}");
		}
	}

	private void setIsSelectedByPath(string photoPath, bool value)
	{
		foreach (PhotoThumbnailItem photo in photosState.photos)
		{
			if (photo.PhotoPath == photoPath)
			{
				photo.IsSelected = value;
				break;
			}
		}
	}

	private void onFiltersChanged(object? sender, PropertyChangedEventArgs e)
	{
		try
		{
			if (filtersState.IsBatchUpdating)
			{
				return;
			}
			string propertyName = e.PropertyName;
			if (propertyName == null)
			{
				return;
			}
			switch (propertyName.Length)
			{
			case 14:
				switch (propertyName[0])
				{
				default:
					return;
				case 'B':
					if (!(propertyName == "BatchCompleted"))
					{
						return;
					}
					break;
				case 'D':
					if (!(propertyName == "DebouncedQuery"))
					{
						return;
					}
					break;
				}
				break;
			case 8:
				switch (propertyName[0])
				{
				default:
					return;
				case 'D':
					if (!(propertyName == "DateFrom"))
					{
						return;
					}
					break;
				case 'S':
					if (!(propertyName == "SortMode"))
					{
						return;
					}
					break;
				}
				break;
			case 17:
				switch (propertyName[0])
				{
				default:
					return;
				case 'O':
					if (!(propertyName == "OrientationFilter"))
					{
						return;
					}
					break;
				case 'D':
					if (!(propertyName == "DisplayFolderMode"))
					{
						return;
					}
					break;
				}
				break;
			case 12:
				if (propertyName == "GroupingMode")
				{
					photosState.rebuildDisplayItems(filtersState.GroupingMode);
				}
				return;
			case 6:
				if (!(propertyName == "DateTo"))
				{
					return;
				}
				break;
			case 13:
				if (!(propertyName == "FavoritesOnly"))
				{
					return;
				}
				break;
			default:
				return;
			}
			applyFiltersAndReload();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.onFiltersChanged: threw: {value}");
		}
	}

	private void onFiltersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		try
		{
			applyFiltersAndReload();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.onFiltersCollectionChanged: threw: {value}");
		}
	}

	public void applySearchNow()
	{
		try
		{
			SearchCommandResult searchCommandResult = SearchCommandParser.Parse(filtersState.SearchQuery);
			if (searchCommandResult.HasCommands)
			{
				filtersState.applySearchCommands(searchCommandResult);
			}
			if (filtersState.DebouncedQuery == searchCommandResult.PlainText)
			{
				applyFiltersAndReload();
			}
			else
			{
				filtersState.DebouncedQuery = searchCommandResult.PlainText;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.applySearchNow: threw: {value}");
		}
	}

	public Task applyFiltersAndReload()
	{
		photosState.SetFilters(buildCurrentFilters());
		return photosState.loadPhotos();
	}

	private PhotoQueryFilters buildCurrentFilters()
	{
		string debouncedQuery = filtersState.DebouncedQuery;
		IReadOnlyList<string> worldFilters = new _003C_003Ez__ReadOnlyArray<string>(filtersState.worldFilters.ToArray());
		string dateFrom = filtersState.DateFrom;
		string dateTo = filtersState.DateTo;
		string orientationFilter = filtersState.OrientationFilter;
		bool favoritesOnly = filtersState.FavoritesOnly;
		IReadOnlyList<string> tagFilters = new _003C_003Ez__ReadOnlyArray<string>(filtersState.tagFilters.ToArray());
		ViewMode viewMode = displayState.ViewMode;
		return new PhotoQueryFilters(debouncedQuery, worldFilters, dateFrom, dateTo, orientationFilter, favoritesOnly, tagFilters, includePhash: false, pagingEnabled: false, viewMode, filtersState.DisplayFolderMode switch
		{
			DisplayFolderMode.primary => 1L, 
			DisplayFolderMode.secondary => 2L, 
			_ => null, 
		}, filtersState.GroupingMode, filtersState.SortMode);
	}

	public async Task loadWorldFilterOptions()
	{
		try
		{
			IReadOnlyList<WorldFilterOptionDto> obj = await photoService.GetWorldFilterOptionsAsync().ConfigureAwait(continueOnCapturedContext: false);
			filtersState.worldFilterOptions.Clear();
			foreach (WorldFilterOptionDto item in obj)
			{
				filtersState.worldFilterOptions.Add(item);
			}
		}
		catch (Exception value)
		{
			AppLogger.Warn($"GalleryViewModel.loadWorldFilterOptions: threw: {value}");
		}
	}

	public async Task loadTagFilterCounts()
	{
		try
		{
			IReadOnlyDictionary<string, long> tagFilterCounts = await photoService.GetTagFilterCountsAsync().ConfigureAwait(continueOnCapturedContext: false);
			filtersState.setTagFilterCounts(tagFilterCounts);
		}
		catch (Exception value)
		{
			AppLogger.Warn($"GalleryViewModel.loadTagFilterCounts: threw: {value}");
		}
	}

	public async Task toggleFavorite(string photoPath, bool current)
	{
		PhotoThumbnailItem photoThumbnailItem = photosState.photos.FirstOrDefault((PhotoThumbnailItem photo) => photo.PhotoPath == photoPath);
		try
		{
			await photoService.SetPhotoFavoriteAsync(photoPath, !current, photoThumbnailItem?.SourceSlot ?? 1).ConfigureAwait(continueOnCapturedContext: false);
			updatePhoto(photoPath, delegate(PhotoThumbnailItem photo)
			{
				photo.IsFavorite = !current;
			});
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.toggleFavorite: threw: {value}");
			toastService.addToast($"お気に入りの更新に失敗しました: {value}", ToastType.error);
		}
	}

	public async Task addTag(string photoPath, string tag)
	{
		string normalized = tag.Trim();
		if (string.IsNullOrEmpty(normalized))
		{
			return;
		}
		if (normalized.Length > 20)
		{
			toastService.addToast($"タグは{20}文字以内で入力してください。", ToastType.error);
			return;
		}
		PhotoThumbnailItem photoThumbnailItem = photosState.photos.FirstOrDefault((PhotoThumbnailItem photo) => photo.PhotoPath == photoPath);
		if (photoThumbnailItem != null && photoThumbnailItem.Tags.Contains(normalized))
		{
			return;
		}
		try
		{
			await photoService.AddPhotoTagAsync(photoPath, normalized, photoThumbnailItem?.SourceSlot ?? 1).ConfigureAwait(continueOnCapturedContext: false);
			updatePhoto(photoPath, delegate(PhotoThumbnailItem photo)
			{
				photo.Tags = photo.Tags.Concat(new _003C_003Ez__ReadOnlySingleElementList<string>(normalized)).OrderBy<string, string>((string item) => item, StringComparer.Create(new CultureInfo("ja-JP"), ignoreCase: false)).ToArray();
			});
			toastService.addToast("タグを追加しました。");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.addTag: threw: {value}");
			toastService.addToast($"タグの追加に失敗しました: {value}", ToastType.error);
		}
	}

	public async Task removeTag(string photoPath, string tag)
	{
		PhotoThumbnailItem photoThumbnailItem = photosState.photos.FirstOrDefault((PhotoThumbnailItem photo) => photo.PhotoPath == photoPath);
		try
		{
			await photoService.RemovePhotoTagAsync(photoPath, tag, photoThumbnailItem?.SourceSlot ?? 1).ConfigureAwait(continueOnCapturedContext: false);
			updatePhoto(photoPath, delegate(PhotoThumbnailItem photo)
			{
				photo.Tags = photo.Tags.Where((string item) => item != tag).ToArray();
			});
			toastService.addToast("タグを削除しました。");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.removeTag: threw: {value}");
			toastService.addToast($"タグの削除に失敗しました: {value}", ToastType.error);
		}
	}

	public async Task applySimilarWorldMatch(PhotoThumbnailItem sourcePhoto, PhotoThumbnailItem? selectedPhotoView, Action<PhotoThumbnailItem>? updateSelectedPhoto = null)
	{
		if (selectedPhotoView == null)
		{
			return;
		}
		try
		{
			await worldService.ApplyWorldMatchFromPhotoAsync(selectedPhotoView.PhotoPath, sourcePhoto.PhotoPath).ConfigureAwait(continueOnCapturedContext: false);
			string nextWorldId = sourcePhoto.WorldId;
			string nextWorldName = sourcePhoto.WorldName;
			updatePhoto(selectedPhotoView.PhotoPath, Apply);
			if (updateSelectedPhoto != null)
			{
				Apply(selectedPhotoView);
				updateSelectedPhoto(selectedPhotoView);
			}
			toastService.addToast("ワールド情報を反映しました。");
			void Apply(PhotoThumbnailItem photo)
			{
				photo.WorldId = nextWorldId;
				photo.WorldName = nextWorldName;
				photo.MatchSource = "phash";
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.applySimilarWorldMatch: threw: {value}");
			toastService.addToast($"ワールド情報の反映に失敗しました: {value}", ToastType.error);
		}
	}

	public async Task handleStartUnknownWorldAnalysis()
	{
		try
		{
			await phashService.StartPdqAnalysisAsync().ConfigureAwait(continueOnCapturedContext: false);
			toastService.addToast("ワールド不明写真の一括分析を開始しました");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.handleStartUnknownWorldAnalysis: threw: {value}");
			toastService.addToast($"ワールド不明写真の一括分析を開始できませんでした: {value}", ToastType.error);
		}
	}

	public void handlePhotoActivate(PhotoGridItem item, bool shiftKey, Action<PhotoThumbnailItem> onSelectPhoto)
	{
		try
		{
			if (selectionState.IsMultiSelectMode)
			{
				selectionState.toggleSelectedPhoto(item, shiftKey, photosState.displayItems.ToArray());
			}
			else
			{
				onSelectPhoto(item.Photo);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.handlePhotoActivate: threw: {value}");
		}
	}

	public void resetFilters()
	{
		filtersState.resetFilters();
	}

	public async Task bulkSetFavorite(bool isFavorite)
	{
		List<SelectedPhotoRefDto> refs = selectionState.selectedPhotoRefs.ToList();
		if (refs.Count == 0)
		{
			return;
		}
		try
		{
			await photoService.BulkSetPhotoFavoriteAsync(refs, isFavorite).ConfigureAwait(continueOnCapturedContext: false);
			foreach (SelectedPhotoRefDto item in refs)
			{
				updatePhoto(item.photo_path, delegate(PhotoThumbnailItem p)
				{
					p.IsFavorite = isFavorite;
				});
			}
			toastService.addToast(isFavorite ? "お気に入りに追加しました" : "お気に入りを解除しました");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.bulkSetFavorite: threw: {value}");
			toastService.addToast($"お気に入りの更新に失敗しました: {value}", ToastType.error);
		}
	}

	public async Task bulkAddTag(string tag)
	{
		List<SelectedPhotoRefDto> refs = selectionState.selectedPhotoRefs.ToList();
		if (refs.Count == 0 || string.IsNullOrWhiteSpace(tag))
		{
			return;
		}
		try
		{
			await photoService.BulkAddPhotoTagAsync(refs, tag).ConfigureAwait(continueOnCapturedContext: false);
			toastService.addToast($"タグ \"{tag}\" を {refs.Count} 枚に追加しました");
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.bulkAddTag: threw: {value}");
			toastService.addToast($"タグの追加に失敗しました: {value}", ToastType.error);
		}
	}

	public async Task bulkCopyPhotos(string destinationFolder)
	{
		List<SelectedPhotoRefDto> list = selectionState.selectedPhotoRefs.ToList();
		if (list.Count == 0 || string.IsNullOrWhiteSpace(destinationFolder))
		{
			return;
		}
		try
		{
			var (value, num) = await photoService.BulkCopyPhotosAsync(list, destinationFolder).ConfigureAwait(continueOnCapturedContext: false);
			if (num == 0)
			{
				toastService.addToast($"{value} 枚のファイルをコピーしました");
			}
			else
			{
				toastService.addToast($"{value} 枚をコピーしました ({num} 枚はスキップ)");
			}
		}
		catch (Exception value2)
		{
			AppLogger.Error($"GalleryViewModel.bulkCopyPhotos: threw: {value2}");
			toastService.addToast($"コピーに失敗しました: {value2}", ToastType.error);
		}
	}

	public async Task<IReadOnlyList<PhotoThumbnailItem>> getGroupPhotosAsync(string groupKey)
	{
		try
		{
			PhotoQueryFilters photoQueryFilters = buildCurrentFilters();
			return (await photoService.GetWorldGroupPhotosAsync(groupKey, string.IsNullOrEmpty(photoQueryFilters.dateFrom) ? null : photoQueryFilters.dateFrom, string.IsNullOrEmpty(photoQueryFilters.dateTo) ? null : photoQueryFilters.dateTo, photoQueryFilters.sourceSlot, (photoQueryFilters.orientationFilter == "all") ? null : photoQueryFilters.orientationFilter, photoQueryFilters.favoritesOnly ? new bool?(true) : ((bool?)null), (photoQueryFilters.tagFilters.Count > 0) ? photoQueryFilters.tagFilters : null).ConfigureAwait(continueOnCapturedContext: false)).Select(PhotoThumbnailItem.FromDto).ToArray();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.getGroupPhotosAsync: threw: {value}");
			return Array.Empty<PhotoThumbnailItem>();
		}
	}

	private void updatePhoto(string photoPath, Action<PhotoThumbnailItem> updater)
	{
		HashSet<PhotoThumbnailItem> applied;
		try
		{
			applied = new HashSet<PhotoThumbnailItem>();
			foreach (PhotoThumbnailItem item in photosState.photos.Where((PhotoThumbnailItem photo) => photo.PhotoPath == photoPath))
			{
				applyOnce(item);
			}
			foreach (PhotoGridItem item2 in photosState.displayItems.Where((PhotoGridItem item) => item.Photo.PhotoPath == photoPath))
			{
				applyOnce(item2.Photo);
			}
			IReadOnlyList<PhotoThumbnailItem> readOnlyList = drillDownPhotosProvider?.Invoke();
			if (readOnlyList == null)
			{
				return;
			}
			foreach (PhotoThumbnailItem item3 in readOnlyList.Where((PhotoThumbnailItem photo) => photo.PhotoPath == photoPath))
			{
				applyOnce(item3);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryViewModel.updatePhoto: threw: {value}");
		}
		void applyOnce(PhotoThumbnailItem photo)
		{
			if (applied.Add(photo))
			{
				updater(photo);
			}
		}
	}
}
