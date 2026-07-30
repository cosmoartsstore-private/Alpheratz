using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Services;

/// <summary>
/// ワールド情報 (世界名 / world_id) を写真に紐付けるためのドメインサービス。
/// PDQ 距離に基づく類似マッチング、Twitter Intent URL の起動、画像クリップボードコピー、
/// VRChat ワールドリンクの起動などを担当する。
/// </summary>
public sealed class WorldService
{
    private const string TwitterIntentHost = "twitter.com";
    private const string XIntentHost = "x.com";
    private const string TweetIntentPath = "/intent/tweet";
    private const string TweetIntentQueryPrefix = "?text=";
    private const string ExplorerExecutableName = "explorer.exe";
    private const string ExplorerSelectArgument = "/select,";

    /// <summary>
    /// PDQ ハミング距離の最大許容値。
    /// 256 ビットハッシュ中 124 ビット以下の差なら「同じワールドで撮影された可能性が高い」と判定する。
    /// 256 の約半分 (128) を閾値にすると偶然一致でも閾値を下回るため、やや厳しめの 124 を採用。
    /// </summary>
    public const int WorldMatchDistanceThreshold = 124;

    private readonly AlpheratzDb _db;
    private readonly PhotoScanner _scanner;

    /// <summary>
    /// PDQ 文字列を候補取得時に一度だけパースした既知ワールド候補。
    /// 大量の未知写真と総当たりするとき、候補側のパースを毎回繰り返さないために使う。
    /// </summary>
    internal sealed record PreparedKnownWorldRow(
        AlpheratzDb.KnownWorldRow Row,
        IReadOnlyList<string> HashVariants);

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
            var launched = await Launcher.LaunchUriAsync(BuildWorldUri(worldId)).AsTask(ct).ConfigureAwait(false);
            if (!launched)
                throw new InvalidOperationException(getMsg("WorldService.browserLaunchFailed"));
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
    public async Task<bool> OpenTweetIntentAsync(string intentUrl, CancellationToken ct = default)
    {
        AppLogger.Trace("WorldService.OpenTweetIntentAsync: enter");
        try
        {
            var launched = await Launcher.LaunchUriAsync(BuildTweetIntentUri(intentUrl)).AsTask(ct).ConfigureAwait(false);
            if (!launched)
                AppLogger.Warn("WorldService.OpenTweetIntentAsync: 既定ブラウザを起動できませんでした");
            return launched;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldService.OpenTweetIntentAsync: threw: {ex}");
            throw;
        }
        finally
        {
            AppLogger.Trace("WorldService.OpenTweetIntentAsync: exit");
        }
    }

