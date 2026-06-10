using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// ギャラリー画面の ViewModel。フィルタ変更の検知・Enter確定検索・写真操作を担当する。
/// 各サブステート（photosState, filtersState, selectionState 等）を束ねる中心的な存在。
/// </summary>
public partial class GalleryViewModel : UiThreadSafeObservableObject
{
    private const int MAX_TAG_LENGTH = 40;

    private readonly PhotoService photoService;
    private readonly WorldService worldService;
    private readonly PhashService phashService;
    private readonly ToastService toastService;
    private readonly DispatcherService dispatcherService;

    public GalleryPhotosState photosState { get; }
    public GalleryFiltersState filtersState { get; }
    public GallerySelectionState selectionState { get; }
    public GalleryDisplayState displayState { get; }
    public GalleryScrollState scrollState { get; }

    /// <summary>
    /// drill-down 用に外部（ShellPage）で保持される写真リスト。
    /// updatePhoto がここにも更新を反映できるように、ShellPage が登録する。
    /// </summary>
    public Func<IReadOnlyList<PhotoThumbnailItem>?>? drillDownPhotosProvider { get; set; }

    // ギャラリーに必要なサービスとサブステートを受け取り、フィルタ変更の監視を開始する。
    public GalleryViewModel(
        PhotoService photoService,
        WorldService worldService,
        PhashService phashService,
        ToastService toastService,
        DispatcherService dispatcherService,
        GalleryPhotosState photosState,
        GalleryFiltersState filtersState,
        GallerySelectionState selectionState,
        GalleryDisplayState displayState,
        GalleryScrollState scrollState)
    {
        AppLogger.Trace("GalleryViewModel.ctor: enter");
        this.photoService = photoService;
        this.worldService = worldService;
        this.phashService = phashService;
        this.toastService = toastService;
        this.dispatcherService = dispatcherService;
        this.photosState = photosState;
        this.filtersState = filtersState;
        this.selectionState = selectionState;
        this.displayState = displayState;
        this.scrollState = scrollState;

        filtersState.PropertyChanged += onFiltersChanged;
        filtersState.worldFilters.CollectionChanged += onFiltersCollectionChanged;
        filtersState.tagFilters.CollectionChanged += onFiltersCollectionChanged;
        AppLogger.Trace("GalleryViewModel.ctor: exit");
    }

    // ViewModel 破棄時にフィルタ監視を解除し、画面参照の保持を止める。
    public void Cleanup()
    {
        filtersState.PropertyChanged -= onFiltersChanged;
        filtersState.worldFilters.CollectionChanged -= onFiltersCollectionChanged;
        filtersState.tagFilters.CollectionChanged -= onFiltersCollectionChanged;
    }

    /// <summary>
    /// filtersState のプロパティ変更を検知し、適切な処理を行う。
    /// GroupingMode 変更 → クライアント側 displayItems 再構築（DB リクエリ不要）。
    /// その他のフィルタ変更 → DB リクエリ。
    /// </summary>
    private void onFiltersChanged(object? sender, PropertyChangedEventArgs e)
    {
        AppLogger.Trace($"GalleryViewModel.onFiltersChanged: enter property={e.PropertyName}");
        try
        {
            if (filtersState.IsBatchUpdating)
            {
                AppLogger.Trace("GalleryViewModel.onFiltersChanged: batch updating, skip");
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(GalleryFiltersState.GroupingMode):
                    AppLogger.Trace("GalleryViewModel.onFiltersChanged: branch=GroupingMode (client rebuild)");
                    photosState.rebuildDisplayItems(filtersState.GroupingMode);
                    break;
                case "BatchCompleted":
                case nameof(GalleryFiltersState.DebouncedQuery):
                case nameof(GalleryFiltersState.DateFrom):
                case nameof(GalleryFiltersState.DateTo):
                case nameof(GalleryFiltersState.OrientationFilter):
                case nameof(GalleryFiltersState.FavoritesOnly):
                case nameof(GalleryFiltersState.DisplayFolderMode):
                case nameof(GalleryFiltersState.SortMode):
                    AppLogger.Trace("GalleryViewModel.onFiltersChanged: branch=reload");
                    _ = applyFiltersAndReload();
                    break;
                case nameof(GalleryFiltersState.SearchQuery):
                    AppLogger.Trace("GalleryViewModel.onFiltersChanged: branch=SearchQuery pending submit");
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryViewModel.onFiltersChanged: threw: {ex}");
        }
        AppLogger.Trace("GalleryViewModel.onFiltersChanged: exit");
    }

    /// <summary>worldFilters / tagFilters のコレクション変更で即時再ロード。</summary>
    private void onFiltersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AppLogger.Trace($"GalleryViewModel.onFiltersCollectionChanged: enter action={e.Action}");
        try
        {
            if (filtersState.IsBatchUpdating)
            {
                AppLogger.Trace("GalleryViewModel.onFiltersCollectionChanged: batch updating, skip");
                return;
            }

            _ = applyFiltersAndReload();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryViewModel.onFiltersCollectionChanged: threw: {ex}");
        }
        AppLogger.Trace("GalleryViewModel.onFiltersCollectionChanged: exit");
    }

