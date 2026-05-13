using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Models;
using Alpheratz.Shared.Models;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Core.Database;

// Tracing convention for AlpheratzDb (per project policy):
// Every public method emits enter/exit traces and wraps its body in
// try { ... } catch { log + rethrow }. Private static helpers are NOT
// individually instrumented because any failure inside them surfaces in
// the calling public method's "threw" trace with full stack info, and
// adding traces inside helpers like NullableString or BuildPhotoWhereClause
// would flood the log on every row mapping / filter build.

// ---------------------------------------------------------------------------
// Main database façade.
// ---------------------------------------------------------------------------
public sealed class AlpheratzDb
{
    // -----------------------------------------------------------------------
    // Connection helper – opens a fresh connection with WAL/NORMAL/FK on.
    // -----------------------------------------------------------------------
    private SqliteConnection OpenConnection()
    {
        var path = AppPaths.GetDbPath()
            ?? throw new InvalidOperationException("Alpheratz DB の保存先を取得できません");
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    // -----------------------------------------------------------------------
    // Schema bootstrap (call once at startup).
    // -----------------------------------------------------------------------
    public void Initialize()
    {
        AppLogger.Trace("AlpheratzDb.Initialize: enter");
        try
        {
            EnsureSchema();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.Initialize: threw: {ex}");
            throw;
        }
        AppLogger.Trace("AlpheratzDb.Initialize: exit");
    }

    private void EnsureSchema()
    {
        using var conn = OpenConnection();

        // --- create tables and indexes ----------------------------------------
        using var batch = conn.CreateCommand();
        batch.CommandText = @"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS photos (
    photo_path      TEXT PRIMARY KEY,
    photo_filename  TEXT NOT NULL,
    world_id        TEXT,
    world_name      TEXT,
    timestamp       TEXT NOT NULL,
    phash           TEXT,
    phash_version   INTEGER DEFAULT 0,
    orientation     TEXT,
    image_width     INTEGER,
    image_height    INTEGER,
    source_slot     INTEGER DEFAULT 1,
    is_favorite     INTEGER DEFAULT 0,
    match_source    TEXT,
    is_missing      INTEGER DEFAULT 0
);

CREATE TABLE IF NOT EXISTS tags (
    id    INTEGER PRIMARY KEY AUTOINCREMENT,
    name  TEXT NOT NULL UNIQUE
);

-- R2-A-4: 新規 DB では photo_tags の FK に ON DELETE CASCADE を付与する。
-- 既存 DB の photo_tags は SQLite の ALTER TABLE 制約により再作成しないと
-- CASCADE を後付けできないため、ResetPhotoCacheBySlotAsync 末尾で孤児削除する救済を併用する。
CREATE TABLE IF NOT EXISTS photo_tags (
    photo_path  TEXT REFERENCES photos(photo_path) ON DELETE CASCADE,
    tag_id      INTEGER REFERENCES tags(id) ON DELETE CASCADE,
    PRIMARY KEY (photo_path, tag_id)
);

CREATE TABLE IF NOT EXISTS archive_world_visits (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    source_log_name  TEXT NOT NULL,
    world_name       TEXT NOT NULL,
    join_time        TEXT NOT NULL,
    leave_time       TEXT
);

CREATE INDEX IF NOT EXISTS idx_photos_timestamp      ON photos(timestamp);
CREATE INDEX IF NOT EXISTS idx_photos_world_name     ON photos(world_name);
CREATE INDEX IF NOT EXISTS idx_photos_is_favorite    ON photos(is_favorite);
CREATE INDEX IF NOT EXISTS idx_photos_is_missing     ON photos(is_missing);
CREATE INDEX IF NOT EXISTS idx_archive_world_visits_join_time       ON archive_world_visits(join_time);
CREATE INDEX IF NOT EXISTS idx_archive_world_visits_source_log_name ON archive_world_visits(source_log_name);
";
        batch.ExecuteNonQuery();

        // --- incremental column migrations ------------------------------------
        AddColumnIfMissing(conn, "photos",        "ALTER TABLE photos ADD COLUMN orientation    TEXT",                "orientation");
        AddColumnIfMissing(conn, "photos",        "ALTER TABLE photos ADD COLUMN image_width    INTEGER",             "image_width");
        AddColumnIfMissing(conn, "photos",        "ALTER TABLE photos ADD COLUMN image_height   INTEGER",             "image_height");
        AddColumnIfMissing(conn, "photos",        "ALTER TABLE photos ADD COLUMN source_slot    INTEGER DEFAULT 1",   "source_slot");
        AddColumnIfMissing(conn, "photos",        "ALTER TABLE photos ADD COLUMN is_favorite    INTEGER DEFAULT 0",   "is_favorite");
        AddColumnIfMissing(conn, "photos",        "ALTER TABLE photos ADD COLUMN match_source   TEXT",                "match_source");
        AddColumnIfMissing(conn, "photos",        "ALTER TABLE photos ADD COLUMN is_missing     INTEGER DEFAULT 0",   "is_missing");
        AddColumnIfMissing(conn, "photos",        "ALTER TABLE photos ADD COLUMN phash_version  INTEGER DEFAULT 0",   "phash_version");

        // Drop legacy table if it somehow survived.
        using var dropCmd = conn.CreateCommand();
        dropCmd.CommandText = "DROP TABLE IF EXISTS photo_embeddings;";
        dropCmd.ExecuteNonQuery();
    }

    // -----------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------

