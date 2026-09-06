using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging;
using Alpheratz.Core.Imaging.Pdq;
using Alpheratz.Models;
using Windows.Storage;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Services;

/// <summary>一括写真更新で個別に完了できなかった写真と理由。</summary>
public sealed record PhotoBulkUpdateFailure(
    SelectedPhotoRefDto Photo,
    string Reason,
    bool IsCanceled);

/// <summary>
/// 一括写真更新の個別結果。
/// 呼び出し側は完了済み写真だけを画面へ反映し、失敗写真を再試行対象として残せる。
/// </summary>
public sealed record PhotoBulkUpdateResult(
    IReadOnlyList<SelectedPhotoRefDto> SucceededPhotos,
    IReadOnlyList<PhotoBulkUpdateFailure> FailedPhotos)
{
    public int SucceededCount => SucceededPhotos.Count;
    public int FailedCount => FailedPhotos.Count;
    public bool WasCanceled => FailedPhotos.Any(failure => failure.IsCanceled);
}

/// <summary>重複写真検出の処理段階。</summary>
public enum DuplicatePhotoScanStage
{
    InspectingFiles,
    ComparingContents,
}

/// <summary>重複写真検出の進捗。</summary>
public sealed record DuplicatePhotoScanProgress(
    DuplicatePhotoScanStage Stage,
    int Processed,
    int Total);

/// <summary>ファイル内容が一致した写真1件。</summary>
public sealed record ExactDuplicatePhoto(
    string PhotoFilename,
    string PhotoPath,
    string Timestamp,
    long SourceSlot,
    long FileSize);

/// <summary>同じファイル内容を持つ写真グループ。</summary>
public sealed record ExactDuplicatePhotoGroup(
    string ContentHash,
    long FileSize,
    IReadOnlyList<ExactDuplicatePhoto> Photos);

/// <summary>重複写真検出の完了結果。</summary>
public sealed record ExactDuplicateDetectionResult(
    IReadOnlyList<ExactDuplicatePhotoGroup> Groups,
    int TotalPhotoCount,
    int UnreadablePhotoCount);

/// <summary>削除直前に完全一致を再検証するための対象情報。</summary>
public sealed record DuplicatePhotoDeleteTarget(
    SelectedPhotoRefDto Photo,
    string ExpectedContentHash,
    long ExpectedFileSize,
    IReadOnlyList<string> RetainedPhotoPaths);

/// <summary>重複写真を削除できなかった理由の分類。</summary>
public enum DuplicatePhotoDeleteFailureKind
{
    NoLongerRegistered,
    FileChanged,
    RetainedPhotoUnavailable,
    ComparisonFailed,
    DeleteFailed,
}

/// <summary>削除できなかった写真と理由。</summary>
public sealed record DuplicatePhotoDeleteFailure(
    SelectedPhotoRefDto Photo,
    DuplicatePhotoDeleteFailureKind Kind);

/// <summary>重複写真の削除結果。DatabaseUpdated=false の場合も DeletedPhotos の元ファイルは削除済み。</summary>
public sealed record DuplicatePhotoDeleteResult(
    IReadOnlyList<SelectedPhotoRefDto> DeletedPhotos,
    IReadOnlyList<DuplicatePhotoDeleteFailure> FailedPhotos,
    bool DatabaseUpdated);

/// <summary>
/// 写真関連の操作を集約するアプリケーションサービス。
/// AlpheratzDb の薄いラッパに見えるが、以下を加味して 1 層挟む意味がある：
///   - 一括更新の直列化 (_bulkWriteGate)
///   - DTO ↔ DB クエリパラメータの変換
///   - ファイルシステム操作 (BulkCopyPhotosAsync)
/// ViewModel から見ると「写真にまつわる操作の唯一の入口」になる。
/// </summary>
public sealed class PhotoService
{
    private readonly AlpheratzDb _db;
    private readonly ThumbnailService? _thumbnailService;
    // SQLite への並列書込を直列化するためのセマフォ。
    // BulkSet*/BulkAdd* で 100 件単位を Task.WhenAll で並列発行すると、SQLite の
    // ファイルロック競合で SQLITE_BUSY が返り、一部行だけ書き込まれてパーシャル更新になる
    // (例：100 件一括お気に入り解除のうち 40 件だけ反映、残り 60 件は元のまま、という状態が発生)。
    // WAL モードでも複数 writer は許容されないため、明示的に 1 件ずつ直列化する。
    // 読み取りは並列のままで影響しない。
    private readonly SemaphoreSlim _bulkWriteGate = new(1, 1);