    /// <summary>現在のフィルタ条件を photosState に反映し、先頭からロードし直す。</summary>
    public Task applyFiltersAndReload()
    {
        AppLogger.Trace("GalleryViewModel.applyFiltersAndReload: enter");
        photosState.SetFilters(buildCurrentFilters());
        var task = photosState.loadPhotos();
        AppLogger.Trace("GalleryViewModel.applyFiltersAndReload: exit (load dispatched)");
        return task;
    }

    // 検索欄の現在値を通常テキストとして確定し、再ロードを同期して実行する。
    public void applySearchNow()
    {
        AppLogger.Trace("GalleryViewModel.applySearchNow: enter");
        try
        {
            var nextQuery = filtersState.SearchQuery.Trim();

            if (filtersState.DebouncedQuery == nextQuery)
            {
                _ = applyFiltersAndReload();
            }
            else
            {
                filtersState.DebouncedQuery = nextQuery;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryViewModel.applySearchNow: threw: {ex}");
        }
        AppLogger.Trace("GalleryViewModel.applySearchNow: exit");
    }

    // UI ステートを PhotoService が受け取れるクエリ条件へ変換する。
    private PhotoQueryFilters buildCurrentFilters()
    {
        AppLogger.Trace("GalleryViewModel.buildCurrentFilters: enter");
        var filters = new PhotoQueryFilters(
            searchQuery: filtersState.DebouncedQuery,
            worldFilters: [.. filtersState.worldFilters],
            dateFrom: filtersState.DateFrom,
            dateTo: filtersState.DateTo,
            orientationFilter: filtersState.OrientationFilter,
            favoritesOnly: filtersState.FavoritesOnly,
            tagFilters: [.. filtersState.tagFilters],
            includePhash: false,
            pagingEnabled: false,
            viewMode: displayState.ViewMode,
            sourceSlot: filtersState.DisplayFolderMode switch
            {
                DisplayFolderMode.primary => 1L,
                DisplayFolderMode.secondary => 2L,
                _ => null,
            },
            groupingMode: filtersState.GroupingMode,
            sortMode: filtersState.SortMode
        );
        AppLogger.Trace("GalleryViewModel.buildCurrentFilters: exit");
        return filters;
    }

    // DB からワールド候補を読み込み、フィルタ一覧へ反映する。
    public async Task loadWorldFilterOptions()
    {
        AppLogger.Trace("GalleryViewModel.loadWorldFilterOptions: enter");
        try
        {
            var options = await photoService.GetWorldFilterOptionsAsync().ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
                filtersState.worldFilterOptions.ReplaceAll(options)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"GalleryViewModel.loadWorldFilterOptions: threw: {ex}");
        }
        AppLogger.Trace("GalleryViewModel.loadWorldFilterOptions: exit");
    }

