using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Alpheratz.Core.Imaging.Pdq;
using Microsoft.Data.Sqlite;

if (args.Length is < 1 or > 2 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: Alpheratz.PerformanceProbe <Alpheratz.db> [--create-proposed-indexes]");
    return 2;
}

var databasePath = Path.GetFullPath(args[0]);
var createProposedIndexes = args.Length == 2
    && string.Equals(args[1], "--create-proposed-indexes", StringComparison.Ordinal);
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
    Mode = createProposedIndexes ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly,
    Cache = SqliteCacheMode.Private,
    Pooling = false,
}.ToString();

using var connection = new SqliteConnection(connectionString);
connection.Open();
if (createProposedIndexes)
{
    ExecuteNonQuery(
        connection,
        """
        DROP INDEX IF EXISTS idx_photos_world_filter;
        CREATE INDEX idx_photos_world_filter
        ON photos(is_missing, TRIM(world_name), timestamp DESC, photo_path)
        """);
}
else
{
    ExecuteNonQuery(connection, "PRAGMA query_only = ON");
}
ExecuteNonQuery(connection, "PRAGMA temp_store = MEMORY");

var summary = ExecuteRow(
    connection,
    """
    SELECT COUNT(*),
           COUNT(DISTINCT CASE
               WHEN world_name IS NULL OR TRIM(world_name) = '' THEN NULL
               ELSE TRIM(world_name)
           END),
           SUM(CASE WHEN phash IS NULL OR phash = '' THEN 1 ELSE 0 END),
           SUM(CASE WHEN world_name IS NULL OR TRIM(world_name) = '' THEN 1 ELSE 0 END)
    FROM photos
    WHERE is_missing = 0
    """);
var archiveVisitCount = Convert.ToInt64(ExecuteRow(connection, "SELECT COUNT(*) FROM archive_world_visits")[0]);

var topWorlds = ExecuteRows(
    connection,
    """
    SELECT TRIM(world_name), COUNT(*)
    FROM photos
    WHERE is_missing = 0
      AND world_name IS NOT NULL
      AND TRIM(world_name) <> ''
    GROUP BY TRIM(world_name)
    ORDER BY COUNT(*) DESC
    LIMIT 5
    """);

var hashStatsBySlot = ExecuteRows(
    connection,
    """
    SELECT source_slot,
           SUM(CASE
               WHEN world_name IS NOT NULL AND TRIM(world_name) <> ''
                AND phash IS NOT NULL AND phash <> '' AND phash <> 'unreadable'
               THEN 1 ELSE 0
           END) AS known_rows,
           COUNT(DISTINCT CASE
               WHEN world_name IS NOT NULL AND TRIM(world_name) <> ''
                AND phash IS NOT NULL AND phash <> '' AND phash <> 'unreadable'
               THEN phash ELSE NULL
           END) AS known_hashes,
           SUM(CASE
               WHEN (world_name IS NULL OR TRIM(world_name) = '') AND world_id IS NULL
                AND phash IS NOT NULL AND phash <> '' AND phash <> 'unreadable'
               THEN 1 ELSE 0
           END) AS unknown_rows,
           COUNT(DISTINCT CASE
               WHEN (world_name IS NULL OR TRIM(world_name) = '') AND world_id IS NULL
                AND phash IS NOT NULL AND phash <> '' AND phash <> 'unreadable'
               THEN phash ELSE NULL
           END) AS unknown_hashes
    FROM photos
    WHERE is_missing = 0
    GROUP BY source_slot
    ORDER BY source_slot
    """);

var world = Convert.ToString(topWorlds[0][0]) ?? throw new InvalidOperationException("ワールド名がありません。");
Console.WriteLine(JsonSerializer.Serialize(new
{
    databasePath,
    createProposedIndexes,
    databaseBytes = new FileInfo(databasePath).Length,
    photoCount = Convert.ToInt64(summary[0]),
    worldCount = Convert.ToInt64(summary[1]),
    pendingPhashCount = Convert.ToInt64(summary[2]),
    unknownWorldCount = Convert.ToInt64(summary[3]),
    archiveVisitCount,
    topWorlds,
    hashStatsBySlot,
}));

