using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Services;
using Alpheratz.Shared.Services;

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
/// 大まかな流れ：
///   1. InitializeAsync で「ワールド不明 + phash 持ち」写真を全件取得
///   2. source_slot ごとに既知写真リスト (knownBySlot キャッシュ) を取り、PDQ 距離で最近隣を割り出す
///   3. Items に「対象写真 + 推奨ワールド + 距離」を詰めて UI に表示
///   4. ユーザが採用ボタンを押したものを ApplyConfirmedAsync で DB 更新
/// </summary>
public partial class WorldResolveViewModel : UiThreadSafeObservableObject
{
    private readonly AlpheratzDb db;
    private readonly ThumbnailWorker thumbnailWorker;
    private readonly ToastService toastService;
    private readonly DispatcherService dispatcherService;

    /// <summary>解析対象写真リスト（UI 上のメインリスト）。</summary>
    public UiObservableCollection<WorldResolveItem> Items { get; } = [];

    private bool isLoading;
    /// <summary>初期解析中フラグ。スピナー表示に使う。</summary>
    public bool IsLoading { get => isLoading; set => SetProperty(ref isLoading, value); }

    private bool isApplying;
    /// <summary>適用中フラグ。ApplyConfirmedAsync 中は true で UI を二重発火させない。</summary>
    public bool IsApplying { get => isApplying; set => SetProperty(ref isApplying, value); }

    private int applyCount;
    /// <summary>「採用する」マーク済み件数。適用ボタンの活性化判定に使う。</summary>
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

