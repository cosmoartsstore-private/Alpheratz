using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alpheratz.Core.Imaging.Pdq;
using Microsoft.Data.Sqlite;

if (args.Length is < 1 or > 4 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: Alpheratz.PdqFileProbe <Alpheratz.db> [sample-count] [parallelism] [both|pooled|allocated|thumbnail|reference]");
    return 2;
}

var databasePath = Path.GetFullPath(args[0]);
var requestedSamples = args.Length >= 2 && int.TryParse(args[1], out var parsedCount)
    ? Math.Clamp(parsedCount, 1, 1000)
    : 128;
var parallelism = args.Length >= 3 && int.TryParse(args[2], out var parsedParallelism)
    ? Math.Clamp(parsedParallelism, 1, 16)
    : Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
var mode = args.Length >= 4 ? args[3] : "both";
if (mode is not ("both" or "pooled" or "allocated" or "thumbnail" or "reference"))
{
    Console.Error.WriteLine($"不明な測定モードです: {mode}");
    return 4;
}

var candidates = LoadCandidates(databasePath, requestedSamples * 32)
    .Where(candidate => File.Exists(candidate.Path))
    .Where(candidate => mode != "thumbnail" || File.Exists(candidate.GridThumbnailPath))
    .Take(requestedSamples)
    .ToArray();
if (candidates.Length == 0)
{
    Console.Error.WriteLine(mode == "thumbnail"
        ? "元画像とグリッド用サムネイルが揃った写真を取得できませんでした。"
        : "実在する写真ファイルを取得できませんでした。");
    return 3;
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = "pdq_file_input",
    databasePath,
    requestedSamples,
    actualSamples = candidates.Length,
    parallelism,
    mode,
    firstPath = candidates[0].Path,
    firstGridThumbnailPath = mode == "thumbnail" ? candidates[0].GridThumbnailPath : null,
}));

// 最初の WinRT 初期化だけを測定外へ出す。全写真を先読みすると実運用のメモリ増加を隠すため1枚に限定する。
await RunOnceAsync("warmup", candidates.Take(1).ToArray(), parallelism, pooled: true);
ForceGc();

if (mode == "thumbnail")
{
    await RunPathOnceAsync("thumbnail_warmup", candidates.Take(1).ToArray(), parallelism, static candidate => candidate.GridThumbnailPath);
    ForceGc();

    var originals = await RunPathOnceAsync("original", candidates, parallelism, static candidate => candidate.Path);
    ForceGc();
    var thumbnails = await RunPathOnceAsync("thumbnail", candidates, parallelism, static candidate => candidate.GridThumbnailPath);
    ForceGc();

    var distances = new List<int>(candidates.Length);
    var exactMatches = 0;
    for (var index = 0; index < candidates.Length; index++)
    {
        var originalHash = originals.Hashes[index];
        var thumbnailHash = thumbnails.Hashes[index];
        if (string.IsNullOrEmpty(originalHash) || string.IsNullOrEmpty(thumbnailHash))
            continue;

        if (string.Equals(originalHash, thumbnailHash, StringComparison.Ordinal))
            exactMatches++;
        var distance = PdqHasher.ClosestHashDistance(
            PdqHasher.ParseHashVariants(originalHash),
            PdqHasher.ParseHashVariants(thumbnailHash));
        if (distance is not null)
            distances.Add(distance.Value);
    }

    distances.Sort();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        probe = "pdq_thumbnail_comparison",
        samples = candidates.Length,
        parallelism,
        originalElapsedMilliseconds = originals.ElapsedMilliseconds,
        thumbnailElapsedMilliseconds = thumbnails.ElapsedMilliseconds,
        originalPhotosPerSecond = candidates.Length / originals.ElapsedMilliseconds * 1000,
        thumbnailPhotosPerSecond = candidates.Length / thumbnails.ElapsedMilliseconds * 1000,
        originalAllocatedBytes = originals.AllocatedBytes,
        thumbnailAllocatedBytes = thumbnails.AllocatedBytes,
        originalPeakWorkingSetDeltaBytes = originals.PeakWorkingSetDeltaBytes,
        thumbnailPeakWorkingSetDeltaBytes = thumbnails.PeakWorkingSetDeltaBytes,
        exactMatches,
        comparableHashes = distances.Count,
        withinDistance64 = distances.Count(distance => distance <= 64),
        medianDistance = Percentile(distances, 0.50),
        p95Distance = Percentile(distances, 0.95),
        maximumDistance = distances.Count == 0 ? (int?)null : distances[^1],
    }));
    return 0;
}