var queries = new[]
{
    new ProbeQuery(
        "world_count",
        "SELECT COUNT(*) FROM photos WHERE photos.is_missing = 0 AND TRIM(photos.world_name) = @world",
        new Dictionary<string, object?> { ["@world"] = world }),
    new ProbeQuery(
        "world_rows",
        """
        SELECT photo_filename, photo_path, world_id, world_name, timestamp,
               NULL, orientation, image_width, image_height, source_slot,
               is_favorite, match_source, is_missing
        FROM photos
        WHERE photos.is_missing = 0 AND TRIM(photos.world_name) = @world
        ORDER BY timestamp DESC, photo_path ASC
        """,
        new Dictionary<string, object?> { ["@world"] = world }),
    new ProbeQuery(
        "world_months",
        """
        SELECT substr(timestamp, 1, 4), substr(timestamp, 6, 2), COUNT(*)
        FROM photos
        WHERE photos.is_missing = 0 AND TRIM(photos.world_name) = @world
        GROUP BY 1, 2
        ORDER BY 1 DESC, 2 DESC
        """,
        new Dictionary<string, object?> { ["@world"] = world }),
    new ProbeQuery(
        "favorite_count",
        "SELECT COUNT(*) FROM photos WHERE photos.is_missing = 0 AND photos.is_favorite = 1"),
    new ProbeQuery(
        "orientation_count",
        "SELECT COUNT(*) FROM photos WHERE photos.is_missing = 0 AND photos.orientation = 'landscape'"),
    new ProbeQuery(
        "source_slot_count",
        "SELECT COUNT(*) FROM photos WHERE photos.is_missing = 0 AND photos.source_slot = 1"),
};

foreach (var query in queries)
{
    var plan = Explain(connection, query);
    _ = ExecuteQuery(connection, query);

    var elapsed = new double[20];
    var rowCount = 0;
    for (var iteration = 0; iteration < elapsed.Length; iteration++)
    {
        var stopwatch = Stopwatch.StartNew();
        rowCount = ExecuteQuery(connection, query);
        stopwatch.Stop();
        elapsed[iteration] = stopwatch.Elapsed.TotalMilliseconds;
    }

    Array.Sort(elapsed);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        query = query.Name,
        rowCount,
        medianMilliseconds = Median(elapsed),
        p95Milliseconds = elapsed[18],
        maximumMilliseconds = elapsed[^1],
        plan,
    }));
}

RunWorldMatchProbe(connection);
RunHashGenerationProbe();

return 0;

static void ExecuteNonQuery(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
}

static object?[] ExecuteRow(SqliteConnection connection, string sql)
    => ExecuteRows(connection, sql).Single();

static List<object?[]> ExecuteRows(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    using var reader = command.ExecuteReader();
    var rows = new List<object?[]>();
    while (reader.Read())
    {
        var values = new object?[reader.FieldCount];
        reader.GetValues(values);
        rows.Add(values);
    }
    return rows;
}

static int ExecuteQuery(SqliteConnection connection, ProbeQuery query)
{
    using var command = CreateCommand(connection, query.Sql, query.Parameters);
    using var reader = command.ExecuteReader();
    var count = 0;
    while (reader.Read())
        count++;
    return count;
}

static IReadOnlyList<string> Explain(SqliteConnection connection, ProbeQuery query)
{
    using var command = CreateCommand(connection, "EXPLAIN QUERY PLAN " + query.Sql, query.Parameters);
    using var reader = command.ExecuteReader();
    var details = new List<string>();
    while (reader.Read())
        details.Add(reader.GetString(3));
    return details;
}

static SqliteCommand CreateCommand(
    SqliteConnection connection,
    string sql,
    IReadOnlyDictionary<string, object?> parameters)
{
    var command = connection.CreateCommand();
    command.CommandText = sql;
    foreach (var parameter in parameters)
        command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
    return command;
}

static double Median(IReadOnlyList<double> sortedValues)
{
    var middle = sortedValues.Count / 2;
    return sortedValues.Count % 2 == 0
        ? (sortedValues[middle - 1] + sortedValues[middle]) / 2
        : sortedValues[middle];
}

