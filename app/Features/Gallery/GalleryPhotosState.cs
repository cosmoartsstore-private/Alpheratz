using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// ギャラリー画面の写真データを管理するステート。
/// フィルタ変更時に DB から全メタデータを一括取得し、photos に保持する。
/// ビューモード切替・グルーピング切替では DB リクエリせず、
/// クライアント側で displayItems を再構築する。
/// サムネイル画像のみ遅延ロード。
/// </summary>
public partial class GalleryPhotosState : UiThreadSafeObservableObject, IAsyncDisposable
{
    private readonly PhotoService photoService;
    private readonly LocalEventBus eventBus;
    private readonly ToastService toastService;
    private readonly DispatcherService dispatcherService;
    private readonly ThumbnailWorker thumbnailWorker;

    private PhotoQueryFilters filters = new("", [], "", "", "all", false, [], false, false, ViewMode.standard, null, GroupingMode.none);
    private CancellationTokenSource? thumbnailCts;

    private int transitionToken;

    private readonly List<PhotoThumbnailItem> photosRef = [];
    private readonly List<PhotoGridItem> displayItemsRef = [];

    private IAsyncDisposable? scanCompletedUnlisten;
    private IAsyncDisposable? scanEnrichCompletedUnlisten;

    /// <summary>ギャラリーに表示する写真。MasonryView がバインドする。</summary>
    public UiObservableCollection<PhotoThumbnailItem> photos { get; } = [];

    /// <summary>GridView 用の表示アイテム（グルーピング情報付き）。</summary>
    public UiObservableCollection<PhotoGridItem> displayItems { get; } = [];

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private int totalCount;

    /// <summary>DB から取得した全期間の月グループ。MonthNav に使用。</summary>
    public IReadOnlyList<GalleryMonthGroup> monthGroups { get; private set; } = [];

    /// <summary>月グループが更新されたときに発火するコールバック。</summary>
    public Action<IReadOnlyList<GalleryMonthGroup>>? OnMonthGroupsChanged { get; set; }

    /// <summary>写真一覧が全置換されたときに発火する（scroll-to-top 用）。</summary>
    public Action? OnPhotosReplaced { get; set; }

    public GalleryPhotosState(PhotoService photoService, LocalEventBus eventBus, ToastService toastService, DispatcherService dispatcherService, ThumbnailWorker thumbnailWorker)
    {
        AppLogger.Trace("GalleryPhotosState.ctor: enter");
        this.photoService = photoService;
        this.eventBus = eventBus;
        this.toastService = toastService;
        this.dispatcherService = dispatcherService;
        this.thumbnailWorker = thumbnailWorker;
        AppLogger.Trace("GalleryPhotosState.ctor: exit");
    }

    /// <summary>フィルタ条件を差し替える。loadPhotos 呼び出し前に実行すること。</summary>
    public void SetFilters(PhotoQueryFilters nextFilters)
    {
        AppLogger.Trace("GalleryPhotosState.SetFilters: enter");
        filters = nextFilters;
        AppLogger.Trace("GalleryPhotosState.SetFilters: exit");
    }

    /// <summary>DB から全期間の月別件数を取得し、MonthNav 用のグループリストを構築する。</summary>
    public async Task loadMonthSummary()
    {
        AppLogger.Trace("GalleryPhotosState.loadMonthSummary: enter");
        try
        {
            var payload = new PhotoQueryPayload(
                startDate: string.IsNullOrWhiteSpace(filters.dateFrom) ? null : filters.dateFrom,
                endDate: string.IsNullOrWhiteSpace(filters.dateTo) ? null : filters.dateTo,
                worldQuery: string.IsNullOrWhiteSpace(filters.searchQuery) ? null : filters.searchQuery.Trim(),
                worldExacts: filters.worldFilters.Count > 0 ? filters.worldFilters : null,
                orientation: filters.orientationFilter == "all" ? null : filters.orientationFilter,
                favoritesOnly: filters.favoritesOnly ? true : null,
                tagFilters: filters.tagFilters.Count > 0 ? filters.tagFilters : null,
                sourceSlot: filters.sourceSlot,
                limit: null,
                offset: null);
            var summaries = await photoService.GetMonthSummaryAsync(payload).ConfigureAwait(false);

            var groups = new List<GalleryMonthGroup>(summaries.Count);
            var runningIndex = 0;
            foreach (var s in summaries)
            {
                var key = $"{s.Year:D4}-{s.Month:D2}";
                groups.Add(new GalleryMonthGroup(key, s.Year, s.Month, $"{s.Month}月", runningIndex, s.Count));
                runningIndex += s.Count;
            }

            monthGroups = groups;
            OnMonthGroupsChanged?.Invoke(groups);
            AppLogger.Trace($"GalleryPhotosState.loadMonthSummary: exit groups={groups.Count}");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPhotosState.loadMonthSummary: threw: {ex}");
        }
    }

