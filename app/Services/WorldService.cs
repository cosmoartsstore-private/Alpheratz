using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging.Pdq;
using Alpheratz.Core.Scanner;
using Alpheratz.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;

namespace Alpheratz.Services;

public sealed class WorldService
{
    // Per legacy alpheratz: matches with a hamming distance > 124 are rejected.
    public const int WorldMatchDistanceThreshold = 124;

    private readonly AlpheratzDb _db;
    private readonly PhotoScanner _scanner;

    public WorldService(AlpheratzDb db, PhotoScanner scanner)
    {
        AppLogger.Trace("WorldService.ctor: enter");
        _db = db;
        _scanner = scanner;
        AppLogger.Trace("WorldService.ctor: exit");
    }

    public async Task OpenWorldUrlAsync(string worldId, CancellationToken ct = default)
    {
        AppLogger.Trace($"WorldService.OpenWorldUrlAsync: enter worldId={worldId}");
        try
        {
            if (!worldId.StartsWith("wrld_", StringComparison.Ordinal))
                throw new ArgumentException($"VRChat ワールドIDの形式が不正です: {worldId}");
            var url = new Uri($"https://vrchat.com/home/world/{worldId}/info");
            await Launcher.LaunchUriAsync(url).AsTask(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Rethrow: caller (PhotoModalViewModel) catches and toasts.
            AppLogger.Error($"WorldService.OpenWorldUrlAsync: threw: {ex}");
            throw;
        }
        AppLogger.Trace("WorldService.OpenWorldUrlAsync: exit");
    }

    public async Task OpenTweetIntentAsync(string intentUrl, CancellationToken ct = default)
    {
        AppLogger.Trace("WorldService.OpenTweetIntentAsync: enter");
        try
        {
            if (!intentUrl.StartsWith("https://twitter.com/intent/tweet?text=", StringComparison.Ordinal)
                && !intentUrl.StartsWith("https://x.com/intent/tweet?text=", StringComparison.Ordinal))
                throw new ArgumentException($"Tweet intent URL の形式が不正です: {intentUrl}");
            await Launcher.LaunchUriAsync(new Uri(intentUrl)).AsTask(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldService.OpenTweetIntentAsync: threw: {ex}");
            throw;
        }
        AppLogger.Trace("WorldService.OpenTweetIntentAsync: exit");
    }

    public Task ShowInExplorerAsync(string path, CancellationToken ct = default)
    {
        AppLogger.Trace($"WorldService.ShowInExplorerAsync: enter path={path}");
        try
        {
            var normalizedPath = System.IO.Path.GetFullPath(path.Replace('/', '\\'));
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{normalizedPath}\"");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldService.ShowInExplorerAsync: threw: {ex}");
            throw;
        }
        AppLogger.Trace("WorldService.ShowInExplorerAsync: exit");
        return Task.CompletedTask;
    }

    public async Task CopyImageToClipboardAsync(string photoPath, CancellationToken ct = default)
    {
        AppLogger.Trace($"WorldService.CopyImageToClipboardAsync: enter path={photoPath}");
        try
        {
            var normalizedPath = System.IO.Path.GetFullPath(photoPath.Replace('/', '\\'));
            var file = await StorageFile.GetFileFromPathAsync(normalizedPath).AsTask(ct).ConfigureAwait(false);
            var dataPackage = new DataPackage();
            dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            dataPackage.SetStorageItems(new[] { file });
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldService.CopyImageToClipboardAsync: threw: {ex}");
            throw;
        }
        AppLogger.Trace("WorldService.CopyImageToClipboardAsync: exit");
    }

    public Task ApplyWorldMatchFromPhotoAsync(string targetPhotoPath, string sourcePhotoPath, CancellationToken ct = default)
    {
        AppLogger.Trace("WorldService.ApplyWorldMatchFromPhotoAsync: enter");
        var task = _db.ApplyWorldMatchFromPhotoAsync(targetPhotoPath, sourcePhotoPath, ct);
        AppLogger.Trace("WorldService.ApplyWorldMatchFromPhotoAsync: exit");
        return task;
    }

    public Task<int> ResolveUnknownWorldsFromArchiveAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("WorldService.ResolveUnknownWorldsFromArchiveAsync: enter");
        var task = _scanner.ResolveUnknownWorldsFromArchiveAsync(ct);
        AppLogger.Trace("WorldService.ResolveUnknownWorldsFromArchiveAsync: exit");
        return task;
    }