static void RunWorldMatchProbe(SqliteConnection connection)
{
    const int sampleLimit = 128;
    var knownRows = new List<ProbeKnownRow>();
    using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            SELECT photo_path, TRIM(world_name), phash
            FROM photos
            WHERE is_missing = 0
              AND source_slot = 1
              AND world_name IS NOT NULL AND TRIM(world_name) <> ''
              AND phash IS NOT NULL AND phash <> '' AND phash <> 'unreadable'
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            knownRows.Add(new ProbeKnownRow(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
    }

    var unknownHashes = new List<string>(sampleLimit);
    using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            SELECT DISTINCT phash
            FROM photos
            WHERE is_missing = 0
              AND source_slot = 1
              AND (world_name IS NULL OR TRIM(world_name) = '')
              AND world_id IS NULL
              AND phash IS NOT NULL AND phash <> '' AND phash <> 'unreadable'
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@limit", sampleLimit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            unknownHashes.Add(reader.GetString(0));
    }

    ForceGc();
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
    var prepareWatch = Stopwatch.StartNew();
    var prepared = knownRows
        .Select(row => new ProbePreparedKnownRow(row, PdqHasher.ParsePackedHashVariants(row.Phash)))
        .Where(row => row.Variants.Length > 0)
        .ToArray();
    prepareWatch.Stop();
    var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
    var workingSetAfter = Process.GetCurrentProcess().WorkingSet64;

    var currentResults = new ProbeMatch?[unknownHashes.Count];
    var boundedResults = new ProbeMatch?[unknownHashes.Count];
    var indexedResults = new ProbeMatch?[unknownHashes.Count];
    var optimizedResults = new ProbeMatch?[unknownHashes.Count];
    var parallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 12);

    ForceGc();
    var indexAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var indexWorkingSetBefore = Process.GetCurrentProcess().WorkingSet64;
    var indexWatch = Stopwatch.StartNew();
    var exactIndex = new Dictionary<PdqHasher.PackedHash256, int>(prepared.Length * 4);
    for (var candidateIndex = 0; candidateIndex < prepared.Length; candidateIndex++)
    {
        foreach (var variant in prepared[candidateIndex].Variants)
            exactIndex.TryAdd(variant, candidateIndex);
    }
    indexWatch.Stop();
    var indexAllocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
    var indexWorkingSetAfter = Process.GetCurrentProcess().WorkingSet64;

    var currentWatch = Stopwatch.StartNew();
    Parallel.For(0, unknownHashes.Count, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, index =>
    {
        currentResults[index] = FindCurrent(unknownHashes[index], prepared);
    });
    currentWatch.Stop();

    var parallelMeasurements = new List<object>();
    foreach (var measuredParallelism in new[] { 8, 12, 16, Math.Max(1, Environment.ProcessorCount - 1) }.Distinct())
    {
        var measuredResults = new ProbeMatch?[unknownHashes.Count];
        var measuredWatch = Stopwatch.StartNew();
        Parallel.For(0, unknownHashes.Count, new ParallelOptions { MaxDegreeOfParallelism = measuredParallelism }, index =>
        {
            measuredResults[index] = FindCurrent(unknownHashes[index], prepared);
        });
        measuredWatch.Stop();
        parallelMeasurements.Add(new
        {
            parallelism = measuredParallelism,
            milliseconds = measuredWatch.Elapsed.TotalMilliseconds,
            resultMismatches = measuredResults.Where((result, index) => result != currentResults[index]).Count(),
        });
    }

    var boundedWatch = Stopwatch.StartNew();
    Parallel.For(0, unknownHashes.Count, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, index =>
    {
        boundedResults[index] = FindBounded(unknownHashes[index], prepared);
    });
    boundedWatch.Stop();

    var indexedWatch = Stopwatch.StartNew();
    Parallel.For(0, unknownHashes.Count, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, index =>
    {
        indexedResults[index] = FindWithExactIndex(unknownHashes[index], prepared, exactIndex);
    });
    indexedWatch.Stop();

    var optimizedWatch = Stopwatch.StartNew();
    Parallel.For(0, unknownHashes.Count, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, index =>
    {
        optimizedResults[index] = FindOriginalAgainstRotations(unknownHashes[index], prepared);
    });
    optimizedWatch.Stop();

    var mismatches = 0;
    var boundedMismatches = 0;
    var indexedMismatches = 0;
    var exactMatches = 0;
    for (var index = 0; index < currentResults.Length; index++)
    {
        if (currentResults[index] != optimizedResults[index])
            mismatches++;
        if (currentResults[index] != boundedResults[index])
            boundedMismatches++;
        if (currentResults[index] != indexedResults[index])
            indexedMismatches++;
        if (indexedResults[index]?.Distance == 0)
            exactMatches++;
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        probe = "world_match",
        knownRows = prepared.Length,
        unknownSamples = unknownHashes.Count,
        parallelism,
        prepareMilliseconds = prepareWatch.Elapsed.TotalMilliseconds,
        prepareAllocatedBytes = allocatedAfter - allocatedBefore,
        prepareWorkingSetDeltaBytes = workingSetAfter - workingSetBefore,
        exactIndexEntries = exactIndex.Count,
        exactIndexBuildMilliseconds = indexWatch.Elapsed.TotalMilliseconds,
        exactIndexAllocatedBytes = indexAllocatedAfter - indexAllocatedBefore,
        exactIndexWorkingSetDeltaBytes = indexWorkingSetAfter - indexWorkingSetBefore,
        exactMatches,
        currentMilliseconds = currentWatch.Elapsed.TotalMilliseconds,
        parallelMeasurements,
        boundedMilliseconds = boundedWatch.Elapsed.TotalMilliseconds,
        boundedResultMismatches = boundedMismatches,
        exactIndexMilliseconds = indexedWatch.Elapsed.TotalMilliseconds,
        exactIndexResultMismatches = indexedMismatches,
        originalAgainstRotationsMilliseconds = optimizedWatch.Elapsed.TotalMilliseconds,
        resultMismatches = mismatches,
    }));
}

