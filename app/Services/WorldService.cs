using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging.Pdq;
using Alpheratz.Core.Scanner;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;

namespace Alpheratz.Services;

/// <summary>
/// ワールド情報 (世界名 / world_id) を写真に紐付けるためのドメインサービス。
/// PDQ 距離に基づく類似マッチング、Twitter Intent URL の起動、画像クリップボードコピー、
/// VRChat ワールドリンクの起動などを担当する。
/// </summary>
public sealed class WorldService
{
    /// <summary>
    /// PDQ ハミング距離の最大許容値。
    /// 256 ビットハッシュ中 124 ビット以下の差なら「同じワールドで撮影された可能性が高い」と判定する。
    /// 256 の約半分 (128) を閾値にすると偶然一致でも閾値を下回るため、やや厳しめの 124 を採用。
    /// </summary>
    public const int WorldMatchDistanceThreshold = 124;

    private readonly AlpheratzDb _db;
    private readonly PhotoScanner _scanner;

    /// <summary>ワールド情報を扱う DB とログ由来の解決処理を受け取ってサービスを作成する。</summary>
    public WorldService(AlpheratzDb db, PhotoScanner scanner)
    {
        AppLogger.Trace("WorldService.ctor: enter");
        _db = db;
        _scanner = scanner;
        AppLogger.Trace("WorldService.ctor: exit");
    }

    /// <summary>VRChat のワールド詳細 URL を既定ブラウザで開く。</summary>
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
            // 呼出側がユーザー向け通知を出せるよう、入力不正や起動失敗は返す。
            AppLogger.Error($"WorldService.OpenWorldUrlAsync: threw: {ex}");
            throw;
        }
        AppLogger.Trace("WorldService.OpenWorldUrlAsync: exit");
    }

    /// <summary>Twitter/X の Web Intent URL を既定ブラウザで開く。</summary>
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

    /// <summary>指定写真を Explorer 上で選択表示する。</summary>
    public Task ShowInExplorerAsync(string path, CancellationToken ct = default)
    {
        AppLogger.Trace($"WorldService.ShowInExplorerAsync: enter path={path}");
        try
        {
            var normalizedPath = System.IO.Path.GetFullPath(path.Replace('/', '\\'));
            // 文字列連結で /select,"..." を組むとパス内のダブルクォートやコマンド区切りで
            // 任意のシェルコマンドが実行できてしまう。ProcessStartInfo.ArgumentList を使い、
            // OS 側に引数エスケープを任せる。
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("/select,");
            psi.ArgumentList.Add(normalizedPath);
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldService.ShowInExplorerAsync: threw: {ex}");
            throw;
        }
        AppLogger.Trace("WorldService.ShowInExplorerAsync: exit");
        return Task.CompletedTask;
    }

    /// <summary>指定写真を Windows クリップボードへ画像として設定する。</summary>
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

    /// <summary>source 写真のワールド情報を target 写真へコピーする。</summary>
    public Task ApplyWorldMatchFromPhotoAsync(string targetPhotoPath, string sourcePhotoPath, CancellationToken ct = default)
    {
        AppLogger.Trace("WorldService.ApplyWorldMatchFromPhotoAsync: enter");
        var task = _db.ApplyWorldMatchFromPhotoAsync(targetPhotoPath, sourcePhotoPath, ct);
        AppLogger.Trace("WorldService.ApplyWorldMatchFromPhotoAsync: exit");
        return task;
    }

    /// <summary>Polaris archive の訪問履歴から未知ワールド写真を解決する。</summary>
    public Task<int> ResolveUnknownWorldsFromArchiveAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("WorldService.ResolveUnknownWorldsFromArchiveAsync: enter");
        var task = _scanner.ResolveUnknownWorldsFromArchiveAsync(ct);
        AppLogger.Trace("WorldService.ResolveUnknownWorldsFromArchiveAsync: exit");
        return task;
    }

    /// <summary>既知ワールド写真との PDQ 距離から、未知ワールド写真を自動解決する。</summary>
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

    private static (string WorldName, string? WorldId)? FindBestMatch(string targetPhash, IReadOnlyList<AlpheratzDb.KnownWorldRow> candidates)
    {
        var result = FindBestMatchWithDetails(targetPhash, candidates);
        if (result is null) return null;
        return (result.Value.Row.WorldName, result.Value.Row.WorldId);
    }

    internal static (AlpheratzDb.KnownWorldRow Row, int Distance)? FindBestMatchWithDetails(
        string targetPhash, IReadOnlyList<AlpheratzDb.KnownWorldRow> candidates)
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
        return (best, bestDistance);
    }

    internal static List<(AlpheratzDb.KnownWorldRow Row, int Distance)> RankCandidatesByDistance(
        string targetPhash, IReadOnlyList<AlpheratzDb.KnownWorldRow> candidates)
    {
        var targetVariants = PdqHasher.ParseHashVariants(targetPhash);
        if (targetVariants.Count == 0) return [];

        var ranked = new List<(AlpheratzDb.KnownWorldRow Row, int Distance)>();
        foreach (var candidate in candidates)
        {
            var candVariants = PdqHasher.ParseHashVariants(candidate.Phash);
            var d = PdqHasher.ClosestHashDistance(targetVariants, candVariants);
            if (d is null) continue;
            ranked.Add((candidate, d.Value));
        }
        ranked.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return ranked;
    }
}