if (mode == "reference")
{
    var comparison = await RunReferenceComparisonAsync(candidates, parallelism);
    Console.WriteLine(JsonSerializer.Serialize(comparison));
    return comparison.OptimizedReferenceMismatches == 0 ? 0 : 5;
}

var results = new List<Measurement>();
for (var round = 0; round < 3; round++)
{
    if (mode is "both" or "pooled")
    {
        results.Add(await RunOnceAsync($"pooled_{round + 1}", candidates, parallelism, pooled: true));
        ForceGc();
    }
    if (mode is "both" or "allocated")
    {
        results.Add(await RunOnceAsync($"allocated_{round + 1}", candidates, parallelism, pooled: false));
        ForceGc();
    }
}

foreach (var result in results)
    Console.WriteLine(JsonSerializer.Serialize(result));

foreach (var group in results.GroupBy(result => result.Pooled))
{
    var orderedElapsed = group.Select(result => result.ElapsedMilliseconds).Order().ToArray();
    var orderedAllocated = group.Select(result => result.AllocatedBytes).Order().ToArray();
    var orderedPeak = group.Select(result => result.PeakWorkingSetDeltaBytes).Order().ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        probe = "pdq_file_summary",
        pooled = group.Key,
        samples = candidates.Length,
        parallelism,
        medianElapsedMilliseconds = orderedElapsed[orderedElapsed.Length / 2],
        medianMillisecondsPerImage = orderedElapsed[orderedElapsed.Length / 2] / candidates.Length,
        medianAllocatedBytes = orderedAllocated[orderedAllocated.Length / 2],
        medianAllocatedBytesPerImage = orderedAllocated[orderedAllocated.Length / 2] / candidates.Length,
        medianPeakWorkingSetDeltaBytes = orderedPeak[orderedPeak.Length / 2],
        hashMismatches = group.Sum(result => result.HashMismatches),
        unreadable = group.Sum(result => result.Unreadable),
    }));
}

return 0;