static ProbeMatch? FindBounded(string targetHash, IReadOnlyList<ProbePreparedKnownRow> candidates)
{
    var target = PdqHasher.ParsePackedHashVariants(targetHash);
    if (target.Length == 0) return null;

    var bestDistance = 76;
    ProbeKnownRow? best = null;
    foreach (var candidate in candidates)
    {
        var distance = ClosestBoundedDistance(target, candidate.Variants, bestDistance - 1);
        if (distance >= bestDistance) continue;
        bestDistance = distance;
        best = candidate.Row;
        if (distance == 0) break;
    }
    return best is null ? null : new ProbeMatch(best.PhotoPath, bestDistance);
}

static int ClosestBoundedDistance(
    IReadOnlyList<PdqHasher.PackedHash256> left,
    IReadOnlyList<PdqHasher.PackedHash256> right,
    int maximumDistance)
{
    var best = maximumDistance + 1;
    foreach (var leftVariant in left)
    {
        foreach (var rightVariant in right)
        {
            var distance = 0;
            distance += BitOperations.PopCount(leftVariant.Part0 ^ rightVariant.Part0);
            if (distance >= best) continue;
            distance += BitOperations.PopCount(leftVariant.Part1 ^ rightVariant.Part1);
            if (distance >= best) continue;
            distance += BitOperations.PopCount(leftVariant.Part2 ^ rightVariant.Part2);
            if (distance >= best) continue;
            distance += BitOperations.PopCount(leftVariant.Part3 ^ rightVariant.Part3);
            if (distance < best) best = distance;
            if (best == 0) return 0;
        }
    }
    return best;
}

static ProbeMatch? FindWithExactIndex(
    string targetHash,
    IReadOnlyList<ProbePreparedKnownRow> candidates,
    IReadOnlyDictionary<PdqHasher.PackedHash256, int> exactIndex)
{
    var target = PdqHasher.ParsePackedHashVariants(targetHash);
    if (target.Length == 0) return null;

    var exactCandidateIndex = int.MaxValue;
    foreach (var variant in target)
    {
        if (exactIndex.TryGetValue(variant, out var candidateIndex)
            && candidateIndex < exactCandidateIndex)
        {
            exactCandidateIndex = candidateIndex;
        }
    }
    if (exactCandidateIndex != int.MaxValue)
        return new ProbeMatch(candidates[exactCandidateIndex].Row.PhotoPath, 0);

    return FindCurrent(targetHash, candidates);
}

