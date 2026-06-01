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

public sealed class AlpheratzDb
{
	public sealed record KnownWorldRow(string PhotoPath, string PhotoFilename, string WorldName, string? WorldId, string Phash, long SourceSlot);

	public sealed record UnknownPhashRow(string PhotoPath, string PhotoFilename, string Phash, long SourceSlot);

	private SqliteConnection OpenConnection()
	{
		string text = AppPaths.GetDbPath() ?? throw new InvalidOperationException("Alpheratz DB の保存先を取得できません");
		SqliteConnection sqliteConnection = new SqliteConnection("Data Source=" + text);
		sqliteConnection.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
		sqliteCommand.ExecuteNonQuery();
		return sqliteConnection;
	}

	public void Initialize()
	{
		try
		{
			EnsureSchema();
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.Initialize: threw: {value}");
			throw;
		}
	}

	private void EnsureSchema()
	{
		using SqliteConnection sqliteConnection = OpenConnection();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "\nPRAGMA journal_mode = WAL;\nPRAGMA synchronous = NORMAL;\nPRAGMA foreign_keys = ON;\n\nCREATE TABLE IF NOT EXISTS photos (\n    photo_path      TEXT PRIMARY KEY,\n    photo_filename  TEXT NOT NULL,\n    world_id        TEXT,\n    world_name      TEXT,\n    timestamp       TEXT NOT NULL,\n    phash           TEXT,\n    phash_version   INTEGER DEFAULT 0,\n    orientation     TEXT,\n    image_width     INTEGER,\n    image_height    INTEGER,\n    source_slot     INTEGER DEFAULT 1,\n    is_favorite     INTEGER DEFAULT 0,\n    match_source    TEXT,\n    is_missing      INTEGER DEFAULT 0\n);\n\nCREATE TABLE IF NOT EXISTS tags (\n    id    INTEGER PRIMARY KEY AUTOINCREMENT,\n    name  TEXT NOT NULL UNIQUE\n);\n\n-- R2-A-4: 新規 DB では photo_tags の FK に ON DELETE CASCADE を付与する。\n-- 既存 DB の photo_tags は SQLite の ALTER TABLE 制約により再作成しないと\n-- CASCADE を後付けできないため、ResetPhotoCacheBySlotAsync 末尾で孤児削除する救済を併用する。\nCREATE TABLE IF NOT EXISTS photo_tags (\n    photo_path  TEXT REFERENCES photos(photo_path) ON DELETE CASCADE,\n    tag_id      INTEGER REFERENCES tags(id) ON DELETE CASCADE,\n    PRIMARY KEY (photo_path, tag_id)\n);\n\nCREATE TABLE IF NOT EXISTS archive_world_visits (\n    id               INTEGER PRIMARY KEY AUTOINCREMENT,\n    source_log_name  TEXT NOT NULL,\n    world_name       TEXT NOT NULL,\n    join_time        TEXT NOT NULL,\n    leave_time       TEXT\n);\n\nCREATE INDEX IF NOT EXISTS idx_photos_timestamp      ON photos(timestamp);\nCREATE INDEX IF NOT EXISTS idx_photos_world_name     ON photos(world_name);\nCREATE INDEX IF NOT EXISTS idx_photos_is_favorite    ON photos(is_favorite);\nCREATE INDEX IF NOT EXISTS idx_photos_is_missing     ON photos(is_missing);\nCREATE INDEX IF NOT EXISTS idx_archive_world_visits_join_time       ON archive_world_visits(join_time);\nCREATE INDEX IF NOT EXISTS idx_archive_world_visits_source_log_name ON archive_world_visits(source_log_name);\n";
		sqliteCommand.ExecuteNonQuery();
		AddColumnIfMissing(sqliteConnection, "photos", "ALTER TABLE photos ADD COLUMN orientation    TEXT", "orientation");
		AddColumnIfMissing(sqliteConnection, "photos", "ALTER TABLE photos ADD COLUMN image_width    INTEGER", "image_width");
		AddColumnIfMissing(sqliteConnection, "photos", "ALTER TABLE photos ADD COLUMN image_height   INTEGER", "image_height");
		AddColumnIfMissing(sqliteConnection, "photos", "ALTER TABLE photos ADD COLUMN source_slot    INTEGER DEFAULT 1", "source_slot");
		AddColumnIfMissing(sqliteConnection, "photos", "ALTER TABLE photos ADD COLUMN is_favorite    INTEGER DEFAULT 0", "is_favorite");
		AddColumnIfMissing(sqliteConnection, "photos", "ALTER TABLE photos ADD COLUMN match_source   TEXT", "match_source");
		AddColumnIfMissing(sqliteConnection, "photos", "ALTER TABLE photos ADD COLUMN is_missing     INTEGER DEFAULT 0", "is_missing");
		AddColumnIfMissing(sqliteConnection, "photos", "ALTER TABLE photos ADD COLUMN phash_version  INTEGER DEFAULT 0", "phash_version");
		using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
		sqliteCommand2.CommandText = "DROP TABLE IF EXISTS photo_embeddings;";
		sqliteCommand2.ExecuteNonQuery();
	}

