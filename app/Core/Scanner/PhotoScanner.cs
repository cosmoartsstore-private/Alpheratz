using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core.Database;
using Alpheratz.Models;
using Alpheratz.Models.Events;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Core.Scanner;

public sealed partial class PhotoScanner
{
    private const int MaxItxtSize = 4 * 1024 * 1024;
    private const int ScanDbBatchSize = 500;
    private const int ScanProgressUpdateIntervalMilliseconds = 100;
    // 画像ヘッダーと PNG メタデータの読込みを並列化する。UI と他処理へ余力を残すため最大 4 並列に抑える。
    private static int ScanAnalysisParallelism
        => Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
    // VRChat の output_log 1 行の上限。ここを超える行は破損または異常データとみなして
    // Regex を走らせずに打ち切る。デフォルト 64 KiB。
    private const int MaxLogLineLength = 64 * 1024;
    // 任意追加の Windows codec に依存せず、対応 OS だけで全処理を完結できる形式に限定する。
    private static readonly string[] SupportedExtensions = ["png", "jpg", "jpeg"];
    private static readonly string[] SkipDirs = ["node_modules", "vendor", "cache", "$recycle.bin", "system volume information", "thumbnails"];

    [GeneratedRegex(@"VRChat_(\d{4}-\d{2}-\d{2})_(\d{2}-\d{2}-\d{2})\.(\d{3})")]
    private static partial Regex ReFilename();
    [GeneratedRegex(@"<vrc:WorldID>([^<]+)</vrc:WorldID>")]
    private static partial Regex ReWorldId();
    [GeneratedRegex(@"<vrc:WorldDisplayName>([^<]+)</vrc:WorldDisplayName>")]
    private static partial Regex ReWorldName();

    private readonly AppConfig _config;
    private readonly AlpheratzDb _db;
    private readonly LocalEventBus _bus;
    private volatile CancellationTokenSource? _cancelSource;

    public PhotoScanner(AppConfig config, AlpheratzDb db, LocalEventBus bus)
    {
        AppLogger.Trace("PhotoScanner.ctor: enter");
        _config = config;
        _db = db;
        _bus = bus;
        AppLogger.Trace("PhotoScanner.ctor: exit");
    }

    /// <summary>現在実行中のスキャンへキャンセルを要求する。</summary>
    public void RequestCancel()
    {
        AppLogger.Trace("PhotoScanner.RequestCancel: enter");
        try { _cancelSource?.Cancel(); }
        catch (Exception ex) { AppLogger.Warn($"PhotoScanner.RequestCancel: threw: {ex}"); }
        AppLogger.Trace("PhotoScanner.RequestCancel: exit");
    }

    /// <summary>設定された写真フォルダをスキャンし、DB の写真メタデータを更新する。</summary>
    public async Task ScanAsync(CancellationToken externalCt = default)
    {
        AppLogger.Trace("PhotoScanner.ScanAsync: enter");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        _cancelSource = cts;
        var ct = cts.Token;

        try
        {
            await DoScanAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AppLogger.Trace("PhotoScanner.ScanAsync: cancelled");
            await _bus.PublishAsync(EventNames.ScanCancelled, getMsg("PhotoScanner.cancelled")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"スキャン中に予期しないエラーが発生しました: {ex}");
            await _bus.PublishAsync(EventNames.ScanError, getMsg("PhotoScanner.failed")).ConfigureAwait(false);
        }
        finally
        {
            _cancelSource = null;
            AppLogger.Trace("PhotoScanner.ScanAsync: exit");
        }
    }

