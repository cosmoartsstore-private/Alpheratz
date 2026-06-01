using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

public sealed class WorldService
{
	public const int WorldMatchDistanceThreshold = 124;

	private readonly AlpheratzDb _db;

	private readonly PhotoScanner _scanner;

	public WorldService(AlpheratzDb db, PhotoScanner scanner)
	{
		_db = db;
		_scanner = scanner;
	}

	public async Task OpenWorldUrlAsync(string worldId, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			if (!worldId.StartsWith("wrld_", StringComparison.Ordinal))
			{
				throw new ArgumentException("VRChat ワールドIDの形式が不正です: " + worldId);
			}
			await Launcher.LaunchUriAsync(new Uri("https://vrchat.com/home/world/" + worldId + "/info")).AsTask(ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"WorldService.OpenWorldUrlAsync: threw: {value}");
			throw;
		}
	}

	public async Task OpenTweetIntentAsync(string intentUrl, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			if (!intentUrl.StartsWith("https://twitter.com/intent/tweet?text=", StringComparison.Ordinal) && !intentUrl.StartsWith("https://x.com/intent/tweet?text=", StringComparison.Ordinal))
			{
				throw new ArgumentException("Tweet intent URL の形式が不正です: " + intentUrl);
			}
			await Launcher.LaunchUriAsync(new Uri(intentUrl)).AsTask(ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"WorldService.OpenTweetIntentAsync: threw: {value}");
			throw;
		}
	}

	public Task ShowInExplorerAsync(string path, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			string fullPath = Path.GetFullPath(path.Replace('/', '\\'));
			Process.Start(new ProcessStartInfo
			{
				FileName = "explorer.exe",
				UseShellExecute = false,
				ArgumentList = { "/select,", fullPath }
			});
		}
		catch (Exception value)
		{
			AppLogger.Error($"WorldService.ShowInExplorerAsync: threw: {value}");
			throw;
		}
		return Task.CompletedTask;
	}

	public async Task CopyImageToClipboardAsync(string photoPath, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			StorageFile storageFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(photoPath.Replace('/', '\\'))).AsTask(ct).ConfigureAwait(continueOnCapturedContext: false);
			DataPackage dataPackage = new DataPackage();
			dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromFile(storageFile));
			dataPackage.SetStorageItems(new StorageFile[1] { storageFile });
			Clipboard.SetContent(dataPackage);
			Clipboard.Flush();
		}
		catch (Exception value)
		{
			AppLogger.Error($"WorldService.CopyImageToClipboardAsync: threw: {value}");
			throw;
		}
	}

	public Task ApplyWorldMatchFromPhotoAsync(string targetPhotoPath, string sourcePhotoPath, CancellationToken ct = default(CancellationToken))
	{
		return _db.ApplyWorldMatchFromPhotoAsync(targetPhotoPath, sourcePhotoPath, ct);
	}

	public Task<int> ResolveUnknownWorldsFromArchiveAsync(CancellationToken ct = default(CancellationToken))
	{
		return _scanner.ResolveUnknownWorldsFromArchiveAsync(ct);
	}

	internal static (AlpheratzDb.KnownWorldRow Row, int Distance)? FindBestMatchWithDetails(string targetPhash, IReadOnlyList<AlpheratzDb.KnownWorldRow> candidates)
	{
		List<string> list = PdqHasher.ParseHashVariants(targetPhash);
		if (list.Count == 0)
		{
			return null;
		}
		int num = int.MaxValue;
		AlpheratzDb.KnownWorldRow knownWorldRow = null;
		foreach (AlpheratzDb.KnownWorldRow candidate in candidates)
		{
			List<string> rightVariants = PdqHasher.ParseHashVariants(candidate.Phash);
			int? num2 = PdqHasher.ClosestHashDistance(list, rightVariants);
			if (num2.HasValue && num2.Value <= 124 && num2.Value < num)
			{
				num = num2.Value;
				knownWorldRow = candidate;
				if (num == 0)
				{
					break;
				}
			}
		}
		if ((object)knownWorldRow == null)
		{
			return null;
		}
		return (knownWorldRow, num);
	}

	internal static (AlpheratzDb.KnownWorldRow Row, int Distance)? FindBestMatchWithDetails(string targetPhash, IReadOnlyList<AlpheratzDb.KnownWorldRow> candidates, IReadOnlyList<IReadOnlyList<string>> parsedVariants)
	{
		List<string> list = PdqHasher.ParseHashVariants(targetPhash);
		if (list.Count == 0)
		{
			return null;
		}
		int num = int.MaxValue;
		AlpheratzDb.KnownWorldRow knownWorldRow = null;
		for (int i = 0; i < candidates.Count; i++)
		{
			int? num2 = PdqHasher.ClosestHashDistance(list, parsedVariants[i]);
			if (num2.HasValue && num2.Value <= 124 && num2.Value < num)
			{
				num = num2.Value;
				knownWorldRow = candidates[i];
				if (num == 0)
				{
					break;
				}
			}
		}
		if ((object)knownWorldRow == null)
		{
			return null;
		}
		return (knownWorldRow, num);
	}

	internal static List<(AlpheratzDb.KnownWorldRow Row, int Distance)> RankCandidatesByDistance(string targetPhash, IReadOnlyList<AlpheratzDb.KnownWorldRow> candidates)
	{
		List<string> list = PdqHasher.ParseHashVariants(targetPhash);
		if (list.Count == 0)
		{
			return new List<(AlpheratzDb.KnownWorldRow, int)>();
		}
		List<(AlpheratzDb.KnownWorldRow, int)> list2 = new List<(AlpheratzDb.KnownWorldRow, int)>();
		foreach (AlpheratzDb.KnownWorldRow candidate in candidates)
		{
			List<string> rightVariants = PdqHasher.ParseHashVariants(candidate.Phash);
			int? num = PdqHasher.ClosestHashDistance(list, rightVariants);
			if (num.HasValue)
			{
				list2.Add((candidate, num.Value));
			}
		}
		list2.Sort(((AlpheratzDb.KnownWorldRow Row, int Distance) a, (AlpheratzDb.KnownWorldRow Row, int Distance) b) => a.Distance.CompareTo(b.Distance));
		return list2;
	}
}
