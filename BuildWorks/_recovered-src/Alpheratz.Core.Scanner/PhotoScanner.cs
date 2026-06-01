using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core.Database;
using Alpheratz.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Alpheratz.Core.Scanner;

public sealed class PhotoScanner
{
	private const int MaxItxtSize = 4194304;

	private const int MaxLogLineLength = 65536;

	private static readonly string[] SupportedExtensions = new string[6] { "png", "jpg", "jpeg", "webp", "psd", "xcf" };

	private static readonly string[] SkipDirs = new string[6] { "node_modules", "vendor", "cache", "$recycle.bin", "system volume information", "thumbnails" };

	private readonly AppConfig _config;

	private readonly AlpheratzDb _db;

	private readonly LocalEventBus _bus;

	private volatile CancellationTokenSource? _cancelSource;

	private static readonly Regex ReLogTime = new Regex("^(\\d{4}\\.\\d{2}\\.\\d{2} \\d{2}:\\d{2}:\\d{2})", RegexOptions.Compiled);

	private static readonly Regex ReLogEntering = new Regex("\\[Behaviour\\] Entering Room: (.*)", RegexOptions.Compiled);

	private static readonly Regex ReLogLeftRoom = new Regex("\\[Behaviour\\] OnLeftRoom", RegexOptions.Compiled);

	[GeneratedRegex("VRChat_(\\d{4}-\\d{2}-\\d{2})_(\\d{2}-\\d{2}-\\d{2})\\.(\\d{3})")]
	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.14.16921")]
	private static Regex ReFilename()
	{
		return _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__ReFilename_0.Instance;
	}

	[GeneratedRegex("<vrc:WorldID>([^<]+)</vrc:WorldID>")]
	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.14.16921")]
	private static Regex ReWorldId()
	{
		return _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__ReWorldId_1.Instance;
	}

	[GeneratedRegex("<vrc:WorldDisplayName>([^<]+)</vrc:WorldDisplayName>")]
	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.14.16921")]
	private static Regex ReWorldName()
	{
		return _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__ReWorldName_2.Instance;
	}

	public PhotoScanner(AppConfig config, AlpheratzDb db, LocalEventBus bus)
	{
		_config = config;
		_db = db;
		_bus = bus;
	}

	public void RequestCancel()
	{
		try
		{
			_cancelSource?.Cancel();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoScanner.RequestCancel: threw: {value}");
		}
	}

	public async Task ScanAsync(CancellationToken externalCt = default(CancellationToken))
	{
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
		_cancelSource = cts;
		CancellationToken token = cts.Token;
		try
		{
			await DoScanAsync(token).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			await _bus.PublishAsync("scan:error", "スキャンを中断しました").ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex2)
		{
			AppLogger.Error($"スキャン中に予期しないエラーが発生しました: {ex2}");
			await _bus.PublishAsync("scan:error", ex2.Message).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_cancelSource = null;
		}
	}