    public PhotoService(AlpheratzDb db, ThumbnailService? thumbnailService = null)
    {
        AppLogger.Trace("PhotoService.ctor: enter");
        _db = db;
        _thumbnailService = thumbnailService;
        AppLogger.Trace("PhotoService.ctor: exit");
    }

    private static Task RunWriteOffUiThread(Func<Task> write, CancellationToken ct)
        => Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            await write().ConfigureAwait(false);
        }, ct);

    private static Task<T> RunWriteOffUiThread<T>(Func<Task<T>> write, CancellationToken ct)
        => Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            return await write().ConfigureAwait(false);
        }, ct);

    // DB の公開 API は Task を返すが内部では同期 SQLite API を使うため、
    // 一覧取得を UI スレッドから明示的に切り離す。
    private static Task<T> RunReadOffUiThread<T>(Func<Task<T>> read, CancellationToken ct)
        => Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            return await read().ConfigureAwait(false);
        }, ct);

    /// <summary>外向け Payload を DB 内部の Params に変換する。includePhash は転送量削減のため別パラメータ化。</summary>
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

    /// <summary>フィルタ条件で写真をページ単位で取得する。詳細は AlpheratzDb.GetPhotosPageAsync を参照。</summary>
    public async Task<PhotoPageDto> GetPhotosAsync(PhotoQueryPayload payload, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.GetPhotosAsync: enter limit={payload.limit} offset={payload.offset}");
        try
        {
            var query = ToParams(payload, payload.includePhash == true);
            var page = await RunReadOffUiThread(
                () => _db.GetPhotosPageAsync(query, ct),
                ct).ConfigureAwait(false);
            AppLogger.Trace($"PhotoService.GetPhotosAsync: exit total={page.Total}");
            return new PhotoPageDto { items = page.Items, total = page.Total };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.GetPhotosAsync: threw: {ex}");
            throw;
        }
    }

    /// <summary>MonthNav 用に同じフィルタ条件で年月別件数を取得する。</summary>
    public async Task<IReadOnlyList<Core.Database.MonthSummaryItem>> GetMonthSummaryAsync(
        PhotoQueryPayload payload,
        CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoService.GetMonthSummaryAsync: enter");
        try
        {
            var query = ToParams(payload);
            var result = await RunReadOffUiThread(
                () => _db.GetMonthSummaryAsync(query, ct),
                ct).ConfigureAwait(false);
            AppLogger.Trace("PhotoService.GetMonthSummaryAsync: exit");
            return result;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.GetMonthSummaryAsync: threw: {ex}");
            throw;
        }
    }

    /// <summary>
    /// グループドリルダウン用のクエリ。groupKey をワールド名として完全一致検索し、
    /// 空文字または空白だけの値は「不明なワールド」として扱う。
    /// </summary>
    public async Task<IReadOnlyList<PhotoRecordDto>> GetWorldGroupPhotosAsync(
        string groupKey, string? startDate, string? endDate, long? sourceSlot,
        string? orientation, bool? favoritesOnly, IReadOnlyList<string>? tagFilters,
        CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.GetWorldGroupPhotosAsync: enter groupKey={groupKey}");
        try
        {
            var page = await _db.GetPhotosPageAsync(new PhotoQueryParams
            {
                StartDate = startDate,
                EndDate = endDate,
                WorldExacts = [groupKey],
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

    /// <summary>フィルタパネルのワールド候補リストを取得する（撮影枚数の多い順）。</summary>
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

    public async Task<IReadOnlyDictionary<string, long>> GetTagFilterCountsAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoService.GetTagFilterCountsAsync: enter");
        try
        {
            var result = await _db.GetTagFilterCountsAsync(ct).ConfigureAwait(false);
            AppLogger.Trace($"PhotoService.GetTagFilterCountsAsync: exit count={result.Count}");
            return result;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.GetTagFilterCountsAsync: threw: {ex}");
            throw;
        }
    }

    /// <summary>
    /// 選択写真パスの配列から (photo_path, source_slot) ペアを引く。
    /// マルチセレクト中に「どのスロットの写真か」を保持する必要があり、UI 側では
    /// パスしか持っていないことがあるため DB に問い合わせる。
    /// </summary>
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

    /// <summary>単体お気に入りトグル。sourceSlot は将来スロット別更新が必要になった場合のために受け取る。</summary>
    public Task SetPhotoFavoriteAsync(string photoPath, bool isFavorite, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.SetPhotoFavoriteAsync: enter isFavorite={isFavorite}");
        var task = _db.SetPhotoFavoriteAsync(photoPath, isFavorite, ct);
        AppLogger.Trace("PhotoService.SetPhotoFavoriteAsync: exit");
        return task;
    }

    /// <summary>
    /// 選択写真を 1 件ずつ更新し、完了・失敗を写真単位で返す。
    /// キャンセルは写真間で受け付け、実行中の 1 写真を完了させてから残りを未更新として返す。
    /// </summary>
    internal async Task<PhotoBulkUpdateResult> RunBulkPhotoUpdatesAsync(
        IReadOnlyList<SelectedPhotoRefDto> photos,
        string operationName,
        Func<SelectedPhotoRefDto, Task> update,
        CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.RunBulkPhotoUpdatesAsync: enter operation={operationName} count={photos.Count}");
        if (photos.Count == 0)
        {
            AppLogger.Trace("PhotoService.RunBulkPhotoUpdatesAsync: exit (empty)");
            return new PhotoBulkUpdateResult([], []);
        }

        var succeeded = new List<SelectedPhotoRefDto>(photos.Count);
        var failed = new List<PhotoBulkUpdateFailure>();
        var gateEntered = false;
        try
        {
            try
            {
                await _bulkWriteGate.WaitAsync(ct).ConfigureAwait(false);
                gateEntered = true;
            }
            catch (OperationCanceledException)
            {
                AddCanceledFailures(failed, photos, 0);
                AppLogger.Trace($"PhotoService.RunBulkPhotoUpdatesAsync: exit operation={operationName} succeeded=0 failed={failed.Count} canceled=true");
                return new PhotoBulkUpdateResult([], failed.ToArray());
            }

            await Task.Run(async () =>
            {
                for (var index = 0; index < photos.Count; index++)
                {
                    if (ct.IsCancellationRequested)
                    {
                        AddCanceledFailures(failed, photos, index);
                        break;
                    }

                    var photo = photos[index];
                    try
                    {
                        await update(photo).ConfigureAwait(false);
                        succeeded.Add(photo);
                    }
                    catch (OperationCanceledException ex)
                    {
                        AppLogger.Warn($"PhotoService.RunBulkPhotoUpdatesAsync: canceled operation={operationName} path={photo.photo_path}: {ex.Message}");
                        AddCanceledFailures(failed, photos, index);
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"PhotoService.RunBulkPhotoUpdatesAsync: failed operation={operationName} path={photo.photo_path}: {ex.Message}");
                        failed.Add(new PhotoBulkUpdateFailure(photo, getMsg("PhotoService.operationFailed"), IsCanceled: false));
                    }
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            if (gateEntered)
                _bulkWriteGate.Release();
        }

        var result = new PhotoBulkUpdateResult(succeeded.ToArray(), failed.ToArray());
        AppLogger.Trace(
            $"PhotoService.RunBulkPhotoUpdatesAsync: exit operation={operationName} " +
            $"succeeded={result.SucceededCount} failed={result.FailedCount} canceled={result.WasCanceled}");
        return result;
    }

    private static void AddCanceledFailures(
        ICollection<PhotoBulkUpdateFailure> failed,
        IReadOnlyList<SelectedPhotoRefDto> photos,
        int startIndex)
    {
        for (var index = startIndex; index < photos.Count; index++)
        {
            failed.Add(new PhotoBulkUpdateFailure(
                photos[index],
                getMsg("PhotoService.operationCancelled"),
                IsCanceled: true));
        }
    }

    /// <summary>選択写真一括お気に入り設定。完了・失敗を写真単位で返す。</summary>
    public Task<PhotoBulkUpdateResult> BulkSetPhotoFavoriteAsync(
        IReadOnlyList<SelectedPhotoRefDto> photos,
        bool isFavorite,
        CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.BulkSetPhotoFavoriteAsync: enter count={photos.Count} isFavorite={isFavorite}");
        var task = RunBulkPhotoUpdatesAsync(
            photos,
            nameof(BulkSetPhotoFavoriteAsync),
            async photo =>
            {
                var updated = await _db.TrySetPhotoFavoriteAsync(
                    photo.photo_path,
                    isFavorite,
                    CancellationToken.None).ConfigureAwait(false);
                if (!updated)
                    throw new InvalidOperationException($"写真が見つかりません: {photo.photo_path}");
            },
            ct);
        AppLogger.Trace("PhotoService.BulkSetPhotoFavoriteAsync: exit (dispatched)");
        return task;
    }

    /// <summary>単体写真へのタグ追加（tags マスタへの登録も DB 側でまとめて行う）。</summary>
    public async Task AddPhotoTagAsync(string photoPath, string tag, long sourceSlot, CancellationToken ct = default)
        => await AddPhotoTagsAsync(photoPath, [tag], sourceSlot, ct).ConfigureAwait(false);

    /// <summary>単体写真へ複数タグを追加する。1 回のゲート取得でまとめて書き込み、UI スレッドを塞がない。</summary>
    public async Task AddPhotoTagsAsync(string photoPath, IReadOnlyList<string> tags, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.AddPhotoTagsAsync: enter count={tags.Count}");
        if (tags.Count == 0) return;
        await _bulkWriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RunWriteOffUiThread(
                () => _db.AddPhotoTagsAsync(photoPath, tags, ct),
                ct).ConfigureAwait(false);
        }
        finally
        {
            _bulkWriteGate.Release();
        }
        AppLogger.Trace("PhotoService.AddPhotoTagsAsync: exit");
    }

    /// <summary>選択写真への一括タグ追加。完了・失敗を写真単位で返す。</summary>
    public Task<PhotoBulkUpdateResult> BulkAddPhotoTagAsync(
        IReadOnlyList<SelectedPhotoRefDto> photos,
        string tag,
        CancellationToken ct = default)
        => BulkAddPhotoTagsAsync(photos, [tag], ct);

    /// <summary>
    /// 選択写真へ複数タグを一括追加し、完了・失敗を写真単位で返す。
    /// タグ間ではキャンセルせず、1 写真に対する要求タグを完了してから次の写真へ進む。
    /// </summary>
    public Task<PhotoBulkUpdateResult> BulkAddPhotoTagsAsync(
        IReadOnlyList<SelectedPhotoRefDto> photos,
        IReadOnlyList<string> tags,
        CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.BulkAddPhotoTagsAsync: enter photoCount={photos.Count} tagCount={tags.Count}");
        if (photos.Count == 0 || tags.Count == 0)
        {
            AppLogger.Trace("PhotoService.BulkAddPhotoTagsAsync: exit (empty)");
            return Task.FromResult(new PhotoBulkUpdateResult([], []));
        }

        var task = RunBulkPhotoUpdatesAsync(
            photos,
            nameof(BulkAddPhotoTagsAsync),
            photo => _db.AddPhotoTagsAsync(photo.photo_path, tags, CancellationToken.None),
            ct);
        AppLogger.Trace("PhotoService.BulkAddPhotoTagsAsync: exit (dispatched)");
        return task;
    }

    /// <summary>単体写真からタグを 1 件外す。tags マスタ自体は残す。</summary>
    public async Task RemovePhotoTagAsync(string photoPath, string tag, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.RemovePhotoTagAsync: enter tag={tag}");
        await _bulkWriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RunWriteOffUiThread(() => _db.RemovePhotoTagAsync(photoPath, tag, ct), ct).ConfigureAwait(false);
        }
        finally
        {
            _bulkWriteGate.Release();
        }
        AppLogger.Trace("PhotoService.RemovePhotoTagAsync: exit");
    }

    /// <summary>単体写真に紐づくタグ名一覧を取得する。</summary>
    public Task<IReadOnlyList<string>> GetPhotoTagsAsync(string photoPath, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoService.GetPhotoTagsAsync: enter");
        var task = _db.GetPhotoTagsAsync(photoPath, ct);
        AppLogger.Trace("PhotoService.GetPhotoTagsAsync: exit");
        return task;
    }

    /// <summary>
    /// DB に保存済みの PDQ 分析結果とファイルサイズで候補を絞り込み、
    /// SHA-256 が一致する写真だけを重複として返す。PDQ 未計算または不正な値が同じ
    /// サイズ群に含まれる場合は、その群をすべて比較して取りこぼしを防ぐ。
    /// </summary>
    public async Task<ExactDuplicateDetectionResult> FindExactDuplicatePhotosAsync(
        IProgress<DuplicatePhotoScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoService.FindExactDuplicatePhotosAsync: enter");
        try
        {
            var photos = await RunReadOffUiThread(
                () => _db.GetPhotosForDuplicateDetectionAsync(ct),
                ct).ConfigureAwait(false);

            var candidates = new List<DuplicateFileCandidate>(photos.Count);
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unreadableCount = 0;
            ReportDuplicateProgress(progress, DuplicatePhotoScanStage.InspectingFiles, 0, photos.Count);

            for (var index = 0; index < photos.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var photo = photos[index];
                if (seenPaths.Add(photo.photo_path))
                {
                    try
                    {
                        var nativePath = ToNativePath(photo.photo_path);
                        var info = new FileInfo(nativePath);
                        if (info.Exists)
                            candidates.Add(new DuplicateFileCandidate(photo, nativePath, info.Length));
                        else
                            unreadableCount++;
                    }
                    catch (Exception ex)
                    {
                        unreadableCount++;
                        AppLogger.Warn($"PhotoService.FindExactDuplicatePhotosAsync: metadata skip path={photo.photo_path}: {ex.Message}");
                    }
                }

                ReportDuplicateProgress(
                    progress,
                    DuplicatePhotoScanStage.InspectingFiles,
                    index + 1,
                    photos.Count);
            }

            var filesToHash = BuildExactDuplicateHashCandidates(candidates);
            var hashedFiles = new List<HashedDuplicateFile>(filesToHash.Length);
            ReportDuplicateProgress(progress, DuplicatePhotoScanStage.ComparingContents, 0, filesToHash.Length);

            for (var index = 0; index < filesToHash.Length; index++)
            {
                ct.ThrowIfCancellationRequested();
                var candidate = filesToHash[index];
                try
                {
                    var snapshot = await ReadFileContentSnapshotAsync(candidate.NativePath, ct).ConfigureAwait(false);
                    if (snapshot is null || snapshot.FileSize != candidate.FileSize)
                    {
                        unreadableCount++;
                    }
                    else
                    {
                        hashedFiles.Add(new HashedDuplicateFile(candidate, snapshot.ContentHash));
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    unreadableCount++;
                    AppLogger.Warn($"PhotoService.FindExactDuplicatePhotosAsync: hash skip path={candidate.Photo.photo_path}: {ex.Message}");
                }

                ReportDuplicateProgress(
                    progress,
                    DuplicatePhotoScanStage.ComparingContents,
                    index + 1,
                    filesToHash.Length);
            }

            var groups = hashedFiles
                .GroupBy(file => file.ContentHash, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group =>
                {
                    var photosInGroup = group
                        .Select(file => new ExactDuplicatePhoto(
                            file.Candidate.Photo.photo_filename,
                            file.Candidate.Photo.photo_path,
                            file.Candidate.Photo.timestamp,
                            file.Candidate.Photo.source_slot,
                            file.Candidate.FileSize))
                        .OrderBy(photo => photo.Timestamp, StringComparer.Ordinal)
                        .ThenBy(photo => photo.PhotoPath, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    return new ExactDuplicatePhotoGroup(group.Key, photosInGroup[0].FileSize, photosInGroup);
                })
                .OrderByDescending(group => group.FileSize * (group.Photos.Count - 1L))
                .ThenBy(group => group.Photos[0].PhotoPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AppLogger.Trace(
                $"PhotoService.FindExactDuplicatePhotosAsync: exit groups={groups.Length} unreadable={unreadableCount}");
            return new ExactDuplicateDetectionResult(groups, photos.Count, unreadableCount);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.FindExactDuplicatePhotosAsync: threw: {ex}");
            throw;
        }
    }

    /// <summary>
    /// 選択写真が現在も登録済みで検出時と同じ内容かを確認し、同じ内容の保持写真を固定してから削除する。
    /// 元ファイルを先に削除し、成功したパスだけを DB とサムネイルキャッシュから除去する。
    /// </summary>
    public async Task<DuplicatePhotoDeleteResult> DeleteDuplicatePhotosAsync(
        IReadOnlyList<DuplicatePhotoDeleteTarget> targets,
        CancellationToken ct = default)
    {
        AppLogger.Trace($"PhotoService.DeleteDuplicatePhotosAsync: enter count={targets.Count}");
        await _bulkWriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var deletedPhotos = new List<SelectedPhotoRefDto>(targets.Count);
            var failedPhotos = new List<DuplicatePhotoDeleteFailure>();
            var currentPhotos = await RunReadOffUiThread(
                () => _db.GetPhotosForDuplicateDetectionAsync(ct),
                ct).ConfigureAwait(false);
            var currentPhotosByPath = currentPhotos
                .GroupBy(photo => photo.photo_path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var uniqueTargets = targets
                .GroupBy(entry => entry.Photo.photo_path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            var validTargets = new List<DuplicatePhotoDeleteTarget>(uniqueTargets.Length);
            foreach (var target in uniqueTargets)
            {
                if (string.IsNullOrWhiteSpace(target.Photo.photo_path)
                    || !currentPhotosByPath.TryGetValue(target.Photo.photo_path, out var currentPhoto)
                    || currentPhoto.source_slot != target.Photo.source_slot)
                {
                    failedPhotos.Add(new DuplicatePhotoDeleteFailure(
                        target.Photo,
                        DuplicatePhotoDeleteFailureKind.NoLongerRegistered));
                    continue;
                }
                validTargets.Add(target);
            }

            var selectedPaths = validTargets
                .Select(target => target.Photo.photo_path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var targetGroup in validTargets.GroupBy(target => new DuplicateDeleteGroupKey(
                target.ExpectedContentHash,
                target.ExpectedFileSize)))
            {
                ct.ThrowIfCancellationRequested();
                FileContentReadLease? retainedLease = null;
                foreach (var retainedPath in targetGroup
                    .SelectMany(target => target.RetainedPhotoPaths)
                    .Where(path => !string.IsNullOrWhiteSpace(path)
                        && currentPhotosByPath.ContainsKey(path)
                        && !selectedPaths.Contains(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    FileContentReadLease? candidateLease = null;
                    try
                    {
                        candidateLease = await OpenFileContentReadLeaseAsync(
                            ToNativePath(retainedPath),
                            allowDelete: false,
                            ct).ConfigureAwait(false);
                        if (candidateLease is not null
                            && candidateLease.Snapshot.FileSize == targetGroup.Key.ExpectedFileSize
                            && string.Equals(
                                candidateLease.Snapshot.ContentHash,
                                targetGroup.Key.ExpectedContentHash,
                                StringComparison.Ordinal))
                        {
                            retainedLease = candidateLease;
                            candidateLease = null;
                            break;
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn(
                            $"PhotoService.DeleteDuplicatePhotosAsync: retained comparison failed path={retainedPath}: {ex.Message}");
                    }
                    finally
                    {
                        if (candidateLease is not null)
                            await candidateLease.DisposeAsync().ConfigureAwait(false);
                    }
                }

                if (retainedLease is null)
                {
                    foreach (var target in targetGroup)
                    {
                        failedPhotos.Add(new DuplicatePhotoDeleteFailure(
                            target.Photo,
                            DuplicatePhotoDeleteFailureKind.RetainedPhotoUnavailable));
                    }
                    continue;
                }

                await using (retainedLease.ConfigureAwait(false))
                {
                    foreach (var target in targetGroup)
                    {
                        ct.ThrowIfCancellationRequested();
                        var nativeTargetPath = ToNativePath(target.Photo.photo_path);
                        FileContentReadLease? targetLease;
                        try
                        {
                            targetLease = await OpenFileContentReadLeaseAsync(
                                nativeTargetPath,
                                allowDelete: true,
                                ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn(
                                $"PhotoService.DeleteDuplicatePhotosAsync: target comparison failed path={target.Photo.photo_path}: {ex.Message}");
                            failedPhotos.Add(new DuplicatePhotoDeleteFailure(
                                target.Photo,
                                DuplicatePhotoDeleteFailureKind.ComparisonFailed));
                            continue;
                        }

                        // 既に存在しない写真はファイル削除済みとして DB とキャッシュだけを整理する。
                        if (targetLease is null)
                        {
                            deletedPhotos.Add(target.Photo);
                            _thumbnailService?.DeleteCachedThumbnails(
                                target.Photo.photo_path,
                                target.Photo.source_slot);
                            continue;
                        }

                        await using (targetLease.ConfigureAwait(false))
                        {
                            if (targetLease.Snapshot.FileSize != target.ExpectedFileSize
                                || !string.Equals(
                                    targetLease.Snapshot.ContentHash,
                                    target.ExpectedContentHash,
                                    StringComparison.Ordinal))
                            {
                                failedPhotos.Add(new DuplicatePhotoDeleteFailure(
                                    target.Photo,
                                    DuplicatePhotoDeleteFailureKind.FileChanged));
                                continue;
                            }

                            try
                            {
                                await DeleteFileUsingExplorerDefaultsAsync(nativeTargetPath, ct).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex) when (IsMissingFileException(ex))
                            {
                                AppLogger.Trace(
                                    $"PhotoService.DeleteDuplicatePhotosAsync: already missing path={target.Photo.photo_path}");
                            }
                            catch (Exception ex) when (!IsMissingFileException(ex))
                            {
                                AppLogger.Warn(
                                    $"PhotoService.DeleteDuplicatePhotosAsync: delete failed path={target.Photo.photo_path}: {ex.Message}");
                                failedPhotos.Add(new DuplicatePhotoDeleteFailure(
                                    target.Photo,
                                    DuplicatePhotoDeleteFailureKind.DeleteFailed));
                                continue;
                            }
                        }

                        deletedPhotos.Add(target.Photo);
                        _thumbnailService?.DeleteCachedThumbnails(
                            target.Photo.photo_path,
                            target.Photo.source_slot);
                    }
                }
            }

            var databaseUpdated = true;
            if (deletedPhotos.Count > 0)
            {
                try
                {
                    var deletedPaths = deletedPhotos.Select(photo => photo.photo_path).ToArray();
                    await RunWriteOffUiThread(
                        () => _db.DeletePhotosByPathsAsync(deletedPaths, ct),
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    databaseUpdated = false;
                    AppLogger.Error($"PhotoService.DeleteDuplicatePhotosAsync: database cleanup failed: {ex}");
                }
            }

            AppLogger.Trace(
                $"PhotoService.DeleteDuplicatePhotosAsync: exit deleted={deletedPhotos.Count} failed={failedPhotos.Count} db={databaseUpdated}");
            return new DuplicatePhotoDeleteResult(deletedPhotos, failedPhotos, databaseUpdated);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.DeleteDuplicatePhotosAsync: threw: {ex}");
            throw;
        }
        finally
        {
            _bulkWriteGate.Release();
        }
    }

    /// <summary>
    /// 現在読み込まれている写真のお気に入りとタグを、ファイル名単位でバックアップする。
    /// SQLite の同期処理は UI スレッド外で実行し、他の利用者操作による写真更新とは直列化する。
    /// </summary>
    public async Task<PhotoUserDataBackupResult> CreatePhotoUserDataBackupAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoService.CreatePhotoUserDataBackupAsync: enter");
        await _bulkWriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await RunWriteOffUiThread(
                () => _db.CreatePhotoUserDataBackupAsync(ct),
                ct).ConfigureAwait(false);
            AppLogger.Trace(
                $"PhotoService.CreatePhotoUserDataBackupAsync: exit source={result.SourcePhotoCount} entries={result.BackupEntryCount}");
            return result;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.CreatePhotoUserDataBackupAsync: threw: {ex}");
            throw;
        }
        finally
        {
            _bulkWriteGate.Release();
        }
    }

    /// <summary>
    /// 保存済みのお気に入りとタグを、現在の写真へファイル名一致で復元する。
    /// 復元全体は DB 側の単一トランザクションで処理する。
    /// </summary>
    public async Task<PhotoUserDataRestoreResult> RestorePhotoUserDataBackupAsync(CancellationToken ct = default)
    {
        AppLogger.Trace("PhotoService.RestorePhotoUserDataBackupAsync: enter");
        await _bulkWriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await RunWriteOffUiThread(
                () => _db.RestorePhotoUserDataBackupAsync(ct),
                ct).ConfigureAwait(false);
            AppLogger.Trace(
                $"PhotoService.RestorePhotoUserDataBackupAsync: exit entries={result.BackupEntryCount} matched={result.MatchedPhotoCount}");
            return result;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoService.RestorePhotoUserDataBackupAsync: threw: {ex}");
            throw;
        }
        finally
        {
            _bulkWriteGate.Release();
        }
    }

    /// <summary>
    /// 選択した写真を destinationFolder にコピーする。
    /// 戻り値: (copied, skipped) — 既に同名がある場合は個別にスキップして処理継続。
    /// 1 件の失敗で全体が中断されると、複数選択コピーが事実上使えなくなるため。
    ///
    /// 実装メモ：
    /// - DB の photo_path は forward-slash 正規化済みのため、File.Copy に渡す前に
    ///   Path.DirectorySeparatorChar (Windows なら '\') へ戻す。UNC パス (\\server\share)
    ///   は '/' 区切りだと「壊れた長いパス」と認識されて失敗するため必須。
    /// - I/O はバックグラウンドスレッドで実行 (Task.Run) し、UI スレッドをブロックしない。
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

    private static string ToNativePath(string path)
        => path.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>
    /// 既存の類似分析結果を候補グループとして再利用し、完全一致確認が必要な写真だけを返す。
    /// 分析結果が揃っていないサイズ群は全件を返し、未分析写真との重複も検出対象に含める。
    /// </summary>
    private static DuplicateFileCandidate[] BuildExactDuplicateHashCandidates(
        IReadOnlyList<DuplicateFileCandidate> candidates)
    {
        var result = new List<DuplicateFileCandidate>();
        foreach (var sizeGroup in candidates.GroupBy(candidate => candidate.FileSize))
        {
            var sameSize = sizeGroup.ToArray();
            if (sameSize.Length < 2)
                continue;

            var analyzed = sameSize
                .Select(candidate => new
                {
                    Candidate = candidate,
                    SimilarityKey = GetReusableSimilarityHashKey(candidate.Photo.phash),
                })
                .ToArray();

            if (analyzed.Any(item => item.SimilarityKey is null))
            {
                result.AddRange(sameSize);
                continue;
            }

            result.AddRange(analyzed
                .GroupBy(item => item.SimilarityKey!, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group.Select(item => item.Candidate)));
        }
        return result.ToArray();
    }

    /// <summary>有効な PDQ バリアントを順序に依存しない候補グループキーへ正規化する。</summary>
    private static string? GetReusableSimilarityHashKey(string? phash)
    {
        var variants = PdqHasher.ParseHashVariants(phash);
        return variants.Count == 0
            ? null
            : string.Join("|", variants.OrderBy(static value => value, StringComparer.Ordinal));
    }

    private static void ReportDuplicateProgress(
        IProgress<DuplicatePhotoScanProgress>? progress,
        DuplicatePhotoScanStage stage,
        int processed,
        int total)
    {
        if (progress is null)
            return;
        if (processed == 0 || processed == total || (processed & 0x0F) == 0)
            progress.Report(new DuplicatePhotoScanProgress(stage, processed, total));
    }

    /// <summary>共有読み取りだけを許可して内容を固定し、サイズと完全一致判定用ハッシュを返す。</summary>
    private static async Task<FileContentSnapshot?> ReadFileContentSnapshotAsync(
        string nativePath,
        CancellationToken ct)
    {
        var lease = await OpenFileContentReadLeaseAsync(
            nativePath,
            allowDelete: false,
            ct).ConfigureAwait(false);
        if (lease is null)
            return null;

        await using (lease.ConfigureAwait(false))
            return lease.Snapshot;
    }

    /// <summary>
    /// ハッシュ計算後も読み取りハンドルを保持し、呼び出し側の検証区間で書き換えを防ぐ。
    /// 削除対象だけは StorageFile による削除を許可するため FileShare.Delete を追加する。
    /// </summary>
    private static async Task<FileContentReadLease?> OpenFileContentReadLeaseAsync(
        string nativePath,
        bool allowDelete,
        CancellationToken ct)
    {
        try
        {
            var stream = new FileStream(
                nativePath,
                FileMode.Open,
                FileAccess.Read,
                allowDelete ? FileShare.Read | FileShare.Delete : FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            try
            {
                var fileSize = stream.Length;
                var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
                return new FileContentReadLease(
                    stream,
                    new FileContentSnapshot(fileSize, Convert.ToHexString(hash)));
            }
            catch
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex) when (IsMissingFileException(ex))
        {
            return null;
        }
    }

    private static async Task DeleteFileUsingExplorerDefaultsAsync(
        string nativePath,
        CancellationToken ct)
    {
        var file = await StorageFile.GetFileFromPathAsync(nativePath).AsTask(ct).ConfigureAwait(false);
        await file.DeleteAsync(StorageDeleteOption.Default).AsTask(ct).ConfigureAwait(false);
    }

    private static bool IsMissingFileException(Exception ex)
        => ex is FileNotFoundException or DirectoryNotFoundException
            || ex.HResult == unchecked((int)0x80070002)
            || ex.HResult == unchecked((int)0x80070003);

    private sealed record DuplicateFileCandidate(
        PhotoRecordDto Photo,
        string NativePath,
        long FileSize);

    private sealed record HashedDuplicateFile(
        DuplicateFileCandidate Candidate,
        string ContentHash);

    private sealed record FileContentSnapshot(
        long FileSize,
        string ContentHash);

    private sealed record DuplicateDeleteGroupKey(
        string ExpectedContentHash,
        long ExpectedFileSize);

    private sealed class FileContentReadLease(
        FileStream stream,
        FileContentSnapshot snapshot) : IAsyncDisposable
    {
        public FileContentSnapshot Snapshot { get; } = snapshot;

        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }

}