    /// <summary>
    /// イベントバスを購読し初回データをロードする。
    /// scan:completed / scan:enrich_completed 受信で自動再ロード。
    /// </summary>
    public Task InitializeAsync()
    {
        AppLogger.Trace("GalleryPhotosState.InitializeAsync: enter");
        try
        {
            scanCompletedUnlisten = eventBus.Subscribe("scan:completed", () => loadPhotos());
            scanEnrichCompletedUnlisten = eventBus.Subscribe("scan:enrich_completed", () => loadPhotos());
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPhotosState.InitializeAsync: subscribe threw: {ex}");
            throw;
        }
        var task = loadPhotos(0);
        AppLogger.Trace("GalleryPhotosState.InitializeAsync: exit");
        return task;
    }

    /// <summary>
    /// 写真コレクションを一括差し替える。
    /// displayItems 内の Photo 参照も新しいオブジェクトに同期する。
    /// </summary>
    public void setPhotos(IEnumerable<PhotoThumbnailItem> nextPhotos, bool autoGenerateThumbnails = true)
    {
        AppLogger.Trace("GalleryPhotosState.setPhotos: enter");
        try
        {
            photos.ReplaceAll(nextPhotos);

            photosRef.Clear();
            photosRef.AddRange(photos);

            var nextPhotoMap = photosRef.ToDictionary(photo => photo.PhotoPath, photo => photo);

            // displayItems 内の Photo 参照を新しいインスタンスに差し替える
            static PhotoGridItem SyncItem(PhotoGridItem item, IReadOnlyDictionary<string, PhotoThumbnailItem> map)
            {
                if (map.TryGetValue(item.Photo.PhotoPath, out var nextPhoto))
                {
                    item.Photo = nextPhoto;
                }

                if (item.GroupPhotos is not null)
                {
                    item.GroupPhotos = item.GroupPhotos
                        .Select(photo => map.TryGetValue(photo.PhotoPath, out var replacement) ? replacement : photo)
                        .ToArray();
                }

                return item;
            }

            for (var i = 0; i < displayItems.Count; i++)
            {
                displayItems[i] = SyncItem(displayItems[i], nextPhotoMap);
            }

            if (autoGenerateThumbnails)
                kickThumbnailGeneration(nextPhotoMap);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPhotosState.setPhotos: threw: {ex}");
        }
        AppLogger.Trace($"GalleryPhotosState.setPhotos: exit count={photos.Count}");
    }

    /// <summary>
    /// ドリルダウン等、photos に含まれない外部リスト向けにサムネイル生成を要求する。
    /// メイン側の進行中生成をキャンセルしないので、メインの表示は維持される。
    /// </summary>
    public void kickThumbnailsForExternal(IReadOnlyList<PhotoThumbnailItem> items)
    {
        if (items.Count == 0) return;
        var photoMap = new Dictionary<string, PhotoThumbnailItem>(items.Count);
        foreach (var p in items)
        {
            if (!string.IsNullOrEmpty(p.PhotoPath))
                photoMap.TryAdd(p.PhotoPath, p);
        }
        if (photoMap.Count == 0) return;
        kickThumbnailGeneration(photoMap, cancelPrevious: false);
    }

    /// <summary>
    /// MasonryView のビューポート内に入った写真のサムネイル生成を要求する。
    /// 既にサムネイルがある写真はスキップされる。
    /// </summary>
    public void requestVisibleThumbnails(IReadOnlyList<PhotoThumbnailItem> items)
    {
        if (items.Count == 0) return;
        var photoMap = new Dictionary<string, PhotoThumbnailItem>(items.Count);
        foreach (var p in items)
        {
            if (!string.IsNullOrEmpty(p.PhotoPath) && string.IsNullOrEmpty(p.GridThumbPath))
                photoMap.TryAdd(p.PhotoPath, p);
        }
        if (photoMap.Count == 0) return;
        kickThumbnailGeneration(photoMap, cancelPrevious: false);
    }