static Candidate[] LoadCandidates(string databasePath, int limit)
{
    var connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false,
    }.ToString();
    using var connection = new SqliteConnection(connectionString);
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT source_slot, photo_path, phash
        FROM photos
        WHERE is_missing = 0
          AND phash IS NOT NULL AND phash <> '' AND phash <> 'unreadable'
        ORDER BY timestamp DESC, photo_path
        LIMIT @limit
        """;
    command.Parameters.AddWithValue("@limit", limit);
    using var reader = command.ExecuteReader();
    var candidates = new List<Candidate>();
    while (reader.Read())
    {
        var sourceSlot = reader.GetInt64(0);
        var databasePhotoPath = reader.GetString(1);
        candidates.Add(new Candidate(
            databasePhotoPath.Replace('/', '\\'),
            reader.GetString(2),
            BuildGridThumbnailPath(databasePath, sourceSlot, databasePhotoPath)));
    }
    return candidates.ToArray();
}

static string BuildGridThumbnailPath(string databasePath, long sourceSlot, string photoPath)
{
    var dbDirectory = Directory.GetParent(databasePath)
        ?? throw new InvalidOperationException("DB ディレクトリを取得できません。");
    var dataDirectory = dbDirectory.Parent
        ?? throw new InvalidOperationException("Data ディレクトリを取得できません。");
    var slotDirectory = sourceSlot == 2 ? "2nd-cache" : "1st-cache";
    var normalized = photoPath.Replace('\\', '/').ToUpperInvariant();
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
    var cacheKey = Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    return Path.Combine(
        dataDirectory.FullName,
        "cache",
        slotDirectory,
        "imgCache",
        $"{Path.GetFileName(photoPath)}.{cacheKey}.thumb.grid-512.jpg");
}

static async Task<PathMeasurement> RunPathOnceAsync(
    string name,
    IReadOnlyList<Candidate> candidates,
    int parallelism,
    Func<Candidate, string> selectPath)
{
    var hashes = new string?[candidates.Count];
    var workingSetBefore = Environment.WorkingSet;
    var peakWorkingSet = workingSetBefore;
    using var monitorCts = new CancellationTokenSource();
    var monitor = Task.Run(async () =>
    {
        while (!monitorCts.IsCancellationRequested)
        {
            UpdateMaximum(ref peakWorkingSet, Environment.WorkingSet);
            try { await Task.Delay(10, monitorCts.Token); }
            catch (OperationCanceledException) { }
        }
    });

    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var stopwatch = Stopwatch.StartNew();
    await Parallel.ForEachAsync(Enumerable.Range(0, candidates.Count), new ParallelOptions
    {
        MaxDegreeOfParallelism = parallelism,
    }, async (index, ct) =>
    {
        using var image = await PdqImageReader.ReadPooledLumaAsync(selectPath(candidates[index]), ct).ConfigureAwait(false);
        hashes[index] = image is null
            ? null
            : PdqHasher.ComputeHashVariantsHex(image.Buffer, image.Width, image.Height);
    });
    stopwatch.Stop();
    monitorCts.Cancel();
    await monitor;
    UpdateMaximum(ref peakWorkingSet, Environment.WorkingSet);

    return new PathMeasurement(
        name,
        stopwatch.Elapsed.TotalMilliseconds,
        GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
        peakWorkingSet - workingSetBefore,
        hashes);
}

static int? Percentile(IReadOnlyList<int> sortedValues, double percentile)
{
    if (sortedValues.Count == 0) return null;
    var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
    return sortedValues[Math.Clamp(index, 0, sortedValues.Count - 1)];
}

static async Task<Measurement> RunOnceAsync(
    string name,
    IReadOnlyList<Candidate> candidates,
    int parallelism,
    bool pooled)
{
    var unreadable = 0;
    var mismatches = 0;
    var workingSetBefore = Environment.WorkingSet;
    var peakWorkingSet = workingSetBefore;
    using var monitorCts = new CancellationTokenSource();
    var monitor = Task.Run(async () =>
    {
        while (!monitorCts.IsCancellationRequested)
        {
            UpdateMaximum(ref peakWorkingSet, Environment.WorkingSet);
            try { await Task.Delay(10, monitorCts.Token); }
            catch (OperationCanceledException) { }
        }
    });

    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var gen2Before = GC.CollectionCount(2);
    var stopwatch = Stopwatch.StartNew();
    await Parallel.ForEachAsync(candidates, new ParallelOptions
    {
        MaxDegreeOfParallelism = parallelism,
    }, async (candidate, ct) =>
    {
        string? hash;
        if (pooled)
        {
            using var image = await PdqImageReader.ReadPooledLumaAsync(candidate.Path, ct).ConfigureAwait(false);
            hash = image is null
                ? null
                : PdqHasher.ComputeHashVariantsHex(image.Buffer, image.Width, image.Height);
        }
        else
        {
            var image = await PdqImageReader.ReadLumaAsync(candidate.Path, ct).ConfigureAwait(false);
            hash = image is null
                ? null
                : PdqHasher.ComputeHashVariantsHex(image.Value.luma, image.Value.width, image.Value.height);
        }

        if (hash is null)
            Interlocked.Increment(ref unreadable);
        else if (!string.Equals(hash, candidate.StoredHash, StringComparison.Ordinal))
            Interlocked.Increment(ref mismatches);
    });
    stopwatch.Stop();
    monitorCts.Cancel();
    await monitor;
    UpdateMaximum(ref peakWorkingSet, Environment.WorkingSet);

    return new Measurement(
        Probe: "pdq_file_round",
        Name: name,
        Pooled: pooled,
        Samples: candidates.Count,
        ElapsedMilliseconds: stopwatch.Elapsed.TotalMilliseconds,
        AllocatedBytes: GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
        PeakWorkingSetDeltaBytes: peakWorkingSet - workingSetBefore,
        Gen2Collections: GC.CollectionCount(2) - gen2Before,
        HashMismatches: mismatches,
        Unreadable: unreadable);
}

/// <summary>
/// 作業領域を再利用する現行経路と、公開APIを方向ごとに呼ぶ従来経路を同じ輝度配列で比較する。
/// 画像読込の差を混ぜず、ハッシュ計算の高速化が保存値を変えていないかだけを確認する。
/// </summary>
static async Task<ReferenceComparison> RunReferenceComparisonAsync(
    IReadOnlyList<Candidate> candidates,
    int parallelism)
{
    var unreadable = 0;
    var optimizedReferenceMismatches = 0;
    var referenceStoredMismatches = 0;
    string? firstMismatchPath = null;
    var stopwatch = Stopwatch.StartNew();

    await Parallel.ForEachAsync(candidates, new ParallelOptions
    {
        MaxDegreeOfParallelism = parallelism,
    }, async (candidate, ct) =>
    {
        using var image = await PdqImageReader.ReadPooledLumaAsync(candidate.Path, ct).ConfigureAwait(false);
        if (image is null)
        {
            Interlocked.Increment(ref unreadable);
            return;
        }

        var optimized = PdqHasher.ComputeHashVariantsHex(image.Buffer, image.Width, image.Height);
        var reference = ComputeReferenceHashVariantsHex(image.Buffer, image.Width, image.Height);
        if (!string.Equals(optimized, reference, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref optimizedReferenceMismatches);
            Interlocked.CompareExchange(ref firstMismatchPath, candidate.Path, null);
        }
        if (!string.Equals(reference, candidate.StoredHash, StringComparison.Ordinal))
            Interlocked.Increment(ref referenceStoredMismatches);
    });

    stopwatch.Stop();
    return new ReferenceComparison(
        Probe: "pdq_reference_comparison",
        Samples: candidates.Count,
        Parallelism: parallelism,
        ElapsedMilliseconds: stopwatch.Elapsed.TotalMilliseconds,
        OptimizedReferenceMismatches: optimizedReferenceMismatches,
        ReferenceStoredMismatches: referenceStoredMismatches,
        Unreadable: unreadable,
        FirstMismatchPath: firstMismatchPath);
}

static string ComputeReferenceHashVariantsHex(float[] luma, int width, int height)
{
    var variants = new string[4];
    var original = PdqHasher.GeneratePdq(luma, width, height);
    if (original is null) return string.Empty;
    variants[0] = PdqHasher.ToHex(original.Value.Hash);

    var rotated90 = PdqHasher.Rotate90(luma, width, height);
    var hash90 = PdqHasher.GeneratePdq(rotated90.luma, rotated90.width, rotated90.height);
    variants[1] = hash90 is null ? string.Empty : PdqHasher.ToHex(hash90.Value.Hash);

    var rotated180 = PdqHasher.Rotate180(luma, width, height);
    var hash180 = PdqHasher.GeneratePdq(rotated180.luma, rotated180.width, rotated180.height);
    variants[2] = hash180 is null ? string.Empty : PdqHasher.ToHex(hash180.Value.Hash);

    var rotated270 = PdqHasher.Rotate270(luma, width, height);
    var hash270 = PdqHasher.GeneratePdq(rotated270.luma, rotated270.width, rotated270.height);
    variants[3] = hash270 is null ? string.Empty : PdqHasher.ToHex(hash270.Value.Hash);

    return string.Join('|', variants.Where(static variant => !string.IsNullOrEmpty(variant)));
}

static void UpdateMaximum(ref long target, long value)
{
    while (true)
    {
        var current = Volatile.Read(ref target);
        if (value <= current || Interlocked.CompareExchange(ref target, value, current) == current)
            return;
    }
}

static void ForceGc()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

internal sealed record Candidate(string Path, string StoredHash, string GridThumbnailPath);

internal sealed record PathMeasurement(
    string Name,
    double ElapsedMilliseconds,
    long AllocatedBytes,
    long PeakWorkingSetDeltaBytes,
    string?[] Hashes);

internal sealed record ReferenceComparison(
    string Probe,
    int Samples,
    int Parallelism,
    double ElapsedMilliseconds,
    int OptimizedReferenceMismatches,
    int ReferenceStoredMismatches,
    int Unreadable,
    string? FirstMismatchPath);

internal sealed record Measurement(
    string Probe,
    string Name,
    bool Pooled,
    int Samples,
    double ElapsedMilliseconds,
    long AllocatedBytes,
    long PeakWorkingSetDeltaBytes,
    int Gen2Collections,
    int HashMismatches,
    int Unreadable);