    /// <summary>写真ファイルの列挙、差分判定、DB 更新、進捗通知を実行する本体処理。</summary>
    private async Task DoScanAsync(CancellationToken ct)
    {
        AppLogger.Trace("PhotoScanner.DoScanAsync: enter");
        // Resolve photo dirs
        var setting = _config.LoadSetting();
        var photoDirs = new List<(long slot, string path)>();

        if (!string.IsNullOrWhiteSpace(setting.PhotoFolderPath) && Directory.Exists(setting.PhotoFolderPath))
            photoDirs.Add((1, setting.PhotoFolderPath));
        else if (!string.IsNullOrWhiteSpace(setting.PhotoFolderPath))
        {
            await _bus.PublishAsync(EventNames.ScanError, getMsg("PhotoScanner.primaryFolderMissing")).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(setting.SecondaryPhotoFolderPath) && Directory.Exists(setting.SecondaryPhotoFolderPath))
        {
            photoDirs.Add((2, setting.SecondaryPhotoFolderPath));
        }
        else if (!string.IsNullOrWhiteSpace(setting.SecondaryPhotoFolderPath))
        {
            await _bus.PublishAsync(EventNames.ScanError, getMsg("PhotoScanner.secondaryFolderMissing")).ConfigureAwait(false);
            return;
        }

        if (photoDirs.Count == 0)
        {
            await _bus.PublishAsync(EventNames.ScanError, getMsg("PhotoScanner.folderUnconfigured")).ConfigureAwait(false);
            return;
        }
        if (photoDirs.Count == 2
            && AppPaths.AreOverlappingDirectories(photoDirs[0].path, photoDirs[1].path))
        {
            await _bus.PublishAsync(
                EventNames.ScanError,
                getMsg("PhotoScanner.foldersOverlap")).ConfigureAwait(false);
            return;
        }

        await _bus.PublishAsync(EventNames.ScanProgress, new ScanProgressDto { processed = 0, total = 0, current_world = getMsg("PhotoScanner.collecting"), phase = "scan" }).ConfigureAwait(false);

        // 既存 DB 情報を読み、再スキャン時に保持できるメタデータを判断する。
        var existing = await _db.GetExistingPhotosAsync(ct).ConfigureAwait(false);

        // シンボリックリンクなどで同じディレクトリへ戻る経路があるため、
        // 訪問済みの正規化フルパスを持って無限再帰を防ぐ。
        var foundFiles = new List<(long slot, string filename, string path)>();
        var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var completedSlots = new HashSet<long>();
        var incompleteSlots = new HashSet<long>();
        foreach (var (slot, dir) in photoDirs)
        {
            ct.ThrowIfCancellationRequested();
            if (CollectPhotosRecursive(slot, dir, foundFiles, visitedDirs, ct))
                completedSlots.Add(slot);
            else
                incompleteSlots.Add(slot);
        }

        ct.ThrowIfCancellationRequested();

        var foundPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (slot, _, path) in foundFiles)
        {
            if (completedSlots.Contains(slot))
                foundPathSet.Add(AppPaths.NormalizePathForDb(path));
        }

        // root 全体を例外なく走査できたスロットだけ、ディスク上に存在しなくなった写真を消す。
        // 一部のディレクトリを読めなかったスロットは、読み取れなかった写真を欠落と誤認しないよう
        // 今回の削除対象から外し、次回の完全な走査まで削除を延期する。
        if (completedSlots.Count > 0)
            await _db.DeleteMissingPhotosAsync(foundPathSet, completedSlots.ToArray(), ct).ConfigureAwait(false);

        // 既存値と比較し、DB 更新が必要な写真だけを候補にする。
        var candidates = new List<(long slot, string filename, string path, ScanRefreshKind kind)>();
        foreach (var (slot, filename, path) in foundFiles)
        {
            var normalizedPath = AppPaths.NormalizePathForDb(path);
            if (!existing.TryGetValue(normalizedPath, out var ex))
            {
                candidates.Add((slot, filename, path, ScanRefreshKind.Full));
            }
            else
            {
                var reappeared = ex.IsMissing;
                var sourceChanged = ex.SourceSlot != slot;
                var initialWorldAnalysisNotRecorded = ex.WorldName is null
                    && ex.WorldId is null
                    && ex.MatchSource is null;
                var filenameChanged = ex.PhotoFilename != filename;
                var fileModified = IsFileModifiedSinceStoredMtime(path, ex);

                // 新規・再出現・内容変更では必ず初回解析を行う。一度解析して情報が無かった写真は
                // match_source = unresolved として保持し、通常スキャンでは再解析しない。
                // match_source 自体が null の PNG は初回解析の完了記録が無いため、その未完了分だけ補う。
                // 将来の StellaRecordDB 再取得も、設定画面の明示操作から MetadataOnly を使う。
                if (reappeared || sourceChanged || fileModified)
                    candidates.Add((slot, filename, path, ScanRefreshKind.Full));
                else if (filenameChanged)
                    candidates.Add((slot, filename, path, ScanRefreshKind.PathOnly));
                else if (initialWorldAnalysisNotRecorded
                    && filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add((slot, filename, path, ScanRefreshKind.MetadataOnly));
                }
            }
        }

        var total = candidates.Count;
        await _bus.PublishAsync(EventNames.ScanProgress, new ScanProgressDto { processed = 0, total = total, current_world = getMsg("PhotoScanner.updatesFound", ("count", total)), phase = "scan" }).ConfigureAwait(false);

