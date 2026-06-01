using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Models;

namespace Alpheratz.Services;

public sealed class PhotoService
{
	private readonly AlpheratzDb _db;

	private readonly SemaphoreSlim _bulkWriteGate = new SemaphoreSlim(1, 1);

	public PhotoService(AlpheratzDb db)
	{
		_db = db;
	}

	private PhotoQueryParams ToParams(PhotoQueryPayload p, bool includePhash = false)
	{
		return new PhotoQueryParams
		{
			StartDate = p.startDate,
			EndDate = p.endDate,
			WorldQuery = p.worldQuery,
			WorldExacts = p.worldExacts,
			Orientation = p.orientation,
			FavoritesOnly = p.favoritesOnly,
			TagFilters = p.tagFilters,
			SourceSlot = p.sourceSlot,
			Limit = p.limit,
			Offset = p.offset,
			IncludePhash = includePhash,
			Sort = p.sortMode
		};
	}

	public async Task<PhotoPageDto> GetPhotosAsync(PhotoQueryPayload payload, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			PhotoPage photoPage = await _db.GetPhotosPageAsync(ToParams(payload, payload.includePhash == true), ct).ConfigureAwait(continueOnCapturedContext: false);
			return new PhotoPageDto
			{
				items = photoPage.Items,
				total = photoPage.Total
			};
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoService.GetPhotosAsync: threw: {value}");
			throw;
		}
	}

	public Task<IReadOnlyList<MonthSummaryItem>> GetMonthSummaryAsync(PhotoQueryPayload payload, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			return _db.GetMonthSummaryAsync(ToParams(payload), ct);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoService.GetMonthSummaryAsync: threw: {value}");
			throw;
		}
	}

	public async Task<IReadOnlyList<PhotoRecordDto>> GetWorldGroupPhotosAsync(string groupKey, string? startDate, string? endDate, long? sourceSlot, string? orientation, bool? favoritesOnly, IReadOnlyList<string>? tagFilters, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			bool num = groupKey.Contains('\\') || groupKey.Contains('/');
			string worldIdExact = null;
			bool worldIdIsNull = false;
			IReadOnlyList<string> worldExacts = null;
			string photoPathExact = null;
			if (num)
			{
				photoPathExact = groupKey;
			}
			else if (groupKey.StartsWith("id:", StringComparison.Ordinal))
			{
				worldIdExact = groupKey.Substring(3);
			}
			else if (groupKey.StartsWith("name:", StringComparison.Ordinal))
			{
				worldExacts = new _003C_003Ez__ReadOnlySingleElementList<string>(groupKey.Substring(5));
				worldIdIsNull = true;
			}
			else if (groupKey == "unknown")
			{
				worldExacts = new _003C_003Ez__ReadOnlySingleElementList<string>("unknown");
				worldIdIsNull = true;
			}
			else
			{
				worldExacts = new _003C_003Ez__ReadOnlySingleElementList<string>(groupKey);
			}
			return (await _db.GetPhotosPageAsync(new PhotoQueryParams
			{
				StartDate = startDate,
				EndDate = endDate,
				WorldExacts = worldExacts,
				WorldIdExact = worldIdExact,
				WorldIdIsNull = worldIdIsNull,
				PhotoPathExact = photoPathExact,
				SourceSlot = sourceSlot,
				Orientation = orientation,
				FavoritesOnly = favoritesOnly,
				TagFilters = tagFilters
			}, ct).ConfigureAwait(continueOnCapturedContext: false)).Items;
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoService.GetWorldGroupPhotosAsync: threw: {value}");
			throw;
		}
	}

	public async Task<IReadOnlyList<WorldFilterOptionDto>> GetWorldFilterOptionsAsync(CancellationToken ct = default(CancellationToken))
	{
		try
		{
			return await _db.GetWorldFilterOptionsAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoService.GetWorldFilterOptionsAsync: threw: {value}");
			throw;
		}
	}

	public async Task<IReadOnlyDictionary<string, long>> GetTagFilterCountsAsync(CancellationToken ct = default(CancellationToken))
	{
		try
		{
			return await _db.GetTagFilterCountsAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoService.GetTagFilterCountsAsync: threw: {value}");
			throw;
		}
	}

	public async Task<IReadOnlyList<SelectedPhotoRefDto>> GetSelectedPhotoRefsAsync(IReadOnlyList<string> photoPaths, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			List<SelectedPhotoRefDto> result = new List<SelectedPhotoRefDto>();
			foreach (string photoPath in photoPaths)
			{
				PhotoRecordDto photoRecordDto = await _db.GetPhotoRecordAsync(photoPath, includePhash: false, ct).ConfigureAwait(continueOnCapturedContext: false);
				if ((object)photoRecordDto != null)
				{
					result.Add(new SelectedPhotoRefDto
					{
						photo_path = photoRecordDto.photo_path,
						source_slot = photoRecordDto.source_slot
					});
				}
			}
			return result;
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoService.GetSelectedPhotoRefsAsync: threw: {value}");
			throw;
		}
	}

	public Task SetPhotoFavoriteAsync(string photoPath, bool isFavorite, long sourceSlot, CancellationToken ct = default(CancellationToken))
	{
		return _db.SetPhotoFavoriteAsync(photoPath, isFavorite, ct);
	}

	public async Task BulkSetPhotoFavoriteAsync(IReadOnlyList<SelectedPhotoRefDto> photos, bool isFavorite, CancellationToken ct = default(CancellationToken))
	{
		await _bulkWriteGate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			foreach (SelectedPhotoRefDto photo in photos)
			{
				ct.ThrowIfCancellationRequested();
				await _db.SetPhotoFavoriteAsync(photo.photo_path, isFavorite, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		finally
		{
			_bulkWriteGate.Release();
		}
	}

	public Task AddPhotoTagAsync(string photoPath, string tag, long sourceSlot, CancellationToken ct = default(CancellationToken))
	{
		return _db.AddPhotoTagAsync(photoPath, tag, ct);
	}

	public async Task BulkAddPhotoTagAsync(IReadOnlyList<SelectedPhotoRefDto> photos, string tag, CancellationToken ct = default(CancellationToken))
	{
		await _bulkWriteGate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			foreach (SelectedPhotoRefDto photo in photos)
			{
				ct.ThrowIfCancellationRequested();
				await _db.AddPhotoTagAsync(photo.photo_path, tag, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		finally
		{
			_bulkWriteGate.Release();
		}
	}

	public Task RemovePhotoTagAsync(string photoPath, string tag, long sourceSlot, CancellationToken ct = default(CancellationToken))
	{
		return _db.RemovePhotoTagAsync(photoPath, tag, ct);
	}

	public Task<IReadOnlyList<string>> GetPhotoTagsAsync(string photoPath, long sourceSlot, CancellationToken ct = default(CancellationToken))
	{
		return _db.GetPhotoTagsAsync(photoPath, ct);
	}

	public Task<(int copied, int skipped)> BulkCopyPhotosAsync(IReadOnlyList<SelectedPhotoRefDto> photos, string destinationFolder, CancellationToken ct = default(CancellationToken))
	{
		return Task.Run(delegate
		{
			try
			{
				if (!Directory.Exists(destinationFolder))
				{
					throw new DirectoryNotFoundException("コピー先フォルダが見つかりません: " + destinationFolder);
				}
				int num = 0;
				int num2 = 0;
				foreach (SelectedPhotoRefDto photo in photos)
				{
					ct.ThrowIfCancellationRequested();
					string text = photo.photo_path.Replace('/', Path.DirectorySeparatorChar);
					string fileName = Path.GetFileName(text);
					string text2 = Path.Combine(destinationFolder, fileName);
					try
					{
						File.Copy(text, text2, overwrite: false);
						num2++;
					}
					catch (IOException ex)
					{
						AppLogger.Warn($"PhotoService.BulkCopyPhotosAsync: skip [{text}] -> [{text2}]: {ex.Message}");
						num++;
					}
					catch (UnauthorizedAccessException ex2)
					{
						AppLogger.Warn($"PhotoService.BulkCopyPhotosAsync: skip [{text}] -> [{text2}]: {ex2.Message}");
						num++;
					}
				}
				return (copied: num2, skipped: num);
			}
			catch (Exception value)
			{
				AppLogger.Error($"PhotoService.BulkCopyPhotosAsync.work: threw: {value}");
				throw;
			}
		}, ct);
	}
}
