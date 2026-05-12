using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Models;

namespace Alpheratz.Services;

public sealed class PhotoService
{
    private readonly AlpheratzDb _db;
    // SQLite への並列書込を直列化するためのセマフォ。
    // BulkSet*/BulkAdd* など複数行更新では Task.WhenAll で並列発行すると
    // SQLite が SQLITE_BUSY を返してパーシャル更新になるため、1 件ずつ走らせる。
    private readonly SemaphoreSlim _bulkWriteGate = new(1, 1);

    public PhotoService(AlpheratzDb db)
    {
        AppLogger.Trace("PhotoService.ctor: enter");
        _db = db;
        AppLogger.Trace("PhotoService.ctor: exit");
    }

    private PhotoQueryParams ToParams(PhotoQueryPayload p, bool includePhash = false) => new()
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
        Sort = p.sortMode,
    };

    public async Task<PhotoPageDto> GetPhotosAsync(PhotoQueryPayload payload, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.GetPhotosAsync: enter limit={payload.limit} offset={payload.offset}");
        try
        {
            var page = await _db.GetPhotosPageAsync(ToParams(payload, payload.includePhash == true), ct).ConfigureAwait(false);
            AppLogger.Trace($"PhotoService.GetPhotosAsync: exit total={page.Total}");
            return new PhotoPageDto { items = page.Items, total = page.Total };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.GetPhotosAsync: threw: {ex}");
            throw;
        }
    }

    public Task<IReadOnlyList<Core.Database.MonthSummaryItem>> GetMonthSummaryAsync(PhotoQueryPayload payload, CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoService.GetMonthSummaryAsync: enter");
        try
        {
            var task = _db.GetMonthSummaryAsync(ToParams(payload), ct);
            AppLogger.Trace("PhotoService.GetMonthSummaryAsync: exit");
            return task;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.GetMonthSummaryAsync: threw: {ex}");
            throw;
        }
    }

    public async Task<GroupedPhotoPageDto> GetWorldGroupedPhotosAsync(PhotoQueryPayload payload, CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoService.GetWorldGroupedPhotosAsync: enter");
        try
        {
            var result = await _db.GetWorldGroupedPageAsync(ToParams(payload with { includePhash = null }), ct).ConfigureAwait(false);
            AppLogger.Trace($"PhotoService.GetWorldGroupedPhotosAsync: exit groups={result.items.Count} total={result.total}");
            return result;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.GetWorldGroupedPhotosAsync: threw: {ex}");
            throw;
        }
    }

    public async Task<IReadOnlyList<PhotoRecordDto>> GetWorldGroupPhotosAsync(
        string groupKey, string? startDate, string? endDate, long? sourceSlot,
        string? orientation, bool? favoritesOnly, IReadOnlyList<string>? tagFilters,
        CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.GetWorldGroupPhotosAsync: enter groupKey={groupKey}");
        try
        {
            var isPathKey = groupKey.Contains('\\') || groupKey.Contains('/');
            var page = await _db.GetPhotosPageAsync(new PhotoQueryParams
            {
                StartDate = startDate,
                EndDate = endDate,
                WorldExacts = isPathKey ? null : [groupKey],
                PhotoPathExact = isPathKey ? groupKey : null,
                SourceSlot = sourceSlot,
                Orientation = orientation,
                FavoritesOnly = favoritesOnly,
                TagFilters = tagFilters,
            }, ct).ConfigureAwait(false);
            AppLogger.Trace($"PhotoService.GetWorldGroupPhotosAsync: exit count={page.Items.Count}");
            return page.Items;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.GetWorldGroupPhotosAsync: threw: {ex}");
            throw;
        }
    }

    public async Task<IReadOnlyList<WorldFilterOptionDto>> GetWorldFilterOptionsAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoService.GetWorldFilterOptionsAsync: enter");
        try
        {
            var result = await _db.GetWorldFilterOptionsAsync(ct).ConfigureAwait(false);
            AppLogger.Trace($"PhotoService.GetWorldFilterOptionsAsync: exit count={result.Count}");
            return result;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.GetWorldFilterOptionsAsync: threw: {ex}");
            throw;
        }
    }

    public async Task<IReadOnlyList<SelectedPhotoRefDto>> GetSelectedPhotoRefsAsync(IReadOnlyList<string> photoPaths, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.GetSelectedPhotoRefsAsync: enter count={photoPaths.Count}");
        try
        {
            var result = new List<SelectedPhotoRefDto>();
            foreach (var path in photoPaths)
            {
                var photo = await _db.GetPhotoRecordAsync(path, false, ct).ConfigureAwait(false);
                if (photo is not null)
                    result.Add(new SelectedPhotoRefDto { photo_path = photo.photo_path, source_slot = photo.source_slot });
            }
            AppLogger.Trace($"PhotoService.GetSelectedPhotoRefsAsync: exit resolved={result.Count}");
            return result;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.GetSelectedPhotoRefsAsync: threw: {ex}");
            throw;
        }
    }

    public Task SetPhotoFavoriteAsync(string photoPath, bool isFavorite, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.SetPhotoFavoriteAsync: enter isFavorite={isFavorite}");
        var task = _db.SetPhotoFavoriteAsync(photoPath, isFavorite, ct);
        AppLogger.Trace("PhotoService.SetPhotoFavoriteAsync: exit");
        return task;
    }

    public async Task BulkSetPhotoFavoriteAsync(IReadOnlyList<SelectedPhotoRefDto> photos, bool isFavorite, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.BulkSetPhotoFavoriteAsync: enter count={photos.Count} isFavorite={isFavorite}");
        await _bulkWriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var p in photos)
            {
                ct.ThrowIfCancellationRequested();
                await _db.SetPhotoFavoriteAsync(p.photo_path, isFavorite, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _bulkWriteGate.Release();
        }
        AppLogger.Trace("PhotoService.BulkSetPhotoFavoriteAsync: exit");
    }

    public Task AddPhotoTagAsync(string photoPath, string tag, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.AddPhotoTagAsync: enter tag={tag}");
        var task = _db.AddPhotoTagAsync(photoPath, tag, ct);
        AppLogger.Trace("PhotoService.AddPhotoTagAsync: exit");
        return task;
    }

    public async Task BulkAddPhotoTagAsync(IReadOnlyList<SelectedPhotoRefDto> photos, string tag, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.BulkAddPhotoTagAsync: enter count={photos.Count} tag={tag}");
        await _bulkWriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var p in photos)
            {
                ct.ThrowIfCancellationRequested();
                await _db.AddPhotoTagAsync(p.photo_path, tag, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _bulkWriteGate.Release();
        }
        AppLogger.Trace("PhotoService.BulkAddPhotoTagAsync: exit");
    }

    public Task RemovePhotoTagAsync(string photoPath, string tag, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.RemovePhotoTagAsync: enter tag={tag}");
        var task = _db.RemovePhotoTagAsync(photoPath, tag, ct);
        AppLogger.Trace("PhotoService.RemovePhotoTagAsync: exit");
        return task;
    }

    public Task<IReadOnlyList<string>> GetPhotoTagsAsync(string photoPath, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoService.GetPhotoTagsAsync: enter");
        var task = _db.GetPhotoTagsAsync(photoPath, ct);
        AppLogger.Trace("PhotoService.GetPhotoTagsAsync: exit");
        return task;
    }

    /// <summary>
    /// 選択した写真を destinationFolder にコピーする。
    /// R2-A-7: copied / skipped 件数を返すことで呼出側のメッセージを実態に合わせる。
    /// R2-A-8: DB に格納された photo_path は forward-slash 区切りである可能性があるため、
    ///         File.Copy に渡す前にネイティブのディレクトリセパレータへ正規化する
    ///         （UNC \\server\share 形式での失敗を防ぐ）。
    /// </summary>
    public Task<(int copied, int skipped)> BulkCopyPhotosAsync(IReadOnlyList<SelectedPhotoRefDto> photos, string destinationFolder, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.BulkCopyPhotosAsync: enter count={photos.Count} dest={destinationFolder}");
        var task = Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(destinationFolder))
                    throw new DirectoryNotFoundException($"コピー先フォルダが見つかりません: {destinationFolder}");

                var skipped = 0;
                var copied = 0;
                foreach (var photo in photos)
                {
                    ct.ThrowIfCancellationRequested();
                    var nativeSource = photo.photo_path.Replace('/', Path.DirectorySeparatorChar);
                    var filename = Path.GetFileName(nativeSource);
                    var dest = Path.Combine(destinationFolder, filename);
                    try
                    {
                        File.Copy(nativeSource, dest, overwrite: false);
                        copied++;
                    }
                    catch (IOException ex)
                    {
                        // 既に同名ファイルが存在する場合などは個別にスキップして処理を継続する。
                        // 1 件の失敗で全体が中断されると、複数選択コピーが事実上使えなくなるため。
                        AppLogger.Warn($"PhotoService.BulkCopyPhotosAsync: skip [{nativeSource}] -> [{dest}]: {ex.Message}");
                        skipped++;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        AppLogger.Warn($"PhotoService.BulkCopyPhotosAsync: skip [{nativeSource}] -> [{dest}]: {ex.Message}");
                        skipped++;
                    }
                }
                AppLogger.Trace($"PhotoService.BulkCopyPhotosAsync.work: copied={copied} skipped={skipped}");
                return (copied, skipped);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"PhotoService.BulkCopyPhotosAsync.work: threw: {ex}");
                throw;
            }
        }, ct);
        AppLogger.Trace("PhotoService.BulkCopyPhotosAsync: exit (dispatched)");
        return task;
    }

    /// <summary>紛失写真の救済 UI 用。is_missing=1 の写真を返す。</summary>
    public Task<IReadOnlyList<PhotoRecordDto>> GetMissingPhotosAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoService.GetMissingPhotosAsync: enter");
        var task = _db.GetMissingPhotosAsync(ct);
        AppLogger.Trace("PhotoService.GetMissingPhotosAsync: exit");
        return task;
    }

    /// <summary>紛失写真の救済 UI 用。指定パスを DB から完全削除する。</summary>
    public Task DeletePhotosByPathsAsync(IReadOnlyList<string> photoPaths, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.DeletePhotosByPathsAsync: enter count={photoPaths.Count}");
        var task = _db.DeletePhotosByPathsAsync(photoPaths, ct);
        AppLogger.Trace("PhotoService.DeletePhotosByPathsAsync: exit");
        return task;
    }
}