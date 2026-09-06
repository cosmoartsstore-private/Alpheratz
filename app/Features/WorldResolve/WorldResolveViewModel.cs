using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Services;
using Alpheratz.Shared.Services;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Features.WorldResolve;

/// <summary>
/// 候補ワールドリストの 1 エントリ。WorldResolve 画面の「別ワールドを選ぶ」ピッカーで使用。
/// Distance は対象写真の PDQ ハミング距離で、小さいほど類似度が高い。
/// </summary>
public sealed record CandidateEntry(
    string PhotoPath, string PhotoFilename, string WorldName, string? WorldId,
    int Distance, long SourceSlot);

/// <summary>
/// ワールド不明写真を PDQ 距離で既知写真とマッチさせる解析画面の ViewModel。
///
/// 処理手順：
///   1. InitializeAsync で「ワールド不明 + phash 持ち」写真を全件取得
///   2. source_slot ごとに固定長へ変換済みの既知写真を保持し、PDQ 距離で最近隣を割り出す
///   3. Items に「対象写真 + 推奨ワールド + 距離」を詰めて UI に表示
///   4. 利用者が適用対象にした項目を ApplyConfirmedAsync で DB 更新
/// </summary>
public partial class WorldResolveViewModel : UiThreadSafeObservableObject
{
    private const long SearchProgressUpdateMinIntervalMs = 125;
    // UI 用に論理 CPU の約20%を残しつつ、固定長整数の距離計算を最大16並列まで利用する。
    // 20論理CPUの実測では12並列より16並列が速く、19並列では逆に遅くなった。
    private static int CandidateSearchParallelism
        => Math.Clamp(
            Environment.ProcessorCount - Math.Max(1, Environment.ProcessorCount / 5),
            1,
            16);

    private readonly AlpheratzDb db;
    private readonly ThumbnailWorker thumbnailWorker;
    private readonly ToastService toastService;
    private readonly DispatcherService dispatcherService;
    private readonly object generationOperationGate = new();
    private readonly CancellationTokenSource generationLifetimeCts = new();
    private readonly List<GenerationOperation> generationOperations = [];
    private CancellationTokenSource? candidateThumbnailSource;
    private Task? stopGenerationTask;
    private bool generationStopping;
    private int candidateThumbnailGeneration;

    private sealed record GenerationOperation(Task Task, CancellationTokenSource Source);

    /// <summary>解析対象写真リスト（UI 上のメインリスト）。</summary>
    public UiObservableCollection<WorldResolveItem> Items { get; } = [];

    private bool isLoading;
    /// <summary>初期解析中フラグ。解析オーバーレイと進捗表示に使う。</summary>
    public bool IsLoading { get => isLoading; set => SetProperty(ref isLoading, value); }

    private bool isApplying;
    private int applyOperationInProgress;
    /// <summary>適用中フラグ。ApplyConfirmedAsync 中は true で UI を二重発火させない。</summary>
    public bool IsApplying { get => isApplying; set => SetProperty(ref isApplying, value); }

    private int searchProgressProcessed;
    /// <summary>候補探索で処理済みの対象写真数。</summary>
    public int SearchProgressProcessed { get => searchProgressProcessed; private set => SetProperty(ref searchProgressProcessed, value); }

    private int searchProgressTotal;
    /// <summary>候補探索の対象写真総数。0 の間は対象取得中として不定進捗を表示する。</summary>
    public int SearchProgressTotal { get => searchProgressTotal; private set => SetProperty(ref searchProgressTotal, value); }

    private string searchProgressText = getMsg("WorldResolveViewModel.checkingTargetPhotos");
    /// <summary>候補探索中に表示する現在状態の説明または処理件数。</summary>
    public string SearchProgressText { get => searchProgressText; private set => SetProperty(ref searchProgressText, value); }

    private int applyCount;
    /// <summary>適用対象として選択された件数。適用ボタンの活性化判定に使う。</summary>
    public int ApplyCount { get => applyCount; set => SetProperty(ref applyCount, value); }

    // --- Candidate picker ---
    private bool isCandidatePickerOpen;
    public bool IsCandidatePickerOpen { get => isCandidatePickerOpen; set => SetProperty(ref isCandidatePickerOpen, value); }

    private WorldResolveItem? activePickerItem;
    public WorldResolveItem? ActivePickerItem { get => activePickerItem; set => SetProperty(ref activePickerItem, value); }