    // 現在のタグ別件数を読み込み、タグフィルタ表示を更新する。
    public async Task loadTagFilterCounts()
    {
        AppLogger.Trace("GalleryViewModel.loadTagFilterCounts: enter");
        try
        {
            var counts = await photoService.GetTagFilterCountsAsync().ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
                filtersState.setTagFilterCounts(counts)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"GalleryViewModel.loadTagFilterCounts: threw: {ex}");
        }
        AppLogger.Trace("GalleryViewModel.loadTagFilterCounts: exit");
    }

    // 対象写真のお気に入り状態を反転し、表示中の同一写真にも反映する。
    public async Task toggleFavorite(string photoPath, bool currentIsFavorite)
    {
        AppLogger.Trace($"GalleryViewModel.toggleFavorite: enter photoPath={photoPath} current={currentIsFavorite}");
        var currentPhoto = photosState.photos.FirstOrDefault(photo => photo.PhotoPath == photoPath);
        try
        {
            var nextIsFavorite = !currentIsFavorite;
            await photoService.SetPhotoFavoriteAsync(photoPath, nextIsFavorite, currentPhoto?.SourceSlot ?? 1).ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
                updatePhoto(photoPath, photo => photo.IsFavorite = nextIsFavorite)).ConfigureAwait(false);
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.toggleFavorite: threw: {err}");
            toastService.addToast($"お気に入りの更新に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.toggleFavorite: exit");
    }

    // 対象写真にタグを追加し、重複・空文字・長すぎるタグは保存前に弾く。
    public async Task addTag(string photoPath, string tag)
    {
        AppLogger.Trace($"GalleryViewModel.addTag: enter photoPath={photoPath} tag={tag}");
        var normalized = tag.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            AppLogger.Trace("GalleryViewModel.addTag: skip (empty)");
            return;
        }

        if (normalized.Length > MAX_TAG_LENGTH)
        {
            AppLogger.Trace("GalleryViewModel.addTag: skip (too long)");
            toastService.addToast($"タグは{MAX_TAG_LENGTH}文字以内で入力してください。", ToastType.error);
            return;
        }

        var currentPhoto = photosState.photos.FirstOrDefault(photo => photo.PhotoPath == photoPath);
        if (currentPhoto?.Tags.Contains(normalized, StringComparer.OrdinalIgnoreCase) == true)
        {
            AppLogger.Trace("GalleryViewModel.addTag: skip (duplicate)");
            return;
        }

        try
        {
            await photoService.AddPhotoTagAsync(photoPath, normalized, currentPhoto?.SourceSlot ?? 1).ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
                updatePhoto(photoPath, photo => photo.Tags = photo.Tags
                    .Concat([normalized])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.Create(new CultureInfo("ja-JP"), false))
                    .ToArray())).ConfigureAwait(false);
            await loadTagFilterCounts().ConfigureAwait(false);
            toastService.addToast("タグを追加しました。");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.addTag: threw: {err}");
            toastService.addToast($"タグの追加に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.addTag: exit");
    }

    // 対象写真に複数タグをまとめて追加し、入力重複と既存タグ重複をまとめて除外する。
    public async Task addTags(string photoPath, IEnumerable<string> tags)
    {
        var normalizedTags = tags
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrEmpty(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AppLogger.Trace($"GalleryViewModel.addTags: enter photoPath={photoPath} count={normalizedTags.Length}");
        if (normalizedTags.Length == 0) return;

        if (normalizedTags.Any(tag => tag.Length > MAX_TAG_LENGTH))
        {
            toastService.addToast($"タグは{MAX_TAG_LENGTH}文字以内で入力してください。", ToastType.error);
            return;
        }

        var currentPhoto = photosState.photos.FirstOrDefault(photo => photo.PhotoPath == photoPath);
        var additions = normalizedTags
            .Where(tag => currentPhoto?.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase) != true)
            .ToArray();
        if (additions.Length == 0)
        {
            AppLogger.Trace("GalleryViewModel.addTags: skip (duplicate)");
            return;
        }

        try
        {
            foreach (var tag in additions)
                await photoService.AddPhotoTagAsync(photoPath, tag, currentPhoto?.SourceSlot ?? 1).ConfigureAwait(false);

            await dispatcherService.RunOnUiThread(() =>
                updatePhoto(photoPath, photo => photo.Tags = photo.Tags
                    .Concat(additions)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.Create(new CultureInfo("ja-JP"), false))
                    .ToArray())).ConfigureAwait(false);
            await loadTagFilterCounts().ConfigureAwait(false);
            toastService.addToast(additions.Length == 1 ? "タグを追加しました。" : $"{additions.Length} 件のタグを追加しました。");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.addTags: threw: {err}");
            toastService.addToast($"タグの追加に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.addTags: exit");
    }

    // 対象写真からタグを削除し、タグ件数と表示中データを更新する。
    public async Task removeTag(string photoPath, string tag)
    {
        AppLogger.Trace($"GalleryViewModel.removeTag: enter photoPath={photoPath} tag={tag}");
        var currentPhoto = photosState.photos.FirstOrDefault(photo => photo.PhotoPath == photoPath);
        try
        {
            await photoService.RemovePhotoTagAsync(photoPath, tag, currentPhoto?.SourceSlot ?? 1).ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
                updatePhoto(photoPath, photo => photo.Tags = photo.Tags.Where(item => item != tag).ToArray())).ConfigureAwait(false);
            await loadTagFilterCounts().ConfigureAwait(false);
            toastService.addToast("タグを削除しました。");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.removeTag: threw: {err}");
            toastService.addToast($"タグの削除に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.removeTag: exit");
    }

    public async Task applySimilarWorldMatch(
        PhotoThumbnailItem sourcePhoto,
        PhotoThumbnailItem? selectedPhotoView,
        Action<PhotoThumbnailItem>? updateSelectedPhoto = null)
    {
        AppLogger.Trace("GalleryViewModel.applySimilarWorldMatch: enter");
        if (selectedPhotoView is null)
        {
            AppLogger.Trace("GalleryViewModel.applySimilarWorldMatch: skip (no selection)");
            return;
        }

        try
        {
            await worldService.ApplyWorldMatchFromPhotoAsync(selectedPhotoView.PhotoPath, sourcePhoto.PhotoPath).ConfigureAwait(false);
            var nextWorldId = sourcePhoto.WorldId;
            var nextWorldName = sourcePhoto.WorldName;

            void Apply(PhotoThumbnailItem photo)
            {
                photo.WorldId = nextWorldId;
                photo.WorldName = nextWorldName;
                photo.MatchSource = "phash";
            }

            await dispatcherService.RunOnUiThread(() =>
            {
                updatePhoto(selectedPhotoView.PhotoPath, Apply);

                if (updateSelectedPhoto is not null)
                {
                    Apply(selectedPhotoView);
                    updateSelectedPhoto(selectedPhotoView);
                }

                if (filtersState.GroupingMode == GroupingMode.world)
                {
                    photosState.rebuildDisplayItems(filtersState.GroupingMode);
                }
            }).ConfigureAwait(false);
            await loadWorldFilterOptions().ConfigureAwait(false);

            toastService.addToast("ワールド情報を反映しました。");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.applySimilarWorldMatch: threw: {err}");
            toastService.addToast($"ワールド情報の反映に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.applySimilarWorldMatch: exit");
    }

    // ワールド未判定写真の PDQ 解析を開始し、結果をトーストで通知する。
    public async Task handleStartUnknownWorldAnalysis()
    {
        AppLogger.Trace("GalleryViewModel.handleStartUnknownWorldAnalysis: enter");
        try
        {
            await phashService.StartPdqAnalysisAsync().ConfigureAwait(false);
            toastService.addToast("ワールド不明写真の一括分析を開始しました");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.handleStartUnknownWorldAnalysis: threw: {err}");
            toastService.addToast($"ワールド不明写真の一括分析を開始できませんでした: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.handleStartUnknownWorldAnalysis: exit");
    }

    // 写真カードの起動操作を、複数選択の切替または詳細表示へ振り分ける。
    public void handlePhotoActivate(PhotoGridItem item, bool shiftKey, Action<PhotoThumbnailItem> onSelectPhoto)
    {
        AppLogger.Trace($"GalleryViewModel.handlePhotoActivate: enter shiftKey={shiftKey}");
        try
        {
            if (selectionState.IsMultiSelectMode)
            {
                AppLogger.Trace("GalleryViewModel.handlePhotoActivate: branch=multiselect toggle");
                selectionState.toggleSelectedPhoto(item, shiftKey, photosState.displayItems.ToArray());
                return;
            }

            onSelectPhoto(item.Photo);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryViewModel.handlePhotoActivate: threw: {ex}");
        }
        AppLogger.Trace("GalleryViewModel.handlePhotoActivate: exit");
    }

    // ギャラリーの絞り込み条件を初期状態へ戻す。
    public void resetFilters()
    {
        AppLogger.Trace("GalleryViewModel.resetFilters: enter");
        filtersState.resetFilters();
        AppLogger.Trace("GalleryViewModel.resetFilters: exit");
    }

    // 選択中写真のお気に入り状態を一括変更し、ローカル表示にも反映する。
    public async Task bulkSetFavorite(bool isFavorite)
    {
        AppLogger.Trace($"GalleryViewModel.bulkSetFavorite: enter isFavorite={isFavorite}");
        var refs = (await selectionState.getSelectedPhotoRefsSnapshot().ConfigureAwait(false)).ToList();
        if (refs.Count == 0) return;
        try
        {
            await photoService.BulkSetPhotoFavoriteAsync(refs, isFavorite).ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
            {
                foreach (var r in refs)
                    updatePhoto(r.photo_path, p => p.IsFavorite = isFavorite);
            }).ConfigureAwait(false);
            toastService.addToast(isFavorite ? "お気に入りに追加しました" : "お気に入りを解除しました");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.bulkSetFavorite: threw: {err}");
            toastService.addToast($"お気に入りの更新に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.bulkSetFavorite: exit");
    }

    // 選択中写真へタグを一括追加し、各写真のタグ配列を重複なく更新する。
    public async Task bulkAddTag(string tag)
        => await bulkAddTags([tag]).ConfigureAwait(false);

    // 選択中写真へ複数タグを一括追加し、入力重複と既存タグ重複をまとめて除外する。
    public async Task bulkAddTags(IEnumerable<string> tags)
    {
        var normalizedTags = tags
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrEmpty(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AppLogger.Trace($"GalleryViewModel.bulkAddTags: enter count={normalizedTags.Length}");
        if (normalizedTags.Length == 0)
        {
            AppLogger.Trace("GalleryViewModel.bulkAddTags: skip (empty)");
            return;
        }

        if (normalizedTags.Any(tag => tag.Length > MAX_TAG_LENGTH))
        {
            AppLogger.Trace("GalleryViewModel.bulkAddTags: skip (too long)");
            toastService.addToast($"タグは{MAX_TAG_LENGTH}文字以内で入力してください。", ToastType.error);
            return;
        }

        var refs = (await selectionState.getSelectedPhotoRefsSnapshot().ConfigureAwait(false)).ToList();
        if (refs.Count == 0) return;
        try
        {
            foreach (var normalized in normalizedTags)
                await photoService.BulkAddPhotoTagAsync(refs, normalized).ConfigureAwait(false);

            await dispatcherService.RunOnUiThread(() =>
            {
                foreach (var r in refs)
                {
                    updatePhoto(r.photo_path, photo =>
                    {
                        var additions = normalizedTags
                            .Where(tag => !photo.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                            .ToArray();
                        if (additions.Length == 0) return;

                        photo.Tags = photo.Tags
                            .Concat(additions)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(item => item, StringComparer.Create(new CultureInfo("ja-JP"), false))
                            .ToArray();
                    });
                }
            }).ConfigureAwait(false);
            await loadTagFilterCounts().ConfigureAwait(false);
            var label = normalizedTags.Length == 1
                ? $"タグ \"{normalizedTags[0]}\""
                : $"{normalizedTags.Length} 件のタグ";
            toastService.addToast($"{label} を {refs.Count} 枚に追加しました");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.bulkAddTags: threw: {err}");
            toastService.addToast($"タグの追加に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.bulkAddTags: exit");
    }

    // 選択中写真を指定フォルダへコピーし、コピー件数とスキップ件数を通知する。
    public async Task bulkCopyPhotos(string destinationFolder)
    {
        AppLogger.Trace($"GalleryViewModel.bulkCopyPhotos: enter destinationFolder={destinationFolder}");
        var refs = (await selectionState.getSelectedPhotoRefsSnapshot().ConfigureAwait(false)).ToList();
        if (refs.Count == 0 || string.IsNullOrWhiteSpace(destinationFolder)) return;
        try
        {
            // 既存ファイル名でスキップされた件数も、コピー結果としてユーザーに伝える。
            var (copied, skipped) = await photoService.BulkCopyPhotosAsync(refs, destinationFolder).ConfigureAwait(false);
            if (skipped == 0)
                toastService.addToast($"{copied} 枚のファイルをコピーしました");
            else
                toastService.addToast($"{copied} 枚をコピーしました ({skipped} 枚はスキップ)", ToastType.info);
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.bulkCopyPhotos: threw: {err}");
            toastService.addToast($"コピーに失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.bulkCopyPhotos: exit");
    }

    /// <summary>
    /// 指定グループの写真一覧を取得する（ドリルダウン用）。
    /// 既存の photosState.photos 参照を再利用せず、毎回 SQL を発行して
    /// 新しい PhotoThumbnailItem インスタンスを生成する。これにより
    /// メインビューの状態変化（並べ替え・差し替え）の影響を受けず、
    /// ドリルダウンは独立したコレクションとして振る舞える。
    /// 表示はメインと同じパイプライン（FromDto → サムネイル遅延生成）。
    /// </summary>
    public async Task<IReadOnlyList<PhotoThumbnailItem>> getGroupPhotosAsync(string groupKey)
    {
        AppLogger.Trace($"GalleryViewModel.getGroupPhotosAsync: enter groupKey={groupKey}");
        try
        {
            var filters = buildCurrentFilters();
            var photos = await photoService.GetWorldGroupPhotosAsync(
                groupKey,
                string.IsNullOrEmpty(filters.dateFrom) ? null : filters.dateFrom,
                string.IsNullOrEmpty(filters.dateTo) ? null : filters.dateTo,
                filters.sourceSlot,
                filters.orientationFilter == "all" ? null : filters.orientationFilter,
                filters.favoritesOnly ? true : null,
                filters.tagFilters.Count > 0 ? filters.tagFilters : null
            ).ConfigureAwait(false);

            var items = photos.Select(PhotoThumbnailItem.FromDto).ToArray();
            AppLogger.Trace($"GalleryViewModel.getGroupPhotosAsync: exit count={items.Length}");
            return items;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryViewModel.getGroupPhotosAsync: threw: {ex}");
            return [];
        }
    }

    /// <summary>
    /// photoPath に一致する写真を photos / displayItems / drillDownPhotos から探し、updater を適用する。
    /// </summary>
    private void updatePhoto(string photoPath, Action<PhotoThumbnailItem> updater)
    {
        try
        {
            foreach (var photo in photosState.photos.Where(photo => photo.PhotoPath == photoPath))
            {
                updater(photo);
            }

            foreach (var item in photosState.displayItems.Where(item => item.Photo.PhotoPath == photoPath))
            {
                updater(item.Photo);
            }

            var drill = drillDownPhotosProvider?.Invoke();
            if (drill is not null)
            {
                foreach (var photo in drill.Where(photo => photo.PhotoPath == photoPath))
                {
                    updater(photo);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryViewModel.updatePhoto: threw: {ex}");
        }
    }
}
