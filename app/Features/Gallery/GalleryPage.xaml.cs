using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery.Controls;
using Alpheratz.Shared.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// ギャラリー画面の Page。ViewModel と各子コントロールのコールバックを結線する。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class GalleryPage : Page
{
    private readonly GalleryViewModel viewModel;

    /// <summary>
    /// 検索条件パネル。ShellPage 側の XAML (FilterRailHost) で生成されたインスタンスを
    /// constructor で受け取って結線する。XAML の中に直接置くと再 parent で COM 例外が
    /// 出るケースがあったため、所有を ShellPage に持たせて参照だけ受け取る形にした。
    /// </summary>
    private readonly GalleryFilterPanel FilterPanel;

    public Action? OnResetFilters { get; set; }
    public Action<string>? OnDatePresetSelect { get; set; }
    public Action<PhotoThumbnailItem>? OnSelectPhoto { get; set; }
    public Action<PhotoGridItem>? OnDrillIntoGroup { get; set; }
    public Func<Task<string?>>? OnChooseFolder { get; set; }
    public Action? OnOpenSettings { get; set; }

    // ギャラリー ViewModel と ShellPage 所有のフィルタパネルを受け取り、子コントロールを結線する。
    public GalleryPage(GalleryViewModel viewModel, GalleryFilterPanel filterPanel)
    {
        AppLogger.Trace("GalleryPage.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("GalleryPage.ctor: InitializeComponent done");
        this.viewModel = viewModel;
        this.FilterPanel = filterPanel;
        DataContext = viewModel;

        try
        {
            // フィルタパネルのコールバック結線
            FilterPanel.OnResetFilters = () => OnResetFilters?.Invoke();
            FilterPanel.OnDatePresetSelect = preset => OnDatePresetSelect?.Invoke(preset);
            FilterPanel.OnOrientationSelect = orientation => viewModel.filtersState.OrientationFilter = orientation;
            FilterPanel.OnSortSelect = sort => viewModel.filtersState.SortMode = sort;
            FilterPanel.OnDisplayFolderSelect = mode => viewModel.displayState.prepareDisplayFolderModeChange(
                viewModel.filtersState.DisplayFolderMode, mode, m => viewModel.filtersState.DisplayFolderMode = m);
            FilterPanel.OnGroupingSelect = mode =>
            {
                if (mode != Shared.Models.GroupingMode.none && viewModel.displayState.ViewMode == Shared.Models.ViewMode.gallery)
                    return;
                viewModel.displayState.prepareGroupingModeChange(
                    viewModel.filtersState.GroupingMode, mode, m => viewModel.filtersState.GroupingMode = m);
            };
            FilterPanel.OnWorldFilterAdd = worldName =>
            {
                if (!viewModel.filtersState.worldFilters.Contains(worldName))
                    viewModel.filtersState.worldFilters.Add(worldName);
            };
            FilterPanel.OnWorldFilterRemove = worldName => viewModel.filtersState.worldFilters.Remove(worldName);
            FilterPanel.OnTagFilterAdd = tag =>
            {
                if (!viewModel.filtersState.tagFilters.Contains(tag))
                    viewModel.filtersState.tagFilters.Add(tag);
            };
            FilterPanel.OnTagFilterRemove = tag => viewModel.filtersState.tagFilters.Remove(tag);
            FilterPanel.setWorldFilterOptions(viewModel.filtersState.worldFilterOptions);
            FilterPanel.bindFiltersState(viewModel.filtersState);

            // PhotoGrid コントロールのコールバック結線
            AppLogger.Trace("GalleryPage.ctor: setting GridDataContext");
            GridStage.GridDataContext = viewModel.photosState;
            GridStage.SetGridItemsSource(viewModel.photosState.displayItems);
            AppLogger.Trace("GalleryPage.ctor: GridDataContext set");

            if (GridStage.PhotoGridControlRef is { } photoGrid)
            {
                AppLogger.Trace("GalleryPage.ctor: wiring PhotoGrid callbacks");
                photoGrid.OnRightPanelMeasured = width => viewModel.displayState.rightPanelRef(width);
                photoGrid.OnGridWrapperMeasured = height => viewModel.displayState.gridWrapperRef(height);
                photoGrid.OnGridScroll = offset => viewModel.scrollState.handleGridScroll(offset);
                photoGrid.OnGridWheel = delta => viewModel.scrollState.handleGridWheel(delta);
                photoGrid.OnFirstVisibleIndexChanged = idx => SyncMonthNavToIndex(idx);
                photoGrid.OnFavoriteClicked = item => _ = viewModel.toggleFavorite(item.Photo.PhotoPath, item.Photo.IsFavorite);
                photoGrid.OnPhotoActivated = item =>
                {
                    if (viewModel.filtersState.GroupingMode != Shared.Models.GroupingMode.none && item.GroupKey is not null)
                        OnDrillIntoGroup?.Invoke(item);
                    else
                        viewModel.handlePhotoActivate(item, false, photo => OnSelectPhoto?.Invoke(photo));
                };
                AppLogger.Trace("GalleryPage.ctor: PhotoGrid callbacks wired");
            }
            else
            {
                AppLogger.Warn("GalleryPage.ctor: PhotoGridControlRef is null - callbacks not wired");
            }

            viewModel.selectionState.PropertyChanged += OnSelectionStateChanged;
            viewModel.selectionState.selectedPhotoPaths.CollectionChanged += OnSelectedPathsChanged;
            viewModel.photosState.PropertyChanged += OnPhotosStateChanged;

            // MasonryView のコールバック結線。x:Load=False で遅延生成されるため、
            // 最初に SetMasonryActive(true) が呼ばれて実体化された瞬間に結線する。
            GridStage.OnMasonryRealized = WireMasonryCallbacks;

            if (GridStage.MonthNavControlRef is { } monthNav)
            {
                monthNav.OnJumpToMonth = group =>
                {
                    if (viewModel.displayState.ViewMode == Alpheratz.Shared.Models.ViewMode.gallery)
                        GridStage.MasonryViewControlRef?.ScrollToPhotoIndex(group.FirstIndex);
                    else
                        GridStage.PhotoGridControlRef?.ScrollToPhotoIndex(group.FirstIndex);
                };
            }
            GridStage.SetMasonryActive(viewModel.displayState.ViewMode == Alpheratz.Shared.Models.ViewMode.gallery);
            FilterPanel.SetGroupingEnabled(viewModel.displayState.ViewMode != Alpheratz.Shared.Models.ViewMode.gallery);

            viewModel.photosState.OnMonthGroupsChanged = groups =>
            {
                try
                {
                    if (GridStage.MonthNavControlRef is not { } nav) return;
                    if (DispatcherQueue is { } dq)
                        dq.TryEnqueue(() => nav.SetGroups(groups));
                    else
                        nav.SetGroups(groups);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"GalleryPage.OnMonthGroupsChanged: threw: {ex}");
                }
            };

            viewModel.displayState.PropertyChanged += OnDisplayStateChanged;

            viewModel.photosState.OnPhotosReplaced = () =>
            {
                try
                {
                    if (DispatcherQueue is not { } dq) return;
                    dq.TryEnqueue(() =>
                    {
                        GridStage.MasonryViewControlRef?.ScrollToTop();
                        GridStage.PhotoGridControlRef?.ScrollToTop();
                    });
                }
                catch (Exception ex) { AppLogger.Error($"GalleryPage.OnPhotosReplaced: threw: {ex}"); }
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.ctor: wiring failed: {ex}");
            throw;
        }

        AppLogger.Trace("GalleryPage.ctor: exit");
    }

    /// <summary>タグマスタのドロップダウン候補をフィルタパネルとバルク操作バーに設定する。</summary>
    public void SetMasterTags(UiObservableCollection<string> tags)
    {
        AppLogger.Trace($"GalleryPage.SetMasterTags: enter count={tags.Count}");
        try
        {
            FilterPanel.setMasterTagsSource(tags);
            BulkTagCombo.ItemsSource = tags;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.SetMasterTags: threw: {ex}");
        }
        AppLogger.Trace("GalleryPage.SetMasterTags: exit");
    }

    // 選択写真の増減に合わせてバルク操作バーの表示を更新する。
    private void OnSelectedPathsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => updateBulkOpBar();

    /// <summary>ViewMode 変更時に Masonry/Grid の表示を切り替える。</summary>
    private void OnDisplayStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            if (e.PropertyName == nameof(viewModel.displayState.ViewMode))
            {
                var isGallery = viewModel.displayState.ViewMode == Alpheratz.Shared.Models.ViewMode.gallery;
                GridStage.SetMasonryActive(isGallery);
                FilterPanel.SetGroupingEnabled(!isGallery);

                if (isGallery && GridStage.MasonryViewControlRef is { } masonry)
                {
                    masonry.SetPhotos(viewModel.photosState.photos);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.OnDisplayStateChanged: threw: {ex}");
        }
    }

    /// <summary>
    /// MasonryView が遅延生成されたタイミングで一度だけ呼ばれ、コールバックと初期データを結線する。
    /// 以降のフィルタ変更や ViewMode 切替時は photos コレクション自体が共有参照なので
    /// 再 SetPhotos しなくても要素変化が伝播する。
    /// </summary>
    private void WireMasonryCallbacks(Controls.GalleryMasonryView masonry)
    {
        try
        {
            masonry.SetColumnCount(5);
            masonry.SetPhotos(viewModel.photosState.photos);
            masonry.OnPhotoTapped = photo => viewModel.handlePhotoActivate(
                new PhotoGridItem { Photo = photo }, false, p => OnSelectPhoto?.Invoke(p));
            masonry.OnThumbnailsNeeded = items => viewModel.photosState.requestVisibleThumbnails(items);
            masonry.OnFirstVisibleIndexChanged = idx => SyncMonthNavToIndex(idx);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.WireMasonryCallbacks: threw: {ex}");
        }
    }

    /// <summary>IsLoading / TotalCount の変化を GridStage の表示出し分けに反映する。</summary>
    private void OnPhotosStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            if (e.PropertyName == nameof(viewModel.photosState.IsLoading)
                || e.PropertyName == nameof(viewModel.photosState.TotalCount))
            {
                GridStage.UpdateLoadingState(viewModel.photosState.IsLoading, viewModel.photosState.TotalCount);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.OnPhotosStateChanged: threw: {ex}");
        }
    }

    /// <summary>マルチセレクトモード変更時にバルク操作バーの表示を更新する。</summary>
    private void OnSelectionStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        AppLogger.Trace($"GalleryPage.OnSelectionStateChanged: enter property={e.PropertyName}");
        try
        {
            if (e.PropertyName == nameof(GallerySelectionState.IsMultiSelectMode))
            {
                AppLogger.Trace("GalleryPage.OnSelectionStateChanged: branch=IsMultiSelectMode");
                updateBulkOpBar();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.OnSelectionStateChanged: threw: {ex}");
        }
        AppLogger.Trace("GalleryPage.OnSelectionStateChanged: exit");
    }

    private bool bulkOpBarWasVisible;

    /// <summary>バルク操作バーの表示/非表示と選択枚数ラベルを更新する。</summary>
    private void updateBulkOpBar()
    {
        AppLogger.Trace("GalleryPage.updateBulkOpBar: enter");
        try
        {
            var state = GalleryPageLogic.ComputeBulkOperationBarState(
                viewModel.selectionState.IsMultiSelectMode,
                bulkOpBarWasVisible,
                viewModel.selectionState.selectedPhotoPaths.Count);
            if (state.Transition == BulkOperationBarTransition.Show)
            {
                BulkOpBar.Visibility = Visibility.Visible;
                AnimationHelper.SlideIn(BulkOpBar, fromY: 20f, durationMs: 200);
            }
            else if (state.Transition == BulkOperationBarTransition.Hide)
            {
                AnimationHelper.SlideOut(BulkOpBar, toY: 20f, durationMs: 150, onCompleted: () =>
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        BulkOpBar.Visibility = Visibility.Collapsed;
                        AnimationHelper.ResetVisual(BulkOpBar);
                    });
                });
            }
            bulkOpBarWasVisible = state.NextWasVisible;
            SelectionCountLabel.Text = state.SelectionLabel;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.updateBulkOpBar: threw: {ex}");
        }
        AppLogger.Trace("GalleryPage.updateBulkOpBar: exit");
    }

    /// <summary>マルチセレクトモードを終了する。</summary>
    private void ExitMultiSelect_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryPage.ExitMultiSelect_Click: enter");
        try
        {
            viewModel.selectionState.handleToggleMultiSelectMode();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.ExitMultiSelect_Click: threw: {ex}");
        }
        AppLogger.Trace("GalleryPage.ExitMultiSelect_Click: exit");
    }

    /// <summary>選択写真を一括お気に入り追加する。</summary>
    private async void BulkFavorite_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryPage.BulkFavorite_Click: enter");
        try
        {
            await viewModel.bulkSetFavorite(true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.BulkFavorite_Click: threw: {ex}");
        }
        AppLogger.Trace("GalleryPage.BulkFavorite_Click: exit");
    }

    /// <summary>選択写真を一括お気に入り解除する。</summary>
    private async void BulkUnfavorite_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryPage.BulkUnfavorite_Click: enter");
        try
        {
            await viewModel.bulkSetFavorite(false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.BulkUnfavorite_Click: threw: {ex}");
        }
        AppLogger.Trace("GalleryPage.BulkUnfavorite_Click: exit");
    }

    /// <summary>ドロップダウンから選択されたタグを一括追加する。</summary>
    private async void BulkTagCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AppLogger.Trace("GalleryPage.BulkTagCombo_SelectionChanged: enter");
        try
        {
            if (BulkTagCombo.SelectedItem is string tag)
            {
                BulkTagCombo.SelectedIndex = -1;
                await viewModel.bulkAddTag(tag).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.BulkTagCombo_SelectionChanged: threw: {ex}");
        }
        AppLogger.Trace("GalleryPage.BulkTagCombo_SelectionChanged: exit");
    }

    // 表示中の先頭写真インデックスから、月ナビのアクティブ月を決める。
    private void SyncMonthNavToIndex(int firstVisibleIndex)
    {
        try
        {
            var groups = viewModel.photosState.monthGroups;
            if (groups.Count == 0 || GridStage.MonthNavControlRef is not { } nav) return;

            if (GalleryPageLogic.FindActiveMonthGroupIndex(groups, firstVisibleIndex) is { } matchedGroupIdx)
                nav.SetActiveIndex(matchedGroupIdx);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.SyncMonthNavToIndex: threw: {ex}");
        }
    }

    // 再表示時に現在の表示モードと読み込み状態を子コントロールへ同期する。
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // ctor で購読、Unloaded で解除する片付け済みパスを通すので、ここでは
        // 二重購読しない。再ナビゲーション時の状態同期のみ実行する。
        var isGallery = viewModel.displayState.ViewMode == Shared.Models.ViewMode.gallery;
        GridStage.SetMasonryActive(isGallery);
        FilterPanel.SetGroupingEnabled(!isGallery);
        GridStage.UpdateLoadingState(viewModel.photosState.IsLoading, viewModel.photosState.TotalCount);
    }

    // ページ破棄時に ViewModel と子コントロールへの購読・コールバックを解除する。
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        viewModel.selectionState.PropertyChanged -= OnSelectionStateChanged;
        viewModel.selectionState.selectedPhotoPaths.CollectionChanged -= OnSelectedPathsChanged;
        viewModel.displayState.PropertyChanged -= OnDisplayStateChanged;
        viewModel.photosState.PropertyChanged -= OnPhotosStateChanged;

        viewModel.photosState.OnMonthGroupsChanged = null;
        viewModel.photosState.OnPhotosReplaced = null;
        GridStage.OnMasonryRealized = null;
        if (GridStage.MasonryViewControlRef is { } masonry)
        {
            masonry.OnPhotoTapped = null;
            masonry.OnThumbnailsNeeded = null;
            masonry.OnFirstVisibleIndexChanged = null;
        }
        if (GridStage.MonthNavControlRef is { } monthNav)
        {
            monthNav.OnJumpToMonth = null;
        }
    }

    /// <summary>フォルダ選択ダイアログを表示し、選択写真を一括コピーする。</summary>
    private async void BulkCopy_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryPage.BulkCopy_Click: enter");
        try
        {
            if (OnChooseFolder is null)
            {
                AppLogger.Trace("GalleryPage.BulkCopy_Click: skip (no OnChooseFolder)");
                return;
            }
            var folder = await OnChooseFolder().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                await viewModel.bulkCopyPhotos(folder).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.BulkCopy_Click: threw: {ex}");
        }
        AppLogger.Trace("GalleryPage.BulkCopy_Click: exit");
    }
}