    /// <summary>「別ワールドを選ぶ」ピッカーに表示する候補リスト（距離昇順）。</summary>
    public UiObservableCollection<CandidateEntry> CandidateList { get; } = [];

    private bool isCandidateLoading;
    public bool IsCandidateLoading { get => isCandidateLoading; set => SetProperty(ref isCandidateLoading, value); }
    private int candidatePickerGeneration;

    /// <summary>
    /// source_slot ごとに PDQ 文字列を固定長整数へ変換済みにした既知写真候補キャッシュ。
    /// 元の長いハッシュ文字列は保持せず、照合中の常駐メモリを抑える。
    /// </summary>
    private Dictionary<long, IReadOnlyList<WorldService.PreparedKnownWorldRow>> preparedKnownBySlot = new();

    // ワールド解決に必要な DB、サムネイル生成、通知サービスを受け取る。
    public WorldResolveViewModel(
        AlpheratzDb db,
        ThumbnailWorker thumbnailWorker,
        ToastService toastService,
        DispatcherService? dispatcherService = null)
    {
        this.db = db;
        this.thumbnailWorker = thumbnailWorker;
        this.toastService = toastService;
        this.dispatcherService = dispatcherService ?? new DispatcherService();
    }

    /// <summary>
    /// 解析を初期化する：ワールド不明写真を全件取得 → スロットごとに既知写真リストを引いて
    /// PDQ 最近隣を計算 → Items を構築 → サムネイル並列生成キック。
    /// CT で中断可能（モーダル閉じや別解析開始時）。
    /// </summary>
    public Task InitializeAsync(CancellationToken ct = default)
        => TrackGenerationOperation(InitializeCoreAsync, ct);

