using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// ギャラリー画面の ViewModel。フィルタ変更の検知・デバウンス・写真操作を担当する。
/// 各サブステート（photosState, filtersState, selectionState 等）を束ねる中心的な存在。
/// </summary>
public partial class GalleryViewModel : UiThreadSafeObservableObject
{
    private const int MAX_TAG_LENGTH = 40;

    private readonly PhotoService photoService;
    private readonly WorldService worldService;
    private readonly PhashService phashService;
    private readonly ToastService toastService;

    public GalleryPhotosState photosState { get; }
    public GalleryFiltersState filtersState { get; }
    public GallerySelectionState selectionState { get; }
    public GalleryDisplayState displayState { get; }
    public GalleryScrollState scrollState { get; }

    private CancellationTokenSource? searchDebounceCts;

    public GalleryViewModel(
        PhotoService photoService,
        WorldService worldService,
        PhashService phashService,
        ToastService toastService,
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
                    AppLogger.Trace("GalleryViewModel.onFiltersChanged: branch=reload");
                    _ = applyFiltersAndReload();
                    break;
                case nameof(GalleryFiltersState.SearchQuery):
                    AppLogger.Trace("GalleryViewModel.onFiltersChanged: branch=SearchQuery debounce");
                    _ = debounceAndApplySearch();
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
            _ = applyFiltersAndReload();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryViewModel.onFiltersCollectionChanged: threw: {ex}");
        }
        AppLogger.Trace("GalleryViewModel.onFiltersCollectionChanged: exit");
    }

    /// <summary>
    /// 検索クエリ入力を 400ms デバウンスし、確定後に DebouncedQuery を更新する。
    /// DebouncedQuery の PropertyChanged が onFiltersChanged を再度トリガーし再ロードが走る。
    /// </summary>
    private async Task debounceAndApplySearch()
    {
        AppLogger.Trace("GalleryViewModel.debounceAndApplySearch: enter");
        searchDebounceCts?.Cancel();
        searchDebounceCts?.Dispose();
        searchDebounceCts = new CancellationTokenSource();
        var token = searchDebounceCts.Token;
        try
        {
            await Task.Delay(400, token).ConfigureAwait(false);
            filtersState.DebouncedQuery = filtersState.SearchQuery;
        }
        catch (OperationCanceledException)
        {
            AppLogger.Trace("GalleryViewModel.debounceAndApplySearch: cancelled");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryViewModel.debounceAndApplySearch: threw: {ex}");
        }
        AppLogger.Trace("GalleryViewModel.debounceAndApplySearch: exit");
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
            groupingMode: filtersState.GroupingMode
        );
        AppLogger.Trace("GalleryViewModel.buildCurrentFilters: exit");
        return filters;
    }

    /// <summary>ワールドフィルタのドロップダウン候補を DB から取得する。</summary>
    public async Task loadWorldFilterOptions()
    {
        AppLogger.Trace("GalleryViewModel.loadWorldFilterOptions: enter");
        try
        {
            var options = await photoService.GetWorldFilterOptionsAsync().ConfigureAwait(false);
            filtersState.worldFilterOptions.Clear();
            foreach (var opt in options)
                filtersState.worldFilterOptions.Add(opt);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"GalleryViewModel.loadWorldFilterOptions: threw: {ex}");
        }
        AppLogger.Trace("GalleryViewModel.loadWorldFilterOptions: exit");
    }

    /// <summary>指定写真のお気に入り状態をトグルする。</summary>
    public async Task toggleFavorite(string photoPath, bool current)
    {
        AppLogger.Trace($"GalleryViewModel.toggleFavorite: enter photoPath={photoPath} current={current}");
        var currentPhoto = photosState.photos.FirstOrDefault(photo => photo.PhotoPath == photoPath);
        try
        {
            await photoService.SetPhotoFavoriteAsync(photoPath, !current, currentPhoto?.SourceSlot ?? 1).ConfigureAwait(false);
            updatePhoto(photoPath, photo => photo.IsFavorite = !current);
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.toggleFavorite: threw: {err}");
            toastService.addToast($"お気に入りの更新に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.toggleFavorite: exit");
    }

    /// <summary>指定写真にタグを追加する。重複・長さ超過はスキップ。</summary>
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
        if (currentPhoto?.Tags.Contains(normalized) == true)
        {
            AppLogger.Trace("GalleryViewModel.addTag: skip (duplicate)");
            return;
        }

        try
        {
            await photoService.AddPhotoTagAsync(photoPath, normalized, currentPhoto?.SourceSlot ?? 1).ConfigureAwait(false);
            updatePhoto(photoPath, photo => photo.Tags = photo.Tags
                .Concat([normalized])
                .OrderBy(item => item, StringComparer.Create(new CultureInfo("ja-JP"), false))
                .ToArray());
            toastService.addToast("タグを追加しました。");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.addTag: threw: {err}");
            toastService.addToast($"タグの追加に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.addTag: exit");
    }

    /// <summary>指定写真からタグを削除する。</summary>
    public async Task removeTag(string photoPath, string tag)
    {
        AppLogger.Trace($"GalleryViewModel.removeTag: enter photoPath={photoPath} tag={tag}");
        var currentPhoto = photosState.photos.FirstOrDefault(photo => photo.PhotoPath == photoPath);
        try
        {
            await photoService.RemovePhotoTagAsync(photoPath, tag, currentPhoto?.SourceSlot ?? 1).ConfigureAwait(false);
            updatePhoto(photoPath, photo => photo.Tags = photo.Tags.Where(item => item != tag).ToArray());
            toastService.addToast("タグを削除しました。");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.removeTag: threw: {err}");
            toastService.addToast($"タグの削除に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.removeTag: exit");
    }

    /// <summary>
    /// 類似ワールド候補を選択写真に適用する。
    /// sourcePhoto のワールド情報を selectedPhotoView にコピーし DB に記録する。
    /// </summary>
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

            updatePhoto(selectedPhotoView.PhotoPath, Apply);

            if (updateSelectedPhoto is not null)
            {
                Apply(selectedPhotoView);
                updateSelectedPhoto(selectedPhotoView);
            }

            toastService.addToast("ワールド情報を反映しました。");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.applySimilarWorldMatch: threw: {err}");
            toastService.addToast($"ワールド情報の反映に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.applySimilarWorldMatch: exit");
    }

    /// <summary>ワールド不明写真の一括 PDQ 分析を開始する。</summary>
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

    /// <summary>
    /// 写真カードのクリック/タップ時のハンドラ。
    /// マルチセレクトモード中は選択トグル、通常時は PhotoModal を開く。
    /// </summary>
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

    /// <summary>全フィルタをデフォルト値にリセットする。</summary>
    public void resetFilters()
    {
        AppLogger.Trace("GalleryViewModel.resetFilters: enter");
        filtersState.resetFilters();
        AppLogger.Trace("GalleryViewModel.resetFilters: exit");
    }

    /// <summary>選択中の写真を一括でお気に入り設定/解除する。</summary>
    public async Task bulkSetFavorite(bool isFavorite)
    {
        AppLogger.Trace($"GalleryViewModel.bulkSetFavorite: enter isFavorite={isFavorite}");
        var refs = selectionState.selectedPhotoRefs.ToList();
        if (refs.Count == 0)
        {
            AppLogger.Trace("GalleryViewModel.bulkSetFavorite: skip (no refs)");
            return;
        }
        try
        {
            await photoService.BulkSetPhotoFavoriteAsync(refs, isFavorite).ConfigureAwait(false);
            foreach (var r in refs)
                updatePhoto(r.photo_path, p => p.IsFavorite = isFavorite);
            toastService.addToast(isFavorite ? "お気に入りに追加しました" : "お気に入りを解除しました");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.bulkSetFavorite: threw: {err}");
            toastService.addToast($"お気に入りの更新に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.bulkSetFavorite: exit");
    }

    /// <summary>選択中の写真に一括でタグを追加する。</summary>
    public async Task bulkAddTag(string tag)
    {
        AppLogger.Trace($"GalleryViewModel.bulkAddTag: enter tag={tag}");
        var refs = selectionState.selectedPhotoRefs.ToList();
        if (refs.Count == 0 || string.IsNullOrWhiteSpace(tag))
        {
            AppLogger.Trace("GalleryViewModel.bulkAddTag: skip (no refs or empty tag)");
            return;
        }
        try
        {
            await photoService.BulkAddPhotoTagAsync(refs, tag).ConfigureAwait(false);
            toastService.addToast($"タグ \"{tag}\" を {refs.Count} 枚に追加しました");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.bulkAddTag: threw: {err}");
            toastService.addToast($"タグの追加に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.bulkAddTag: exit");
    }

    /// <summary>選択中の写真を指定フォルダにコピーする。</summary>
    public async Task bulkCopyPhotos(string destinationFolder)
    {
        AppLogger.Trace($"GalleryViewModel.bulkCopyPhotos: enter destinationFolder={destinationFolder}");
        var refs = selectionState.selectedPhotoRefs.ToList();
        if (refs.Count == 0 || string.IsNullOrWhiteSpace(destinationFolder))
        {
            AppLogger.Trace("GalleryViewModel.bulkCopyPhotos: skip (no refs or empty folder)");
            return;
        }
        try
        {
            await photoService.BulkCopyPhotosAsync(refs, destinationFolder).ConfigureAwait(false);
            toastService.addToast($"{refs.Count} 枚のファイルをコピーしました");
        }
        catch (Exception err)
        {
            AppLogger.Error($"GalleryViewModel.bulkCopyPhotos: threw: {err}");
            toastService.addToast($"コピーに失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("GalleryViewModel.bulkCopyPhotos: exit");
    }

    /// <summary>指定グループの写真一覧を取得する（ドリルダウン用）。</summary>
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
    /// photoPath に一致する写真を photos / displayItems から探し、updater を適用する。
    /// 同一写真が複数コレクションに存在しうるため全てを走査する。
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
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryViewModel.updatePhoto: threw: {ex}");
        }
    }
}