    /// <summary>
    /// source_slot ごとの既知写真リストキャッシュ。スロット内の写真は同じ参照集合と
    /// 比較するため、複数の WorldResolveItem 解析中に DB を何度も叩かないようキャッシュする。
    /// 本 VM の生存期間中だけ有効（モーダルを閉じれば VM ごと破棄）。
    /// </summary>
    private Dictionary<long, IReadOnlyList<AlpheratzDb.KnownWorldRow>> knownBySlot = new();

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
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await dispatcherService.RunOnUiThread(() => IsLoading = true).ConfigureAwait(false);
        try
        {
            // Leave the UI thread before DB scans and PDQ matching; some async calls can complete synchronously.
            await Task.Run(static () => { }, ct).ConfigureAwait(false);

            var unknowns = await db.GetUnknownWorldPhotosWithPhashAsync(ct).ConfigureAwait(false);
            if (unknowns.Count == 0)
            {
                return;
            }

            knownBySlot = new Dictionary<long, IReadOnlyList<AlpheratzDb.KnownWorldRow>>();
            var items = new List<WorldResolveItem>();

            foreach (var unknown in unknowns)
            {
                ct.ThrowIfCancellationRequested();
                if (!knownBySlot.TryGetValue(unknown.SourceSlot, out var knownPhotos))
                {
                    knownPhotos = await db.GetKnownWorldPhotosAsync(unknown.SourceSlot, null, ct).ConfigureAwait(false);
                    knownBySlot[unknown.SourceSlot] = knownPhotos;
                }

                var item = new WorldResolveItem(unknown.PhotoPath, unknown.PhotoFilename, unknown.Phash, unknown.SourceSlot);

                if (knownPhotos.Count > 0)
                {
                    var match = WorldService.FindBestMatchWithDetails(unknown.Phash, knownPhotos);
                    if (match is not null)
                    {
                        item.MatchPhotoPath = match.Value.Row.PhotoPath;
                        item.MatchPhotoFilename = match.Value.Row.PhotoFilename;
                        item.MatchWorldName = match.Value.Row.WorldName;
                        item.MatchWorldId = match.Value.Row.WorldId;
                        item.MatchDistance = match.Value.Distance;
                    }
                }

                items.Add(item);
            }

            await dispatcherService.RunOnUiThread(() =>
            {
                Items.ReplaceAll(items);
                IsLoading = false;
            }).ConfigureAwait(false);

            var thumbTargets = new List<(string path, long slot)>();
            foreach (var item in items)
            {
                thumbTargets.Add((item.TargetPhotoPath, item.TargetSourceSlot));
                if (item.MatchPhotoPath is not null)
                    thumbTargets.Add((item.MatchPhotoPath, item.TargetSourceSlot));
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await thumbnailWorker.GenerateGridAsync(thumbTargets, result =>
                    {
                        foreach (var item in items.Where(item =>
                            item.TargetPhotoPath == result.PhotoPath || item.MatchPhotoPath == result.PhotoPath))
                        {
                            _ = dispatcherService.RunOnUiThread(() =>
                            {
                                if (item.TargetPhotoPath == result.PhotoPath)
                                    item.TargetThumbPath = result.ThumbPath;
                                if (item.MatchPhotoPath == result.PhotoPath)
                                    item.MatchThumbPath = result.ThumbPath;
                            });
                        }
                    }, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { AppLogger.Warn($"WorldResolve thumb generation: {ex.Message}"); }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldResolveViewModel.InitializeAsync: {ex}");
            toastService.addToast($"初期化に失敗しました: {ex.Message}", Shared.Models.ToastType.error);
        }
        finally
        {
            await dispatcherService.RunOnUiThread(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    // 個別候補の適用予定状態を反転し、適用件数を更新する。
    public void ToggleApply(WorldResolveItem item)
    {
        item.IsApplied = !item.IsApplied;
        RecountApply();
    }

    // マッチが見つかっている全候補を適用予定にする。
    public void ApplyAll()
    {
        foreach (var item in Items)
            if (item.HasMatch) item.IsApplied = true;
        RecountApply();
    }

    // 全候補を適用予定から外す。
    public void SkipAll()
    {
        foreach (var item in Items) item.IsApplied = false;
        RecountApply();
    }

    // Items 内の適用予定件数を数え直し、ボタン表示へ反映する。
    private void RecountApply()
    {
        ApplyCount = Items.Count(x => x.IsApplied);
    }

    /// <summary>
    /// IsApplied=true マークされた Items を順次 DB に書き込む。
    /// match_source は "phash_confirmed" 固定でユーザ確認済みである旨をマーキングする。
    /// finally で IsApplying=false に必ず戻すことで、例外発生時に UI が二度と操作可能に戻らない
    /// 状態を防ぐ。戻り値は実際に適用できた件数。
    /// </summary>
    public async Task<int> ApplyConfirmedAsync(CancellationToken ct = default)
    {
        await dispatcherService.RunOnUiThread(() => IsApplying = true).ConfigureAwait(false);
        var applied = 0;
        try
        {
            foreach (var item in Items)
            {
                ct.ThrowIfCancellationRequested();
                if (!item.IsApplied || item.MatchWorldName is null) continue;
                await db.UpdatePhotoWorldAsync(item.TargetPhotoPath, item.MatchWorldName, item.MatchWorldId, "phash_confirmed", ct).ConfigureAwait(false);
                applied++;
            }
        }
        finally
        {
            await dispatcherService.RunOnUiThread(() => IsApplying = false).ConfigureAwait(false);
        }
        return applied;
    }

    /// <summary>
    /// 「別ワールドを選ぶ」ピッカーを開く。
    /// 既知写真リストを knownBySlot キャッシュから取り、距離順にランク付けして CandidateList に詰める。
    /// 候補のサムネイル生成は fire-and-forget の Task.Run で並行起動する（UI ブロックを避けるため）。
    /// </summary>
    public async Task OpenCandidatePickerAsync(WorldResolveItem item, CancellationToken ct = default)
    {
        await dispatcherService.RunOnUiThread(() =>
        {
            ActivePickerItem = item;
            IsCandidateLoading = true;
            IsCandidatePickerOpen = true;
        }).ConfigureAwait(false);

        try
        {
            if (!knownBySlot.TryGetValue(item.TargetSourceSlot, out var knownPhotos))
            {
                knownPhotos = await db.GetKnownWorldPhotosAsync(item.TargetSourceSlot, null, ct).ConfigureAwait(false);
                knownBySlot[item.TargetSourceSlot] = knownPhotos;
            }

            var ranked = await Task.Run(() => WorldService.RankCandidatesByDistance(item.TargetPhash, knownPhotos), ct).ConfigureAwait(false);
            var entries = ranked.Select(r => new CandidateEntry(
                r.Row.PhotoPath, r.Row.PhotoFilename, r.Row.WorldName, r.Row.WorldId,
                r.Distance, r.Row.SourceSlot)).ToList();

            await dispatcherService.RunOnUiThread(() =>
            {
                CandidateList.ReplaceAll(entries);
                IsCandidateLoading = false;
            }).ConfigureAwait(false);

            var thumbTargets = entries.Select(e => (e.PhotoPath, e.SourceSlot)).ToList();
            _ = Task.Run(async () =>
            {
                try
                {
                    await thumbnailWorker.GenerateGridAsync(thumbTargets, result =>
                    {
                        // CandidateEntry は immutable record なので、候補サムネイルは XAML 側のコンバータで解決する。
                    }, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { AppLogger.Warn($"Candidate thumb generation: {ex.Message}"); }
            });
        }
        catch (OperationCanceledException)
        {
            await dispatcherService.RunOnUiThread(CloseCandidatePicker).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldResolveViewModel.OpenCandidatePickerAsync: {ex}");
            await dispatcherService.RunOnUiThread(CloseCandidatePicker).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// ピッカーで選ばれた候補を ActivePickerItem に反映する。
    /// MatchThumbPath を一度 null にしてから再生成キックするのは、UI が古いサムネイルを
    /// 出し続けるのを防ぎ、ロード完了まで「サムネ無し」状態を経由させるため。
    /// </summary>
    public void SelectCandidate(CandidateEntry entry)
    {
        if (ActivePickerItem is not { } item) return;
        item.MatchPhotoPath = entry.PhotoPath;
        item.MatchPhotoFilename = entry.PhotoFilename;
        item.MatchWorldName = entry.WorldName;
        item.MatchWorldId = entry.WorldId;
        item.MatchDistance = entry.Distance;
        item.MatchThumbPath = null;

        _ = Task.Run(async () =>
        {
            try
            {
                await thumbnailWorker.GenerateGridAsync(
                    [(entry.PhotoPath, entry.SourceSlot)],
                    result =>
                    {
                        _ = dispatcherService.RunOnUiThread(() => item.MatchThumbPath = result.ThumbPath);
                    }).ConfigureAwait(false);
            }
            catch (Exception ex) { AppLogger.Warn($"SelectCandidate thumb: {ex.Message}"); }
        });

        CloseCandidatePicker();
    }

    // 候補ピッカーの選択状態と読み込み状態をクリアして閉じる。
    public void CloseCandidatePicker()
    {
        IsCandidatePickerOpen = false;
        ActivePickerItem = null;
        IsCandidateLoading = false;
    }
}