    private async Task InitializeCoreAsync(CancellationToken ct)
    {
        var initializationStartedAt = Stopwatch.GetTimestamp();
        var managedBytesBefore = GC.GetTotalMemory(forceFullCollection: false);
        var workingSetBefore = Environment.WorkingSet;
        var unknownQueryMilliseconds = 0d;
        var knownQueryMilliseconds = 0d;
        var prepareMilliseconds = 0d;
        var matchMilliseconds = 0d;
        var uiApplyMilliseconds = 0d;
        var unknownCount = 0;
        var knownCount = 0;
        var uniqueUnknownCount = 0;
        var completed = false;
        await dispatcherService.RunOnUiThread(() =>
        {
            SetSearchProgress(0, 0);
            IsLoading = true;
        }).ConfigureAwait(false);
        try
        {
            // DB 検索と PDQ 照合の前に UI スレッドを離す。一部の非同期処理は同期完了する場合がある。
            await Task.Run(static () => { }, ct).ConfigureAwait(false);

            var phaseStartedAt = Stopwatch.GetTimestamp();
            var unknowns = await db.GetUnknownWorldPhotosWithPhashAsync(ct).ConfigureAwait(false);
            unknownQueryMilliseconds = Stopwatch.GetElapsedTime(phaseStartedAt).TotalMilliseconds;
            unknownCount = unknowns.Count;
            if (unknowns.Count == 0)
            {
                completed = true;
                return;
            }
            await UpdateSearchProgressAsync(0, unknowns.Count, ct).ConfigureAwait(false);

            preparedKnownBySlot = new Dictionary<long, IReadOnlyList<WorldService.PreparedKnownWorldRow>>();
            foreach (var slot in unknowns.Select(item => item.SourceSlot).Distinct())
            {
                phaseStartedAt = Stopwatch.GetTimestamp();
                var knownPhotos = await db.GetKnownWorldPhotosAsync(slot, null, ct).ConfigureAwait(false);
                knownQueryMilliseconds += Stopwatch.GetElapsedTime(phaseStartedAt).TotalMilliseconds;
                knownCount += knownPhotos.Count;

                phaseStartedAt = Stopwatch.GetTimestamp();
                preparedKnownBySlot[slot] = WorldService.PrepareKnownWorldRows(knownPhotos, ct);
                prepareMilliseconds += Stopwatch.GetElapsedTime(phaseStartedAt).TotalMilliseconds;
            }

            var itemArray = new WorldResolveItem?[unknowns.Count];
            // 同じ source_slot と PDQ を持つ写真は照合結果も同じになるため、総当たり計算を 1 回にまとめる。
            var groupedUnknownIndexes = Enumerable.Range(0, unknowns.Count)
                .GroupBy(index => (unknowns[index].SourceSlot, unknowns[index].Phash))
                .Select(static group => group.ToArray())
                .ToArray();
            uniqueUnknownCount = groupedUnknownIndexes.Length;
            var progressWatch = Stopwatch.StartNew();
            var progressLock = new object();
            var progressUpdates = new ConcurrentBag<Task>();
            var processed = 0;

            var matchStartedAt = Stopwatch.GetTimestamp();
            try
            {
                await Task.Run(() =>
                {
                    Parallel.For(0, groupedUnknownIndexes.Length, new ParallelOptions
                    {
                        CancellationToken = ct,
                        MaxDegreeOfParallelism = CandidateSearchParallelism,
                    }, groupIndex =>
                    {
                        var unknownIndexes = groupedUnknownIndexes[groupIndex];
                        var representative = unknowns[unknownIndexes[0]];
                        (AlpheratzDb.KnownWorldRow Row, int Distance)? match = null;

                        if (preparedKnownBySlot.TryGetValue(representative.SourceSlot, out var knownPhotos) && knownPhotos.Count > 0)
                        {
                            match = WorldService.FindBestMatchWithDetails(representative.Phash, knownPhotos, ct);
                        }

                        foreach (var unknownIndex in unknownIndexes)
                        {
                            var unknown = unknowns[unknownIndex];
                            var item = new WorldResolveItem(
                                unknown.PhotoPath,
                                unknown.PhotoFilename,
                                unknown.Phash,
                                unknown.SourceSlot);
                            if (match is not null)
                            {
                                item.MatchPhotoPath = match.Value.Row.PhotoPath;
                                item.MatchPhotoFilename = match.Value.Row.PhotoFilename;
                                item.MatchWorldName = match.Value.Row.WorldName;
                                item.MatchWorldId = match.Value.Row.WorldId;
                                item.MatchDistance = match.Value.Distance;
                            }
                            itemArray[unknownIndex] = item;
                        }

                        var current = Interlocked.Add(ref processed, unknownIndexes.Length);
                        var shouldUpdate = false;
                        lock (progressLock)
                        {
                            if (ShouldUpdateSearchProgress(current, unknowns.Count, progressWatch))
                            {
                                progressWatch.Restart();
                                shouldUpdate = true;
                            }
                        }
                        if (shouldUpdate)
                            progressUpdates.Add(UpdateSearchProgressAsync(current, unknowns.Count, ct));
                    });
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                if (!progressUpdates.IsEmpty)
                {
                    try { await Task.WhenAll(progressUpdates).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
                matchMilliseconds = Stopwatch.GetElapsedTime(matchStartedAt).TotalMilliseconds;
            }

            var items = itemArray
                .Where(static item => item is not null)
                .Cast<WorldResolveItem>()
                .OrderBy(static item => item.MatchDistance ?? int.MaxValue)
                .ThenBy(static item => item.TargetPhotoFilename, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            await UpdateSearchProgressAsync(unknowns.Count, unknowns.Count, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            await dispatcherService.RunOnUiThread(() =>
            {
                var uiApplyStartedAt = Stopwatch.GetTimestamp();
                Items.ReplaceAll(items);
                IsLoading = false;
                uiApplyMilliseconds = Stopwatch.GetElapsedTime(uiApplyStartedAt).TotalMilliseconds;
            }).ConfigureAwait(false);
            completed = true;

            var thumbTargets = new List<(string path, long slot)>();
            foreach (var item in items)
            {
                thumbTargets.Add((item.TargetPhotoPath, item.TargetSourceSlot));
                if (item.MatchPhotoPath is not null)
                    thumbTargets.Add((item.MatchPhotoPath, item.TargetSourceSlot));
            }

            StartGenerationOperation(
                operationToken => GenerateInitialThumbnailsAsync(items, thumbTargets, operationToken),
                ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldResolveViewModel.InitializeAsync: {ex}");
            toastService.addToast(
                getMsg("WorldResolveViewModel.initializationFailed"),
                Shared.Models.ToastType.error);
        }
        finally
        {
            await dispatcherService.RunOnUiThread(() => IsLoading = false).ConfigureAwait(false);
            AppLogger.Info(
                $"Performance.WorldResolve completed={completed} unknown={unknownCount} " +
                $"unique_unknown={uniqueUnknownCount} known={knownCount} " +
                $"unknown_query_ms={unknownQueryMilliseconds:F3} known_query_ms={knownQueryMilliseconds:F3} " +
                $"prepare_ms={prepareMilliseconds:F3} match_ms={matchMilliseconds:F3} " +
                $"ui_apply_ms={uiApplyMilliseconds:F3} " +
                $"total_ms={Stopwatch.GetElapsedTime(initializationStartedAt).TotalMilliseconds:F3} " +
                $"managed_delta_bytes={GC.GetTotalMemory(forceFullCollection: false) - managedBytesBefore} " +
                $"working_set_delta_bytes={Environment.WorkingSet - workingSetBefore}");
        }
    }

    private async Task GenerateInitialThumbnailsAsync(
        IReadOnlyList<WorldResolveItem> items,
        IReadOnlyList<(string path, long slot)> targets,
        CancellationToken ct)
    {
        var uiUpdates = new ConcurrentBag<Task>();
        var itemsByPhotoPath = new Dictionary<string, List<WorldResolveItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            AddThumbnailTarget(itemsByPhotoPath, item.TargetPhotoPath, item);
            if (item.MatchPhotoPath is not null
                && !string.Equals(item.MatchPhotoPath, item.TargetPhotoPath, StringComparison.OrdinalIgnoreCase))
            {
                AddThumbnailTarget(itemsByPhotoPath, item.MatchPhotoPath, item);
            }
        }
        var uniqueTargets = targets
            .DistinctBy(static target => target.path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        try
        {
            // ThumbnailWorker は呼出時に全体停止ゲートへ同期登録する。
            // cleanup の登録境界を越えないよう、この呼出しより前に未完了 await を追加しない。
            await thumbnailWorker.GenerateGridAsync(uniqueTargets, result =>
            {
                if (ct.IsCancellationRequested || IsGenerationStopping())
                    return;
                if (!itemsByPhotoPath.TryGetValue(result.PhotoPath, out var matchingItems))
                    return;

                foreach (var item in matchingItems)
                {
                    uiUpdates.Add(dispatcherService.RunOnUiThread(() =>
                    {
                        if (ct.IsCancellationRequested || IsGenerationStopping())
                            return;
                        if (string.Equals(item.TargetPhotoPath, result.PhotoPath, StringComparison.OrdinalIgnoreCase))
                            item.TargetThumbPath = result.ThumbPath;
                        if (string.Equals(item.MatchPhotoPath, result.PhotoPath, StringComparison.OrdinalIgnoreCase))
                            item.MatchThumbPath = result.ThumbPath;
                    }));
                }
            }, ct).ConfigureAwait(false);

            if (!uiUpdates.IsEmpty)
                await Task.WhenAll(uiUpdates).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLogger.Warn($"WorldResolve thumb generation: {ex.Message}"); }
    }

    private static void AddThumbnailTarget(
        Dictionary<string, List<WorldResolveItem>> itemsByPhotoPath,
        string photoPath,
        WorldResolveItem item)
    {
        if (!itemsByPhotoPath.TryGetValue(photoPath, out var matchingItems))
        {
            matchingItems = [];
            itemsByPhotoPath.Add(photoPath, matchingItems);
        }
        matchingItems.Add(item);
    }

    private static bool ShouldUpdateSearchProgress(int processed, int total, Stopwatch progressWatch)
        => processed == 1
            || processed >= total
            || progressWatch.ElapsedMilliseconds >= SearchProgressUpdateMinIntervalMs;

    private Task UpdateSearchProgressAsync(int processed, int total, CancellationToken ct)
        => dispatcherService.RunOnUiThread(() =>
        {
            if (!ct.IsCancellationRequested && !IsGenerationStopping())
                SetSearchProgress(processed, total);
        });

    private void SetSearchProgress(int processed, int total)
    {
        SearchProgressProcessed = Math.Clamp(processed, 0, Math.Max(total, 0));
        SearchProgressTotal = Math.Max(total, 0);
        SearchProgressText = SearchProgressTotal > 0
            ? getMsg(
                "WorldResolveViewModel.searchProgress",
                ("processed", SearchProgressProcessed),
                ("total", SearchProgressTotal))
            : getMsg("WorldResolveViewModel.checkingTargetPhotos");
    }

    private async Task<IReadOnlyList<WorldService.PreparedKnownWorldRow>> GetPreparedKnownPhotosAsync(long sourceSlot, CancellationToken ct)
    {
        if (preparedKnownBySlot.TryGetValue(sourceSlot, out var prepared))
            return prepared;

        var knownPhotos = await db.GetKnownWorldPhotosAsync(sourceSlot, null, ct).ConfigureAwait(false);
        prepared = WorldService.PrepareKnownWorldRows(knownPhotos, ct);
        preparedKnownBySlot[sourceSlot] = prepared;
        return prepared;
    }

    // 個別候補の適用予定状態を反転し、適用件数を更新する。
    public void ToggleApply(WorldResolveItem item)
    {
        if (IsApplying) return;
        if (!item.CanApply)
        {
            item.IsApplied = false;
            RecountApply();
            return;
        }
        item.IsApplied = !item.IsApplied;
        RecountApply();
    }

    // マッチが見つかっている全候補を適用予定にする。
    public void ApplyAll()
    {
        if (IsApplying) return;
        foreach (var item in Items)
            if (item.CanApply) item.IsApplied = true;
        RecountApply();
    }

    // 全候補を適用予定から外す。
    public void SkipAll()
    {
        if (IsApplying) return;
        foreach (var item in Items) item.IsApplied = false;
        RecountApply();
    }

    // Items 内の適用予定件数を数え直し、ボタン表示へ反映する。
    private void RecountApply()
    {
        ApplyCount = Items.Count(x => x.IsApplied && x.CanApply);
    }

    /// <summary>
    /// 呼出時点で IsApplied=true の Items を確定し、順次 DB に書き込む。
    /// match_source は "phash_confirmed" 固定で利用者による選択済みであることを記録する。
    /// finally で IsApplying=false に必ず戻すことで、例外発生時に UI が二度と操作可能に戻らない
    /// 状態を防ぐ。戻り値は実際に適用できた件数。
    /// </summary>
    public async Task<int> ApplyConfirmedAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref applyOperationInProgress, 1, 0) != 0)
            return 0;

        var applied = 0;
        List<(string PhotoPath, string WorldName, string? WorldId, string MatchSource)> updates = [];
        try
        {
            await dispatcherService.RunOnUiThread(() =>
            {
                IsApplying = true;
                updates = Items
                    .Where(item => item.IsApplied && item.CanApply && item.MatchWorldName is not null)
                    .Select(item => (
                        PhotoPath: item.TargetPhotoPath,
                        WorldName: item.MatchWorldName!,
                        WorldId: item.MatchWorldId,
                        MatchSource: item.IsManualMatch ? "manual" : "phash_confirmed"))
                    .ToList();
            }).ConfigureAwait(false);

            foreach (var update in updates)
            {
                ct.ThrowIfCancellationRequested();
                await db.UpdatePhotoWorldAsync(
                    update.PhotoPath,
                    update.WorldName,
                    update.WorldId,
                    update.MatchSource,
                    ct).ConfigureAwait(false);
                applied++;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldResolveViewModel.ApplyConfirmedAsync: {ex}");
            toastService.addToast(
                getMsg("WorldResolveViewModel.applyFailed"),
                Shared.Models.ToastType.error);
            throw;
        }
        finally
        {
            try
            {
                await dispatcherService.RunOnUiThread(() => IsApplying = false).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref applyOperationInProgress, 0);
            }
        }
        return applied;
    }

    /// <summary>
    /// 「別ワールドを選ぶ」ピッカーを開く。
    /// 固定長へ変換済みの既知写真キャッシュから、距離順に CandidateList を組み立てる。
    /// 候補画像は XAML のコンバーターが元写真を表示用サイズで読み込む。
    /// </summary>
    public Task OpenCandidatePickerAsync(WorldResolveItem item, CancellationToken ct = default)
        => TrackGenerationOperation(token => OpenCandidatePickerCoreAsync(item, token), ct);

    private async Task OpenCandidatePickerCoreAsync(WorldResolveItem item, CancellationToken ct)
    {
        var generation = Interlocked.Increment(ref candidatePickerGeneration);
        await dispatcherService.RunOnUiThread(() =>
        {
            if (!IsCurrentCandidatePickerRequest(generation))
                return;
            ActivePickerItem = item;
            IsCandidateLoading = true;
            IsCandidatePickerOpen = true;
        }).ConfigureAwait(false);

        try
        {
            var knownPhotos = await GetPreparedKnownPhotosAsync(item.TargetSourceSlot, ct).ConfigureAwait(false);
            var ranked = await Task.Run(
                () => WorldService.RankCandidatesByDistance(item.TargetPhash, knownPhotos, ct),
                ct).ConfigureAwait(false);
            var entries = ranked
                .Where(static result => WorldService.CanAutomaticallyComplete(result.Distance))
                .GroupBy(
                    static result => result.Row.WorldName,
                    StringComparer.CurrentCultureIgnoreCase)
                .Select(static group => group.First())
                .OrderBy(static result => result.Distance)
                .ThenBy(static result => result.Row.WorldName, StringComparer.CurrentCultureIgnoreCase)
                .Select(result => new CandidateEntry(
                    result.Row.PhotoPath,
                    result.Row.PhotoFilename,
                    result.Row.WorldName,
                    result.Row.WorldId,
                    result.Distance,
                    result.Row.SourceSlot))
                .ToList();

            await dispatcherService.RunOnUiThread(() =>
            {
                if (!IsCurrentCandidatePickerRequest(generation))
                    return;
                CandidateList.ReplaceAll(entries);
                IsCandidateLoading = false;
            }).ConfigureAwait(false);

        }
        catch (OperationCanceledException)
        {
            await CloseCandidatePickerIfCurrentAsync(generation).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldResolveViewModel.OpenCandidatePickerAsync: {ex}");
            if (IsCurrentCandidatePickerRequest(generation))
            {
                toastService.addToast(
                    getMsg("WorldResolveViewModel.candidateLoadFailed"),
                    Shared.Models.ToastType.error);
            }
            await CloseCandidatePickerIfCurrentAsync(generation).ConfigureAwait(false);
        }
    }

    private bool IsCurrentCandidatePickerRequest(int generation)
        => Volatile.Read(ref candidatePickerGeneration) == generation;

    private Task CloseCandidatePickerIfCurrentAsync(int generation)
        => dispatcherService.RunOnUiThread(() =>
        {
            if (IsCurrentCandidatePickerRequest(generation))
                CloseCandidatePicker();
        });

    /// <summary>
    /// ピッカーで選ばれた候補を ActivePickerItem に反映する。
    /// MatchThumbPath を一度 null にしてから再生成キックするのは、UI が古いサムネイルを
    /// 出し続けるのを防ぎ、ロード完了まで「サムネ無し」状態を経由させるため。
    /// </summary>
    public void SelectCandidate(CandidateEntry entry, CancellationToken ct = default)
    {
        if (ActivePickerItem is not { } item
            || !WorldService.CanAutomaticallyComplete(entry.Distance)) return;
        item.MatchPhotoPath = entry.PhotoPath;
        item.MatchPhotoFilename = entry.PhotoFilename;
        item.MatchWorldName = entry.WorldName;
        item.MatchWorldId = entry.WorldId;
        item.MatchDistance = entry.Distance;
        item.MatchThumbPath = null;
        item.IsManualMatch = false;

        var generation = Interlocked.Increment(ref candidateThumbnailGeneration);
        StartCandidateThumbnailOperation(
            operationToken => GenerateSelectedCandidateThumbnailAsync(
                item,
                entry,
                generation,
                operationToken),
            ct);

        CloseCandidatePicker();
    }

    /// <summary>自由入力したワールド名を現在の対象写真の補完候補として設定する。</summary>
    public bool SelectManualWorldName(string worldName)
    {
        if (ActivePickerItem is not { } item)
            return false;

        var normalized = worldName.Trim();
        if (normalized.Length == 0)
            return false;

        Interlocked.Increment(ref candidateThumbnailGeneration);
        item.MatchPhotoPath = null;
        item.MatchPhotoFilename = null;
        item.MatchWorldName = normalized;
        item.MatchWorldId = null;
        item.MatchDistance = null;
        item.MatchThumbPath = null;
        item.IsManualMatch = true;
        CloseCandidatePicker();
        return true;
    }

    private async Task GenerateSelectedCandidateThumbnailAsync(
        WorldResolveItem item,
        CandidateEntry entry,
        int generation,
        CancellationToken ct)
    {
        var uiUpdates = new ConcurrentBag<Task>();
        try
        {
            // ThumbnailWorker は呼出時に全体停止ゲートへ同期登録する。
            // cleanup の登録境界を越えないよう、この呼出しより前に未完了 await を追加しない。
            await thumbnailWorker.GenerateGridAsync(
                [(entry.PhotoPath, entry.SourceSlot)],
                result =>
                {
                    if (ct.IsCancellationRequested || !IsCurrentCandidateThumbnailRequest(generation))
                        return;

                    uiUpdates.Add(dispatcherService.RunOnUiThread(() =>
                    {
                        if (!ct.IsCancellationRequested
                            && IsCurrentCandidateThumbnailRequest(generation)
                            && string.Equals(item.MatchPhotoPath, entry.PhotoPath, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(result.PhotoPath, entry.PhotoPath, StringComparison.OrdinalIgnoreCase))
                        {
                            item.MatchThumbPath = result.ThumbPath;
                        }
                    }));
                },
                ct).ConfigureAwait(false);

            if (!uiUpdates.IsEmpty)
                await Task.WhenAll(uiUpdates).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLogger.Warn($"SelectCandidate thumb: {ex.Message}"); }
    }

    private bool IsCurrentCandidateThumbnailRequest(int generation)
        => !IsGenerationStopping()
            && Volatile.Read(ref candidateThumbnailGeneration) == generation;

    // 候補ピッカーの選択状態と読み込み状態をクリアして閉じる。
    public void CloseCandidatePicker()
    {
        Interlocked.Increment(ref candidatePickerGeneration);
        IsCandidatePickerOpen = false;
        ActivePickerItem = null;
        IsCandidateLoading = false;
    }

    private void StartGenerationOperation(
        Func<CancellationToken, Task> operation,
        CancellationToken ct)
        => TrackGenerationOperation(operation, ct);

    private void StartCandidateThumbnailOperation(
        Func<CancellationToken, Task> operation,
        CancellationToken ct)
    {
        CancellationTokenSource? previousCandidate = null;
        lock (generationOperationGate)
        {
            if (generationStopping)
                return;

            var source = CancellationTokenSource.CreateLinkedTokenSource(generationLifetimeCts.Token, ct);
            try
            {
                var task = operation(source.Token);
                generationOperations.Add(new GenerationOperation(task, source));
                previousCandidate = candidateThumbnailSource;
                candidateThumbnailSource = source;
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }

        if (previousCandidate is not null)
        {
            try { previousCandidate.Cancel(); }
            catch (ObjectDisposedException) { }
        }

    }

    private Task TrackGenerationOperation(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        lock (generationOperationGate)
        {
            if (generationStopping)
                return Task.CompletedTask;

            var source = CancellationTokenSource.CreateLinkedTokenSource(generationLifetimeCts.Token, ct);
            try
            {
                var task = operation(source.Token);
                generationOperations.Add(new GenerationOperation(task, source));
                return task;
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }
    }

    private bool IsGenerationStopping()
    {
        lock (generationOperationGate)
            return generationStopping;
    }

    /// <summary>WorldResolve が開始した処理をすべて停止し、サムネイル書込みと UI 反映の完了を待つ。</summary>
    public Task StopGenerationAsync()
    {
        lock (generationOperationGate)
        {
            if (stopGenerationTask is not null)
                return stopGenerationTask;

            generationStopping = true;
            candidateThumbnailSource = null;
            Interlocked.Increment(ref candidatePickerGeneration);
            Interlocked.Increment(ref candidateThumbnailGeneration);
            stopGenerationTask = StopGenerationCoreAsync(generationOperations.ToArray());
            return stopGenerationTask;
        }
    }

    private async Task StopGenerationCoreAsync(GenerationOperation[] operations)
    {
        try { generationLifetimeCts.Cancel(); }
        catch (ObjectDisposedException) { }

        foreach (var operation in operations)
        {
            try { operation.Source.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        if (operations.Length > 0)
        {
            try { await Task.WhenAll(operations.Select(operation => operation.Task)).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLogger.Warn($"WorldResolveViewModel.StopGenerationAsync: {ex.Message}"); }
        }

        lock (generationOperationGate)
            generationOperations.Clear();

        foreach (var operation in operations)
            operation.Source.Dispose();
        generationLifetimeCts.Dispose();

        preparedKnownBySlot.Clear();
    }
}
