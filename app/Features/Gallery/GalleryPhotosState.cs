using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Models.Events;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using static Alpheratz.Messages.MessageCatalog;

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
    private readonly object thumbnailGenerationGate = new();
    private readonly Dictionary<Task, CancellationTokenSource> thumbnailOperations = [];
    private readonly HashSet<CancellationTokenSource> retiredThumbnailSources = [];
    private CancellationTokenSource? thumbnailCts;
    private bool thumbnailRequestsSuspended;
    private bool thumbnailFolderMutationSuspended;
    private bool thumbnailStateDisposed;
    private long thumbnailSuspensionVersion;

    private sealed record ThumbnailSuspension(
        long Version,
        Task[] Operations,
        CancellationTokenSource[] Sources);

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

    // 写真取得、イベント購読、通知、UI スレッド実行、サムネイル生成の依存を受け取る。
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

    /// <summary>DB 再取得を行わず、現在の検索条件と表示項目へ同じグループ化方式を反映する。</summary>
    public void SetGroupingMode(GroupingMode groupingMode)
    {
        filters = filters with { groupingMode = groupingMode };
        rebuildDisplayItems(groupingMode);
    }

    /// <summary>DB から全期間の月別件数を取得し、MonthNav 用のグループリストを構築する。</summary>
    public async Task loadMonthSummary()
    {
        AppLogger.Trace("GalleryPhotosState.loadMonthSummary: enter");
        try
        {
            var groups = await fetchMonthSummary(filters).ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
            {
                monthGroups = groups;
                OnMonthGroupsChanged?.Invoke(groups);
            }).ConfigureAwait(false);
            AppLogger.Trace($"GalleryPhotosState.loadMonthSummary: exit groups={groups.Count}");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPhotosState.loadMonthSummary: threw: {ex}");
        }
    }

    // 現在フィルタを月別件数クエリへ変換し、月ナビ用の連続インデックスを作る。
    private async Task<IReadOnlyList<GalleryMonthGroup>> fetchMonthSummary(PhotoQueryFilters currentFilters)
    {
        var payload = new PhotoQueryPayload(
            startDate: string.IsNullOrWhiteSpace(currentFilters.dateFrom) ? null : currentFilters.dateFrom,
            endDate: string.IsNullOrWhiteSpace(currentFilters.dateTo) ? null : currentFilters.dateTo,
            worldQuery: string.IsNullOrWhiteSpace(currentFilters.searchQuery) ? null : currentFilters.searchQuery.Trim(),
            worldExacts: currentFilters.worldFilters.Count > 0 ? currentFilters.worldFilters : null,
            orientation: currentFilters.orientationFilter == "all" ? null : currentFilters.orientationFilter,
            favoritesOnly: currentFilters.favoritesOnly ? true : null,
            tagFilters: currentFilters.tagFilters.Count > 0 ? currentFilters.tagFilters : null,
            sourceSlot: currentFilters.sourceSlot,
            limit: null,
            offset: null);
        var summaries = await photoService.GetMonthSummaryAsync(payload).ConfigureAwait(false);

        var groups = new List<GalleryMonthGroup>(summaries.Count);
        var runningIndex = 0;
        foreach (var s in summaries)
        {
            var key = $"{s.Year:D4}-{s.Month:D2}";
            groups.Add(new GalleryMonthGroup(
                key,
                s.Year,
                s.Month,
                getMsg("GalleryMonthGroup.monthLabel", ("month", s.Month)),
                runningIndex,
                s.Count));
            runningIndex += s.Count;
        }

        return groups;
    }

    /// <summary>
    /// イベントバスを購読し初回データをロードする。
    /// scan:completed / scan:enrich_completed 受信で自動再ロード。
    /// 初回ロードが完了してから購読を開始することで、
    /// 起動直後にスキャンが即完了するケースの DB 二重発行を防ぐ。
    /// </summary>
    public async Task InitializeAsync(bool loadInitialData = true)
    {
        AppLogger.Trace("GalleryPhotosState.InitializeAsync: enter");
        if (loadInitialData)
        {
            try
            {
                await loadPhotos(0).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"GalleryPhotosState.InitializeAsync: initial load threw: {ex}");
            }
        }

        try
        {
            scanCompletedUnlisten ??= eventBus.Subscribe(EventNames.ScanCompleted, () => loadPhotos());
            scanEnrichCompletedUnlisten ??= eventBus.Subscribe(EventNames.ScanEnrichCompleted, () => loadPhotos());
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPhotosState.InitializeAsync: subscribe threw: {ex}");
            throw;
        }
        AppLogger.Trace("GalleryPhotosState.InitializeAsync: exit");
    }

    /// <summary>
    /// 写真コレクションを一括差し替える。
    ///
    /// photos と displayItems の二重管理について：
    ///   - photos: 生の PhotoThumbnailItem 配列。MasonryView がこれを直接見る。
    ///   - displayItems: グルーピング情報付き PhotoGridItem 配列。標準グリッドが見る。
    ///   グルーピング変更は DB を叩かずに displayItems だけ再構築できる（高速）。
    ///
    /// 同期処理：差し替えで PhotoThumbnailItem の参照アドレスが変わると、
    /// displayItems が古い参照を持ったままになるので、photo_path をキーに新参照へ差し替える。
    /// グループ代表写真と GroupPhotos のサブ配列の両方を辿る必要がある。
    /// </summary>
    public void setPhotos(IEnumerable<PhotoThumbnailItem> nextPhotos, bool autoGenerateThumbnails = true)
    {
        AppLogger.Trace("GalleryPhotosState.setPhotos: enter");
        try
        {
            photos.ReplaceAll(nextPhotos);

            photosRef.Clear();
            photosRef.AddRange(photos);

            var nextPhotoMap = GalleryPhotosStateLogic.BuildReplacementMap(photosRef);

            for (var i = 0; i < displayItems.Count; i++)
            {
                displayItems[i] = GalleryPhotosStateLogic.SyncDisplayItem(displayItems[i], nextPhotoMap);
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
        var photoMap = GalleryPhotosStateLogic.BuildThumbnailRequestMap(items, requireMissingGridThumb: false);
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
        var photoMap = GalleryPhotosStateLogic.BuildThumbnailRequestMap(items, requireMissingGridThumb: true);
        if (photoMap.Count == 0) return;
        kickThumbnailGeneration(photoMap, cancelPrevious: false);
    }

    /// <summary>
    /// グリッドサムネイルのバックグラウンド生成を開始する。
    ///
    /// cancelPrevious=true（フィルタ変更・フルリロード時）：
    ///   進行中の生成をキャンセルしてから新しい CTS で開始する。
    ///   旧フィルタの写真に対する生成が新フィルタ表示の邪魔をしないようにするため。
    ///
    /// cancelPrevious=false（ビューポート進入時の lazy 要求）：
    ///   既に CTS が動いていればそれに相乗りし、無ければ新規生成する。
    ///   スクロール中に何度も呼ばれる経路で CTS を作り直すと、走り始めの生成が
    ///   即キャンセルされて UI が真っ白なまま、という症状になるため。
    ///
    /// タスクと CTS は thumbnailGenerationGate 配下で同時に登録する。
    /// フォルダ変更時は全登録タスクを停止して完了まで待つため、登録前後の隙間を作らない。
    /// </summary>
    private void kickThumbnailGeneration(IReadOnlyDictionary<string, PhotoThumbnailItem> photoMap, bool cancelPrevious = true)
    {
        var targets = GalleryPhotosStateLogic.BuildThumbnailTargets(photoMap.Values);
        if (targets.Count == 0) return;

        CancellationTokenSource? previousCts = null;
        CancellationTokenSource cts;
        CancellationToken ct;
        Task operation;
        lock (thumbnailGenerationGate)
        {
            if (thumbnailRequestsSuspended
                || thumbnailFolderMutationSuspended
                || thumbnailStateDisposed)
            {
                AppLogger.Trace("GalleryPhotosState.kickThumbnailGeneration: skip (suspended)");
                return;
            }

            if (cancelPrevious || thumbnailCts is null)
            {
                previousCts = thumbnailCts;
                if (previousCts is not null)
                    retiredThumbnailSources.Add(previousCts);
                thumbnailCts = new CancellationTokenSource();
            }

            cts = thumbnailCts!;
            ct = cts.Token;
            operation = Task.Run(async () =>
            {
                try
                {
                    await thumbnailWorker.GenerateGridAsync(targets, result =>
                    {
                        if (ct.IsCancellationRequested) return;
                        if (!photoMap.TryGetValue(result.PhotoPath, out var item)) return;
                        _ = dispatcherService.RunOnUiThread(() =>
                        {
                            if (!ct.IsCancellationRequested)
                                item.GridThumbPath = result.ThumbPath;
                        });
                    }, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    AppLogger.Error($"GalleryPhotosState.kickThumbnailGeneration: threw: {ex}");
                }
            });
            thumbnailOperations.Add(operation, cts);
        }

        _ = operation.ContinueWith(
            completeThumbnailOperation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        if (previousCts is not null)
        {
            try { previousCts.Cancel(); }
            catch (ObjectDisposedException) { }
            disposeIdleRetiredThumbnailSources();
        }
        AppLogger.Trace($"GalleryPhotosState.kickThumbnailGeneration: dispatching count={targets.Count} cancel={cancelPrevious}");
    }

    private void completeThumbnailOperation(Task operation)
    {
        CancellationTokenSource? sourceToDispose = null;
        lock (thumbnailGenerationGate)
        {
            if (!thumbnailOperations.Remove(operation, out var source))
                return;
            if (!ReferenceEquals(source, thumbnailCts)
                && !thumbnailOperations.Values.Any(candidate => ReferenceEquals(candidate, source)))
            {
                retiredThumbnailSources.Remove(source);
                sourceToDispose = source;
            }
        }
        sourceToDispose?.Dispose();
    }

    private void disposeIdleRetiredThumbnailSources()
    {
        CancellationTokenSource[] sourcesToDispose;
        lock (thumbnailGenerationGate)
        {
            sourcesToDispose = retiredThumbnailSources
                .Where(source => !thumbnailOperations.Values.Any(candidate => ReferenceEquals(candidate, source)))
                .ToArray();
            foreach (var source in sourcesToDispose)
                retiredThumbnailSources.Remove(source);
        }
        foreach (var source in sourcesToDispose)
            source.Dispose();
    }

    /// <summary>
    /// 現在登録済みのサムネイル生成をすべて停止し、ファイル書込みが終わるまで待つ。
    /// 停止後の新規要求は、次の loadPhotos が新一覧を反映するまで受け付けない。
    /// </summary>
    public async Task suspendThumbnailGenerationAndWait()
    {
        // フォルダ整理開始前の停止は、進行中の写真一覧読込も旧世代にする。
        // 旧一覧の適用経路からサムネイル生成が再開されることを防ぐために必要となる。
        ThumbnailSuspension suspension;
        lock (thumbnailGenerationGate)
        {
            thumbnailFolderMutationSuspended = true;
            Interlocked.Increment(ref transitionToken);
            suspension = beginThumbnailSuspensionLocked();
        }
        await waitThumbnailSuspension(suspension).ConfigureAwait(false);
        await dispatcherService.RunOnUiThread(() => IsLoading = false).ConfigureAwait(false);
    }

    /// <summary>フォルダ整理完了後、新しい一覧読込だけを許可する。生成再開は一覧適用時に行う。</summary>
    public void allowThumbnailReloadAfterFolderCleanup()
    {
        lock (thumbnailGenerationGate)
            thumbnailFolderMutationSuspended = false;
    }

    // transitionToken と同じ gate 内で呼び、写真一覧世代と停止世代の順序を一致させる。
    private ThumbnailSuspension beginThumbnailSuspensionLocked()
    {
        thumbnailRequestsSuspended = true;
        var suspensionVersion = unchecked(++thumbnailSuspensionVersion);
        if (thumbnailCts is not null)
        {
            retiredThumbnailSources.Add(thumbnailCts);
            thumbnailCts = null;
        }
        return new ThumbnailSuspension(
            suspensionVersion,
            thumbnailOperations.Keys.ToArray(),
            retiredThumbnailSources
                .Concat(thumbnailOperations.Values)
                .Distinct()
                .ToArray());
    }

    private async Task waitThumbnailSuspension(ThumbnailSuspension suspension)
    {
        foreach (var source in suspension.Sources)
        {
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        if (suspension.Operations.Length > 0)
        {
            try { await Task.WhenAll(suspension.Operations).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLogger.Warn($"GalleryPhotosState.suspendThumbnailGenerationAndWait: completion failed: {ex.Message}");
            }
        }
        disposeIdleRetiredThumbnailSources();
    }

    private bool resumeThumbnailGeneration(long suspensionVersion)
    {
        lock (thumbnailGenerationGate)
        {
            if (thumbnailStateDisposed
                || thumbnailFolderMutationSuspended
                || thumbnailSuspensionVersion != suspensionVersion)
                return false;
            thumbnailRequestsSuspended = false;
            return true;
        }
    }

    private bool isThumbnailStateDisposed()
    {
        lock (thumbnailGenerationGate)
            return thumbnailStateDisposed;
    }

    // PhotoQueryFilters を PhotoService が受け取る DTO へ変換する。
    private PhotoQueryPayload buildQueryParams(PhotoQueryFilters currentFilters)
    {
        return new PhotoQueryPayload(
            startDate: string.IsNullOrWhiteSpace(currentFilters.dateFrom) ? null : currentFilters.dateFrom,
            endDate: string.IsNullOrWhiteSpace(currentFilters.dateTo) ? null : currentFilters.dateTo,
            worldQuery: string.IsNullOrWhiteSpace(currentFilters.searchQuery) ? null : currentFilters.searchQuery.Trim(),
            worldExacts: currentFilters.worldFilters.Count > 0 ? currentFilters.worldFilters : null,
            orientation: currentFilters.orientationFilter == "all" ? null : currentFilters.orientationFilter,
            favoritesOnly: currentFilters.favoritesOnly ? true : null,
            tagFilters: currentFilters.tagFilters.Count > 0 ? currentFilters.tagFilters : null,
            sourceSlot: currentFilters.sourceSlot,
            limit: null,
            offset: null,
            includePhash: null,
            sortMode: currentFilters.sortMode);
    }

    // 現在フィルタに一致する写真を全件取得し、表示用アイテムへ変換する。
    private async Task<IReadOnlyList<PhotoThumbnailItem>> fetchAllPhotos(PhotoQueryFilters currentFilters)
    {
        AppLogger.Trace("GalleryPhotosState.fetchAllPhotos: enter");
        var result = await photoService.GetPhotosAsync(buildQueryParams(currentFilters)).ConfigureAwait(false);
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
                .GroupBy(p => GalleryPhotosStateLogic.BuildWorldGroupKey(p.WorldName))
                .OrderByDescending(g => g.First().Timestamp)
                .Select(g =>
                {
                    var representative = g.First();
                    return new PhotoGridItem
                    {
                        Photo = representative,
                        GroupCount = g.Count(),
                        GroupKey = g.Key,
                        GroupPhotos = g.ToArray(),
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
    /// フィルタ条件に基づき DB から全メタデータを一括取得し、UI コレクションへ反映する。
    ///
    /// transitionToken の役割：
    ///   フィルタを高速に切り替えると、古いクエリの結果が後から戻ってきて
    ///   新しいクエリの結果を上書きしてしまう可能性がある。
    ///   呼び出しごとにトークンをインクリメントし、await から戻った時点で
    ///   transitionToken が変わっていたら「自分は古い世代」と判断して破棄する。
    ///
    /// フロー：
    ///   1. transitionToken++ → 全サムネイル生成をキャンセル・完了待ち → IsLoading = true
    ///   2. fetchAllPhotos と loadMonthSummary を Task.WhenAll で並列取得
    ///   3. トークン整合性チェック → photosRef / photos / displayItems を更新
    ///   4. 新しい photoMap でサムネイル生成をキック
    /// </summary>
    public async Task loadPhotos(int page = 0)
    {
        AppLogger.Trace("GalleryPhotosState.loadPhotos: enter");
        long token;
        ThumbnailSuspension thumbnailSuspension;
        lock (thumbnailGenerationGate)
        {
            if (thumbnailStateDisposed || thumbnailFolderMutationSuspended)
            {
                AppLogger.Trace("GalleryPhotosState.loadPhotos: skip (thumbnail generation suspended)");
                return;
            }
            token = Interlocked.Increment(ref transitionToken);
            thumbnailSuspension = beginThumbnailSuspensionLocked();
        }
        await waitThumbnailSuspension(thumbnailSuspension).ConfigureAwait(false);
        if (Volatile.Read(ref transitionToken) != token || isThumbnailStateDisposed())
        {
            AppLogger.Trace("GalleryPhotosState.loadPhotos: superseded while stopping thumbnails");
            return;
        }

        await dispatcherService.RunOnUiThread(() =>
        {
            if (Volatile.Read(ref transitionToken) == token)
                IsLoading = true;
        }).ConfigureAwait(false);

        try
        {
            // 月集計と全件取得は独立した SQL なので、同じフィルタ snapshot で並行取得する。
            var filterSnapshot = filters;
            var photosTask = fetchAllPhotos(filterSnapshot);
            var monthTask = fetchMonthSummary(filterSnapshot);
            await Task.WhenAll(photosTask, monthTask).ConfigureAwait(false);
            var allPhotos = await photosTask.ConfigureAwait(false);
            var nextMonthGroups = await monthTask.ConfigureAwait(false);
            if (Volatile.Read(ref transitionToken) != token)
            {
                AppLogger.Trace("GalleryPhotosState.loadPhotos: superseded by newer load");
                return;
            }

            AppLogger.Trace($"GalleryPhotosState.loadPhotos: fetched {allPhotos.Count} photos, rebuilding UI");

            await dispatcherService.RunOnUiThread(() =>
            {
                // 世代確認から UI キュー実行までにも新しい読込が始まり得る。
                // 適用直前に再確認し、古い結果で現在の一覧を置き換えない。
                if (Volatile.Read(ref transitionToken) != token)
                    return;

                monthGroups = nextMonthGroups;
                OnMonthGroupsChanged?.Invoke(nextMonthGroups);

                photosRef.Clear();
                photosRef.AddRange(allPhotos);

                photos.ReplaceAll(allPhotos);
                TotalCount = allPhotos.Count;
                rebuildDisplayItems(filters.groupingMode);
                OnPhotosReplaced?.Invoke();

                var photoMap = GalleryPhotosStateLogic.BuildReplacementMap(allPhotos);
                if (resumeThumbnailGeneration(thumbnailSuspension.Version))
                    kickThumbnailGeneration(photoMap);
            }).ConfigureAwait(false);
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryPhotosState.loadPhotos: threw: {err}");
            if (Volatile.Read(ref transitionToken) == token)
                toastService.addToast(getMsg("GalleryPhotosState.photoLoadFailed"), ToastType.error);
        }
        finally
        {
            if (Volatile.Read(ref transitionToken) == token)
            {
                resumeThumbnailGeneration(thumbnailSuspension.Version);
                await dispatcherService.RunOnUiThread(() =>
                {
                    if (Volatile.Read(ref transitionToken) == token)
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
        ThumbnailSuspension suspension;
        lock (thumbnailGenerationGate)
        {
            thumbnailStateDisposed = true;
            Interlocked.Increment(ref transitionToken);
            suspension = beginThumbnailSuspensionLocked();
        }

        try { await dispose(scanCompletedUnlisten).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"GalleryPhotosState.DisposeAsync: scanCompleted unlisten threw: {ex}"); }

        try { await dispose(scanEnrichCompletedUnlisten).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"GalleryPhotosState.DisposeAsync: scanEnrichCompleted unlisten threw: {ex}"); }

        await waitThumbnailSuspension(suspension).ConfigureAwait(false);
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
