using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core.Database;
using Alpheratz.Models;

namespace Alpheratz.Core.Scanner;

public sealed partial class PhotoScanner
{
    private const int MaxItxtSize = 4 * 1024 * 1024;
    // VRChat の output_log 1 行の上限。ここを超える行は破損または異常データとみなして
    // Regex を走らせずに打ち切る。デフォルト 64 KiB。
    private const int MaxLogLineLength = 64 * 1024;
    private static readonly string[] SupportedExtensions = ["png", "jpg", "jpeg", "webp", "psd", "xcf"];
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

    public void RequestCancel()
    {
        AppLogger.Trace("PhotoScanner.RequestCancel: enter");
        try { _cancelSource?.Cancel(); }
        catch (Exception ex) { AppLogger.Error($"PhotoScanner.RequestCancel: threw: {ex}"); }
        AppLogger.Trace("PhotoScanner.RequestCancel: exit");
    }

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
            await _bus.PublishAsync("scan:error", "スキャンを中断しました").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"スキャン中に予期しないエラーが発生しました: {ex}");
            await _bus.PublishAsync("scan:error", ex.Message).ConfigureAwait(false);
        }
        finally
        {
            _cancelSource = null;
            AppLogger.Trace("PhotoScanner.ScanAsync: exit");
        }
    }

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
            await _bus.PublishAsync("scan:error", $"写真フォルダが見つかりません: {setting.PhotoFolderPath}").ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(setting.SecondaryPhotoFolderPath) && Directory.Exists(setting.SecondaryPhotoFolderPath))
        {
            if (!photoDirs.Exists(d => d.path == setting.SecondaryPhotoFolderPath))
                photoDirs.Add((2, setting.SecondaryPhotoFolderPath));
        }

        if (photoDirs.Count == 0)
        {
            // Try default VRChat photos folder
            var myPics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var defaultDir = Path.Combine(myPics, "VRChat");
            if (Directory.Exists(defaultDir))
                photoDirs.Add((1, defaultDir));
            else
            {
                await _bus.PublishAsync("scan:error", "写真フォルダが未設定です。設定から参照フォルダを選択してください。").ConfigureAwait(false);
                return;
            }
        }

        await _bus.PublishAsync("scan:progress", new ScanProgressDto { processed = 0, total = 0, current_world = "ファイルを収集中...", phase = "scan" }).ConfigureAwait(false);

        // Load existing photos
        var existing = await _db.GetExistingPhotosAsync(ct).ConfigureAwait(false);

        // Collect files.
        // R2-A-25: シンボリックリンクのループ（A -> B -> A）で無限再帰しないように
        // 訪問済みディレクトリの正規化フルパスを集合で管理する。
        var foundFiles = new List<(long slot, string filename, string path)>();
        var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (slot, dir) in photoDirs)
        {
            ct.ThrowIfCancellationRequested();
            CollectPhotosRecursive(slot, dir, foundFiles, visitedDirs, ct);
        }

        ct.ThrowIfCancellationRequested();

        var foundPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, _, path) in foundFiles)
            foundPathSet.Add(AppPaths.NormalizePathForDb(path));

        // Mark missing
        await _db.MarkMissingPhotosAsync(foundPathSet, ct).ConfigureAwait(false);

        // Filter to candidates needing update
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
                var missingWorld = ex.WorldName is null && ex.WorldId is null && ex.MatchSource is null;
                var filenameChanged = ex.PhotoFilename != filename;
                var fileModified = IsFileModifiedSinceTimestamp(path, ex);

                if (reappeared || sourceChanged || fileModified)
                    candidates.Add((slot, filename, path, ScanRefreshKind.Full));
                else if (filenameChanged)
                    candidates.Add((slot, filename, path, ScanRefreshKind.PathOnly));
                else if (missingWorld && filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    candidates.Add((slot, filename, path, ScanRefreshKind.MetadataOnly));
            }
        }

        var total = candidates.Count;
        await _bus.PublishAsync("scan:progress", new ScanProgressDto { processed = 0, total = total, current_world = $"{total} 件の更新対象を確認しました", phase = "scan" }).ConfigureAwait(false);

        for (var i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (slot, filename, path, kind) = candidates[i];
            var normalizedPath = AppPaths.NormalizePathForDb(path);
            existing.TryGetValue(normalizedPath, out var ex);

            var photo = AnalyzePhoto(path, filename, slot, ex, kind);
            await _db.UpsertPhotoAsync(photo, ct).ConfigureAwait(false);

            if (i % 10 == 0 || i == total - 1)
            {
                await _bus.PublishAsync("scan:progress", new ScanProgressDto
                {
                    processed = i + 1,
                    total = total,
                    current_world = photo.WorldName ?? "ワールド不明",
                    phase = "scan"
                }).ConfigureAwait(false);
            }
        }

        await _bus.PublishAsync("scan:completed", null).ConfigureAwait(false);
    }

    private static PhotoUpsertData AnalyzePhoto(string path, string filename, long slot, ExistingPhotoInfo? existing, ScanRefreshKind kind)
    {
        var timestamp = ResolveTimestamp(path, filename);
        var normalizedPath = AppPaths.NormalizePathForDb(path);

        string? worldId, worldName, matchSource;
        if (kind == ScanRefreshKind.PathOnly)
        {
            worldId = existing?.WorldId;
            worldName = existing?.WorldName;
            matchSource = existing?.MatchSource;
        }
        else
        {
            (worldName, worldId, matchSource) = ResolveWorldInfo(normalizedPath, filename, path, existing);
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
            // R2-A-18: MetadataOnly はワールド情報の補完を目的とした再走査なので、
            //          ファイルを再オープンせずに既存の寸法/orientation を保持する。
            //          既存値が "unknown" または null/0 の場合のみ ResolveImageDimensions を実行する。
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
            Orientation = orientation,
            ImageWidth = imageWidth,
            ImageHeight = imageHeight,
            SourceSlot = slot,
            MatchSource = matchSource,
        };
    }

    private static (string? worldName, string? worldId, string? matchSource) ResolveWorldInfo(
        string normalizedPath, string filename, string path, ExistingPhotoInfo? existing)
    {
        if (filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            var (name, id) = ExtractVrcMetadataFromPng(path);
            if (name is not null || id is not null)
                return (name, id, "metadata");
        }

        if (existing is { WorldName: not null } or { WorldId: not null })
            return (existing.WorldName, existing.WorldId, "title");

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
        var buf = new byte[4];
        if (stream.Read(buf, 0, 2) < 2 || buf[0] != 0xFF || buf[1] != 0xD8) return (0, 0);
        while (stream.Position < stream.Length)
        {
            if (stream.Read(buf, 0, 2) < 2) break;
            if (buf[0] != 0xFF) break;
            var marker = buf[1];
            if (stream.Read(buf, 0, 2) < 2) break;
            int segLen = (buf[0] << 8) | buf[1];
            // 破損 JPEG では segLen<2 が起こり得る。Seek(segLen-2,...) が後退して
            // 無限ループになるのを防ぐためここで打ち切る。
            if (segLen < 2) break;
            if (marker is >= 0xC0 and <= 0xC3)
            {
                if (stream.Read(buf, 0, 1) < 1) break;
                if (stream.Read(buf, 0, 4) < 4) break;
                long h = (buf[0] << 8) | buf[1];
                long w = (buf[2] << 8) | buf[3];
                return (w, h);
            }
            stream.Seek(segLen - 2, SeekOrigin.Current);
        }
        return (0, 0);
    }

    private static string ResolveTimestamp(string path, string filename)
    {
        var m = ReFilename().Match(filename);
        if (m.Success)
            return $"{m.Groups[1].Value} {m.Groups[2].Value.Replace('-', ':')}";
        try
        {
            var modified = File.GetLastWriteTime(path);
            return modified.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    private static (string? name, string? id) ExtractVrcMetadataFromPng(string path)
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
            AppLogger.Error($"PNG メタデータ読み取り失敗 (I/O) [{path}]: {ex.Message}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"PNG メタデータ解析に失敗しました [{path}]: {ex.Message}");
        }
        return (null, null);
    }

    private static (string? name, string? id) ParseVrcFromXmp(string xmp)
    {
        var idMatch = ReWorldId().Match(xmp);
        var nameMatch = ReWorldName().Match(xmp);
        return (
            nameMatch.Success ? nameMatch.Groups[1].Value : null,
            idMatch.Success ? idMatch.Groups[1].Value : null
        );
    }

    private static bool IsFileModifiedSinceTimestamp(string path, ExistingPhotoInfo existing)
    {
        try
        {
            var fileMtime = File.GetLastWriteTimeUtc(path);
            var creationTs = ResolveTimestamp(path, existing.PhotoFilename);
            if (!DateTime.TryParse(creationTs, out var created))
                return false;
            return fileMtime > created.ToUniversalTime().AddMinutes(1);
        }
        catch { return false; }
    }

    private static void CollectPhotosRecursive(
        long slot,
        string dir,
        List<(long, string, string)> files,
        HashSet<string> visitedDirs,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // R2-A-25: シンボリックリンク循環（A -> B -> A）を検出して無限再帰を防ぐ。
        // また、訪問済みディレクトリは再帰しない（ハードリンクや bind mount でも同様の事故を防ぐ）。
        string canonical;
        try
        {
            canonical = Path.GetFullPath(dir);
            var attrs = File.GetAttributes(canonical);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                AppLogger.Trace($"PhotoScanner.CollectPhotosRecursive: skip reparse point [{canonical}]");
                return;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"ディレクトリ属性を取得できません [{dir}]: {ex.Message}");
            return;
        }
        if (!visitedDirs.Add(canonical))
        {
            AppLogger.Trace($"PhotoScanner.CollectPhotosRecursive: skip already-visited [{canonical}]");
            return;
        }

        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir); }
        catch (Exception ex)
        {
            AppLogger.Warn($"ディレクトリを読み取れません [{dir}]: {ex.Message}");
            return;
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(entry);
            if (Directory.Exists(entry))
            {
                if (name.StartsWith('.')) continue;
                if (Array.Exists(SkipDirs, s => s.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                CollectPhotosRecursive(slot, entry, files, visitedDirs, ct);
            }
            else if (File.Exists(entry))
            {
                var ext = Path.GetExtension(entry).TrimStart('.').ToLowerInvariant();
                if (Array.Exists(SupportedExtensions, s => s == ext))
                    files.Add((slot, name, entry));
            }
        }
    }

    // === World resolution from Polaris archive ===

    public async Task<int> ResolveUnknownWorldsFromArchiveAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoScanner.ResolveUnknownWorldsFromArchiveAsync: enter");
        var archiveDir = AppPaths.GetPolarisArchiveDir();
        if (archiveDir is null)
        {
            AppLogger.Trace("PhotoScanner.ResolveUnknownWorldsFromArchiveAsync: exit (no archive dir)");
            return 0;
        }

        var visits = LoadPolarisWorldVisits(archiveDir);
        await _db.UpsertArchiveWorldVisitsAsync(visits, ct).ConfigureAwait(false);

        var unknownPhotos = await _db.GetUnknownWorldPhotosAsync("all", ct).ConfigureAwait(false);
        var resolved = 0;
        foreach (var (photoPath, timestamp) in unknownPhotos)
        {
            ct.ThrowIfCancellationRequested();
            var worldName = await _db.LookupWorldNameFromArchiveAsync(timestamp, ct).ConfigureAwait(false);
            if (worldName is null) continue;
            await _db.UpdatePhotoWorldNameAsync(photoPath, worldName, "polaris_archive", ct).ConfigureAwait(false);
            resolved++;
        }
        AppLogger.Trace($"PhotoScanner.ResolveUnknownWorldsFromArchiveAsync: exit resolved={resolved}");
        return resolved;
    }

    private static readonly Regex ReLogTime = new(@"^(\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2})", RegexOptions.Compiled);
    private static readonly Regex ReLogEntering = new(@"\[Behaviour\] Entering Room: (.*)", RegexOptions.Compiled);
    private static readonly Regex ReLogLeftRoom = new(@"\[Behaviour\] OnLeftRoom", RegexOptions.Compiled);

    private static List<ArchiveWorldVisitData> LoadPolarisWorldVisits(string archiveDir)
    {
        var visits = new List<ArchiveWorldVisitData>();
        IEnumerable<string> logFiles;
        try { logFiles = Directory.GetFiles(archiveDir, "output_log_*.txt"); }
        catch { return visits; }

        Array.Sort(logFiles as string[] ?? [.. logFiles]);
        foreach (var logFile in logFiles)
            LoadVisitsFromLog(logFile, visits);
        return visits;
    }

    /// <summary>
    /// 行末まで読むが、<see cref="MaxLogLineLength"/> を超える行はそこで打ち切って残りを破棄する。
    /// File.ReadLines だと巨大行が 1 つでもあると LOH に乗って OOM することがあるため、
    /// 手動でストリームを舐めて長さ制限を効かせる。
    /// </summary>
    private static IEnumerable<string> ReadCappedLines(string path)
    {
        using var sr = new StreamReader(path);
        var sb = new StringBuilder();
        var truncating = false;
        while (true)
        {
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

    private static void LoadVisitsFromLog(string logPath, List<ArchiveWorldVisitData> visits)
    {
        string? currentWorld = null;
        string? currentJoinTime = null;
        try
        {
            foreach (var line in ReadCappedLines(logPath))
            {
                var timeMatch = ReLogTime.Match(line);
                var lineTime = timeMatch.Success
                    ? DateTime.TryParseExact(timeMatch.Groups[1].Value, "yyyy.MM.dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var dt)
                        ? dt.ToString("yyyy-MM-dd HH:mm:ss") : null
                    : null;

                var enterMatch = ReLogEntering.Match(line);
                if (enterMatch.Success)
                {
                    // R2-A-10: 旧実装は前のワールドを閉じる際 LeaveTime に「次の Entering の時刻」を入れていたが、
                    //          実際にはユーザーがいつ前のワールドを抜けたかは不明である（OnLeftRoom が来ていない）。
                    //          LeaveTime=null として未確定であることを明示する。
                    if (currentWorld is not null && currentJoinTime is not null)
                        visits.Add(new ArchiveWorldVisitData { SourceLogName = Path.GetFileName(logPath), WorldName = currentWorld, JoinTime = currentJoinTime, LeaveTime = null });
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
        catch (Exception ex) { AppLogger.Warn($"ログ読み取りに失敗しました [{logPath}]: {ex.Message}"); }
    }
}

internal enum ScanRefreshKind { Full, MetadataOnly, PathOnly }