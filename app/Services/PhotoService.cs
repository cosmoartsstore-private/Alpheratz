using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Models;
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
    // SQLite への並列書込を直列化するためのセマフォ。
    // BulkSet*/BulkAdd* で 100 件単位を Task.WhenAll で並列発行すると、SQLite の
    // ファイルロック競合で SQLITE_BUSY が返り、一部行だけ書き込まれてパーシャル更新になる
    // (例：100 件一括お気に入り解除のうち 40 件だけ反映、残り 60 件は元のまま、という状態が発生)。
    // WAL モードでも複数 writer は許容されないため、明示的に 1 件ずつ直列化する。
    // 読み取りは並列のままで影響しない。
    private readonly SemaphoreSlim _bulkWriteGate = new(1, 1);

    public PhotoService(AlpheratzDb db)
    {
        AppLogger.Trace("PhotoService.ctor: enter");
        _db = db;
        AppLogger.Trace("PhotoService.ctor: exit");
    }

    private static Task RunWriteOffUiThread(Func<Task> write, CancellationToken ct)
        => Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            await write().ConfigureAwait(false);
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

    /// <summary>MonthNav 用に同じフィルタ条件で年月別件数を取得する。</summary>
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

    /// <summary>
    /// グループドリルダウン用のクエリ。groupKey が世界名なのかパスなのかを推測して
    /// WorldExacts / PhotoPathExact に振り分ける（パス文字には '/' か '\' が必ず含まれる）。
    /// 現実装ではグルーピングはワールド単位だが、将来フォルダ単位にも拡張できる設計。
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

}