    /// <summary>指定写真を Explorer 上で選択表示する。</summary>
    public Task ShowInExplorerAsync(string path, CancellationToken ct = default)
    {
        AppLogger.Trace($"WorldService.ShowInExplorerAsync: enter path={path}");
        try
        {
            ct.ThrowIfCancellationRequested();
            using var process = Process.Start(BuildExplorerSelectionStartInfo(path));
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

    /// <summary>VRChat ワールド詳細を開くための URL を組み立て、ID 形式の不正を起動前に拒否する。</summary>
    internal static Uri BuildWorldUri(string worldId)
    {
        if (!IsValidWorldId(worldId))
            throw new ArgumentException("VRChat ワールドIDの形式が不正です。");

        return new Uri($"https://vrchat.com/home/world/{worldId}/info");
    }

    /// <summary>Twitter/X の Web Intent URL として許可するホスト・パス・クエリだけを Uri に変換する。</summary>
    internal static Uri BuildTweetIntentUri(string intentUrl)
    {
        if (string.IsNullOrWhiteSpace(intentUrl)
            || intentUrl != intentUrl.Trim()
            || !Uri.TryCreate(intentUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length > 0
            || !uri.IsDefaultPort
            || (uri.Host != TwitterIntentHost && uri.Host != XIntentHost)
            || uri.AbsolutePath != TweetIntentPath
            || uri.Fragment.Length > 0
            || !IsTextOnlyTweetIntentQuery(uri.Query))
        {
            throw new ArgumentException("Tweet intent URL の形式が不正です。");
        }

        return uri;
    }

    /// <summary>Explorer で対象ファイルを選択表示する ProcessStartInfo を構築する。</summary>
    internal static ProcessStartInfo BuildExplorerSelectionStartInfo(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Explorer で表示する写真パスが空です。", nameof(path));

        // /select と対象パスを別引数にすることで、引用符や区切り文字を含むパスをコマンド文字列へ混ぜない。
        var psi = new ProcessStartInfo
        {
            FileName = ResolveExplorerExecutablePath(),
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(ExplorerSelectArgument);
        psi.ArgumentList.Add(System.IO.Path.GetFullPath(path.Replace('/', '\\')));
        return psi;
    }

    /// <summary>VRChat の world_id として URL 構成文字だけを含む `wrld_` ID かを確認する。</summary>
    private static bool IsValidWorldId(string? worldId)
    {
        const string Prefix = "wrld_";
        if (string.IsNullOrWhiteSpace(worldId) || !worldId.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        if (worldId.Length == Prefix.Length)
            return false;

        for (var i = Prefix.Length; i < worldId.Length; i++)
        {
            var ch = worldId[i];
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_')
                return false;
        }
        return true;
    }

    /// <summary>Tweet Intent の query が `text` だけを持ち、追加パラメータを含まないことを確認する。</summary>
    private static bool IsTextOnlyTweetIntentQuery(string query)
        => query.StartsWith(TweetIntentQueryPrefix, StringComparison.Ordinal)
            && !query.Contains('&', StringComparison.Ordinal)
            && !query.Contains(';', StringComparison.Ordinal);

    /// <summary>PATH 探索に依存せず Windows 配下の Explorer 実行ファイルを返す。</summary>
    private static string ResolveExplorerExecutablePath()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
            windowsDirectory = Environment.GetEnvironmentVariable("WINDIR") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(windowsDirectory))
            throw new InvalidOperationException("Windows ディレクトリを特定できないため Explorer を起動できません。");

        return System.IO.Path.Combine(windowsDirectory, ExplorerExecutableName);
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
        var unknowns = await _db.GetUnknownWorldPhotosWithPhashAsync(target, ct).ConfigureAwait(false);
        if (unknowns.Count == 0)
        {
            AppLogger.Trace("WorldService.ResolveUnknownWorldsFromSimilarPhotosAsync: no unknowns");
            return 0;
        }

        // Group known photos by source_slot (each slot keeps its own DB ordering).
        var resolved = 0;
        var knownBySlot = new Dictionary<long, IReadOnlyList<PreparedKnownWorldRow>>();

        foreach (var unknown in unknowns)
        {
            ct.ThrowIfCancellationRequested();
            if (!knownBySlot.TryGetValue(unknown.SourceSlot, out var knownPhotos))
            {
                var rows = await _db.GetKnownWorldPhotosAsync(unknown.SourceSlot, null, ct).ConfigureAwait(false);
                knownPhotos = PrepareKnownWorldRows(rows, ct);
                knownBySlot[unknown.SourceSlot] = knownPhotos;
            }
            if (knownPhotos.Count == 0) continue;

            var match = FindBestMatch(unknown.Phash, knownPhotos, ct);
            if (match is null) continue;

            await _db.UpdatePhotoWorldAsync(unknown.PhotoPath, match.Value.WorldName, match.Value.WorldId, "phash", ct).ConfigureAwait(false);
            resolved++;
        }

        AppLogger.Trace($"WorldService.ResolveUnknownWorldsFromSimilarPhotosAsync: exit resolved={resolved}");
        return resolved;
    }

    private static (string WorldName, string? WorldId)? FindBestMatch(
        string targetPhash,
        IReadOnlyList<PreparedKnownWorldRow> candidates,
        CancellationToken ct)
    {
        var result = FindBestMatchWithDetails(targetPhash, candidates, ct);
        if (result is null) return null;
        return (result.Value.Row.WorldName, result.Value.Row.WorldId);
    }

    internal static (AlpheratzDb.KnownWorldRow Row, int Distance)? FindBestMatchWithDetails(
        string targetPhash,
        IReadOnlyList<AlpheratzDb.KnownWorldRow> candidates,
        CancellationToken ct = default)
        => FindBestMatchWithDetails(targetPhash, PrepareKnownWorldRows(candidates, ct), ct);

    internal static (AlpheratzDb.KnownWorldRow Row, int Distance)? FindBestMatchWithDetails(
        string targetPhash,
        IReadOnlyList<PreparedKnownWorldRow> candidates,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var targetVariants = PdqHasher.ParseHashVariants(targetPhash);
        if (targetVariants.Count == 0) return null;

        var bestDistance = int.MaxValue;
        AlpheratzDb.KnownWorldRow? best = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            if ((index & 0x7F) == 0)
                ct.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            var d = PdqHasher.ClosestHashDistance(targetVariants, candidate.HashVariants);
            if (d is null || d.Value > WorldMatchDistanceThreshold) continue;
            if (d.Value >= bestDistance) continue;
            bestDistance = d.Value;
            best = candidate.Row;
            if (bestDistance == 0) break;
        }
        if (best is null) return null;
        return (best, bestDistance);
    }

    internal static List<(AlpheratzDb.KnownWorldRow Row, int Distance)> RankCandidatesByDistance(
        string targetPhash,
        IReadOnlyList<AlpheratzDb.KnownWorldRow> candidates,
        CancellationToken ct = default)
        => RankCandidatesByDistance(targetPhash, PrepareKnownWorldRows(candidates, ct), ct);

    internal static List<(AlpheratzDb.KnownWorldRow Row, int Distance)> RankCandidatesByDistance(
        string targetPhash,
        IReadOnlyList<PreparedKnownWorldRow> candidates,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var targetVariants = PdqHasher.ParseHashVariants(targetPhash);
        if (targetVariants.Count == 0) return [];

        var ranked = new List<(AlpheratzDb.KnownWorldRow Row, int Distance)>();
        for (var index = 0; index < candidates.Count; index++)
        {
            if ((index & 0x7F) == 0)
                ct.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            var d = PdqHasher.ClosestHashDistance(targetVariants, candidate.HashVariants);
            if (d is null) continue;
            ranked.Add((candidate.Row, d.Value));
        }
        ranked.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return ranked;
    }

    internal static IReadOnlyList<PreparedKnownWorldRow> PrepareKnownWorldRows(
        IReadOnlyList<AlpheratzDb.KnownWorldRow> candidates,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var prepared = new List<PreparedKnownWorldRow>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            if ((index & 0x7F) == 0)
                ct.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            var variants = PdqHasher.ParseHashVariants(candidate.Phash);
            if (variants.Count > 0)
                prepared.Add(new PreparedKnownWorldRow(candidate, variants));
        }
        return prepared;
    }
}