        var processed = 0;
        var currentWorld = getMsg("common.unknownWorld");
        using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var progressTask = PublishScanProgressAsync(
            total,
            () => Volatile.Read(ref processed),
            () => Volatile.Read(ref currentWorld),
            progressCts.Token);

        try
        {
            for (var batchStart = 0; batchStart < candidates.Count; batchStart += ScanDbBatchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batchLength = Math.Min(ScanDbBatchSize, candidates.Count - batchStart);
                var batchUpserts = new PhotoUpsertData[batchLength];

                await Task.Run(() =>
                {
                    Parallel.For(0, batchLength, new ParallelOptions
                    {
                        CancellationToken = ct,
                        MaxDegreeOfParallelism = ScanAnalysisParallelism,
                    }, offset =>
                    {
                        var (slot, filename, path, kind) = candidates[batchStart + offset];
                        var normalizedPath = AppPaths.NormalizePathForDb(path);
                        existing.TryGetValue(normalizedPath, out var existingPhoto);

                        var photo = AnalyzePhoto(path, filename, slot, existingPhoto, kind);
                        batchUpserts[offset] = photo;
                        Volatile.Write(ref currentWorld, photo.WorldName ?? getMsg("common.unknownWorld"));
                        Interlocked.Increment(ref processed);
                    });
                }, ct).ConfigureAwait(false);

                // SQLite 書込みは順序を保ったまま 1 バッチずつ確定する。
                await _db.UpsertPhotosAsync(batchUpserts, ct).ConfigureAwait(false);
            }

            if (total > 0)
            {
                await _bus.PublishAsync(EventNames.ScanProgress, new ScanProgressDto
                {
                    processed = total,
                    total = total,
                    current_world = Volatile.Read(ref currentWorld),
                    phase = "scan"
                }).ConfigureAwait(false);
            }
        }
        finally
        {
            progressCts.Cancel();
            try { await progressTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        if (incompleteSlots.Count > 0)
        {
            var slotLabels = string.Join("、", incompleteSlots.OrderBy(slot => slot).Select(FormatSourceSlot));
            var message = getMsg("PhotoScanner.incompleteFolders", ("folders", slotLabels));
            AppLogger.Warn($"PhotoScanner.DoScanAsync: {message}");
            await _bus.PublishAsync(EventNames.ScanWarning, message).ConfigureAwait(false);
        }

        await _bus.PublishAsync(EventNames.ScanCompleted, null).ConfigureAwait(false);
    }

    private static string FormatSourceSlot(long slot)
        => getMsg("PhotoScanner.sourceSlot", ("slot", slot));

    /// <summary>
    /// 解析件数の変化を一定時間ごとに通知する。件数刻みではなく時間刻みにすることで、
    /// 高速な走査でも UI 更新を増やしすぎず、バーが大きく飛ぶ見え方を避ける。
    /// </summary>
    private async Task PublishScanProgressAsync(
        int total,
        Func<int> getProcessed,
        Func<string> getCurrentWorld,
        CancellationToken ct)
    {
        if (total <= 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ScanProgressUpdateIntervalMilliseconds));
        var lastPublished = 0;
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            var current = Math.Clamp(getProcessed(), 0, total);
            if (current <= lastPublished)
                continue;

            await _bus.PublishAsync(EventNames.ScanProgress, new ScanProgressDto
            {
                processed = current,
                total = total,
                current_world = getCurrentWorld(),
                phase = "scan"
            }).ConfigureAwait(false);
            lastPublished = current;
        }
    }

    /// <summary>1枚の写真から DB upsert 用のメタデータを組み立てる。</summary>
    internal static PhotoUpsertData AnalyzePhoto(string path, string filename, long slot, ExistingPhotoInfo? existing, ScanRefreshKind kind)
    {
        var timestamp = ResolveTimestamp(path, filename);
        var discoveredPath = AppPaths.NormalizePathForDb(path);
        // Windows では大小文字だけが異なるパスも同じファイルを指す。既存行をその比較で
        // 見つけた場合は DB の主キー表記を維持し、BINARY 主キーへ重複行を作らない。
        var normalizedPath = existing is not null
            && string.Equals(existing.PhotoPath, discoveredPath, StringComparison.OrdinalIgnoreCase)
            ? existing.PhotoPath
            : discoveredPath;
        var contentChanged = kind == ScanRefreshKind.Full
            && existing is not null
            && IsFileModifiedSinceStoredMtime(path, existing);
        var resetPhash = contentChanged;

        string? worldId, worldName, matchSource;
        if (kind == ScanRefreshKind.PathOnly)
        {
            worldId = existing?.WorldId;
            worldName = existing?.WorldName;
            matchSource = existing?.MatchSource;
        }
        else
        {
            // 同じパスの内容が変わった場合、既存のワールド情報は旧画像に属する可能性がある。
            // 新しい画像からメタデータを取得できた場合だけ採用し、それ以外は未解決へ戻す。
            var existingWorld = contentChanged ? null : existing;
            (worldName, worldId, matchSource) = ResolveWorldInfo(normalizedPath, filename, path, existingWorld);
        }

        string? orientation;
        long? imageWidth, imageHeight;
        if (kind == ScanRefreshKind.PathOnly)
        {
            orientation = existing?.Orientation;
            imageWidth = existing?.ImageWidth;
            imageHeight = existing?.ImageHeight;
        }
        else if (kind == ScanRefreshKind.MetadataOnly
            && existing is { ImageWidth: { } existingWidth, ImageHeight: { } existingHeight }
            && existing.Orientation is { Length: > 0 } existingOrient
            && existingOrient != "unknown")
        {
            // MetadataOnly は初回解析の未完了分と、将来の設定画面から利用者が明示的に
            // 再取得するときの経路である。寸法や orientation は既存値を優先し、
            // 未確定の場合だけ画像を再読込する。
            orientation = existingOrient;
            imageWidth = existingWidth;
            imageHeight = existingHeight;
        }
        else
        {
            (orientation, imageWidth, imageHeight) = ResolveImageDimensions(path);
        }

        return new PhotoUpsertData
        {
            PhotoPath = normalizedPath,
            PhotoFilename = filename,
            WorldId = worldId,
            WorldName = worldName,
            Timestamp = timestamp,
            LastModifiedUtc = ResolveLastModifiedUtc(path),
            Orientation = orientation,
            ImageWidth = imageWidth,
            ImageHeight = imageHeight,
            SourceSlot = slot,
            MatchSource = matchSource,
            ResetPhash = resetPhash,
        };
    }

    internal static (string? worldName, string? worldId, string? matchSource) ResolveWorldInfo(
        string normalizedPath, string filename, string path, ExistingPhotoInfo? existing)
    {
        if (filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            var (name, id) = ExtractVrcMetadataFromPng(path);
            if (name is not null || id is not null)
                return (name, id, "metadata");
        }

        if (existing is { WorldName: not null } or { WorldId: not null })
            return (existing.WorldName, existing.WorldId, existing.MatchSource ?? "title");

        // 初回解析を実施したが写真内にワールド情報が無かったことを記録する。
        // null と区別し、変更されていない写真を通常スキャンで繰り返し解析しない。
        return (null, null, "unresolved");
    }

    public static (string? orientation, long? width, long? height) ProbeImageDimensions(string path)
        => ResolveImageDimensions(path);

    private static (string? orientation, long? width, long? height) ResolveImageDimensions(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var (w, h) = ReadImageSize(stream, path);
            if (w <= 0 || h <= 0) return ("unknown", null, null);
            var orientation = h > w ? "portrait" : "landscape";
            return (orientation, w, h);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"画像サイズを取得できなかったため unknown として扱います [{path}]: {ex.Message}");
            return ("unknown", null, null);
        }
    }

    private static (long w, long h) ReadImageSize(Stream stream, string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (ext == "png") return ReadPngSize(stream);
        if (ext is "jpg" or "jpeg") return ReadJpegSize(stream);
        return (0, 0);
    }

    private static (long w, long h) ReadPngSize(Stream stream)
    {
        // PNG: 8-byte sig + IHDR chunk (4 len + 4 type + 4 width + 4 height)
        var buf = new byte[24];
        if (stream.Read(buf, 0, 24) < 24) return (0, 0);
        // Signature: 8 bytes, IHDR length: 4 bytes, type IHDR: 4 bytes, width: 4 bytes, height: 4 bytes
        if (buf[0] != 0x89 || buf[1] != 0x50) return (0, 0);
        long w = (buf[16] << 24) | (buf[17] << 16) | (buf[18] << 8) | buf[19];
        long h = (buf[20] << 24) | (buf[21] << 16) | (buf[22] << 8) | buf[23];
        return (w, h);
    }

    private static (long w, long h) ReadJpegSize(Stream stream)
    {
        if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
            return (0, 0);

        long width = 0;
        long height = 0;
        var exifOrientation = 1;
        while (stream.Position < stream.Length)
        {
            int prefix;
            do { prefix = stream.ReadByte(); }
            while (prefix >= 0 && prefix != 0xFF);
            if (prefix < 0) break;

            int marker;
            do { marker = stream.ReadByte(); }
            while (marker == 0xFF);
            if (marker < 0) break;
            if (marker == 0x00) continue;
            if (marker is 0xD9 or 0xDA) break;
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD8)
                continue;

            var lengthHigh = stream.ReadByte();
            var lengthLow = stream.ReadByte();
            if (lengthHigh < 0 || lengthLow < 0) break;
            var segmentLength = (lengthHigh << 8) | lengthLow;
            // 破損 JPEG で後退 seek や範囲外読込みを起こさない。
            if (segmentLength < 2) break;
            var payloadLength = segmentLength - 2;
            if (payloadLength > stream.Length - stream.Position) break;

            if (marker == 0xE1)
            {
                var exifSegment = new byte[payloadLength];
                stream.ReadExactly(exifSegment);
                // JPEG には XMP など Exif 以外の APP1 も複数入る。後続の非 Exif APP1 で
                // 先に取得した orientation を通常向きへ戻さない。
                if (exifSegment.AsSpan().StartsWith("Exif\0\0"u8))
                    exifOrientation = ReadExifOrientation(exifSegment);
            }
            else if (IsJpegStartOfFrameMarker(marker) && payloadLength >= 5)
            {
                _ = stream.ReadByte(); // sample precision
                var heightHigh = stream.ReadByte();
                var heightLow = stream.ReadByte();
                var widthHigh = stream.ReadByte();
                var widthLow = stream.ReadByte();
                if (heightHigh < 0 || heightLow < 0 || widthHigh < 0 || widthLow < 0)
                    break;
                height = (heightHigh << 8) | heightLow;
                width = (widthHigh << 8) | widthLow;
                stream.Seek(payloadLength - 5, SeekOrigin.Current);
            }
            else
            {
                stream.Seek(payloadLength, SeekOrigin.Current);
            }
        }

        if (width <= 0 || height <= 0)
            return (0, 0);
        return exifOrientation is >= 5 and <= 8
            ? (height, width)
            : (width, height);
    }

    private static bool IsJpegStartOfFrameMarker(int marker)
        => marker is 0xC0 or 0xC1 or 0xC2 or 0xC3
            or 0xC5 or 0xC6 or 0xC7
            or 0xC9 or 0xCA or 0xCB
            or 0xCD or 0xCE or 0xCF;

    /// <summary>APP1 Exif の IFD0 から orientation を読み、未設定・破損時は通常向きの1を返す。</summary>
    private static int ReadExifOrientation(ReadOnlySpan<byte> segment)
    {
        if (segment.Length < 14
            || !segment[..6].SequenceEqual("Exif\0\0"u8))
        {
            return 1;
        }

        var tiff = segment[6..];
        var littleEndian = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
        var bigEndian = tiff[0] == (byte)'M' && tiff[1] == (byte)'M';
        if ((!littleEndian && !bigEndian) || ReadExifUInt16(tiff, 2, littleEndian) != 42)
            return 1;

        var ifdOffsetValue = ReadExifUInt32(tiff, 4, littleEndian);
        if (ifdOffsetValue > int.MaxValue)
            return 1;
        var ifdOffset = (int)ifdOffsetValue;
        if (ifdOffset < 0 || ifdOffset + 2 > tiff.Length)
            return 1;

        var entryCount = ReadExifUInt16(tiff, ifdOffset, littleEndian);
        for (var index = 0; index < entryCount; index++)
        {
            var entryOffset = ifdOffset + 2 + index * 12;
            if (entryOffset < 0 || entryOffset + 12 > tiff.Length)
                break;
            if (ReadExifUInt16(tiff, entryOffset, littleEndian) != 0x0112)
                continue;
            if (ReadExifUInt16(tiff, entryOffset + 2, littleEndian) != 3
                || ReadExifUInt32(tiff, entryOffset + 4, littleEndian) != 1)
            {
                return 1;
            }

            var orientation = ReadExifUInt16(tiff, entryOffset + 8, littleEndian);
            return orientation is >= 1 and <= 8 ? orientation : 1;
        }
        return 1;
    }

    private static ushort ReadExifUInt16(ReadOnlySpan<byte> data, int offset, bool littleEndian)
        => littleEndian
            ? (ushort)(data[offset] | data[offset + 1] << 8)
            : (ushort)(data[offset] << 8 | data[offset + 1]);

    private static uint ReadExifUInt32(ReadOnlySpan<byte> data, int offset, bool littleEndian)
        => littleEndian
            ? (uint)(data[offset]
                | data[offset + 1] << 8
                | data[offset + 2] << 16
                | data[offset + 3] << 24)
            : (uint)(data[offset] << 24
                | data[offset + 1] << 16
                | data[offset + 2] << 8
                | data[offset + 3]);

    /// <summary>ファイル名の日時を優先し、取れない場合はファイル更新時刻から撮影日時文字列を作る。</summary>
    private static string ResolveTimestamp(string path, string filename)
    {
        var m = ReFilename().Match(filename);
        if (m.Success)
            return $"{m.Groups[1].Value} {m.Groups[2].Value.Replace('-', ':')}";
        try
        {
            // ファイルシステムから読んだローカル時刻を InvariantCulture で format することで、
            // PC の地域設定（区切り文字や曜日表記）に依存しない決定的な文字列にする。
            // DB 内の比較は文字列ベースで行うため、format が環境ごとに揺れると並び順が壊れる。
            var modified = File.GetLastWriteTime(path);
            return modified.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>ファイル更新時刻を UTC の固定フォーマット文字列として返す。</summary>
    private static string? ResolveLastModifiedUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"ファイル更新時刻を取得できませんでした [{path}]: {ex.Message}");
            return null;
        }
    }

    internal static (string? name, string? id) ExtractVrcMetadataFromPng(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var reader = new BinaryReader(fs);
            var sig = reader.ReadBytes(8);
            if (sig.Length < 8 || sig[0] != 0x89 || sig[1] != 0x50) return (null, null);

            while (fs.Position < fs.Length - 12)
            {
                var lenBytes = reader.ReadBytes(4);
                if (lenBytes.Length < 4) break;
                int chunkLen = (lenBytes[0] << 24) | (lenBytes[1] << 16) | (lenBytes[2] << 8) | lenBytes[3];
                var typeBytes = reader.ReadBytes(4);
                if (typeBytes.Length < 4) break;
                var chunkType = System.Text.Encoding.ASCII.GetString(typeBytes);

                if (chunkType == "iTXt")
                {
                    if (chunkLen > MaxItxtSize) { fs.Seek(chunkLen + 4, SeekOrigin.Current); continue; }
                    var data = reader.ReadBytes(chunkLen);
                    reader.ReadBytes(4); // CRC
                    var nullPos = Array.IndexOf(data, (byte)0);
                    if (nullPos < 0) continue;
                    var keyword = System.Text.Encoding.Latin1.GetString(data, 0, nullPos);
                    if (keyword != "XML:com.adobe.xmp") continue;
                    var pos = nullPos + 1;
                    if (pos + 2 > data.Length) continue;
                    pos += 2;
                    var langNull = Array.IndexOf(data, (byte)0, pos);
                    if (langNull < 0) continue;
                    pos = langNull + 1;
                    var tkNull = Array.IndexOf(data, (byte)0, pos);
                    if (tkNull < 0) continue;
                    pos = tkNull + 1;
                    var xmp = System.Text.Encoding.UTF8.GetString(data, pos, data.Length - pos);
                    return ParseVrcFromXmp(xmp);
                }
                else if (chunkType is "IDAT" or "IEND")
                {
                    break;
                }
                else
                {
                    fs.Seek(chunkLen + 4, SeekOrigin.Current);
                }
            }
        }
        catch (IOException ex)
        {
            AppLogger.Warn($"PNG メタデータ読み取り失敗 (I/O) [{path}]: {ex.Message}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"PNG メタデータ解析に失敗しました [{path}]: {ex.Message}");
        }
        return (null, null);
    }

    internal static (string? name, string? id) ParseVrcFromXmp(string xmp)
    {
        var idMatch = ReWorldId().Match(xmp);
        var nameMatch = ReWorldName().Match(xmp);
        return (
            nameMatch.Success ? WebUtility.HtmlDecode(nameMatch.Groups[1].Value) : null,
            idMatch.Success ? WebUtility.HtmlDecode(idMatch.Groups[1].Value) : null
        );
    }

    /// <summary>DB に保存済みの更新時刻と現在のファイル更新時刻を比較する。</summary>
    private static bool IsFileModifiedSinceStoredMtime(string path, ExistingPhotoInfo existing)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(existing.LastModifiedUtc))
                return true;

            var fileMtime = File.GetLastWriteTimeUtc(path);
            if (!DateTime.TryParseExact(existing.LastModifiedUtc, "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var storedMtime))
                return true;

            return Math.Abs((fileMtime - storedMtime).TotalSeconds) > 1;
        }
        catch { return false; }
    }

    internal static bool CollectPhotosRecursive(
        long slot,
        string dir,
        List<(long, string, string)> files,
        HashSet<string> visitedDirs,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // シンボリックリンク循環や同一ディレクトリへの別経路で、再帰が終わらなくなるのを防ぐ。
        // 訪問済みディレクトリは再度辿らない。
        string canonical;
        try
        {
            canonical = Path.GetFullPath(dir);
            var attrs = File.GetAttributes(canonical);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                AppLogger.Warn($"PhotoScanner.CollectPhotosRecursive: reparse point のため完全走査できません [{canonical}]");
                return false;
            }
        }
        catch (Exception ex) when (IsKnownDirectoryReadException(ex))
        {
            AppLogger.Warn($"ディレクトリ属性を取得できません [{dir}]: {ex.Message}");
            return false;
        }
        if (!visitedDirs.Add(canonical))
        {
            AppLogger.Warn($"PhotoScanner.CollectPhotosRecursive: 重複経路のため完全走査できません [{canonical}]");
            return false;
        }

        var isComplete = true;
        try
        {
            foreach (var entry in Directory.EnumerateDirectories(canonical))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.')) continue;
                if (Array.Exists(SkipDirs, s => s.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                if (!CollectPhotosRecursive(slot, entry, files, visitedDirs, ct))
                    isComplete = false;
            }
        }
        catch (Exception ex) when (IsKnownDirectoryReadException(ex))
        {
            AppLogger.Warn($"サブディレクトリを完全に読み取れません [{canonical}]: {ex.Message}");
            isComplete = false;
        }

        try
        {
            foreach (var entry in Directory.EnumerateFiles(canonical))
            {
                ct.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(entry).TrimStart('.').ToLowerInvariant();
                if (Array.Exists(SupportedExtensions, s => s == ext))
                    files.Add((slot, Path.GetFileName(entry), entry));
            }
        }
        catch (Exception ex) when (IsKnownDirectoryReadException(ex))
        {
            AppLogger.Warn($"ファイルを完全に読み取れません [{canonical}]: {ex.Message}");
            isComplete = false;
        }

        return isComplete;
    }

    private static bool IsKnownDirectoryReadException(Exception ex)
        => ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException;

    // === World resolution from Polaris archive ===

    public async Task<int> ResolveUnknownWorldsFromArchiveAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoScanner.ResolveUnknownWorldsFromArchiveAsync: enter");
        var startedAt = Stopwatch.GetTimestamp();
        var archiveDir = AppPaths.GetPolarisArchiveDir();
        if (archiveDir is null)
        {
            AppLogger.Trace("PhotoScanner.ResolveUnknownWorldsFromArchiveAsync: exit (no archive dir)");
            return 0;
        }

        var visits = LoadPolarisWorldVisits(archiveDir, ct);
        var archiveReadElapsed = Stopwatch.GetElapsedTime(startedAt);
        var databaseStartedAt = Stopwatch.GetTimestamp();
        await _db.UpsertArchiveWorldVisitsAsync(visits, ct).ConfigureAwait(false);

        // 訪問履歴との照合と保存は DB 内で一括実行する。写真ごとの接続・検索・更新は、
        // 未解決写真が多い初回分析で待ち時間を線形以上に増やすため使用しない。
        var resolved = await _db.ResolveUnknownWorldsFromArchiveAsync(ct).ConfigureAwait(false);
        AppLogger.Info(
            $"Performance.WorldArchive visits={visits.Count} resolved={resolved} " +
            $"archive_read_ms={archiveReadElapsed.TotalMilliseconds:F3} " +
            $"database_ms={Stopwatch.GetElapsedTime(databaseStartedAt).TotalMilliseconds:F3} " +
            $"total_ms={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}");
        AppLogger.Trace($"PhotoScanner.ResolveUnknownWorldsFromArchiveAsync: exit resolved={resolved}");
        return resolved;
    }

    private static readonly Regex ReLogTime = new(@"^(\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2})", RegexOptions.Compiled);
    private static readonly Regex ReLogEntering = new(@"\[Behaviour\] Entering Room: (.*)", RegexOptions.Compiled);
    private static readonly Regex ReLogLeftRoom = new(@"\[Behaviour\] OnLeftRoom", RegexOptions.Compiled);

    /// <summary>Polaris archive 内の VRChat ログからワールド訪問履歴を読み込む。</summary>
    internal static List<ArchiveWorldVisitData> LoadPolarisWorldVisits(
        string archiveDir,
        CancellationToken ct = default)
    {
        var visits = new List<ArchiveWorldVisitData>();
        IEnumerable<string> logFiles;
        try { logFiles = Directory.GetFiles(archiveDir, "output_log_*.txt"); }
        catch { return visits; }

        Array.Sort(logFiles as string[] ?? [.. logFiles]);
        foreach (var logFile in logFiles)
        {
            ct.ThrowIfCancellationRequested();
            LoadVisitsFromLog(logFile, visits, ct);
        }
        return visits;
    }

    /// <summary>
    /// 行末まで読むが、<see cref="MaxLogLineLength"/> を超える行はそこで打ち切って残りを破棄する。
    /// File.ReadLines だと巨大行が 1 つでもあると LOH に乗って OOM することがあるため、
    /// 手動でストリームを舐めて長さ制限を効かせる。
    /// </summary>
    internal static IEnumerable<string> ReadCappedLines(
        string path,
        CancellationToken ct = default)
    {
        using var sr = new StreamReader(path);
        var sb = new StringBuilder();
        var truncating = false;
        var readCount = 0;
        while (true)
        {
            if ((readCount++ & 0xFFF) == 0)
                ct.ThrowIfCancellationRequested();
            var ch = sr.Read();
            if (ch == -1)
            {
                if (sb.Length > 0 || truncating)
                    yield return sb.ToString();
                yield break;
            }
            if (ch == '\n')
            {
                yield return sb.ToString();
                sb.Clear();
                truncating = false;
                continue;
            }
            if (ch == '\r')
            {
                // CRLF / CR どちらも次の文字を見て LF をスキップ
                if (sr.Peek() == '\n') sr.Read();
                yield return sb.ToString();
                sb.Clear();
                truncating = false;
                continue;
            }
            if (truncating) continue;
            if (sb.Length >= MaxLogLineLength)
            {
                truncating = true;
                continue;
            }
            sb.Append((char)ch);
        }
    }

    /// <summary>1つの VRChat ログファイルから入退室イベントを読み取り、訪問区間へ変換する。</summary>
    internal static void LoadVisitsFromLog(
        string logPath,
        List<ArchiveWorldVisitData> visits,
        CancellationToken ct = default)
    {
        string? currentWorld = null;
        string? currentJoinTime = null;
        try
        {
            foreach (var line in ReadCappedLines(logPath, ct))
            {
                var timeMatch = ReLogTime.Match(line);
                var lineTime = timeMatch.Success
                    ? DateTime.TryParseExact(timeMatch.Groups[1].Value, "yyyy.MM.dd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                        ? dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null
                    : null;

                var enterMatch = ReLogEntering.Match(line);
                if (enterMatch.Success)
                {
                    // OnLeftRoom がない限り、前のワールドをいつ抜けたかは分からない。
                    // 次の Entering 時刻で閉じず、LeaveTime=null として未確定の区間にする。
                    if (currentWorld is not null && currentJoinTime is not null)
                        visits.Add(new ArchiveWorldVisitData { SourceLogName = Path.GetFileName(logPath), WorldName = currentWorld, JoinTime = currentJoinTime, LeaveTime = null });
                    currentWorld = null;
                    currentJoinTime = null;
                    if (lineTime is not null) { currentWorld = enterMatch.Groups[1].Value; currentJoinTime = lineTime; }
                    continue;
                }
                if (ReLogLeftRoom.IsMatch(line) && currentWorld is not null && currentJoinTime is not null && lineTime is not null)
                {
                    visits.Add(new ArchiveWorldVisitData { SourceLogName = Path.GetFileName(logPath), WorldName = currentWorld, JoinTime = currentJoinTime, LeaveTime = lineTime });
                    currentWorld = null; currentJoinTime = null;
                }
            }
            if (currentWorld is not null && currentJoinTime is not null)
                visits.Add(new ArchiveWorldVisitData { SourceLogName = Path.GetFileName(logPath), WorldName = currentWorld, JoinTime = currentJoinTime });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { AppLogger.Warn($"ログ読み取りに失敗しました [{logPath}]: {ex.Message}"); }
    }
}

internal enum ScanRefreshKind { Full, MetadataOnly, PathOnly }
