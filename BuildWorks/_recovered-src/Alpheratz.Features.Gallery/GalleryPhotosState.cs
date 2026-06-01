using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Gallery;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class GalleryPhotosState : UiThreadSafeObservableObject, IAsyncDisposable
{
	private readonly PhotoService photoService;

	private readonly LocalEventBus eventBus;

	private readonly ToastService toastService;

	private readonly DispatcherService dispatcherService;

	private readonly ThumbnailWorker thumbnailWorker;

	private PhotoQueryFilters filters = new PhotoQueryFilters("", Array.Empty<string>(), "", "", "all", favoritesOnly: false, Array.Empty<string>(), includePhash: false, pagingEnabled: false, ViewMode.standard, null, GroupingMode.none);

	private CancellationTokenSource? thumbnailCts;

	private int transitionToken;

	private readonly List<PhotoThumbnailItem> photosRef = new List<PhotoThumbnailItem>();

	private readonly List<PhotoGridItem> displayItemsRef = new List<PhotoGridItem>();

	private IAsyncDisposable? scanCompletedUnlisten;

	private IAsyncDisposable? scanEnrichCompletedUnlisten;

	[ObservableProperty]
	private bool isLoading;

	[ObservableProperty]
	private int totalCount;

	public UiObservableCollection<PhotoThumbnailItem> photos { get; } = new UiObservableCollection<PhotoThumbnailItem>();

	public UiObservableCollection<PhotoGridItem> displayItems { get; } = new UiObservableCollection<PhotoGridItem>();

	public IReadOnlyList<GalleryMonthGroup> monthGroups { get; private set; } = Array.Empty<GalleryMonthGroup>();

	public Action<IReadOnlyList<GalleryMonthGroup>>? OnMonthGroupsChanged { get; set; }

	public Action? OnPhotosReplaced { get; set; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsLoading
	{
		get
		{
			return isLoading;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(isLoading, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsLoading);
				isLoading = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsLoading);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int TotalCount
	{
		get
		{
			return totalCount;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(totalCount, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.TotalCount);
				totalCount = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.TotalCount);
			}
		}
	}

	public GalleryPhotosState(PhotoService photoService, LocalEventBus eventBus, ToastService toastService, DispatcherService dispatcherService, ThumbnailWorker thumbnailWorker)
	{
		this.photoService = photoService;
		this.eventBus = eventBus;
		this.toastService = toastService;
		this.dispatcherService = dispatcherService;
		this.thumbnailWorker = thumbnailWorker;
	}

	public void SetFilters(PhotoQueryFilters nextFilters)
	{
		filters = nextFilters;
	}

	public async Task loadMonthSummary()
	{
		try
		{
			PhotoQueryPayload payload = new PhotoQueryPayload(string.IsNullOrWhiteSpace(filters.dateFrom) ? null : filters.dateFrom, string.IsNullOrWhiteSpace(filters.dateTo) ? null : filters.dateTo, string.IsNullOrWhiteSpace(filters.searchQuery) ? null : filters.searchQuery.Trim(), (filters.worldFilters.Count > 0) ? filters.worldFilters : null, (filters.orientationFilter == "all") ? null : filters.orientationFilter, filters.favoritesOnly ? new bool?(true) : ((bool?)null), (filters.tagFilters.Count > 0) ? filters.tagFilters : null, filters.sourceSlot, null, null);
			IReadOnlyList<MonthSummaryItem> obj = await photoService.GetMonthSummaryAsync(payload).ConfigureAwait(continueOnCapturedContext: false);
			List<GalleryMonthGroup> list = new List<GalleryMonthGroup>(obj.Count);
			int num = 0;
			foreach (MonthSummaryItem item in obj)
			{
				string key = $"{item.Year:D4}-{item.Month:D2}";
				list.Add(new GalleryMonthGroup(key, item.Year, item.Month, $"{item.Month}月", num, item.Count));
				num += item.Count;
			}
			monthGroups = list;
			OnMonthGroupsChanged?.Invoke(list);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPhotosState.loadMonthSummary: threw: {value}");
		}
	}

	public async Task InitializeAsync()
	{
		try
		{
			await loadPhotos().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPhotosState.InitializeAsync: initial load threw: {value}");
		}
		try
		{
			scanCompletedUnlisten = eventBus.Subscribe("scan:completed", () => loadPhotos());
			scanEnrichCompletedUnlisten = eventBus.Subscribe("scan:enrich_completed", () => loadPhotos());
		}
		catch (Exception value2)
		{
			AppLogger.Error($"GalleryPhotosState.InitializeAsync: subscribe threw: {value2}");
			throw;
		}
	}

	public void setPhotos(IEnumerable<PhotoThumbnailItem> nextPhotos, bool autoGenerateThumbnails = true)
	{
		try
		{
			photos.ReplaceAll(nextPhotos);
			photosRef.Clear();
			photosRef.AddRange(photos);
			Dictionary<string, PhotoThumbnailItem> dictionary = photosRef.ToDictionary((PhotoThumbnailItem photo) => photo.PhotoPath, (PhotoThumbnailItem photo) => photo);
			for (int num = 0; num < displayItems.Count; num++)
			{
				displayItems[num] = SyncItem(displayItems[num], dictionary);
			}
			if (autoGenerateThumbnails)
			{
				kickThumbnailGeneration(dictionary);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPhotosState.setPhotos: threw: {value}");
		}
		static PhotoGridItem SyncItem(PhotoGridItem item, IReadOnlyDictionary<string, PhotoThumbnailItem> map)
		{
			if (map.TryGetValue(item.Photo.PhotoPath, out PhotoThumbnailItem value2))
			{
				item.Photo = value2;
			}
			if (item.GroupPhotos != null)
			{
				item.GroupPhotos = item.GroupPhotos.Select((PhotoThumbnailItem photo) => (!map.TryGetValue(photo.PhotoPath, out PhotoThumbnailItem value3)) ? photo : value3).ToArray();
			}
			return item;
		}
	}

	public void kickThumbnailsForExternal(IReadOnlyList<PhotoThumbnailItem> items)
	{
		if (items.Count == 0)
		{
			return;
		}
		Dictionary<string, PhotoThumbnailItem> dictionary = new Dictionary<string, PhotoThumbnailItem>(items.Count);
		foreach (PhotoThumbnailItem item in items)
		{
			if (!string.IsNullOrEmpty(item.PhotoPath))
			{
				dictionary.TryAdd(item.PhotoPath, item);
			}
		}
		if (dictionary.Count != 0)
		{
			kickThumbnailGeneration(dictionary, cancelPrevious: false);
		}
	}

	public void requestVisibleThumbnails(IReadOnlyList<PhotoThumbnailItem> items)
	{
		if (items.Count == 0)
		{
			return;
		}
		Dictionary<string, PhotoThumbnailItem> dictionary = new Dictionary<string, PhotoThumbnailItem>(items.Count);
		foreach (PhotoThumbnailItem item in items)
		{
			if (!string.IsNullOrEmpty(item.PhotoPath) && string.IsNullOrEmpty(item.GridThumbPath))
			{
				dictionary.TryAdd(item.PhotoPath, item);
			}
		}
		if (dictionary.Count != 0)
		{
			kickThumbnailGeneration(dictionary, cancelPrevious: false);
		}
	}

	private void kickThumbnailGeneration(IReadOnlyDictionary<string, PhotoThumbnailItem> photoMap, bool cancelPrevious = true)
	{
		CancellationTokenSource cancellationTokenSource3;
		if (cancelPrevious)
		{
			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
			CancellationTokenSource cancellationTokenSource2 = Interlocked.Exchange(ref thumbnailCts, cancellationTokenSource);
			if (cancellationTokenSource2 != null)
			{
				try
				{
					cancellationTokenSource2.Cancel();
				}
				catch
				{
				}
				cancellationTokenSource2.Dispose();
			}
			cancellationTokenSource3 = cancellationTokenSource;
		}
		else
		{
			CancellationTokenSource cancellationTokenSource4 = Volatile.Read(in thumbnailCts);
			if (cancellationTokenSource4 == null)
			{
				CancellationTokenSource cancellationTokenSource5 = new CancellationTokenSource();
				CancellationTokenSource cancellationTokenSource6 = Interlocked.CompareExchange(ref thumbnailCts, cancellationTokenSource5, null);
				if (cancellationTokenSource6 == null)
				{
					cancellationTokenSource3 = cancellationTokenSource5;
				}
				else
				{
					cancellationTokenSource5.Dispose();
					cancellationTokenSource3 = cancellationTokenSource6;
				}
			}
			else
			{
				cancellationTokenSource3 = cancellationTokenSource4;
			}
		}
		CancellationToken ct;
		try
		{
			ct = cancellationTokenSource3.Token;
		}
		catch (ObjectDisposedException)
		{
			return;
		}
		List<(string path, long slot)> targets = (from p in photoMap.Values
			where string.IsNullOrEmpty(p.GridThumbPath) && !string.IsNullOrEmpty(p.PhotoPath)
			select (path: p.PhotoPath, slot: p.SourceSlot)).ToList();
		if (targets.Count == 0)
		{
			return;
		}
		Task.Run(async delegate
		{
			try
			{
				await thumbnailWorker.GenerateGridAsync(targets, delegate(ThumbnailResult result)
				{
					if (!ct.IsCancellationRequested && photoMap.TryGetValue(result.PhotoPath, out PhotoThumbnailItem item))
					{
						UiThread.Run(delegate
						{
							item.GridThumbPath = result.ThumbPath;
						});
					}
				}, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception value)
			{
				AppLogger.Error($"GalleryPhotosState.kickThumbnailGeneration: threw: {value}");
			}
		});
	}

	private PhotoQueryPayload buildQueryParams()
	{
		return new PhotoQueryPayload(string.IsNullOrWhiteSpace(filters.dateFrom) ? null : filters.dateFrom, string.IsNullOrWhiteSpace(filters.dateTo) ? null : filters.dateTo, string.IsNullOrWhiteSpace(filters.searchQuery) ? null : filters.searchQuery.Trim(), (filters.worldFilters.Count > 0) ? filters.worldFilters : null, (filters.orientationFilter == "all") ? null : filters.orientationFilter, filters.favoritesOnly ? new bool?(true) : ((bool?)null), (filters.tagFilters.Count > 0) ? filters.tagFilters : null, filters.sourceSlot, null, null, null, filters.sortMode);
	}

	private async Task<IReadOnlyList<PhotoThumbnailItem>> fetchAllPhotos()
	{
		return (await photoService.GetPhotosAsync(buildQueryParams()).ConfigureAwait(continueOnCapturedContext: false)).items.Select(PhotoThumbnailItem.FromDto).ToArray();
	}

	public static string buildWorldGroupKey(PhotoThumbnailItem p)
	{
		if (!string.IsNullOrWhiteSpace(p.WorldId))
		{
			return "id:" + p.WorldId;
		}
		if (!string.IsNullOrWhiteSpace(p.WorldName))
		{
			return "name:" + p.WorldName;
		}
		return "unknown";
	}

	public void rebuildDisplayItems(GroupingMode groupingMode)
	{
		IReadOnlyList<PhotoGridItem> readOnlyList = ((groupingMode != GroupingMode.world) ? photosRef.Select((PhotoThumbnailItem p) => new PhotoGridItem
		{
			Photo = p
		}).ToArray() : (from g in photosRef.GroupBy(buildWorldGroupKey)
			orderby g.First().Timestamp descending
			select g).Select(delegate(IGrouping<string, PhotoThumbnailItem> g)
		{
			PhotoThumbnailItem photoThumbnailItem = g.First();
			return new PhotoGridItem
			{
				Photo = photoThumbnailItem,
				GroupCount = g.Count(),
				GroupKey = buildWorldGroupKey(photoThumbnailItem)
			};
		}).ToArray());
		displayItemsRef.Clear();
		displayItemsRef.AddRange(readOnlyList);
		displayItems.ReplaceAll(readOnlyList);
	}

	public async Task loadPhotos(int page = 0)
	{
		int token = ++transitionToken;
		CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(ref thumbnailCts, null);
		if (cancellationTokenSource != null)
		{
			try
			{
				cancellationTokenSource.Cancel();
			}
			catch
			{
			}
			cancellationTokenSource.Dispose();
		}
		await dispatcherService.RunOnUiThread(delegate
		{
			IsLoading = true;
		}).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			int num;
			_ = num - 1;
			_ = 2;
			try
			{
				Task<IReadOnlyList<PhotoThumbnailItem>> photosTask = fetchAllPhotos();
				Task task = loadMonthSummary();
				await Task.WhenAll(photosTask, task).ConfigureAwait(continueOnCapturedContext: false);
				IReadOnlyList<PhotoThumbnailItem> allPhotos = await photosTask.ConfigureAwait(continueOnCapturedContext: false);
				if (transitionToken != token)
				{
					goto end_IL_0103;
				}
				await dispatcherService.RunOnUiThread(delegate
				{
					photosRef.Clear();
					photosRef.AddRange(allPhotos);
					photos.ReplaceAll(allPhotos);
					TotalCount = allPhotos.Count;
					rebuildDisplayItems(filters.groupingMode);
					OnPhotosReplaced?.Invoke();
					Dictionary<string, PhotoThumbnailItem> photoMap = allPhotos.ToDictionary((PhotoThumbnailItem p) => p.PhotoPath, (PhotoThumbnailItem p) => p);
					kickThumbnailGeneration(photoMap);
				}).ConfigureAwait(continueOnCapturedContext: false);
				goto end_IL_00e8;
				end_IL_0103:;
			}
			catch (Exception value)
			{
				AppLogger.Error($"GalleryPhotosState.loadPhotos: threw: {value}");
				toastService.addToast($"写真一覧の読み込みに失敗しました: {value}", ToastType.error);
				goto end_IL_00e8;
			}
			end_IL_00e8:;
		}
		finally
		{
			if (transitionToken == token)
			{
				await dispatcherService.RunOnUiThread(delegate
				{
					IsLoading = false;
				}).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			await dispose(scanCompletedUnlisten).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"GalleryPhotosState.DisposeAsync: scanCompleted unlisten threw: {value}");
		}
		try
		{
			await dispose(scanEnrichCompletedUnlisten).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value2)
		{
			AppLogger.Error($"GalleryPhotosState.DisposeAsync: scanEnrichCompleted unlisten threw: {value2}");
		}
		CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(ref thumbnailCts, null);
		if (cancellationTokenSource != null)
		{
			try
			{
				cancellationTokenSource.Cancel();
			}
			catch
			{
			}
			cancellationTokenSource.Dispose();
		}
		transitionToken++;
		static async ValueTask dispose(IAsyncDisposable? disposable)
		{
			if (disposable != null)
			{
				await disposable.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}
}