	private async Task DoScanAsync(CancellationToken ct)
	{
		AlpheratzSetting setting = _config.LoadSetting();
		List<(long slot, string path)> photoDirs = new List<(long, string)>();
		if (!string.IsNullOrWhiteSpace(setting.PhotoFolderPath) && Directory.Exists(setting.PhotoFolderPath))
		{
			photoDirs.Add((1L, setting.PhotoFolderPath));
		}
		else if (!string.IsNullOrWhiteSpace(setting.PhotoFolderPath))
		{
			await _bus.PublishAsync("scan:error", "写真フォルダが見つかりません: " + setting.PhotoFolderPath).ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		if (!string.IsNullOrWhiteSpace(setting.SecondaryPhotoFolderPath) && Directory.Exists(setting.SecondaryPhotoFolderPath) && !photoDirs.Exists(((long slot, string path) d) => d.path == setting.SecondaryPhotoFolderPath))
		{
			photoDirs.Add((2L, setting.SecondaryPhotoFolderPath));
		}
		if (photoDirs.Count == 0)
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "VRChat");
			if (!Directory.Exists(text))
			{
				await _bus.PublishAsync("scan:error", "写真フォルダが未設定です。設定から参照フォルダを選択してください。").ConfigureAwait(continueOnCapturedContext: false);
				return;
			}
			photoDirs.Add((1L, text));
		}
		await _bus.PublishAsync("scan:progress", new ScanProgressDto
		{
			processed = 0,
			total = 0,
			current_world = "ファイルを収集中...",
			phase = "scan"
		}).ConfigureAwait(continueOnCapturedContext: false);
		IDictionary<string, ExistingPhotoInfo> existing = await _db.GetExistingPhotosAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		List<(long slot, string filename, string path)> foundFiles = new List<(long, string, string)>();
		HashSet<string> visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (slot, dir) in photoDirs)
		{
			ct.ThrowIfCancellationRequested();
			CollectPhotosRecursive(slot, dir, foundFiles, visitedDirs, ct);
		}
		ct.ThrowIfCancellationRequested();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var item9 in foundFiles)
		{
			string item = item9.path;
			hashSet.Add(AppPaths.NormalizePathForDb(item));
		}
		await _db.DeleteMissingPhotosAsync(hashSet, ct).ConfigureAwait(continueOnCapturedContext: false);
		List<(long slot, string filename, string path, ScanRefreshKind kind)> candidates = new List<(long, string, string, ScanRefreshKind)>();
		foreach (var item10 in foundFiles)
		{
			long item2 = item10.slot;
			string item3 = item10.filename;
			string item4 = item10.path;
			string key = AppPaths.NormalizePathForDb(item4);
			if (!existing.TryGetValue(key, out ExistingPhotoInfo value))
			{
				candidates.Add((item2, item3, item4, ScanRefreshKind.Full));
				continue;
			}
			bool isMissing = value.IsMissing;
			bool flag = value.SourceSlot != item2;
			bool flag2 = value.WorldName == null && value.WorldId == null && value.MatchSource == null;
			bool flag3 = value.PhotoFilename != item3;
			bool flag4 = IsFileModifiedSinceTimestamp(item4, value);
			if (isMissing || flag || flag4)
			{
				candidates.Add((item2, item3, item4, ScanRefreshKind.Full));
			}
			else if (flag3)
			{
				candidates.Add((item2, item3, item4, ScanRefreshKind.PathOnly));
			}
			else if (flag2 && item3.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			{
				candidates.Add((item2, item3, item4, ScanRefreshKind.MetadataOnly));
			}
		}
		int total = candidates.Count;
		await _bus.PublishAsync("scan:progress", new ScanProgressDto
		{
			processed = 0,
			total = total,
			current_world = $"{total} 件の更新対象を確認しました",
			phase = "scan"
		}).ConfigureAwait(continueOnCapturedContext: false);
		for (int i = 0; i < candidates.Count; i++)
		{
			ct.ThrowIfCancellationRequested();
			(long slot, string filename, string path, ScanRefreshKind kind) tuple2 = candidates[i];
			long item5 = tuple2.slot;
			string item6 = tuple2.filename;
			string item7 = tuple2.path;
			ScanRefreshKind item8 = tuple2.kind;
			string key2 = AppPaths.NormalizePathForDb(item7);
			existing.TryGetValue(key2, out ExistingPhotoInfo value2);
			PhotoUpsertData photo = AnalyzePhoto(item7, item6, item5, value2, item8);
			await _db.UpsertPhotoAsync(photo, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (i % 10 == 0 || i == total - 1)
			{
				await _bus.PublishAsync("scan:progress", new ScanProgressDto
				{
					processed = i + 1,
					total = total,
					current_world = (photo.WorldName ?? "ワールド不明"),
					phase = "scan"
				}).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		await _bus.PublishAsync("scan:completed").ConfigureAwait(continueOnCapturedContext: false);
	}

	private static PhotoUpsertData AnalyzePhoto(string path, string filename, long slot, ExistingPhotoInfo? existing, ScanRefreshKind kind)
	{
		string timestamp = ResolveTimestamp(path, filename);
		string text = AppPaths.NormalizePathForDb(path);
		string worldName;
		string worldId;
		string matchSource;
		if (kind != ScanRefreshKind.PathOnly)
		{
			(worldName, worldId, matchSource) = ResolveWorldInfo(text, filename, path, existing);
		}
		else
		{
			worldId = existing?.WorldId;
			worldName = existing?.WorldName;
			matchSource = existing?.MatchSource;
		}
		string orientation;
		long? imageWidth;
		long? imageHeight;
		if (kind == ScanRefreshKind.PathOnly)
		{
			orientation = existing?.Orientation;
			imageWidth = existing?.ImageWidth;
			imageHeight = existing?.ImageHeight;
		}
		else
		{
			if (kind == ScanRefreshKind.MetadataOnly && existing != null)
			{
				long? imageWidth2 = existing.ImageWidth;
				if (imageWidth2.HasValue)
				{
					long valueOrDefault = imageWidth2.GetValueOrDefault();
					long? imageHeight2 = existing.ImageHeight;
					if (imageHeight2.HasValue)
					{
						long valueOrDefault2 = imageHeight2.GetValueOrDefault();
						string orientation2 = existing.Orientation;
						if (orientation2 != null && orientation2.Length > 0 && orientation2 != "unknown")
						{
							orientation = orientation2;
							imageWidth = valueOrDefault;
							imageHeight = valueOrDefault2;
							goto IL_0137;
						}
					}
				}
			}
			(orientation, imageWidth, imageHeight) = ResolveImageDimensions(path);
		}
		goto IL_0137;
		IL_0137:
		return new PhotoUpsertData
		{
			PhotoPath = text,
			PhotoFilename = filename,
			WorldId = worldId,
			WorldName = worldName,
			Timestamp = timestamp,
			Orientation = orientation,
			ImageWidth = imageWidth,
			ImageHeight = imageHeight,
			SourceSlot = slot,
			MatchSource = matchSource
		};
	}

	private static (string? worldName, string? worldId, string? matchSource) ResolveWorldInfo(string normalizedPath, string filename, string path, ExistingPhotoInfo? existing)
	{
		if (filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
		{
			var (text, text2) = ExtractVrcMetadataFromPng(path);
			if (text != null || text2 != null)
			{
				return (worldName: text, worldId: text2, matchSource: "metadata");
			}
		}
		if ((existing != null && (existing.WorldName != null || existing.WorldId != null)) ? true : false)
		{
			return (worldName: existing.WorldName, worldId: existing.WorldId, matchSource: "title");
		}
		return (worldName: null, worldId: null, matchSource: "unresolved");
	}

	public static (string? orientation, long? width, long? height) ProbeImageDimensions(string path)
	{
		return ResolveImageDimensions(path);
	}

	private static (string? orientation, long? width, long? height) ResolveImageDimensions(string path)
	{
		try
		{
			using IRandomAccessStreamWithContentType stream = StorageFile.GetFileFromPathAsync(path.Replace('/', Path.DirectorySeparatorChar)).AsTask().GetAwaiter()
				.GetResult()
				.OpenReadAsync()
				.AsTask()
				.GetAwaiter()
				.GetResult();
			BitmapDecoder result = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter()
				.GetResult();
			long num = result.OrientedPixelWidth;
			long num2 = result.OrientedPixelHeight;
			if (num <= 0 || num2 <= 0)
			{
				return (orientation: "unknown", width: null, height: null);
			}
			return (orientation: (num2 > num) ? "portrait" : "landscape", width: num, height: num2);
		}
		catch (Exception ex)
		{
			AppLogger.Warn("画像サイズを取得できなかったため unknown として扱います [" + path + "]: " + ex.Message);
			return (orientation: "unknown", width: null, height: null);
		}
	}

	private static (long w, long h) ReadImageSize(Stream stream, string path)
	{
		bool flag;
		switch (Path.GetExtension(path).TrimStart('.').ToLowerInvariant())
		{
		case "png":
			return ReadPngSize(stream);
		case "jpg":
		case "jpeg":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return ReadJpegSize(stream);
		}
		return (w: 0L, h: 0L);
	}

	private static (long w, long h) ReadPngSize(Stream stream)
	{
		byte[] array = new byte[24];
		if (stream.Read(array, 0, 24) < 24)
		{
			return (w: 0L, h: 0L);
		}
		if (array[0] != 137 || array[1] != 80)
		{
			return (w: 0L, h: 0L);
		}
		long item = (array[16] << 24) | (array[17] << 16) | (array[18] << 8) | array[19];
		long item2 = (array[20] << 24) | (array[21] << 16) | (array[22] << 8) | array[23];
		return (w: item, h: item2);
	}

	private static (long w, long h) ReadJpegSize(Stream stream)
	{
		byte[] array = new byte[4];
		if (stream.Read(array, 0, 2) < 2 || array[0] != byte.MaxValue || array[1] != 216)
		{
			return (w: 0L, h: 0L);
		}
		while (stream.Position < stream.Length && stream.Read(array, 0, 2) >= 2 && array[0] == byte.MaxValue)
		{
			byte b = array[1];
			if (stream.Read(array, 0, 2) < 2)
			{
				break;
			}
			int num = (array[0] << 8) | array[1];
			if (num < 2)
			{
				break;
			}
			if (b >= 192 && b <= 195)
			{
				if (stream.Read(array, 0, 1) < 1 || stream.Read(array, 0, 4) < 4)
				{
					break;
				}
				long item = (array[0] << 8) | array[1];
				return (w: (array[2] << 8) | array[3], h: item);
			}
			stream.Seek(num - 2, SeekOrigin.Current);
		}
		return (w: 0L, h: 0L);
	}

	private static string ResolveTimestamp(string path, string filename)
	{
		Match match = ReFilename().Match(filename);
		if (match.Success)
		{
			return match.Groups[1].Value + " " + match.Groups[2].Value.Replace('-', ':');
		}
		try
		{
			return File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
		}
		catch
		{
			return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
		}
	}

	private static (string? name, string? id) ExtractVrcMetadataFromPng(string path)
	{
		try
		{
			using FileStream fileStream = File.OpenRead(path);
			using BinaryReader binaryReader = new BinaryReader(fileStream);
			byte[] array = binaryReader.ReadBytes(8);
			if (array.Length < 8 || array[0] != 137 || array[1] != 80)
			{
				return (name: null, id: null);
			}
			while (fileStream.Position < fileStream.Length - 12)
			{
				byte[] array2 = binaryReader.ReadBytes(4);
				if (array2.Length < 4)
				{
					break;
				}
				int num = (array2[0] << 24) | (array2[1] << 16) | (array2[2] << 8) | array2[3];
				byte[] array3 = binaryReader.ReadBytes(4);
				if (array3.Length < 4)
				{
					break;
				}
				bool flag;
				switch (Encoding.ASCII.GetString(array3))
				{
				case "iTXt":
				{
					if (num > 4194304)
					{
						fileStream.Seek(num + 4, SeekOrigin.Current);
						continue;
					}
					byte[] array4 = binaryReader.ReadBytes(num);
					binaryReader.ReadBytes(4);
					int num2 = Array.IndexOf(array4, (byte)0);
					if (num2 < 0 || Encoding.Latin1.GetString(array4, 0, num2) != "XML:com.adobe.xmp")
					{
						continue;
					}
					int num3 = num2 + 1;
					if (num3 + 2 > array4.Length)
					{
						continue;
					}
					num3 += 2;
					int num4 = Array.IndexOf(array4, (byte)0, num3);
					if (num4 >= 0)
					{
						num3 = num4 + 1;
						int num5 = Array.IndexOf(array4, (byte)0, num3);
						if (num5 >= 0)
						{
							num3 = num5 + 1;
							return ParseVrcFromXmp(Encoding.UTF8.GetString(array4, num3, array4.Length - num3));
						}
					}
					continue;
				}
				case "IDAT":
				case "IEND":
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				if (!flag)
				{
					fileStream.Seek(num + 4, SeekOrigin.Current);
					continue;
				}
				break;
			}
		}
		catch (IOException ex)
		{
			AppLogger.Error("PNG メタデータ読み取り失敗 (I/O) [" + path + "]: " + ex.Message);
		}
		catch (Exception ex2)
		{
			AppLogger.Warn("PNG メタデータ解析に失敗しました [" + path + "]: " + ex2.Message);
		}
		return (name: null, id: null);
	}

	private static (string? name, string? id) ParseVrcFromXmp(string xmp)
	{
		Match match = ReWorldId().Match(xmp);
		Match match2 = ReWorldName().Match(xmp);
		return (name: match2.Success ? match2.Groups[1].Value : null, id: match.Success ? match.Groups[1].Value : null);
	}

	private static bool IsFileModifiedSinceTimestamp(string path, ExistingPhotoInfo existing)
	{
		try
		{
			DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
			if (!DateTime.TryParseExact(ResolveTimestamp(path, existing.PhotoFilename), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var result))
			{
				return false;
			}
			return lastWriteTimeUtc > result.ToUniversalTime().AddMinutes(1.0);
		}
		catch
		{
			return false;
		}
	}

	private static void CollectPhotosRecursive(long slot, string dir, List<(long, string, string)> files, HashSet<string> visitedDirs, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(dir);
			if ((File.GetAttributes(fullPath) & System.IO.FileAttributes.ReparsePoint) != System.IO.FileAttributes.None)
			{
				return;
			}
		}
		catch (Exception ex)
		{
			AppLogger.Warn("ディレクトリ属性を取得できません [" + dir + "]: " + ex.Message);
			return;
		}
		if (!visitedDirs.Add(fullPath))
		{
			return;
		}
		IEnumerable<string> enumerable;
		try
		{
			enumerable = Directory.EnumerateFileSystemEntries(dir);
		}
		catch (Exception ex2)
		{
			AppLogger.Warn("ディレクトリを読み取れません [" + dir + "]: " + ex2.Message);
			return;
		}
		foreach (string item in enumerable)
		{
			ct.ThrowIfCancellationRequested();
			string name = Path.GetFileName(item);
			if (Directory.Exists(item))
			{
				if (!name.StartsWith('.') && !Array.Exists(SkipDirs, (string s) => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
				{
					CollectPhotosRecursive(slot, item, files, visitedDirs, ct);
				}
			}
			else if (File.Exists(item))
			{
				string ext = Path.GetExtension(item).TrimStart('.').ToLowerInvariant();
				if (Array.Exists(SupportedExtensions, (string s) => s == ext))
				{
					files.Add((slot, name, item));
				}
			}
		}
	}

	public async Task<int> ResolveUnknownWorldsFromArchiveAsync(CancellationToken ct = default(CancellationToken))
	{
		string polarisArchiveDir = AppPaths.GetPolarisArchiveDir();
		if (polarisArchiveDir == null)
		{
			return 0;
		}
		List<ArchiveWorldVisitData> visits = LoadPolarisWorldVisits(polarisArchiveDir);
		await _db.UpsertArchiveWorldVisitsAsync(visits, ct).ConfigureAwait(continueOnCapturedContext: false);
		IReadOnlyList<(string, string)> readOnlyList = await _db.GetUnknownWorldPhotosAsync("all", ct).ConfigureAwait(continueOnCapturedContext: false);
		int resolved = 0;
		foreach (var (photoPath, timestamp) in readOnlyList)
		{
			ct.ThrowIfCancellationRequested();
			string text = await _db.LookupWorldNameFromArchiveAsync(timestamp, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (text != null)
			{
				await _db.UpdatePhotoWorldNameAsync(photoPath, text, "polaris_archive", ct).ConfigureAwait(continueOnCapturedContext: false);
				resolved++;
			}
		}
		return resolved;
	}

	private static List<ArchiveWorldVisitData> LoadPolarisWorldVisits(string archiveDir)
	{
		List<ArchiveWorldVisitData> list = new List<ArchiveWorldVisitData>();
		IEnumerable<string> files;
		try
		{
			files = Directory.GetFiles(archiveDir, "output_log_*.txt");
		}
		catch
		{
			return list;
		}
		Array.Sort((files as string[]) ?? files.ToArray());
		foreach (string item in files)
		{
			LoadVisitsFromLog(item, list);
		}
		return list;
	}

	private static IEnumerable<string> ReadCappedLines(string path)
	{
		using StreamReader sr = new StreamReader(path);
		StringBuilder sb = new StringBuilder();
		bool flag = false;
		while (true)
		{
			int num = sr.Read();
			switch (num)
			{
			case -1:
				if (sb.Length > 0 || flag)
				{
					yield return sb.ToString();
				}
				yield break;
			case 10:
				yield return sb.ToString();
				sb.Clear();
				flag = false;
				continue;
			case 13:
				if (sr.Peek() == 10)
				{
					sr.Read();
				}
				yield return sb.ToString();
				sb.Clear();
				flag = false;
				continue;
			}
			if (!flag)
			{
				if (sb.Length >= 65536)
				{
					flag = true;
				}
				else
				{
					sb.Append((char)num);
				}
			}
		}
	}

	private static void LoadVisitsFromLog(string logPath, List<ArchiveWorldVisitData> visits)
	{
		string text = null;
		string text2 = null;
		try
		{
			foreach (string item in ReadCappedLines(logPath))
			{
				Match match = ReLogTime.Match(item);
				DateTime result;
				string text3 = ((!match.Success) ? null : (DateTime.TryParseExact(match.Groups[1].Value, "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ? result.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null));
				Match match2 = ReLogEntering.Match(item);
				if (match2.Success)
				{
					if (text != null && text2 != null)
					{
						visits.Add(new ArchiveWorldVisitData
						{
							SourceLogName = Path.GetFileName(logPath),
							WorldName = text,
							JoinTime = text2,
							LeaveTime = null
						});
					}
					if (text3 != null)
					{
						text = match2.Groups[1].Value;
						text2 = text3;
					}
				}
				else if (ReLogLeftRoom.IsMatch(item) && text != null && text2 != null && text3 != null)
				{
					visits.Add(new ArchiveWorldVisitData
					{
						SourceLogName = Path.GetFileName(logPath),
						WorldName = text,
						JoinTime = text2,
						LeaveTime = text3
					});
					text = null;
					text2 = null;
				}
			}
			if (text != null && text2 != null)
			{
				visits.Add(new ArchiveWorldVisitData
				{
					SourceLogName = Path.GetFileName(logPath),
					WorldName = text,
					JoinTime = text2
				});
			}
		}
		catch (Exception ex)
		{
			AppLogger.Warn("ログ読み取りに失敗しました [" + logPath + "]: " + ex.Message);
		}
	}
}