    public async Task<int> ResolveUnknownWorldsFromSimilarPhotosAsync(string target, CancellationToken ct = default)
    {
        AppLogger.Trace($"WorldService.ResolveUnknownWorldsFromSimilarPhotosAsync: enter target={target}");
        var unknowns = await _db.GetUnknownWorldPhotosWithPhashAsync(ct).ConfigureAwait(false);
        if (unknowns.Count == 0)
        {
            AppLogger.Trace("WorldService.ResolveUnknownWorldsFromSimilarPhotosAsync: no unknowns");
            return 0;
        }

        // Group known photos by source_slot (each slot keeps its own DB ordering).
        var resolved = 0;
        var knownBySlot = new Dictionary<long, IReadOnlyList<AlpheratzDb.KnownWorldRow>>();

        foreach (var unknown in unknowns)
        {
            ct.ThrowIfCancellationRequested();
            if (!knownBySlot.TryGetValue(unknown.SourceSlot, out var knownPhotos))
            {
                knownPhotos = await _db.GetKnownWorldPhotosAsync(unknown.SourceSlot, null, ct).ConfigureAwait(false);
                knownBySlot[unknown.SourceSlot] = knownPhotos;
            }
            if (knownPhotos.Count == 0) continue;

            var match = FindBestMatch(unknown.Phash, knownPhotos);
            if (match is null) continue;

            await _db.UpdatePhotoWorldAsync(unknown.PhotoPath, match.Value.WorldName, match.Value.WorldId, "phash", ct).ConfigureAwait(false);
            resolved++;
        }

        AppLogger.Trace($"WorldService.ResolveUnknownWorldsFromSimilarPhotosAsync: exit resolved={resolved}");
        return resolved;
    }

    public async Task<IReadOnlyList<SimilarWorldCandidateDto>> FindSimilarWorldCandidatesAsync(string photoPath, int limit, CancellationToken ct = default)
    {
        AppLogger.Trace($"WorldService.FindSimilarWorldCandidatesAsync: enter path={photoPath} limit={limit}");
        var src = await _db.GetPhotoPhashRowAsync(photoPath, ct).ConfigureAwait(false);
        if (src is null) return [];

        var srcVariants = PdqHasher.ParseHashVariants(src.Phash);
        if (srcVariants.Count == 0) return [];

        var known = await _db.GetKnownWorldPhotosAsync(src.SourceSlot, photoPath, ct).ConfigureAwait(false);

        var scored = new List<(AlpheratzDb.KnownWorldRow Row, int Distance)>();
        foreach (var k in known)
        {
            var candVariants = PdqHasher.ParseHashVariants(k.Phash);
            var d = PdqHasher.ClosestHashDistance(srcVariants, candVariants);
            if (d is null || d.Value > WorldMatchDistanceThreshold) continue;
            scored.Add((k, d.Value));
        }
        scored.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var top = scored.Take(limit).ToList();
        var dtos = new List<SimilarWorldCandidateDto>(top.Count);
        foreach (var (row, distance) in top)
        {
            var record = await _db.GetPhotoRecordAsync(row.PhotoPath, includePhash: false, ct).ConfigureAwait(false);
            if (record is null) continue;
            dtos.Add(new SimilarWorldCandidateDto
            {
                photo = record,
                distance = distance,
                similarity = 1.0 - (double)distance / 256.0,
            });
        }
        AppLogger.Trace($"WorldService.FindSimilarWorldCandidatesAsync: exit count={dtos.Count}");
        return dtos;
    }

    private static (string WorldName, string? WorldId)? FindBestMatch(string targetPhash, IReadOnlyList<AlpheratzDb.KnownWorldRow> candidates)
    {
        var targetVariants = PdqHasher.ParseHashVariants(targetPhash);
        if (targetVariants.Count == 0) return null;

        var bestDistance = int.MaxValue;
        AlpheratzDb.KnownWorldRow? best = null;
        foreach (var candidate in candidates)
        {
            var candVariants = PdqHasher.ParseHashVariants(candidate.Phash);
            var d = PdqHasher.ClosestHashDistance(targetVariants, candVariants);
            if (d is null || d.Value > WorldMatchDistanceThreshold) continue;
            if (d.Value >= bestDistance) continue;
            bestDistance = d.Value;
            best = candidate;
            if (bestDistance == 0) break;
        }
        if (best is null) return null;
        return (best.WorldName, best.WorldId);
    }
}