    private static bool HasColumn(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // column 1 = name
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void AddColumnIfMissing(SqliteConnection conn, string table, string sql, string column)
    {
        if (HasColumn(conn, table, column)) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads one column of every row as <see cref="string"/> from the given
    /// prepared <see cref="SqliteCommand"/> and returns the result list.
    /// </summary>
    private static List<string> ReadStringColumn(SqliteCommand cmd)
    {
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(r.IsDBNull(0) ? "" : r.GetString(0));
        return list;
    }

    private static string? NullableString(SqliteDataReader r, int ordinal)
        => r.IsDBNull(ordinal) ? null : r.GetString(ordinal);

    private static long? NullableLong(SqliteDataReader r, int ordinal)
        => r.IsDBNull(ordinal) ? null : r.GetInt64(ordinal);

    // -----------------------------------------------------------------------
    // Tag helpers (used by multiple operations)
    // -----------------------------------------------------------------------

    private static List<string> GetPhotoTagsInternal(SqliteConnection conn, string photoPath)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT t.name
FROM photo_tags pt
INNER JOIN tags t ON t.id = pt.tag_id
WHERE pt.photo_path = @p
ORDER BY t.name COLLATE NOCASE ASC";
        cmd.Parameters.AddWithValue("@p", photoPath);
        return ReadStringColumn(cmd);
    }

    /// <summary>
    /// Loads tags for a batch of photo_paths in a single query.
    /// Returns a Dictionary keyed by photo_path.
    /// </summary>
    private static Dictionary<string, List<string>> GetTagsForPaths(SqliteConnection conn, IEnumerable<string> photoPaths)
    {
        // SQLite の既定パラメータ上限 (999) を超えないように 500 件ごとに分割実行する。
        const int ChunkSize = 500;
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var paths = new List<string>(photoPaths);
        if (paths.Count == 0) return result;

        for (var offset = 0; offset < paths.Count; offset += ChunkSize)
        {
            var count = Math.Min(ChunkSize, paths.Count - offset);
            var sb = new StringBuilder();
            sb.Append(@"
SELECT pt.photo_path, t.name
FROM photo_tags pt
INNER JOIN tags t ON t.id = pt.tag_id
WHERE pt.photo_path IN (");
            using var cmd = conn.CreateCommand();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                var pName = $"@pp{i}";
                sb.Append(pName);
                cmd.Parameters.AddWithValue(pName, paths[offset + i]);
            }
            sb.Append(") ORDER BY pt.photo_path, t.name COLLATE NOCASE ASC");
            cmd.CommandText = sb.ToString();

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var pp = r.GetString(0);
                var tag = r.GetString(1);
                if (!result.TryGetValue(pp, out var list))
                {
                    list = new List<string>();
                    result[pp] = list;
                }
                list.Add(tag);
            }
        }
        return result;
    }

    // -----------------------------------------------------------------------
    // Row mapper helpers
    // -----------------------------------------------------------------------

    private static PhotoRecordDto MapPhotoRow(SqliteDataReader r)
    {
        // Column order: 0=photo_filename, 1=photo_path, 2=world_id,
        //               3=world_name, 4=timestamp, 5=phash,
        //               6=orientation, 7=image_width, 8=image_height,
        //               9=source_slot, 10=is_favorite, 11=match_source,
        //               12=is_missing
        return new PhotoRecordDto
        {
            photo_filename   = r.GetString(0),
            photo_path       = r.GetString(1),
            world_id         = NullableString(r, 2),
            world_name       = NullableString(r, 3),
            timestamp        = r.GetString(4),
            phash            = NullableString(r, 5),
            orientation      = NullableString(r, 6),
            image_width      = NullableLong(r, 7),
            image_height     = NullableLong(r, 8),
            source_slot      = r.IsDBNull(9) ? 1L : r.GetInt64(9),
            is_favorite      = !r.IsDBNull(10) && r.GetInt64(10) != 0,
            match_source     = NullableString(r, 11),
            is_missing       = !r.IsDBNull(12) && r.GetInt64(12) != 0,
            tags             = [],
        };
    }

    // -----------------------------------------------------------------------
    // WHERE clause builder for photo filters (shared between COUNT and SELECT)
    // -----------------------------------------------------------------------

    private static string BuildPhotoWhereClause(
        PhotoQueryParams q,
        SqliteCommand cmd,
        string tableAlias = "photos")
    {
        var sb = new StringBuilder();
        sb.Append($" {tableAlias}.is_missing = 0");

        if (q.StartDate is not null)
        {
            sb.Append($" AND {tableAlias}.timestamp >= @startDate");
            cmd.Parameters.AddWithValue("@startDate", q.StartDate);
        }
        if (q.EndDate is not null)
        {
            sb.Append($" AND {tableAlias}.timestamp <= @endDate");
            cmd.Parameters.AddWithValue("@endDate", q.EndDate);
        }
        if (q.WorldQuery is not null)
        {
            sb.Append($" AND ({tableAlias}.world_name LIKE @worldQuery"
                + $" OR {tableAlias}.photo_filename LIKE @worldQuery)");
            cmd.Parameters.AddWithValue("@worldQuery", $"%{q.WorldQuery}%");
        }
        if (q.WorldExacts is { Count: > 0 } exacts)
        {
            if (exacts.Count == 1 && exacts[0] == "unknown")
            {
                sb.Append($" AND {tableAlias}.world_name IS NULL");
            }
            else
            {
                sb.Append($" AND (");
                bool first = true;
                bool hasUnknown = false;
                int wIdx = 0;
                foreach (var w in exacts)
                {
                    if (w == "unknown") { hasUnknown = true; continue; }
                    if (!first) sb.Append(" OR ");
                    var pn = $"@wex{wIdx}";
                    sb.Append($"{tableAlias}.world_name = {pn}");
                    cmd.Parameters.AddWithValue(pn, w);
                    first = false;
                    wIdx++;
                }
                if (hasUnknown)
                {
                    if (!first) sb.Append(" OR ");
                    sb.Append($"{tableAlias}.world_name IS NULL");
                }
                sb.Append(")");
            }
        }
        if (q.Orientation is not null)
        {
            sb.Append($" AND {tableAlias}.orientation = @orientation");
            cmd.Parameters.AddWithValue("@orientation", q.Orientation);
        }
        if (q.FavoritesOnly == true)
        {
            sb.Append($" AND {tableAlias}.is_favorite = 1");
        }
        if (q.SourceSlot is { } slot)
        {
            sb.Append($" AND {tableAlias}.source_slot = @sourceSlot");
            cmd.Parameters.AddWithValue("@sourceSlot", slot);
        }
        if (q.PhotoPathExact is not null)
        {
            sb.Append($" AND {tableAlias}.photo_path = @photoPathExact");
            cmd.Parameters.AddWithValue("@photoPathExact", q.PhotoPathExact);
        }
        if (q.TagFilters is { Count: > 0 } tags)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                var pn = $"@tag{i}";
                sb.Append($@" AND EXISTS (
SELECT 1
FROM photo_tags pt
INNER JOIN tags t ON t.id = pt.tag_id
WHERE pt.photo_path = {tableAlias}.photo_path
  AND t.name = {pn}
)");
                cmd.Parameters.AddWithValue(pn, tags[i]);
            }
        }

        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // 2. GetPhotosPageAsync
    // -----------------------------------------------------------------------
    public Task<PhotoPage> GetPhotosPageAsync(PhotoQueryParams q, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.GetPhotosPageAsync: enter limit={q.Limit} offset={q.Offset}");
        try
        {
        ct.ThrowIfCancellationRequested();

        using var conn = OpenConnection();

        // --- COUNT query --------------------------------------------------
        int total;
        {
            using var countCmd = conn.CreateCommand();
            var where = BuildPhotoWhereClause(q, countCmd);
            countCmd.CommandText = $"SELECT COUNT(*) FROM photos WHERE {where}";
            total = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        }

        if (total == 0)
            return Task.FromResult(new PhotoPage { Items = [], Total = 0 });

        // --- SELECT query -------------------------------------------------
        var phashCol = q.IncludePhash ? "phash" : "NULL";
        using var selCmd = conn.CreateCommand();
        var whereSelect = BuildPhotoWhereClause(q, selCmd);

        var sqlSb = new StringBuilder();
        var orderClause = q.Sort switch
        {
            // R2-A-26: world_name + timestamp が同値の場合に行順がブレるのを防ぐため
            // photo_path を最終タイブレイクとして追加し、安定ソートを保証する。
            SortMode.worldAsc => "world_name COLLATE NOCASE ASC, timestamp DESC, photo_path ASC",
            _ => "timestamp DESC, photo_path ASC",
        };
        sqlSb.Append($@"
SELECT photo_filename, photo_path, world_id, world_name, timestamp,
       {phashCol} AS phash,
       orientation, image_width, image_height, source_slot, is_favorite,
       match_source, is_missing
FROM photos
WHERE {whereSelect}
ORDER BY {orderClause}");

        if (q.Limit.HasValue)
        {
            sqlSb.Append(" LIMIT @limit");
            selCmd.Parameters.AddWithValue("@limit", q.Limit.Value);
        }
        if (q.Offset.HasValue)
        {
            sqlSb.Append(" OFFSET @offset");
            selCmd.Parameters.AddWithValue("@offset", q.Offset.Value);
        }

        selCmd.CommandText = sqlSb.ToString();

        var photoList = new List<PhotoRecordDto>();
        using (var r = selCmd.ExecuteReader())
        {
            while (r.Read())
                photoList.Add(MapPhotoRow(r));
        }

        // Batch-load tags for all returned photos
        if (photoList.Count > 0)
        {
            var paths = new List<string>(photoList.Count);
            foreach (var p in photoList) paths.Add(p.photo_path);
            var tagMap = GetTagsForPaths(conn, paths);
            for (int i = 0; i < photoList.Count; i++)
            {
                var p = photoList[i];
                if (tagMap.TryGetValue(p.photo_path, out var t))
                    photoList[i] = p with { tags = t };
            }
        }

        AppLogger.Trace($"AlpheratzDb.GetPhotosPageAsync: exit total={total} returned={photoList.Count}");
        return Task.FromResult(new PhotoPage { Items = photoList, Total = total });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetPhotosPageAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 2b. GetMonthSummaryAsync – month-level counts for MonthNav
    // -----------------------------------------------------------------------
    public Task<IReadOnlyList<MonthSummaryItem>> GetMonthSummaryAsync(
        PhotoQueryParams q, CancellationToken ct = default)
    {
        AppLogger.Trace("AlpheratzDb.GetMonthSummaryAsync: enter");
        try
        {
            ct.ThrowIfCancellationRequested();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            var where = BuildPhotoWhereClause(q, cmd);
            cmd.CommandText = $@"
SELECT substr(timestamp, 1, 4) AS y,
       substr(timestamp, 6, 2) AS m,
       COUNT(*)                AS cnt
FROM photos
WHERE {where}
GROUP BY y, m
ORDER BY y DESC, m DESC";

            var list = new List<MonthSummaryItem>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var yearStr = r.IsDBNull(0) ? "0" : r.GetString(0);
                var monthStr = r.IsDBNull(1) ? "1" : r.GetString(1);
                int.TryParse(yearStr, out var year);
                int.TryParse(monthStr, out var month);
                var count = r.GetInt32(2);
                list.Add(new MonthSummaryItem(year, month, count));
            }

            AppLogger.Trace($"AlpheratzDb.GetMonthSummaryAsync: exit groups={list.Count}");
            return Task.FromResult<IReadOnlyList<MonthSummaryItem>>(list);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetMonthSummaryAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 3. GetPhotoRecordAsync
    // -----------------------------------------------------------------------
    public Task<PhotoRecordDto?> GetPhotoRecordAsync(
        string photoPath, bool includePhash = false, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.GetPhotoRecordAsync: enter path={photoPath} includePhash={includePhash}");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            var phashCol = includePhash ? "phash" : "NULL";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT photo_filename, photo_path, world_id, world_name, timestamp,
       {phashCol} AS phash,
       orientation, image_width, image_height, source_slot, is_favorite,
       match_source, is_missing
FROM photos
WHERE photo_path = @p";
            cmd.Parameters.AddWithValue("@p", photoPath);

            PhotoRecordDto? dto = null;
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                    dto = MapPhotoRow(r);
            }

            if (dto is null)
            {
                AppLogger.Trace("AlpheratzDb.GetPhotoRecordAsync: exit (not found)");
                return Task.FromResult<PhotoRecordDto?>(null);
            }

            var tags = GetPhotoTagsInternal(conn, photoPath);
            dto = dto with { tags = tags };
            AppLogger.Trace("AlpheratzDb.GetPhotoRecordAsync: exit");
            return Task.FromResult<PhotoRecordDto?>(dto);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetPhotoRecordAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 4. GetPhotoTagsAsync
    // -----------------------------------------------------------------------
    public Task<IReadOnlyList<string>> GetPhotoTagsAsync(
        string photoPath, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.GetPhotoTagsAsync: enter path={photoPath}");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            var tags = GetPhotoTagsInternal(conn, photoPath);
            AppLogger.Trace($"AlpheratzDb.GetPhotoTagsAsync: exit count={tags.Count}");
            return Task.FromResult<IReadOnlyList<string>>(tags);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetPhotoTagsAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 7. SetPhotoFavoriteAsync
    // -----------------------------------------------------------------------
    public Task SetPhotoFavoriteAsync(string photoPath, bool isFavorite, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.SetPhotoFavoriteAsync: enter path={photoPath} isFavorite={isFavorite}");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE photos SET is_favorite = @fav WHERE photo_path = @p";
            cmd.Parameters.AddWithValue("@fav", isFavorite ? 1L : 0L);
            cmd.Parameters.AddWithValue("@p", photoPath);
            var changed = cmd.ExecuteNonQuery();
            if (changed == 0)
                AppLogger.Warn($"SetPhotoFavorite: 写真が見つかりません: {photoPath}");
            AppLogger.Trace("AlpheratzDb.SetPhotoFavoriteAsync: exit");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.SetPhotoFavoriteAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 8. AddPhotoTagAsync
    // -----------------------------------------------------------------------
    public Task AddPhotoTagAsync(string photoPath, string tag, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.AddPhotoTagAsync: enter path={photoPath} tag={tag}");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();

            using var insertTag = conn.CreateCommand();
            insertTag.Transaction = tx;
            insertTag.CommandText = "INSERT INTO tags (name) VALUES (@tag) ON CONFLICT(name) DO NOTHING";
            insertTag.Parameters.AddWithValue("@tag", tag);
            insertTag.ExecuteNonQuery();

            using var linkTag = conn.CreateCommand();
            linkTag.Transaction = tx;
            linkTag.CommandText = @"
INSERT INTO photo_tags (photo_path, tag_id)
SELECT @p, id FROM tags WHERE name = @tag
ON CONFLICT(photo_path, tag_id) DO NOTHING";
            linkTag.Parameters.AddWithValue("@p", photoPath);
            linkTag.Parameters.AddWithValue("@tag", tag);
            linkTag.ExecuteNonQuery();

            tx.Commit();
            AppLogger.Trace("AlpheratzDb.AddPhotoTagAsync: exit");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.AddPhotoTagAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 9. RemovePhotoTagAsync
    // -----------------------------------------------------------------------
    public Task RemovePhotoTagAsync(string photoPath, string tag, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.RemovePhotoTagAsync: enter path={photoPath} tag={tag}");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
DELETE FROM photo_tags
WHERE photo_path = @p
  AND tag_id IN (SELECT id FROM tags WHERE name = @tag)";
            cmd.Parameters.AddWithValue("@p", photoPath);
            cmd.Parameters.AddWithValue("@tag", tag);
            cmd.ExecuteNonQuery();
            AppLogger.Trace("AlpheratzDb.RemovePhotoTagAsync: exit");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.RemovePhotoTagAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 10. GetAllTagsAsync
    // -----------------------------------------------------------------------
    public Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("AlpheratzDb.GetAllTagsAsync: enter");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tags = new List<string>();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM tags ORDER BY name COLLATE NOCASE ASC";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var name = r.GetString(0);
                    if (seen.Add(name))
                        tags.Add(name);
                }
            }

            // Rust does a final sort as well
            tags.Sort(StringComparer.OrdinalIgnoreCase);
            AppLogger.Trace($"AlpheratzDb.GetAllTagsAsync: exit count={tags.Count}");
            return Task.FromResult<IReadOnlyList<string>>(tags);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetAllTagsAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 11. CreateTagMasterAsync
    // -----------------------------------------------------------------------
    public Task CreateTagMasterAsync(string tag, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.CreateTagMasterAsync: enter tag={tag}");
        try
        {
            ct.ThrowIfCancellationRequested();

            var normalized = tag.Trim();
            if (normalized.Length == 0)
                throw new ArgumentException("タグ名が空です", nameof(tag));

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO tags (name) VALUES (@tag) ON CONFLICT(name) DO NOTHING";
            cmd.Parameters.AddWithValue("@tag", normalized);
            cmd.ExecuteNonQuery();
            AppLogger.Trace("AlpheratzDb.CreateTagMasterAsync: exit");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.CreateTagMasterAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 12. DeleteTagMasterAsync
    // -----------------------------------------------------------------------
    public Task DeleteTagMasterAsync(string tag, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.DeleteTagMasterAsync: enter tag={tag}");
        try
        {
            ct.ThrowIfCancellationRequested();

            var normalized = tag.Trim();
            if (normalized.Length == 0)
                throw new ArgumentException("タグ名が空です", nameof(tag));

            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();

            using var delLinks = conn.CreateCommand();
            delLinks.Transaction = tx;
            delLinks.CommandText = "DELETE FROM photo_tags WHERE tag_id IN (SELECT id FROM tags WHERE name = @tag)";
            delLinks.Parameters.AddWithValue("@tag", normalized);
            delLinks.ExecuteNonQuery();

            using var delTag = conn.CreateCommand();
            delTag.Transaction = tx;
            delTag.CommandText = "DELETE FROM tags WHERE name = @tag";
            delTag.Parameters.AddWithValue("@tag", normalized);
            delTag.ExecuteNonQuery();

            tx.Commit();
            AppLogger.Trace("AlpheratzDb.DeleteTagMasterAsync: exit");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.DeleteTagMasterAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 16b. ResetPhotoCacheBySlotAsync
    // DELETE photos/photo_tags for a specific source_slot only.
    // -----------------------------------------------------------------------
    public Task ResetPhotoCacheBySlotAsync(long slot, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.ResetPhotoCacheBySlotAsync: enter slot={slot}");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            using (var tx = conn.BeginTransaction())
            {
                using var del1 = conn.CreateCommand();
                del1.Transaction = tx;
                del1.CommandText = "DELETE FROM photo_tags WHERE photo_path IN (SELECT photo_path FROM photos WHERE source_slot = @slot)";
                del1.Parameters.AddWithValue("@slot", slot);
                del1.ExecuteNonQuery();

                using var del2 = conn.CreateCommand();
                del2.Transaction = tx;
                del2.CommandText = "DELETE FROM photos WHERE source_slot = @slot";
                del2.Parameters.AddWithValue("@slot", slot);
                del2.ExecuteNonQuery();

                // R2-A-4: 既存 DB は photo_tags の FK に ON DELETE CASCADE が付いていない
                // 可能性があり、過去のバグや手動 DELETE FROM photos で孤児行が残る場合が
                // あるため、ここで孤児 photo_tags を最終的に掃除する。
                using var orphan = conn.CreateCommand();
                orphan.Transaction = tx;
                orphan.CommandText = "DELETE FROM photo_tags WHERE photo_path NOT IN (SELECT photo_path FROM photos)";
                orphan.ExecuteNonQuery();

                tx.Commit();
            }

            var imgDir = AppPaths.GetImgCacheDir(slot);
            if (imgDir is not null)
            {
                try { ClearDirectoryContents(imgDir); }
                catch (Exception ex)
                {
                    AppLogger.Warn($"imgCache のリセットに失敗しました [{imgDir}]: {ex.Message}");
                }
            }

            AppLogger.Trace($"AlpheratzDb.ResetPhotoCacheBySlotAsync: exit slot={slot}");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.ResetPhotoCacheBySlotAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 18. UpsertPhotoAsync (scanner)
    // Mirrors upsert_photo_batch in scanner.rs – uses COALESCE to preserve
    // existing world_id/world_name/orientation/match_source unless the new
    // value is non-null.
    // -----------------------------------------------------------------------
    public Task UpsertPhotoAsync(PhotoUpsertData data, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.UpsertPhotoAsync: enter path={data.PhotoPath}");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO photos (
    photo_path, photo_filename, world_id, world_name, timestamp,
    orientation, image_width, image_height, source_slot, match_source, is_missing
) VALUES (
    @photo_path, @photo_filename, @world_id, @world_name, @timestamp,
    @orientation, @image_width, @image_height, @source_slot, @match_source, 0
)
ON CONFLICT(photo_path) DO UPDATE SET
    photo_filename = excluded.photo_filename,
    world_id       = COALESCE(excluded.world_id,      photos.world_id),
    world_name     = COALESCE(excluded.world_name,    photos.world_name),
    timestamp      = excluded.timestamp,
    orientation    = COALESCE(excluded.orientation,   photos.orientation),
    image_width    = COALESCE(excluded.image_width,   photos.image_width),
    image_height   = COALESCE(excluded.image_height,  photos.image_height),
    source_slot    = excluded.source_slot,
    match_source   = COALESCE(excluded.match_source,  photos.match_source),
    is_missing     = 0";

            cmd.Parameters.AddWithValue("@photo_path",     data.PhotoPath);
            cmd.Parameters.AddWithValue("@photo_filename",  data.PhotoFilename);
            cmd.Parameters.AddWithValue("@world_id",        (object?)data.WorldId   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@world_name",      (object?)data.WorldName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@timestamp",       data.Timestamp);
            cmd.Parameters.AddWithValue("@orientation",     (object?)data.Orientation ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@image_width",     (object?)data.ImageWidth  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@image_height",    (object?)data.ImageHeight ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source_slot",     data.SourceSlot);
            cmd.Parameters.AddWithValue("@match_source",    (object?)data.MatchSource ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            AppLogger.Trace("AlpheratzDb.UpsertPhotoAsync: exit");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.UpsertPhotoAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 19. DeleteMissingPhotosAsync
    // Deletes photos from DB whose paths are NOT in foundPaths (i.e. the
    // file no longer exists on disk). Also removes their photo_tags rows.
    // -----------------------------------------------------------------------
    public Task<int> DeleteMissingPhotosAsync(
        IEnumerable<string> foundPaths, CancellationToken ct = default)
    {
        AppLogger.Trace("AlpheratzDb.DeleteMissingPhotosAsync: enter");
        try
        {
            ct.ThrowIfCancellationRequested();

            var foundSet = new HashSet<string>(foundPaths, StringComparer.OrdinalIgnoreCase);

            using var conn = OpenConnection();

            var allPaths = new List<string>();
            using (var selCmd = conn.CreateCommand())
            {
                selCmd.CommandText = "SELECT photo_path FROM photos";
                using var r = selCmd.ExecuteReader();
                var rowIdx = 0;
                while (r.Read())
                {
                    if ((rowIdx++ & 0x3FF) == 0)
                        ct.ThrowIfCancellationRequested();
                    allPaths.Add(r.GetString(0));
                }
            }

            var missing = new List<string>();
            foreach (var p in allPaths)
            {
                if (!foundSet.Contains(p))
                    missing.Add(p);
            }

            if (missing.Count == 0)
            {
                AppLogger.Trace("AlpheratzDb.DeleteMissingPhotosAsync: exit (no missing)");
                return Task.FromResult(0);
            }

            using var tx = conn.BeginTransaction();

            BulkDeleteByPaths(conn, tx, missing, "DELETE FROM photo_tags", ct);
            BulkDeleteByPaths(conn, tx, missing, "DELETE FROM photos", ct);

            tx.Commit();

            AppLogger.Info($"AlpheratzDb.DeleteMissingPhotosAsync: deleted {missing.Count} photo(s)");
            return Task.FromResult(missing.Count);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.DeleteMissingPhotosAsync: threw: {ex}");
            throw;
        }
    }

    private static void BulkDeleteByPaths(
        SqliteConnection conn, SqliteTransaction tx,
        List<string> paths, string deletePrefix, CancellationToken ct)
    {
        const int ChunkSize = 500;
        for (int i = 0; i < paths.Count; i += ChunkSize)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = paths.GetRange(i, Math.Min(ChunkSize, paths.Count - i));
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            var paramNames = new string[chunk.Count];
            for (int j = 0; j < chunk.Count; j++)
            {
                paramNames[j] = $"@p{j}";
                cmd.Parameters.AddWithValue(paramNames[j], chunk[j]);
            }
            cmd.CommandText = $"{deletePrefix} WHERE photo_path IN ({string.Join(',', paramNames)})";
            cmd.ExecuteNonQuery();
        }
    }

    // -----------------------------------------------------------------------
    // 20. GetExistingPhotosAsync (scanner)
    // -----------------------------------------------------------------------
    public Task<IDictionary<string, ExistingPhotoInfo>> GetExistingPhotosAsync(
        CancellationToken ct = default)
    {
        AppLogger.Trace("AlpheratzDb.GetExistingPhotosAsync: enter");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT photo_filename, photo_path, world_id, world_name, timestamp,
       match_source, orientation, image_width, image_height, source_slot, is_missing
FROM photos";

            var map = new Dictionary<string, ExistingPhotoInfo>(StringComparer.Ordinal);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var info = new ExistingPhotoInfo
                {
                    PhotoFilename = r.GetString(0),
                    PhotoPath     = r.GetString(1),
                    WorldId       = NullableString(r, 2),
                    WorldName     = NullableString(r, 3),
                    // col 4 = timestamp (not in ExistingPhotoInfo, skipped)
                    MatchSource   = NullableString(r, 5),
                    Orientation   = NullableString(r, 6),
                    ImageWidth    = NullableLong(r, 7),
                    ImageHeight   = NullableLong(r, 8),
                    SourceSlot    = r.IsDBNull(9) ? 1L : r.GetInt64(9),
                    IsMissing     = !r.IsDBNull(10) && r.GetInt64(10) != 0,
                };
                map[info.PhotoPath] = info;
            }

            AppLogger.Trace($"AlpheratzDb.GetExistingPhotosAsync: exit count={map.Count}");
            return Task.FromResult<IDictionary<string, ExistingPhotoInfo>>(map);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetExistingPhotosAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 21. UpdatePhotoWorldNameAsync
    // -----------------------------------------------------------------------
    public Task UpdatePhotoWorldNameAsync(
        string photoPath, string worldName, string matchSource, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.UpdatePhotoWorldNameAsync: enter path={photoPath} world={worldName}");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE photos
SET world_name = @worldName,
    match_source = @matchSource
WHERE photo_path = @p";
            cmd.Parameters.AddWithValue("@worldName", worldName);
            cmd.Parameters.AddWithValue("@matchSource", matchSource);
            cmd.Parameters.AddWithValue("@p", photoPath);
            cmd.ExecuteNonQuery();
            AppLogger.Trace("AlpheratzDb.UpdatePhotoWorldNameAsync: exit");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.UpdatePhotoWorldNameAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 22. GetUnknownWorldPhotosAsync
    // target: "all" | "primary" | "secondary"
    // -----------------------------------------------------------------------
    public Task<IReadOnlyList<(string photoPath, string timestamp)>> GetUnknownWorldPhotosAsync(
        string target, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.GetUnknownWorldPhotosAsync: enter target={target}");
        try
        {
            ct.ThrowIfCancellationRequested();

            // R2-A-27: 旧実装は target に応じて ORDER BY だけ切り替え、WHERE で source_slot を
            //          絞っていなかったため "primary"/"secondary" でも全 source_slot の写真が
            //          返っていた。期待動作に合わせて WHERE で対象 slot を絞り込む。
            var (slotFilter, orderBy) = target switch
            {
                "primary"   => (" AND COALESCE(source_slot, 1) = 1", "timestamp"),
                "secondary" => (" AND COALESCE(source_slot, 1) = 2", "timestamp"),
                _           => ("", "timestamp"),
            };

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT photo_path, timestamp
FROM photos
WHERE is_missing = 0
  AND world_name IS NULL
  AND world_id IS NULL{slotFilter}
ORDER BY {orderBy}";

            var list = new List<(string, string)>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add((r.GetString(0), r.GetString(1)));

            AppLogger.Trace($"AlpheratzDb.GetUnknownWorldPhotosAsync: exit count={list.Count}");
            return Task.FromResult<IReadOnlyList<(string, string)>>(list);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetUnknownWorldPhotosAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 23. UpsertArchiveWorldVisitsAsync
    // DELETE all existing rows, then INSERT the given visits.
    // -----------------------------------------------------------------------
    public Task UpsertArchiveWorldVisitsAsync(
        IEnumerable<ArchiveWorldVisitData> visits, CancellationToken ct = default)
    {
        AppLogger.Trace("AlpheratzDb.UpsertArchiveWorldVisitsAsync: enter");
        try
        {
            ct.ThrowIfCancellationRequested();

            var visitList = new List<ArchiveWorldVisitData>(visits);

            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();

            using var delCmd = conn.CreateCommand();
            delCmd.Transaction = tx;
            delCmd.CommandText = "DELETE FROM archive_world_visits";
            delCmd.ExecuteNonQuery();

            using var insCmd = conn.CreateCommand();
            insCmd.Transaction = tx;
            insCmd.CommandText = @"
INSERT INTO archive_world_visits (source_log_name, world_name, join_time, leave_time)
VALUES (@source_log_name, @world_name, @join_time, @leave_time)";
            insCmd.Parameters.Add("@source_log_name", SqliteType.Text);
            insCmd.Parameters.Add("@world_name",       SqliteType.Text);
            insCmd.Parameters.Add("@join_time",        SqliteType.Text);
            insCmd.Parameters.Add("@leave_time",       SqliteType.Text);

            foreach (var v in visitList)
            {
                insCmd.Parameters["@source_log_name"].Value = v.SourceLogName;
                insCmd.Parameters["@world_name"].Value       = v.WorldName;
                insCmd.Parameters["@join_time"].Value        = v.JoinTime;
                insCmd.Parameters["@leave_time"].Value       = (object?)v.LeaveTime ?? DBNull.Value;
                insCmd.ExecuteNonQuery();
            }

            tx.Commit();
            AppLogger.Trace($"AlpheratzDb.UpsertArchiveWorldVisitsAsync: exit count={visitList.Count}");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.UpsertArchiveWorldVisitsAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 24. LookupWorldNameFromArchiveAsync
    // -----------------------------------------------------------------------
    public Task<string?> LookupWorldNameFromArchiveAsync(
        string timestamp, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.LookupWorldNameFromArchiveAsync: enter ts={timestamp}");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT world_name
FROM archive_world_visits
WHERE join_time <= @ts
  AND (leave_time IS NULL OR leave_time >= @ts)
ORDER BY join_time DESC
LIMIT 1";
            cmd.Parameters.AddWithValue("@ts", timestamp);
            var result = cmd.ExecuteScalar();
            AppLogger.Trace("AlpheratzDb.LookupWorldNameFromArchiveAsync: exit");
            return Task.FromResult(result is string s ? s : null);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.LookupWorldNameFromArchiveAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 25. ApplyWorldMatchFromPhotoAsync
    // Copies world_id, world_name, match_source from source to target.
    // -----------------------------------------------------------------------
    public Task ApplyWorldMatchFromPhotoAsync(
        string targetPhotoPath, string sourcePhotoPath, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.ApplyWorldMatchFromPhotoAsync: enter target={targetPhotoPath} source={sourcePhotoPath}");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE photos
SET world_id     = (SELECT world_id     FROM photos WHERE photo_path = @src),
    world_name   = (SELECT world_name   FROM photos WHERE photo_path = @src),
    match_source = (SELECT match_source FROM photos WHERE photo_path = @src)
WHERE photo_path = @tgt";
            cmd.Parameters.AddWithValue("@src", sourcePhotoPath);
            cmd.Parameters.AddWithValue("@tgt", targetPhotoPath);
            cmd.ExecuteNonQuery();
            AppLogger.Trace("AlpheratzDb.ApplyWorldMatchFromPhotoAsync: exit");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.ApplyWorldMatchFromPhotoAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 26. GetWorldFilterOptionsAsync
    // Distinct world names with photo count, ordered by count DESC, then name.
    // NULL world_name is included as-is (shown as unknown in UI).
    // -----------------------------------------------------------------------
    public Task<IReadOnlyList<WorldFilterOptionDto>> GetWorldFilterOptionsAsync(
        CancellationToken ct = default)
    {
        AppLogger.Trace("AlpheratzDb.GetWorldFilterOptionsAsync: enter");
        try
        {
            ct.ThrowIfCancellationRequested();

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT world_name, COUNT(*) AS cnt
FROM photos
WHERE is_missing = 0
GROUP BY world_name
ORDER BY cnt DESC, world_name COLLATE NOCASE ASC";

            var list = new List<WorldFilterOptionDto>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new WorldFilterOptionDto
                {
                    world_name = NullableString(r, 0),
                    count      = r.GetInt64(1),
                });
            }
            AppLogger.Trace($"AlpheratzDb.GetWorldFilterOptionsAsync: exit count={list.Count}");
            return Task.FromResult<IReadOnlyList<WorldFilterOptionDto>>(list);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetWorldFilterOptionsAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // Similar-photo / world inference helpers
    // -----------------------------------------------------------------------

    public sealed record KnownWorldRow(string PhotoPath, string PhotoFilename, string WorldName, string? WorldId, string Phash, long SourceSlot);
    public sealed record UnknownPhashRow(string PhotoPath, string PhotoFilename, string Phash, long SourceSlot);

    public Task<IReadOnlyList<KnownWorldRow>> GetKnownWorldPhotosAsync(long sourceSlot, string? excludePhotoPath, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.GetKnownWorldPhotosAsync: enter slot={sourceSlot}");
        try
        {
            ct.ThrowIfCancellationRequested();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT photo_path, photo_filename, world_name, world_id, phash, source_slot FROM photos
                                 WHERE is_missing = 0
                                   AND source_slot = @source_slot
                                   AND world_name IS NOT NULL AND TRIM(world_name) <> ''
                                   AND phash IS NOT NULL AND phash <> ''
                                   AND (@exclude IS NULL OR photo_path <> @exclude)";
            cmd.Parameters.AddWithValue("@source_slot", sourceSlot);
            cmd.Parameters.AddWithValue("@exclude", (object?)excludePhotoPath ?? DBNull.Value);

            var rows = new List<KnownWorldRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new KnownWorldRow(
                    r.GetString(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.GetString(4),
                    r.GetInt64(5)));
            }
            AppLogger.Trace($"AlpheratzDb.GetKnownWorldPhotosAsync: exit count={rows.Count}");
            return Task.FromResult<IReadOnlyList<KnownWorldRow>>(rows);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetKnownWorldPhotosAsync: threw: {ex}");
            throw;
        }
    }

    public Task<IReadOnlyList<UnknownPhashRow>> GetUnknownWorldPhotosWithPhashAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("AlpheratzDb.GetUnknownWorldPhotosWithPhashAsync: enter");
        try
        {
            ct.ThrowIfCancellationRequested();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT photo_path, photo_filename, phash, source_slot FROM photos
                                 WHERE is_missing = 0
                                   AND world_name IS NULL
                                   AND world_id IS NULL
                                   AND phash IS NOT NULL AND phash <> ''";
            var rows = new List<UnknownPhashRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new UnknownPhashRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3)));
            }
            AppLogger.Trace($"AlpheratzDb.GetUnknownWorldPhotosWithPhashAsync: exit count={rows.Count}");
            return Task.FromResult<IReadOnlyList<UnknownPhashRow>>(rows);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetUnknownWorldPhotosWithPhashAsync: threw: {ex}");
            throw;
        }
    }

    public Task UpdatePhotoWorldAsync(string photoPath, string worldName, string? worldId, string matchSource, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.UpdatePhotoWorldAsync: enter path={photoPath} world={worldName}");
        try
        {
            ct.ThrowIfCancellationRequested();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE photos
                                 SET world_name = @world_name,
                                     world_id = COALESCE(@world_id, world_id),
                                     match_source = @match_source
                                 WHERE photo_path = @photo_path";
            cmd.Parameters.AddWithValue("@world_name", worldName);
            cmd.Parameters.AddWithValue("@world_id", (object?)worldId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@match_source", matchSource);
            cmd.Parameters.AddWithValue("@photo_path", photoPath);
            cmd.ExecuteNonQuery();
            AppLogger.Trace("AlpheratzDb.UpdatePhotoWorldAsync: exit");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.UpdatePhotoWorldAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 18. Phash pending queries + batch update
    // -----------------------------------------------------------------------
    public Task<int> GetPendingPhashCountAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("AlpheratzDb.GetPendingPhashCountAsync: enter");
        try
        {
            ct.ThrowIfCancellationRequested();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(*) FROM photos
                                 WHERE is_missing = 0
                                   AND (phash IS NULL OR phash = '')";
            var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0L);
            AppLogger.Trace($"AlpheratzDb.GetPendingPhashCountAsync: exit count={count}");
            return Task.FromResult(count);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetPendingPhashCountAsync: threw: {ex}");
            throw;
        }
    }

    public Task<IReadOnlyList<PendingPhashItem>> GetPendingPhashBatchAsync(int limit, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.GetPendingPhashBatchAsync: enter limit={limit}");
        try
        {
            ct.ThrowIfCancellationRequested();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT source_slot, photo_filename, photo_path FROM photos
                                 WHERE is_missing = 0
                                   AND (phash IS NULL OR phash = '')
                                 ORDER BY timestamp DESC
                                 LIMIT @limit";
            cmd.Parameters.AddWithValue("@limit", limit);

            var items = new List<PendingPhashItem>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                items.Add(new PendingPhashItem(r.GetInt64(0), r.GetString(1), r.GetString(2)));
            }
            AppLogger.Trace($"AlpheratzDb.GetPendingPhashBatchAsync: exit count={items.Count}");
            return Task.FromResult<IReadOnlyList<PendingPhashItem>>(items);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetPendingPhashBatchAsync: threw: {ex}");
            throw;
        }
    }

    public Task UpdatePhotoPhashAsync(string photoPath, string phashHex, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.UpdatePhotoPhashAsync: enter path={photoPath}");
        try
        {
            ct.ThrowIfCancellationRequested();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE photos SET phash = @phash WHERE photo_path = @photo_path";
            cmd.Parameters.AddWithValue("@phash", phashHex);
            cmd.Parameters.AddWithValue("@photo_path", photoPath);
            cmd.ExecuteNonQuery();
            AppLogger.Trace("AlpheratzDb.UpdatePhotoPhashAsync: exit");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.UpdatePhotoPhashAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // 17. Orientation pending queries + batch update
    // -----------------------------------------------------------------------
    public Task<int> GetPendingOrientationCountAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("AlpheratzDb.GetPendingOrientationCountAsync: enter");
        try
        {
            ct.ThrowIfCancellationRequested();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(*) FROM photos
                                 WHERE is_missing = 0
                                   AND (orientation IS NULL OR orientation = '' OR orientation = 'unknown'
                                        OR image_width IS NULL OR image_height IS NULL)";
            var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0L);
            AppLogger.Trace($"AlpheratzDb.GetPendingOrientationCountAsync: exit count={count}");
            return Task.FromResult(count);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetPendingOrientationCountAsync: threw: {ex}");
            throw;
        }
    }

    public Task<IReadOnlyList<PendingOrientationItem>> GetPendingOrientationBatchAsync(int limit, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.GetPendingOrientationBatchAsync: enter limit={limit}");
        try
        {
            ct.ThrowIfCancellationRequested();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT source_slot, photo_filename, photo_path FROM photos
                                 WHERE is_missing = 0
                                   AND (orientation IS NULL OR orientation = '' OR orientation = 'unknown'
                                        OR image_width IS NULL OR image_height IS NULL)
                                 ORDER BY timestamp DESC
                                 LIMIT @limit";
            cmd.Parameters.AddWithValue("@limit", limit);

            var items = new List<PendingOrientationItem>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                items.Add(new PendingOrientationItem(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetString(2)));
            }
            AppLogger.Trace($"AlpheratzDb.GetPendingOrientationBatchAsync: exit count={items.Count}");
            return Task.FromResult<IReadOnlyList<PendingOrientationItem>>(items);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.GetPendingOrientationBatchAsync: threw: {ex}");
            throw;
        }
    }

    public Task UpdatePhotoOrientationAndDimensionsAsync(string photoPath, string? orientation, long? width, long? height, CancellationToken ct = default)
    {
        AppLogger.Trace($"AlpheratzDb.UpdatePhotoOrientationAndDimensionsAsync: enter path={photoPath}");
        try
        {
            ct.ThrowIfCancellationRequested();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE photos
                                 SET orientation = @orientation,
                                     image_width = @image_width,
                                     image_height = @image_height
                                 WHERE photo_path = @photo_path";
            cmd.Parameters.AddWithValue("@orientation", (object?)orientation ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@image_width", (object?)width ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@image_height", (object?)height ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@photo_path", photoPath);
            cmd.ExecuteNonQuery();
            AppLogger.Trace("AlpheratzDb.UpdatePhotoOrientationAndDimensionsAsync: exit");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"AlpheratzDb.UpdatePhotoOrientationAndDimensionsAsync: threw: {ex}");
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // Private filesystem helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Deletes all files and subdirectories inside <paramref name="dir"/>
    /// without removing the directory itself (mirrors Rust clear_directory_contents).
    /// </summary>
    private static void ClearDirectoryContents(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir))
        {
            try { File.Delete(file); }
            catch (Exception ex)
            {
                AppLogger.Warn($"ファイルを削除できません [{file}]: {ex.Message}");
            }
        }
        foreach (var sub in Directory.GetDirectories(dir))
        {
            try { Directory.Delete(sub, recursive: true); }
            catch (Exception ex)
            {
                AppLogger.Warn($"フォルダを削除できません [{sub}]: {ex.Message}");
            }
        }
    }
}