	private static bool HasColumn(SqliteConnection conn, string table, string column)
	{
		using SqliteCommand sqliteCommand = conn.CreateCommand();
		sqliteCommand.CommandText = "PRAGMA table_info(" + table + ")";
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			if (string.Equals(sqliteDataReader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static void AddColumnIfMissing(SqliteConnection conn, string table, string sql, string column)
	{
		if (HasColumn(conn, table, column))
		{
			return;
		}
		using SqliteCommand sqliteCommand = conn.CreateCommand();
		sqliteCommand.CommandText = sql;
		sqliteCommand.ExecuteNonQuery();
	}

	private static List<string> ReadStringColumn(SqliteCommand cmd)
	{
		List<string> list = new List<string>();
		using SqliteDataReader sqliteDataReader = cmd.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			list.Add(sqliteDataReader.IsDBNull(0) ? "" : sqliteDataReader.GetString(0));
		}
		return list;
	}

	private static string? NullableString(SqliteDataReader r, int ordinal)
	{
		if (!r.IsDBNull(ordinal))
		{
			return r.GetString(ordinal);
		}
		return null;
	}

	private static long? NullableLong(SqliteDataReader r, int ordinal)
	{
		if (!r.IsDBNull(ordinal))
		{
			return r.GetInt64(ordinal);
		}
		return null;
	}

	private static List<string> GetPhotoTagsInternal(SqliteConnection conn, string photoPath)
	{
		using SqliteCommand sqliteCommand = conn.CreateCommand();
		sqliteCommand.CommandText = "\nSELECT t.name\nFROM photo_tags pt\nINNER JOIN tags t ON t.id = pt.tag_id\nWHERE pt.photo_path = @p\nORDER BY t.name COLLATE NOCASE ASC";
		sqliteCommand.Parameters.AddWithValue("@p", photoPath);
		return ReadStringColumn(sqliteCommand);
	}

	private static Dictionary<string, List<string>> GetTagsForPaths(SqliteConnection conn, IEnumerable<string> photoPaths)
	{
		Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		List<string> list = new List<string>(photoPaths);
		if (list.Count == 0)
		{
			return dictionary;
		}
		for (int i = 0; i < list.Count; i += 500)
		{
			int num = Math.Min(500, list.Count - i);
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("\nSELECT pt.photo_path, t.name\nFROM photo_tags pt\nINNER JOIN tags t ON t.id = pt.tag_id\nWHERE pt.photo_path IN (");
			using SqliteCommand sqliteCommand = conn.CreateCommand();
			for (int j = 0; j < num; j++)
			{
				if (j > 0)
				{
					stringBuilder.Append(',');
				}
				string text = $"@pp{j}";
				stringBuilder.Append(text);
				sqliteCommand.Parameters.AddWithValue(text, list[i + j]);
			}
			stringBuilder.Append(") ORDER BY pt.photo_path, t.name COLLATE NOCASE ASC");
			sqliteCommand.CommandText = stringBuilder.ToString();
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				string key = sqliteDataReader.GetString(0);
				string item = sqliteDataReader.GetString(1);
				if (!dictionary.TryGetValue(key, out var value))
				{
					value = (dictionary[key] = new List<string>());
				}
				value.Add(item);
			}
		}
		return dictionary;
	}

	private static PhotoRecordDto MapPhotoRow(SqliteDataReader r)
	{
		return new PhotoRecordDto
		{
			photo_filename = r.GetString(0),
			photo_path = r.GetString(1),
			world_id = NullableString(r, 2),
			world_name = NullableString(r, 3),
			timestamp = r.GetString(4),
			phash = NullableString(r, 5),
			orientation = NullableString(r, 6),
			image_width = NullableLong(r, 7),
			image_height = NullableLong(r, 8),
			source_slot = (r.IsDBNull(9) ? 1 : r.GetInt64(9)),
			is_favorite = (!r.IsDBNull(10) && r.GetInt64(10) != 0),
			match_source = NullableString(r, 11),
			is_missing = (!r.IsDBNull(12) && r.GetInt64(12) != 0),
			tags = Array.Empty<string>()
		};
	}

	private static string BuildPhotoWhereClause(PhotoQueryParams q, SqliteCommand cmd, string tableAlias = "photos")
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral(" ");
		handler.AppendFormatted(tableAlias);
		handler.AppendLiteral(".is_missing = 0");
		stringBuilder3.Append(ref handler);
		if (q.StartDate != null)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(29, 1, stringBuilder2);
			handler.AppendLiteral(" AND ");
			handler.AppendFormatted(tableAlias);
			handler.AppendLiteral(".timestamp >= @startDate");
			stringBuilder4.Append(ref handler);
			cmd.Parameters.AddWithValue("@startDate", q.StartDate);
		}
		if (q.EndDate != null)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(27, 1, stringBuilder2);
			handler.AppendLiteral(" AND ");
			handler.AppendFormatted(tableAlias);
			handler.AppendLiteral(".timestamp <= @endDate");
			stringBuilder5.Append(ref handler);
			cmd.Parameters.AddWithValue("@endDate", q.EndDate);
		}
		if (q.WorldQuery != null)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(71, 2, stringBuilder2);
			handler.AppendLiteral(" AND (");
			handler.AppendFormatted(tableAlias);
			handler.AppendLiteral(".world_name LIKE @worldQuery");
			handler.AppendLiteral(" OR ");
			handler.AppendFormatted(tableAlias);
			handler.AppendLiteral(".photo_filename LIKE @worldQuery)");
			stringBuilder6.Append(ref handler);
			cmd.Parameters.AddWithValue("@worldQuery", "%" + q.WorldQuery + "%");
		}
		IReadOnlyList<string> worldExacts = q.WorldExacts;
		if (worldExacts != null && worldExacts.Count > 0)
		{
			if (worldExacts.Count == 1 && worldExacts[0] == "unknown")
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder7 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder2);
				handler.AppendLiteral(" AND ");
				handler.AppendFormatted(tableAlias);
				handler.AppendLiteral(".world_name IS NULL");
				stringBuilder7.Append(ref handler);
			}
			else
			{
				stringBuilder.Append(" AND (");
				bool flag = true;
				bool flag2 = false;
				int num = 0;
				foreach (string item in worldExacts)
				{
					if (item == "unknown")
					{
						flag2 = true;
						continue;
					}
					if (!flag)
					{
						stringBuilder.Append(" OR ");
					}
					string text = $"@wex{num}";
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder8 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(14, 2, stringBuilder2);
					handler.AppendFormatted(tableAlias);
					handler.AppendLiteral(".world_name = ");
					handler.AppendFormatted(text);
					stringBuilder8.Append(ref handler);
					cmd.Parameters.AddWithValue(text, item);
					flag = false;
					num++;
				}
				if (flag2)
				{
					if (!flag)
					{
						stringBuilder.Append(" OR ");
					}
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder9 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder2);
					handler.AppendFormatted(tableAlias);
					handler.AppendLiteral(".world_name IS NULL");
					stringBuilder9.Append(ref handler);
				}
				stringBuilder.Append(")");
			}
		}
		string worldIdExact = q.WorldIdExact;
		if (worldIdExact != null && worldIdExact.Length > 0)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder10 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(30, 1, stringBuilder2);
			handler.AppendLiteral(" AND ");
			handler.AppendFormatted(tableAlias);
			handler.AppendLiteral(".world_id = @worldIdExact");
			stringBuilder10.Append(ref handler);
			cmd.Parameters.AddWithValue("@worldIdExact", worldIdExact);
		}
		if (q.WorldIdIsNull)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder11 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder2);
			handler.AppendLiteral(" AND ");
			handler.AppendFormatted(tableAlias);
			handler.AppendLiteral(".world_id IS NULL");
			stringBuilder11.Append(ref handler);
		}
		if (q.Orientation != null)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder12 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(32, 1, stringBuilder2);
			handler.AppendLiteral(" AND ");
			handler.AppendFormatted(tableAlias);
			handler.AppendLiteral(".orientation = @orientation");
			stringBuilder12.Append(ref handler);
			cmd.Parameters.AddWithValue("@orientation", q.Orientation);
		}
		if (q.FavoritesOnly == true)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder13 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(21, 1, stringBuilder2);
			handler.AppendLiteral(" AND ");
			handler.AppendFormatted(tableAlias);
			handler.AppendLiteral(".is_favorite = 1");
			stringBuilder13.Append(ref handler);
		}
		long? sourceSlot = q.SourceSlot;
		if (sourceSlot.HasValue)
		{
			long valueOrDefault = sourceSlot.GetValueOrDefault();
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder14 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(31, 1, stringBuilder2);
			handler.AppendLiteral(" AND ");
			handler.AppendFormatted(tableAlias);
			handler.AppendLiteral(".source_slot = @sourceSlot");
			stringBuilder14.Append(ref handler);
			cmd.Parameters.AddWithValue("@sourceSlot", valueOrDefault);
		}
		if (q.PhotoPathExact != null)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder15 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(34, 1, stringBuilder2);
			handler.AppendLiteral(" AND ");
			handler.AppendFormatted(tableAlias);
			handler.AppendLiteral(".photo_path = @photoPathExact");
			stringBuilder15.Append(ref handler);
			cmd.Parameters.AddWithValue("@photoPathExact", q.PhotoPathExact);
		}
		IReadOnlyList<string> tagFilters = q.TagFilters;
		if (tagFilters != null && tagFilters.Count > 0)
		{
			for (int i = 0; i < tagFilters.Count; i++)
			{
				string text2 = $"@tag{i}";
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder16 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(131, 2, stringBuilder2);
				handler.AppendLiteral(" AND EXISTS (\nSELECT 1\nFROM photo_tags pt\nINNER JOIN tags t ON t.id = pt.tag_id\nWHERE pt.photo_path = ");
				handler.AppendFormatted(tableAlias);
				handler.AppendLiteral(".photo_path\n  AND t.name = ");
				handler.AppendFormatted(text2);
				handler.AppendLiteral("\n)");
				stringBuilder16.Append(ref handler);
				cmd.Parameters.AddWithValue(text2, tagFilters[i]);
			}
		}
		return stringBuilder.ToString();
	}

	public Task<PhotoPage> GetPhotosPageAsync(PhotoQueryParams q, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			int num;
			using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
			{
				string text = BuildPhotoWhereClause(q, sqliteCommand);
				sqliteCommand.CommandText = "SELECT COUNT(*) FROM photos WHERE " + text;
				num = Convert.ToInt32(sqliteCommand.ExecuteScalar() ?? ((object)0));
			}
			if (num == 0)
			{
				return Task.FromResult(new PhotoPage
				{
					Items = Array.Empty<PhotoRecordDto>(),
					Total = 0
				});
			}
			string value = (q.IncludePhash ? "phash" : "NULL");
			using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
			string value2 = BuildPhotoWhereClause(q, sqliteCommand2);
			StringBuilder stringBuilder = new StringBuilder();
			string text2 = ((q.Sort != SortMode.worldAsc) ? "timestamp DESC, photo_path ASC" : "world_name COLLATE NOCASE ASC, timestamp DESC, photo_path ASC");
			string value3 = text2;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(220, 3, stringBuilder2);
			handler.AppendLiteral("\nSELECT photo_filename, photo_path, world_id, world_name, timestamp,\n       ");
			handler.AppendFormatted(value);
			handler.AppendLiteral(" AS phash,\n       orientation, image_width, image_height, source_slot, is_favorite,\n       match_source, is_missing\nFROM photos\nWHERE ");
			handler.AppendFormatted(value2);
			handler.AppendLiteral("\nORDER BY ");
			handler.AppendFormatted(value3);
			stringBuilder2.Append(ref handler);
			if (q.Limit.HasValue)
			{
				stringBuilder.Append(" LIMIT @limit");
				sqliteCommand2.Parameters.AddWithValue("@limit", q.Limit.Value);
			}
			if (q.Offset.HasValue)
			{
				stringBuilder.Append(" OFFSET @offset");
				sqliteCommand2.Parameters.AddWithValue("@offset", q.Offset.Value);
			}
			sqliteCommand2.CommandText = stringBuilder.ToString();
			List<PhotoRecordDto> list = new List<PhotoRecordDto>();
			using (SqliteDataReader sqliteDataReader = sqliteCommand2.ExecuteReader())
			{
				while (sqliteDataReader.Read())
				{
					list.Add(MapPhotoRow(sqliteDataReader));
				}
			}
			if (list.Count > 0)
			{
				List<string> list2 = new List<string>(list.Count);
				foreach (PhotoRecordDto item in list)
				{
					list2.Add(item.photo_path);
				}
				Dictionary<string, List<string>> tagsForPaths = GetTagsForPaths(sqliteConnection, list2);
				for (int i = 0; i < list.Count; i++)
				{
					PhotoRecordDto photoRecordDto = list[i];
					if (tagsForPaths.TryGetValue(photoRecordDto.photo_path, out var value4))
					{
						list[i] = photoRecordDto with
						{
							tags = value4
						};
					}
				}
			}
			return Task.FromResult(new PhotoPage
			{
				Items = list,
				Total = num
			});
		}
		catch (Exception value5)
		{
			AppLogger.Error($"AlpheratzDb.GetPhotosPageAsync: threw: {value5}");
			throw;
		}
	}

	public Task<IReadOnlyList<MonthSummaryItem>> GetMonthSummaryAsync(PhotoQueryParams q, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			string text = BuildPhotoWhereClause(q, sqliteCommand);
			sqliteCommand.CommandText = "\nSELECT substr(timestamp, 1, 4) AS y,\n       substr(timestamp, 6, 2) AS m,\n       COUNT(*)                AS cnt\nFROM photos\nWHERE " + text + "\nGROUP BY y, m\nORDER BY y DESC, m DESC";
			List<MonthSummaryItem> list = new List<MonthSummaryItem>();
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				string s = (sqliteDataReader.IsDBNull(0) ? "0" : sqliteDataReader.GetString(0));
				string s2 = (sqliteDataReader.IsDBNull(1) ? "1" : sqliteDataReader.GetString(1));
				int.TryParse(s, out var result);
				int.TryParse(s2, out var result2);
				int @int = sqliteDataReader.GetInt32(2);
				list.Add(new MonthSummaryItem(result, result2, @int));
			}
			return Task.FromResult((IReadOnlyList<MonthSummaryItem>)list);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetMonthSummaryAsync: threw: {value}");
			throw;
		}
	}

	public Task<PhotoRecordDto?> GetPhotoRecordAsync(string photoPath, bool includePhash = false, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			string text = (includePhash ? "phash" : "NULL");
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\nSELECT photo_filename, photo_path, world_id, world_name, timestamp,\n       " + text + " AS phash,\n       orientation, image_width, image_height, source_slot, is_favorite,\n       match_source, is_missing\nFROM photos\nWHERE photo_path = @p";
			sqliteCommand.Parameters.AddWithValue("@p", photoPath);
			PhotoRecordDto photoRecordDto = null;
			using (SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader())
			{
				if (sqliteDataReader.Read())
				{
					photoRecordDto = MapPhotoRow(sqliteDataReader);
				}
			}
			if ((object)photoRecordDto == null)
			{
				return Task.FromResult<PhotoRecordDto>(null);
			}
			List<string> photoTagsInternal = GetPhotoTagsInternal(sqliteConnection, photoPath);
			photoRecordDto = photoRecordDto with
			{
				tags = photoTagsInternal
			};
			return Task.FromResult(photoRecordDto);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetPhotoRecordAsync: threw: {value}");
			throw;
		}
	}

	public Task<IReadOnlyList<string>> GetPhotoTagsAsync(string photoPath, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection conn = OpenConnection();
			return Task.FromResult((IReadOnlyList<string>)GetPhotoTagsInternal(conn, photoPath));
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetPhotoTagsAsync: threw: {value}");
			throw;
		}
	}

	public Task SetPhotoFavoriteAsync(string photoPath, bool isFavorite, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "UPDATE photos SET is_favorite = @fav WHERE photo_path = @p";
			sqliteCommand.Parameters.AddWithValue("@fav", (long)(isFavorite ? 1 : 0));
			sqliteCommand.Parameters.AddWithValue("@p", photoPath);
			if (sqliteCommand.ExecuteNonQuery() == 0)
			{
				AppLogger.Warn("SetPhotoFavorite: 写真が見つかりません: " + photoPath);
			}
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.SetPhotoFavoriteAsync: threw: {value}");
			throw;
		}
	}

	public Task AddPhotoTagAsync(string photoPath, string tag, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.Transaction = sqliteTransaction;
			sqliteCommand.CommandText = "INSERT INTO tags (name) VALUES (@tag) ON CONFLICT(name) DO NOTHING";
			sqliteCommand.Parameters.AddWithValue("@tag", tag);
			sqliteCommand.ExecuteNonQuery();
			using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
			sqliteCommand2.Transaction = sqliteTransaction;
			sqliteCommand2.CommandText = "\nINSERT INTO photo_tags (photo_path, tag_id)\nSELECT @p, id FROM tags WHERE name = @tag\nON CONFLICT(photo_path, tag_id) DO NOTHING";
			sqliteCommand2.Parameters.AddWithValue("@p", photoPath);
			sqliteCommand2.Parameters.AddWithValue("@tag", tag);
			sqliteCommand2.ExecuteNonQuery();
			sqliteTransaction.Commit();
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.AddPhotoTagAsync: threw: {value}");
			throw;
		}
	}

	public Task RemovePhotoTagAsync(string photoPath, string tag, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\nDELETE FROM photo_tags\nWHERE photo_path = @p\n  AND tag_id IN (SELECT id FROM tags WHERE name = @tag)";
			sqliteCommand.Parameters.AddWithValue("@p", photoPath);
			sqliteCommand.Parameters.AddWithValue("@tag", tag);
			sqliteCommand.ExecuteNonQuery();
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.RemovePhotoTagAsync: threw: {value}");
			throw;
		}
	}

	public Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<string> list = new List<string>();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT name FROM tags ORDER BY name COLLATE NOCASE ASC";
			using (SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader())
			{
				while (sqliteDataReader.Read())
				{
					string item = sqliteDataReader.GetString(0);
					if (hashSet.Add(item))
					{
						list.Add(item);
					}
				}
			}
			list.Sort(StringComparer.OrdinalIgnoreCase);
			return Task.FromResult((IReadOnlyList<string>)list);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetAllTagsAsync: threw: {value}");
			throw;
		}
	}

	public Task CreateTagMasterAsync(string tag, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			string text = tag.Trim();
			if (text.Length == 0)
			{
				throw new ArgumentException("タグ名が空です", "tag");
			}
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "INSERT INTO tags (name) VALUES (@tag) ON CONFLICT(name) DO NOTHING";
			sqliteCommand.Parameters.AddWithValue("@tag", text);
			sqliteCommand.ExecuteNonQuery();
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.CreateTagMasterAsync: threw: {value}");
			throw;
		}
	}

	public Task DeleteTagMasterAsync(string tag, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			string text = tag.Trim();
			if (text.Length == 0)
			{
				throw new ArgumentException("タグ名が空です", "tag");
			}
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.Transaction = sqliteTransaction;
			sqliteCommand.CommandText = "DELETE FROM photo_tags WHERE tag_id IN (SELECT id FROM tags WHERE name = @tag)";
			sqliteCommand.Parameters.AddWithValue("@tag", text);
			sqliteCommand.ExecuteNonQuery();
			using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
			sqliteCommand2.Transaction = sqliteTransaction;
			sqliteCommand2.CommandText = "DELETE FROM tags WHERE name = @tag";
			sqliteCommand2.Parameters.AddWithValue("@tag", text);
			sqliteCommand2.ExecuteNonQuery();
			sqliteTransaction.Commit();
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.DeleteTagMasterAsync: threw: {value}");
			throw;
		}
	}

	public Task ResetPhotoCacheBySlotAsync(long slot, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using (SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction())
			{
				using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
				sqliteCommand.Transaction = sqliteTransaction;
				sqliteCommand.CommandText = "DELETE FROM photo_tags WHERE photo_path IN (SELECT photo_path FROM photos WHERE source_slot = @slot)";
				sqliteCommand.Parameters.AddWithValue("@slot", slot);
				sqliteCommand.ExecuteNonQuery();
				using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
				sqliteCommand2.Transaction = sqliteTransaction;
				sqliteCommand2.CommandText = "DELETE FROM photos WHERE source_slot = @slot";
				sqliteCommand2.Parameters.AddWithValue("@slot", slot);
				sqliteCommand2.ExecuteNonQuery();
				using SqliteCommand sqliteCommand3 = sqliteConnection.CreateCommand();
				sqliteCommand3.Transaction = sqliteTransaction;
				sqliteCommand3.CommandText = "DELETE FROM photo_tags WHERE photo_path NOT IN (SELECT photo_path FROM photos)";
				sqliteCommand3.ExecuteNonQuery();
				sqliteTransaction.Commit();
			}
			string imgCacheDir = AppPaths.GetImgCacheDir(slot);
			if (imgCacheDir != null)
			{
				try
				{
					ClearDirectoryContents(imgCacheDir);
				}
				catch (Exception ex)
				{
					AppLogger.Warn("imgCache のリセットに失敗しました [" + imgCacheDir + "]: " + ex.Message);
				}
			}
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.ResetPhotoCacheBySlotAsync: threw: {value}");
			throw;
		}
	}

	public Task UpsertPhotoAsync(PhotoUpsertData data, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\nINSERT INTO photos (\n    photo_path, photo_filename, world_id, world_name, timestamp,\n    orientation, image_width, image_height, source_slot, match_source, is_missing\n) VALUES (\n    @photo_path, @photo_filename, @world_id, @world_name, @timestamp,\n    @orientation, @image_width, @image_height, @source_slot, @match_source, 0\n)\nON CONFLICT(photo_path) DO UPDATE SET\n    photo_filename = excluded.photo_filename,\n    world_id       = COALESCE(excluded.world_id,      photos.world_id),\n    world_name     = COALESCE(excluded.world_name,    photos.world_name),\n    timestamp      = excluded.timestamp,\n    orientation    = COALESCE(excluded.orientation,   photos.orientation),\n    image_width    = COALESCE(excluded.image_width,   photos.image_width),\n    image_height   = COALESCE(excluded.image_height,  photos.image_height),\n    source_slot    = excluded.source_slot,\n    match_source   = COALESCE(excluded.match_source,  photos.match_source),\n    is_missing     = 0";
			sqliteCommand.Parameters.AddWithValue("@photo_path", data.PhotoPath);
			sqliteCommand.Parameters.AddWithValue("@photo_filename", data.PhotoFilename);
			sqliteCommand.Parameters.AddWithValue("@world_id", ((object)data.WorldId) ?? ((object)DBNull.Value));
			sqliteCommand.Parameters.AddWithValue("@world_name", ((object)data.WorldName) ?? ((object)DBNull.Value));
			sqliteCommand.Parameters.AddWithValue("@timestamp", data.Timestamp);
			sqliteCommand.Parameters.AddWithValue("@orientation", ((object)data.Orientation) ?? ((object)DBNull.Value));
			sqliteCommand.Parameters.AddWithValue("@image_width", ((object)data.ImageWidth) ?? DBNull.Value);
			sqliteCommand.Parameters.AddWithValue("@image_height", ((object)data.ImageHeight) ?? DBNull.Value);
			sqliteCommand.Parameters.AddWithValue("@source_slot", data.SourceSlot);
			sqliteCommand.Parameters.AddWithValue("@match_source", ((object)data.MatchSource) ?? ((object)DBNull.Value));
			sqliteCommand.ExecuteNonQuery();
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.UpsertPhotoAsync: threw: {value}");
			throw;
		}
	}

	public Task<int> DeleteMissingPhotosAsync(IEnumerable<string> foundPaths, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			HashSet<string> hashSet = new HashSet<string>(foundPaths, StringComparer.OrdinalIgnoreCase);
			using SqliteConnection sqliteConnection = OpenConnection();
			List<string> list = new List<string>();
			using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
			{
				sqliteCommand.CommandText = "SELECT photo_path FROM photos";
				using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
				int num = 0;
				while (sqliteDataReader.Read())
				{
					if ((num++ & 0x3FF) == 0)
					{
						ct.ThrowIfCancellationRequested();
					}
					list.Add(sqliteDataReader.GetString(0));
				}
			}
			List<string> list2 = new List<string>();
			foreach (string item in list)
			{
				if (!hashSet.Contains(item))
				{
					list2.Add(item);
				}
			}
			if (list2.Count == 0)
			{
				return Task.FromResult(0);
			}
			using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
			BulkDeleteByPaths(sqliteConnection, sqliteTransaction, list2, "DELETE FROM photo_tags", ct);
			BulkDeleteByPaths(sqliteConnection, sqliteTransaction, list2, "DELETE FROM photos", ct);
			sqliteTransaction.Commit();
			AppLogger.Info($"AlpheratzDb.DeleteMissingPhotosAsync: deleted {list2.Count} photo(s)");
			return Task.FromResult(list2.Count);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.DeleteMissingPhotosAsync: threw: {value}");
			throw;
		}
	}

	private static void BulkDeleteByPaths(SqliteConnection conn, SqliteTransaction tx, List<string> paths, string deletePrefix, CancellationToken ct)
	{
		for (int i = 0; i < paths.Count; i += 500)
		{
			ct.ThrowIfCancellationRequested();
			List<string> range = paths.GetRange(i, Math.Min(500, paths.Count - i));
			using SqliteCommand sqliteCommand = conn.CreateCommand();
			sqliteCommand.Transaction = tx;
			string[] array = new string[range.Count];
			for (int j = 0; j < range.Count; j++)
			{
				array[j] = $"@p{j}";
				sqliteCommand.Parameters.AddWithValue(array[j], range[j]);
			}
			sqliteCommand.CommandText = deletePrefix + " WHERE photo_path IN (" + string.Join(',', array) + ")";
			sqliteCommand.ExecuteNonQuery();
		}
	}

	public Task<IDictionary<string, ExistingPhotoInfo>> GetExistingPhotosAsync(CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\nSELECT photo_filename, photo_path, world_id, world_name, timestamp,\n       match_source, orientation, image_width, image_height, source_slot, is_missing\nFROM photos";
			Dictionary<string, ExistingPhotoInfo> dictionary = new Dictionary<string, ExistingPhotoInfo>(StringComparer.Ordinal);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				ExistingPhotoInfo existingPhotoInfo = new ExistingPhotoInfo
				{
					PhotoFilename = sqliteDataReader.GetString(0),
					PhotoPath = sqliteDataReader.GetString(1),
					WorldId = NullableString(sqliteDataReader, 2),
					WorldName = NullableString(sqliteDataReader, 3),
					MatchSource = NullableString(sqliteDataReader, 5),
					Orientation = NullableString(sqliteDataReader, 6),
					ImageWidth = NullableLong(sqliteDataReader, 7),
					ImageHeight = NullableLong(sqliteDataReader, 8),
					SourceSlot = (sqliteDataReader.IsDBNull(9) ? 1 : sqliteDataReader.GetInt64(9)),
					IsMissing = (!sqliteDataReader.IsDBNull(10) && sqliteDataReader.GetInt64(10) != 0)
				};
				dictionary[existingPhotoInfo.PhotoPath] = existingPhotoInfo;
			}
			return Task.FromResult((IDictionary<string, ExistingPhotoInfo>)dictionary);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetExistingPhotosAsync: threw: {value}");
			throw;
		}
	}

	public Task UpdatePhotoWorldNameAsync(string photoPath, string worldName, string matchSource, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\nUPDATE photos\nSET world_name = @worldName,\n    match_source = @matchSource\nWHERE photo_path = @p";
			sqliteCommand.Parameters.AddWithValue("@worldName", worldName);
			sqliteCommand.Parameters.AddWithValue("@matchSource", matchSource);
			sqliteCommand.Parameters.AddWithValue("@p", photoPath);
			sqliteCommand.ExecuteNonQuery();
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.UpdatePhotoWorldNameAsync: threw: {value}");
			throw;
		}
	}

	public Task<IReadOnlyList<(string photoPath, string timestamp)>> GetUnknownWorldPhotosAsync(string target, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			var (text, text2) = ((target == "primary") ? (" AND COALESCE(source_slot, 1) = 1", "timestamp") : ((!(target == "secondary")) ? ("", "timestamp") : (" AND COALESCE(source_slot, 1) = 2", "timestamp")));
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\nSELECT photo_path, timestamp\nFROM photos\nWHERE is_missing = 0\n  AND world_name IS NULL\n  AND world_id IS NULL" + text + "\nORDER BY " + text2;
			List<(string, string)> list = new List<(string, string)>();
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add((sqliteDataReader.GetString(0), sqliteDataReader.GetString(1)));
			}
			return Task.FromResult((IReadOnlyList<(string, string)>)list);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetUnknownWorldPhotosAsync: threw: {value}");
			throw;
		}
	}

	public Task UpsertArchiveWorldVisitsAsync(IEnumerable<ArchiveWorldVisitData> visits, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			List<ArchiveWorldVisitData> visits2 = new List<ArchiveWorldVisitData>(visits);
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.Transaction = sqliteTransaction;
			sqliteCommand.CommandText = "DELETE FROM archive_world_visits";
			sqliteCommand.ExecuteNonQuery();
			BulkInsertArchiveVisits(sqliteConnection, sqliteTransaction, visits2, ct);
			sqliteTransaction.Commit();
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.UpsertArchiveWorldVisitsAsync: threw: {value}");
			throw;
		}
	}

	private static void BulkInsertArchiveVisits(SqliteConnection conn, SqliteTransaction tx, List<ArchiveWorldVisitData> visits, CancellationToken ct)
	{
		if (visits.Count == 0)
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < visits.Count; i += 200)
		{
			ct.ThrowIfCancellationRequested();
			int num = Math.Min(200, visits.Count - i);
			stringBuilder.Clear();
			stringBuilder.Append("INSERT INTO archive_world_visits (source_log_name, world_name, join_time, leave_time) VALUES ");
			using SqliteCommand sqliteCommand = conn.CreateCommand();
			sqliteCommand.Transaction = tx;
			for (int j = 0; j < num; j++)
			{
				if (j > 0)
				{
					stringBuilder.Append(',');
				}
				stringBuilder.Append("(@s").Append(j).Append(",@w")
					.Append(j)
					.Append(",@j")
					.Append(j)
					.Append(",@l")
					.Append(j)
					.Append(')');
				ArchiveWorldVisitData archiveWorldVisitData = visits[i + j];
				sqliteCommand.Parameters.AddWithValue($"@s{j}", archiveWorldVisitData.SourceLogName);
				sqliteCommand.Parameters.AddWithValue($"@w{j}", archiveWorldVisitData.WorldName);
				sqliteCommand.Parameters.AddWithValue($"@j{j}", archiveWorldVisitData.JoinTime);
				sqliteCommand.Parameters.AddWithValue($"@l{j}", ((object)archiveWorldVisitData.LeaveTime) ?? ((object)DBNull.Value));
			}
			sqliteCommand.CommandText = stringBuilder.ToString();
			sqliteCommand.ExecuteNonQuery();
		}
	}

	public Task<string?> LookupWorldNameFromArchiveAsync(string timestamp, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\nSELECT world_name\nFROM archive_world_visits\nWHERE join_time <= @ts\n  AND (leave_time IS NULL OR leave_time >= @ts)\nORDER BY join_time DESC\nLIMIT 1";
			sqliteCommand.Parameters.AddWithValue("@ts", timestamp);
			return Task.FromResult((sqliteCommand.ExecuteScalar() is string text) ? text : null);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.LookupWorldNameFromArchiveAsync: threw: {value}");
			throw;
		}
	}

	public Task ApplyWorldMatchFromPhotoAsync(string targetPhotoPath, string sourcePhotoPath, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\nUPDATE photos\nSET world_id     = (SELECT world_id     FROM photos WHERE photo_path = @src),\n    world_name   = (SELECT world_name   FROM photos WHERE photo_path = @src),\n    match_source = (SELECT match_source FROM photos WHERE photo_path = @src)\nWHERE photo_path = @tgt";
			sqliteCommand.Parameters.AddWithValue("@src", sourcePhotoPath);
			sqliteCommand.Parameters.AddWithValue("@tgt", targetPhotoPath);
			sqliteCommand.ExecuteNonQuery();
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.ApplyWorldMatchFromPhotoAsync: threw: {value}");
			throw;
		}
	}

	public Task<IReadOnlyList<WorldFilterOptionDto>> GetWorldFilterOptionsAsync(CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\nSELECT world_name, COUNT(*) AS cnt\nFROM photos\nWHERE is_missing = 0\nGROUP BY world_name\nORDER BY cnt DESC, world_name COLLATE NOCASE ASC";
			List<WorldFilterOptionDto> list = new List<WorldFilterOptionDto>();
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add(new WorldFilterOptionDto
				{
					world_name = NullableString(sqliteDataReader, 0),
					count = sqliteDataReader.GetInt64(1)
				});
			}
			return Task.FromResult((IReadOnlyList<WorldFilterOptionDto>)list);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetWorldFilterOptionsAsync: threw: {value}");
			throw;
		}
	}

	public Task<IReadOnlyDictionary<string, long>> GetTagFilterCountsAsync(CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\nSELECT t.name, COUNT(*) AS cnt\nFROM photo_tags pt\nINNER JOIN tags t ON t.id = pt.tag_id\nINNER JOIN photos p ON p.photo_path = pt.photo_path\nWHERE p.is_missing = 0\nGROUP BY t.name";
			Dictionary<string, long> dictionary = new Dictionary<string, long>(StringComparer.Ordinal);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				string text = NullableString(sqliteDataReader, 0);
				if (text != null)
				{
					dictionary[text] = sqliteDataReader.GetInt64(1);
				}
			}
			return Task.FromResult((IReadOnlyDictionary<string, long>)dictionary);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetTagFilterCountsAsync: threw: {value}");
			throw;
		}
	}

	public Task<IReadOnlyList<KnownWorldRow>> GetKnownWorldPhotosAsync(long sourceSlot, string? excludePhotoPath, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT photo_path, photo_filename, world_name, world_id, phash, source_slot FROM photos\n                                 WHERE is_missing = 0\n                                   AND source_slot = @source_slot\n                                   AND world_name IS NOT NULL AND TRIM(world_name) <> ''\n                                   AND phash IS NOT NULL AND phash <> ''\n                                   AND (@exclude IS NULL OR photo_path <> @exclude)";
			sqliteCommand.Parameters.AddWithValue("@source_slot", sourceSlot);
			sqliteCommand.Parameters.AddWithValue("@exclude", ((object)excludePhotoPath) ?? ((object)DBNull.Value));
			List<KnownWorldRow> list = new List<KnownWorldRow>();
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add(new KnownWorldRow(sqliteDataReader.GetString(0), sqliteDataReader.GetString(1), sqliteDataReader.GetString(2), sqliteDataReader.IsDBNull(3) ? null : sqliteDataReader.GetString(3), sqliteDataReader.GetString(4), sqliteDataReader.GetInt64(5)));
			}
			return Task.FromResult((IReadOnlyList<KnownWorldRow>)list);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetKnownWorldPhotosAsync: threw: {value}");
			throw;
		}
	}

	public Task<IReadOnlyList<UnknownPhashRow>> GetUnknownWorldPhotosWithPhashAsync(CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT photo_path, photo_filename, phash, source_slot FROM photos\n                                 WHERE is_missing = 0\n                                   AND world_name IS NULL\n                                   AND world_id IS NULL\n                                   AND phash IS NOT NULL AND phash <> ''";
			List<UnknownPhashRow> list = new List<UnknownPhashRow>();
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add(new UnknownPhashRow(sqliteDataReader.GetString(0), sqliteDataReader.GetString(1), sqliteDataReader.GetString(2), sqliteDataReader.GetInt64(3)));
			}
			return Task.FromResult((IReadOnlyList<UnknownPhashRow>)list);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetUnknownWorldPhotosWithPhashAsync: threw: {value}");
			throw;
		}
	}

	public Task UpdatePhotoWorldAsync(string photoPath, string worldName, string? worldId, string matchSource, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "UPDATE photos\n                                 SET world_name = @world_name,\n                                     world_id = COALESCE(@world_id, world_id),\n                                     match_source = @match_source\n                                 WHERE photo_path = @photo_path";
			sqliteCommand.Parameters.AddWithValue("@world_name", worldName);
			sqliteCommand.Parameters.AddWithValue("@world_id", ((object)worldId) ?? ((object)DBNull.Value));
			sqliteCommand.Parameters.AddWithValue("@match_source", matchSource);
			sqliteCommand.Parameters.AddWithValue("@photo_path", photoPath);
			sqliteCommand.ExecuteNonQuery();
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.UpdatePhotoWorldAsync: threw: {value}");
			throw;
		}
	}

	public Task<int> GetPendingPhashCountAsync(CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT COUNT(*) FROM photos\n                                 WHERE is_missing = 0\n                                   AND (phash IS NULL OR phash = '')";
			return Task.FromResult(Convert.ToInt32(sqliteCommand.ExecuteScalar() ?? ((object)0L)));
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetPendingPhashCountAsync: threw: {value}");
			throw;
		}
	}

	public Task<IReadOnlyList<PendingPhashItem>> GetPendingPhashBatchAsync(int limit, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT source_slot, photo_filename, photo_path FROM photos\n                                 WHERE is_missing = 0\n                                   AND (phash IS NULL OR phash = '')\n                                 ORDER BY timestamp DESC\n                                 LIMIT @limit";
			sqliteCommand.Parameters.AddWithValue("@limit", limit);
			List<PendingPhashItem> list = new List<PendingPhashItem>();
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add(new PendingPhashItem(sqliteDataReader.GetInt64(0), sqliteDataReader.GetString(1), sqliteDataReader.GetString(2)));
			}
			return Task.FromResult((IReadOnlyList<PendingPhashItem>)list);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetPendingPhashBatchAsync: threw: {value}");
			throw;
		}
	}

	public Task UpdatePhotoPhashAsync(string photoPath, string phashHex, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "UPDATE photos SET phash = @phash WHERE photo_path = @photo_path";
			sqliteCommand.Parameters.AddWithValue("@phash", phashHex);
			sqliteCommand.Parameters.AddWithValue("@photo_path", photoPath);
			sqliteCommand.ExecuteNonQuery();
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.UpdatePhotoPhashAsync: threw: {value}");
			throw;
		}
	}

	public Task<int> GetPendingOrientationCountAsync(CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT COUNT(*) FROM photos\n                                 WHERE is_missing = 0\n                                   AND (orientation IS NULL OR orientation = '' OR orientation = 'unknown'\n                                        OR image_width IS NULL OR image_height IS NULL)";
			return Task.FromResult(Convert.ToInt32(sqliteCommand.ExecuteScalar() ?? ((object)0L)));
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetPendingOrientationCountAsync: threw: {value}");
			throw;
		}
	}

	public Task<IReadOnlyList<PendingOrientationItem>> GetPendingOrientationBatchAsync(int limit, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT source_slot, photo_filename, photo_path FROM photos\n                                 WHERE is_missing = 0\n                                   AND (orientation IS NULL OR orientation = '' OR orientation = 'unknown'\n                                        OR image_width IS NULL OR image_height IS NULL)\n                                 ORDER BY timestamp DESC\n                                 LIMIT @limit";
			sqliteCommand.Parameters.AddWithValue("@limit", limit);
			List<PendingOrientationItem> list = new List<PendingOrientationItem>();
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add(new PendingOrientationItem(sqliteDataReader.GetInt64(0), sqliteDataReader.GetString(1), sqliteDataReader.GetString(2)));
			}
			return Task.FromResult((IReadOnlyList<PendingOrientationItem>)list);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.GetPendingOrientationBatchAsync: threw: {value}");
			throw;
		}
	}

	public Task UpdatePhotoOrientationAndDimensionsAsync(string photoPath, string? orientation, long? width, long? height, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			ct.ThrowIfCancellationRequested();
			using SqliteConnection sqliteConnection = OpenConnection();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "UPDATE photos\n                                 SET orientation = @orientation,\n                                     image_width = @image_width,\n                                     image_height = @image_height\n                                 WHERE photo_path = @photo_path";
			sqliteCommand.Parameters.AddWithValue("@orientation", ((object)orientation) ?? ((object)DBNull.Value));
			sqliteCommand.Parameters.AddWithValue("@image_width", ((object)width) ?? DBNull.Value);
			sqliteCommand.Parameters.AddWithValue("@image_height", ((object)height) ?? DBNull.Value);
			sqliteCommand.Parameters.AddWithValue("@photo_path", photoPath);
			sqliteCommand.ExecuteNonQuery();
			return Task.CompletedTask;
		}
		catch (Exception value)
		{
			AppLogger.Error($"AlpheratzDb.UpdatePhotoOrientationAndDimensionsAsync: threw: {value}");
			throw;
		}
	}

	private static void ClearDirectoryContents(string dir)
	{
		if (!Directory.Exists(dir))
		{
			return;
		}
		string[] files = Directory.GetFiles(dir);
		foreach (string text in files)
		{
			try
			{
				File.Delete(text);
			}
			catch (Exception ex)
			{
				AppLogger.Warn("ファイルを削除できません [" + text + "]: " + ex.Message);
			}
		}
		files = Directory.GetDirectories(dir);
		foreach (string text2 in files)
		{
			try
			{
				Directory.Delete(text2, recursive: true);
			}
			catch (Exception ex2)
			{
				AppLogger.Warn("フォルダを削除できません [" + text2 + "]: " + ex2.Message);
			}
		}
	}
}
