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

public sealed record CandidateEntry(
    string PhotoPath, string PhotoFilename, string WorldName, string? WorldId,
    int Distance, long SourceSlot);

public partial class WorldResolveViewModel : UiThreadSafeObservableObject
{
    private readonly AlpheratzDb db;
    private readonly ThumbnailWorker thumbnailWorker;
    private readonly ToastService toastService;

    public UiObservableCollection<WorldResolveItem> Items { get; } = [];

    private bool isLoading;
    public bool IsLoading { get => isLoading; set => SetProperty(ref isLoading, value); }

    private bool isApplying;
    public bool IsApplying { get => isApplying; set => SetProperty(ref isApplying, value); }

    private int applyCount;
    public int ApplyCount { get => applyCount; set => SetProperty(ref applyCount, value); }

    // --- Candidate picker ---
    private bool isCandidatePickerOpen;
    public bool IsCandidatePickerOpen { get => isCandidatePickerOpen; set => SetProperty(ref isCandidatePickerOpen, value); }

    private WorldResolveItem? activePickerItem;
    public WorldResolveItem? ActivePickerItem { get => activePickerItem; set => SetProperty(ref activePickerItem, value); }

    public UiObservableCollection<CandidateEntry> CandidateList { get; } = [];

    private bool isCandidateLoading;
    public bool IsCandidateLoading { get => isCandidateLoading; set => SetProperty(ref isCandidateLoading, value); }

    private Dictionary<long, IReadOnlyList<AlpheratzDb.KnownWorldRow>> knownBySlot = new();

    public WorldResolveViewModel(AlpheratzDb db, ThumbnailWorker thumbnailWorker, ToastService toastService)
    {
        this.db = db;
        this.thumbnailWorker = thumbnailWorker;
        this.toastService = toastService;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            var unknowns = await db.GetUnknownWorldPhotosWithPhashAsync(ct).ConfigureAwait(false);
            if (unknowns.Count == 0)
            {
                IsLoading = false;
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

            Items.ReplaceAll(items);
            IsLoading = false;

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
                        foreach (var item in items)
                        {
                            if (item.TargetPhotoPath == result.PhotoPath)
                                item.TargetThumbPath = result.ThumbPath;
                            if (item.MatchPhotoPath == result.PhotoPath)
                                item.MatchThumbPath = result.ThumbPath;
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
            IsLoading = false;
        }
    }

    public void ToggleApply(WorldResolveItem item)
    {
        item.IsApplied = !item.IsApplied;
        RecountApply();
    }

    public void ApplyAll()
    {
        foreach (var item in Items)
            if (item.HasMatch) item.IsApplied = true;
        RecountApply();
    }

    public void SkipAll()
    {
        foreach (var item in Items) item.IsApplied = false;
        RecountApply();
    }

    private void RecountApply()
    {
        ApplyCount = Items.Count(x => x.IsApplied);
    }

    public async Task<int> ApplyConfirmedAsync(CancellationToken ct = default)
    {
        IsApplying = true;
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
            IsApplying = false;
        }
        return applied;
    }

    public async Task OpenCandidatePickerAsync(WorldResolveItem item, CancellationToken ct = default)
    {
        ActivePickerItem = item;
        IsCandidateLoading = true;
        IsCandidatePickerOpen = true;

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

            CandidateList.ReplaceAll(entries);
            IsCandidateLoading = false;

            var thumbTargets = entries.Select(e => (e.PhotoPath, e.SourceSlot)).ToList();
            _ = Task.Run(async () =>
            {
                try
                {
                    await thumbnailWorker.GenerateGridAsync(thumbTargets, result =>
                    {
                        // CandidateEntry is immutable record — thumb is handled via converter in XAML
                    }, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { AppLogger.Warn($"Candidate thumb generation: {ex.Message}"); }
            });
        }
        catch (OperationCanceledException) { CloseCandidatePicker(); }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldResolveViewModel.OpenCandidatePickerAsync: {ex}");
            CloseCandidatePicker();
        }
    }

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
                    result => { item.MatchThumbPath = result.ThumbPath; }).ConfigureAwait(false);
            }
            catch (Exception ex) { AppLogger.Warn($"SelectCandidate thumb: {ex.Message}"); }
        });

        CloseCandidatePicker();
    }

    public void CloseCandidatePicker()
    {
        IsCandidatePickerOpen = false;
        ActivePickerItem = null;
        IsCandidateLoading = false;
    }
}