    /// <summary>
    /// グリッドサムネイルの非同期生成を開始する。
    /// cancelPrevious=true（フルリロード時）は実行中の生成をキャンセルしてから開始。
    /// cancelPrevious=false（loadMore 追加分）は既存生成を中断せず追加。
    /// </summary>
    private void kickThumbnailGeneration(IReadOnlyDictionary<string, PhotoThumbnailItem> photoMap, bool cancelPrevious = true)
    {
        CancellationTokenSource cts;
        if (cancelPrevious)
        {
            // Cancel/Dispose をアトミックに置換して、旧 CTS の Dispose と
            // バックグラウンドの ct.IsCancellationRequested 参照が交差しないようにする。
            var nextCts = new CancellationTokenSource();
            var prevCts = Interlocked.Exchange(ref thumbnailCts, nextCts);
            if (prevCts is not null)
            {
                try { prevCts.Cancel(); } catch { }
                prevCts.Dispose();
            }
            cts = nextCts;
        }
        else
        {
            // `??=` は読み込み→比較→代入が直列でないため、Visible thumbnails 要求が
            // 連続して飛んでくると複数の CTS が同時生成され、片方が即 GC される race がある。
            // CompareExchange で「null のときだけ自分の作った CTS を代入」を原子化する。
            var existing = Volatile.Read(ref thumbnailCts);
            if (existing is null)
            {
                var candidate = new CancellationTokenSource();
                var prior = Interlocked.CompareExchange(ref thumbnailCts, candidate, null);
                if (prior is null)
                {
                    cts = candidate;
                }
                else
                {
                    candidate.Dispose();
                    cts = prior;
                }
            }
            else
            {
                cts = existing;
            }
        }
        var ct = cts.Token;

        var targets = photoMap.Values
            .Where(p => string.IsNullOrEmpty(p.GridThumbPath) && !string.IsNullOrEmpty(p.PhotoPath))
            .Select(p => (path: p.PhotoPath, slot: p.SourceSlot))
            .ToList();
        if (targets.Count == 0) return;

        AppLogger.Trace($"GalleryPhotosState.kickThumbnailGeneration: dispatching count={targets.Count} cancel={cancelPrevious}");
        // Task.Run の第二引数に ct を渡すと、Task.Run 起動前に CTS が Dispose された場合
        // ObjectDisposedException が発生する race がある。token は内部で参照するだけにする。
        _ = Task.Run(async () =>
        {
            try
            {
                await thumbnailWorker.GenerateGridAsync(targets, result =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (!photoMap.TryGetValue(result.PhotoPath, out var item)) return;
                    UiThread.Run(() => item.GridThumbPath = result.ThumbPath);
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLogger.Error($"GalleryPhotosState.kickThumbnailGeneration: threw: {ex}");
            }
        });
    }

    private PhotoQueryPayload buildQueryParams()
    {
        return new PhotoQueryPayload(
            startDate: string.IsNullOrWhiteSpace(filters.dateFrom) ? null : filters.dateFrom,
            endDate: string.IsNullOrWhiteSpace(filters.dateTo) ? null : filters.dateTo,
            worldQuery: string.IsNullOrWhiteSpace(filters.searchQuery) ? null : filters.searchQuery.Trim(),
            worldExacts: filters.worldFilters.Count > 0 ? filters.worldFilters : null,
            orientation: filters.orientationFilter == "all" ? null : filters.orientationFilter,
            favoritesOnly: filters.favoritesOnly ? true : null,
            tagFilters: filters.tagFilters.Count > 0 ? filters.tagFilters : null,
            sourceSlot: filters.sourceSlot,
            limit: null,
            offset: null,
            includePhash: null,
            sortMode: filters.sortMode);
    }

    private async Task<IReadOnlyList<PhotoThumbnailItem>> fetchAllPhotos()
    {
        AppLogger.Trace("GalleryPhotosState.fetchAllPhotos: enter");
        var result = await photoService.GetPhotosAsync(buildQueryParams()).ConfigureAwait(false);
        AppLogger.Trace($"GalleryPhotosState.fetchAllPhotos: exit total={result.total}");
        return result.items.Select(PhotoThumbnailItem.FromDto).ToArray();
    }

    /// <summary>
    /// ロード済みの photos からグルーピングモードに応じて displayItems を再構築する。
    /// DB リクエリは行わない。
    /// </summary>
    public void rebuildDisplayItems(GroupingMode groupingMode)
    {
        AppLogger.Trace($"GalleryPhotosState.rebuildDisplayItems: enter mode={groupingMode} count={photosRef.Count}");
        IReadOnlyList<PhotoGridItem> items;

        if (groupingMode == GroupingMode.world)
        {
            var groups = photosRef
                .GroupBy(p => p.WorldName ?? "")
                .OrderByDescending(g => g.First().Timestamp)
                .Select(g =>
                {
                    var representative = g.First();
                    return new PhotoGridItem
                    {
                        Photo = representative,
                        GroupCount = g.Count(),
                        GroupKey = representative.WorldName ?? "",
                    };
                })
                .ToArray();
            items = groups;
        }
        else
        {
            items = photosRef.Select(p => new PhotoGridItem { Photo = p }).ToArray();
        }

        displayItemsRef.Clear();
        displayItemsRef.AddRange(items);
        displayItems.ReplaceAll(items);
        AppLogger.Trace($"GalleryPhotosState.rebuildDisplayItems: exit items={items.Count}");
    }

    /// <summary>
    /// フィルタ条件に基づき DB から全メタデータを一括取得する。
    /// transitionToken で多重ロードを制御し、古いリクエストの結果を自動破棄する。
    /// </summary>
    public async Task loadPhotos(int page = 0)
    {
        AppLogger.Trace("GalleryPhotosState.loadPhotos: enter");
        var token = transitionToken + 1;
        transitionToken = token;

        // 既存のサムネイル生成 CTS をアトミックに引き抜いてキャンセル + Dispose。
        var prevCts = Interlocked.Exchange(ref thumbnailCts, null);
        if (prevCts is not null)
        {
            try { prevCts.Cancel(); } catch { }
            prevCts.Dispose();
        }

        await dispatcherService.RunOnUiThread(() =>
        {
            IsLoading = true;
        }).ConfigureAwait(false);

        try
        {
            var photosTask = fetchAllPhotos();
            var monthTask = loadMonthSummary();
            var allPhotos = await photosTask.ConfigureAwait(false);
            await monthTask.ConfigureAwait(false);
            if (transitionToken != token)
            {
                AppLogger.Trace("GalleryPhotosState.loadPhotos: superseded by newer load");
                return;
            }

            AppLogger.Trace($"GalleryPhotosState.loadPhotos: fetched {allPhotos.Count} photos, rebuilding UI");

            await dispatcherService.RunOnUiThread(() =>
            {
                photosRef.Clear();
                photosRef.AddRange(allPhotos);

                photos.ReplaceAll(allPhotos);
                TotalCount = allPhotos.Count;
                rebuildDisplayItems(filters.groupingMode);
                OnPhotosReplaced?.Invoke();

                var photoMap = allPhotos.ToDictionary(p => p.PhotoPath, p => p);
                kickThumbnailGeneration(photoMap);
            }).ConfigureAwait(false);
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryPhotosState.loadPhotos: threw: {err}");
            toastService.addToast($"写真一覧の読み込みに失敗しました: {err}", ToastType.error);
        }
        finally
        {
            if (transitionToken == token)
            {
                await dispatcherService.RunOnUiThread(() =>
                {
                    IsLoading = false;
                }).ConfigureAwait(false);
            }
        }
        AppLogger.Trace("GalleryPhotosState.loadPhotos: exit");
    }

    /// <summary>イベントバスの購読を解除し、サムネイル生成をキャンセルする。</summary>
    public async ValueTask DisposeAsync()
    {
        AppLogger.Trace("GalleryPhotosState.DisposeAsync: enter");
        try { await dispose(scanCompletedUnlisten).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"GalleryPhotosState.DisposeAsync: scanCompleted unlisten threw: {ex}"); }

        try { await dispose(scanEnrichCompletedUnlisten).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"GalleryPhotosState.DisposeAsync: scanEnrichCompleted unlisten threw: {ex}"); }

        var prevCts = Interlocked.Exchange(ref thumbnailCts, null);
        if (prevCts is not null)
        {
            try { prevCts.Cancel(); } catch { }
            prevCts.Dispose();
        }
        transitionToken += 1;
        AppLogger.Trace("GalleryPhotosState.DisposeAsync: exit");

        static async ValueTask dispose(IAsyncDisposable? disposable)
        {
            if (disposable is not null)
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}