static ProbeMatch? FindCurrent(string targetHash, IReadOnlyList<ProbePreparedKnownRow> candidates)
{
    var target = PdqHasher.ParsePackedHashVariants(targetHash);
    if (target.Length == 0) return null;

    var bestDistance = int.MaxValue;
    ProbeKnownRow? best = null;
    foreach (var candidate in candidates)
    {
        var distance = PdqHasher.ClosestPackedHashDistance(target, candidate.Variants);
        if (distance > 75 || distance >= bestDistance) continue;
        bestDistance = distance;
        best = candidate.Row;
        if (distance == 0) break;
    }
    return best is null ? null : new ProbeMatch(best.PhotoPath, bestDistance);
}

static ProbeMatch? FindOriginalAgainstRotations(string targetHash, IReadOnlyList<ProbePreparedKnownRow> candidates)
{
    var target = PdqHasher.ParsePackedHashVariants(targetHash);
    if (target.Length == 0) return null;

    var bestDistance = int.MaxValue;
    ProbeKnownRow? best = null;
    foreach (var candidate in candidates)
    {
        var distance = OriginalAgainstRotationsDistance(target[0], candidate.Variants, Math.Min(bestDistance - 1, 75));
        if (distance > 75 || distance >= bestDistance) continue;
        bestDistance = distance;
        best = candidate.Row;
        if (distance == 0) break;
    }
    return best is null ? null : new ProbeMatch(best.PhotoPath, bestDistance);
}

static int OriginalAgainstRotationsDistance(
    PdqHasher.PackedHash256 target,
    IReadOnlyList<PdqHasher.PackedHash256> candidates,
    int maximumDistance)
{
    var best = maximumDistance + 1;
    foreach (var candidate in candidates)
    {
        var distance = 0;
        distance += BitOperations.PopCount(target.Part0 ^ candidate.Part0);
        if (distance >= best) continue;
        distance += BitOperations.PopCount(target.Part1 ^ candidate.Part1);
        if (distance >= best) continue;
        distance += BitOperations.PopCount(target.Part2 ^ candidate.Part2);
        if (distance >= best) continue;
        distance += BitOperations.PopCount(target.Part3 ^ candidate.Part3);
        if (distance < best) best = distance;
        if (best == 0) break;
    }
    return best;
}

static void ForceGc()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

static void RunHashGenerationProbe()
{
    const int width = 512;
    const int height = 512;
    const int iterations = 40;
    var luma = new float[width * height];
    var random = new Random(123456);
    for (var index = 0; index < luma.Length; index++)
        luma[index] = random.NextSingle() * 255f;

    _ = PdqHasher.ComputeHashVariantsHex(luma, width, height);
    ForceGc();
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
    var watch = Stopwatch.StartNew();
    string hash = string.Empty;
    for (var iteration = 0; iteration < iterations; iteration++)
        hash = PdqHasher.ComputeHashVariantsHex(luma, width, height);
    watch.Stop();
    var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
    var workingSetAfter = Process.GetCurrentProcess().WorkingSet64;

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        probe = "pdq_hash_generation",
        width,
        height,
        iterations,
        hashLength = hash.Length,
        totalMilliseconds = watch.Elapsed.TotalMilliseconds,
        millisecondsPerImage = watch.Elapsed.TotalMilliseconds / iterations,
        allocatedBytes = allocatedAfter - allocatedBefore,
        allocatedBytesPerImage = (allocatedAfter - allocatedBefore) / iterations,
        workingSetDeltaBytes = workingSetAfter - workingSetBefore,
    }));
}

internal sealed record ProbeQuery(
    string Name,
    string Sql,
    IReadOnlyDictionary<string, object?> Parameters)
{
    public ProbeQuery(string name, string sql)
        : this(name, sql, new Dictionary<string, object?>())
    {
    }
}

internal sealed record ProbeKnownRow(string PhotoPath, string WorldName, string Phash);

internal sealed record ProbePreparedKnownRow(
    ProbeKnownRow Row,
    PdqHasher.PackedHash256[] Variants);

internal sealed record ProbeMatch(string PhotoPath, int Distance);
