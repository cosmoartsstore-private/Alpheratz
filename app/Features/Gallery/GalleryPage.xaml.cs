using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery.Controls;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// ギャラリー画面の Page。ViewModel と各子コントロールのコールバックを結線する。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class GalleryPage : Page
{
    private readonly GalleryViewModel viewModel;
    private UiObservableCollection<string>? masterTags;
    private readonly HashSet<string> pendingBulkTags = new(StringComparer.OrdinalIgnoreCase);
    private Control? bulkTagPreviousFocus;
    public ObservableCollection<string> BulkTagConfirmationTargets { get; } = [];

    /// <summary>
    /// 検索条件パネル。ShellPage 側の XAML (FilterOverlay) で生成されたインスタンスを
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
    public Action<bool>? OnBulkTagConfirmationOpenChanged { get; set; }
    public bool IsBulkTagConfirmationOpen => BulkTagConfirmOverlay.Visibility == Visibility.Visible;

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
            FilterPanel.OnSortSelect = sort =>
            {
                viewModel.filtersState.SortMode = sort;
                SyncMonthNavAvailability();
            };
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
                    var shiftKey = IsShiftKeyDown();
                    if (viewModel.selectionState.IsMultiSelectMode)
                    {
                        viewModel.handlePhotoActivate(item, shiftKey, photo => OnSelectPhoto?.Invoke(photo));
                        return;
                    }

                    if (viewModel.filtersState.GroupingMode != Shared.Models.GroupingMode.none && item.GroupKey is not null)
                        OnDrillIntoGroup?.Invoke(item);
                    else
                        viewModel.handlePhotoActivate(item, shiftKey, photo => OnSelectPhoto?.Invoke(photo));
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
            viewModel.filtersState.PropertyChanged += OnFiltersStateChanged;

            // MasonryView のコールバック結線。x:Load=False で遅延生成されるため、
            // 最初に SetMasonryActive(true) が呼ばれて実体化された瞬間に結線する。
            GridStage.OnMasonryRealized = WireMasonryCallbacks;

            if (GridStage.MonthNavControlRef is { } monthNav)
            {
                monthNav.OnJumpToMonth = group =>
                {
                    if (!IsMonthNavAvailableForCurrentSort()) return;
                    if (viewModel.displayState.ViewMode == Alpheratz.Shared.Models.ViewMode.gallery)
                        GridStage.MasonryViewControlRef?.ScrollToPhotoIndex(group.FirstIndex);
                    else
                        GridStage.PhotoGridControlRef?.ScrollToPhotoIndex(group.FirstIndex);
                };
            }
            GridStage.SetMasonryActive(viewModel.displayState.ViewMode == Alpheratz.Shared.Models.ViewMode.gallery);
            SyncMonthNavAvailability();
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
                        viewModel.selectionState.syncSelectionWithVisiblePhotos(viewModel.photosState.photos);
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

    /// <summary>ソート条件変更時に、日付順専用の MonthNav 表示を同期する。</summary>
    private void OnFiltersStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            if (e.PropertyName == nameof(GalleryFiltersState.SortMode))
            {
                SyncMonthNavAvailability();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.OnFiltersStateChanged: threw: {ex}");
        }
    }

    /// <summary>タグマスタのドロップダウン候補をフィルタパネルとバルク操作バーに設定する。</summary>
    public void SetMasterTags(UiObservableCollection<string> tags)
    {
        AppLogger.Trace($"GalleryPage.SetMasterTags: enter count={tags.Count}");
        try
        {
            FilterPanel.setMasterTagsSource(tags);
            masterTags = tags;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.SetMasterTags: threw: {ex}");
        }
        AppLogger.Trace("GalleryPage.SetMasterTags: exit");
    }

    // 選択写真の増減に合わせてバルク操作バーの表示を更新する。
    private void OnSelectedPathsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // 確認開始後に対象選択が変わった場合、表示中の対象名と実行対象が一致しなくなるため閉じる。
        if (IsBulkTagConfirmationOpen)
            CloseBulkTagConfirmation();
        updateBulkOpBar();
    }

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
                new PhotoGridItem { Photo = photo }, IsShiftKeyDown(), p => OnSelectPhoto?.Invoke(p));
            masonry.OnFavoriteClicked = photo => _ = viewModel.toggleFavorite(photo.PhotoPath, photo.IsFavorite);
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
                if (!viewModel.selectionState.IsMultiSelectMode && IsBulkTagConfirmationOpen)
                    CloseBulkTagConfirmation();
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
            }
            else if (state.Transition == BulkOperationBarTransition.Hide)
            {
                BulkOpBar.Visibility = Visibility.Collapsed;
            }
            bulkOpBarWasVisible = state.NextWasVisible;
            SelectionCountLabel.Text = viewModel.selectionState.selectedPhotoPaths.Count.ToString();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.updateBulkOpBar: threw: {ex}");
        }
        AppLogger.Trace("GalleryPage.updateBulkOpBar: exit");
    }

    /// <summary>バルク操作バー上のポインタ入力をここで止め、背面の写真カードへ渡さない。</summary>
    private void BulkOpBar_PointerPressed(object sender, PointerRoutedEventArgs e)
        => e.Handled = true;

    /// <summary>バルク操作バー内のタップを背面の GridView 選択へ伝播させない。</summary>
    private void BulkOpBar_Tapped(object sender, TappedRoutedEventArgs e)
        => e.Handled = true;

    /// <summary>マルチセレクトモードを終了する。</summary>
    private void ExitMultiSelect_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryPage.ExitMultiSelect_Click: enter");
        try
        {
            viewModel.selectionState.exitMultiSelectMode();
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

    /// <summary>複数選択中の写真へ追加するタグを選ぶ確認モーダルを開く。</summary>
    private void BulkTagButton_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryPage.BulkTagButton_Click: enter");
        try
        {
            ShowBulkTagConfirmation();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.BulkTagButton_Click: threw: {ex}");
        }
        AppLogger.Trace("GalleryPage.BulkTagButton_Click: exit");
    }

    /// <summary>一括タグ追加の対象写真と追加タグを確認するモーダルを開く。</summary>
    private void ShowBulkTagConfirmation()
    {
        var selectedPaths = viewModel.selectionState.selectedPhotoPaths.ToArray();
        if (selectedPaths.Length == 0) return;

        if (!IsBulkTagConfirmationOpen)
            bulkTagPreviousFocus = FocusManager.GetFocusedElement(XamlRoot) as Control;

        pendingBulkTags.Clear();
        rebuildBulkTagSelectionRows();
        BulkTagConfirmationTargets.Clear();

        var photoMap = viewModel.photosState.photos
            .ToDictionary(photo => photo.PhotoPath, photo => photo.PhotoFilename, StringComparer.Ordinal);
        foreach (var path in selectedPaths)
        {
            var name = photoMap.TryGetValue(path, out var filename) && !string.IsNullOrWhiteSpace(filename)
                ? filename
                : Path.GetFileName(path);
            BulkTagConfirmationTargets.Add(string.IsNullOrWhiteSpace(name) ? path : name);
        }

        updateBulkTagConfirmLabels(selectedPaths.Length);
        GridStage.IsEnabled = false;
        BulkOpBar.IsHitTestVisible = false;
        BulkTagConfirmOverlay.Visibility = Visibility.Visible;
        OnBulkTagConfirmationOpenChanged?.Invoke(true);
        BulkTagCancelButton.Focus(FocusState.Programmatic);
    }

    /// <summary>タグ候補を重複なし・空文字なしの選択行として再描画する。</summary>
    private void rebuildBulkTagSelectionRows()
    {
        BulkTagSelectionList.Children.Clear();
        var choices = (masterTags ?? [])
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrEmpty(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.Create(new System.Globalization.CultureInfo("ja-JP"), false))
            .ToArray();

        if (choices.Length == 0)
        {
            BulkTagSelectionList.Children.Add(new TextBlock
            {
                Text = getMsg("GalleryPage.noBulkTags"),
                Foreground = themeBrush("ATextFaint"),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(8, 6, 8, 6),
            });
            return;
        }

        foreach (var tag in choices)
            BulkTagSelectionList.Children.Add(createBulkTagChoiceButton(tag));
    }

    /// <summary>一括追加タグの選択行を作成する。</summary>
    private Button createBulkTagChoiceButton(string tag)
    {
        var selected = pendingBulkTags.Contains(tag);
        var checkBox = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(7),
            Background = selected ? themeBrush("APrimary") : themeBrush("ASurface"),
            BorderBrush = selected ? themeBrush("APrimary") : themeBrush("ABorder"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (selected)
        {
            checkBox.Child = new Alpheratz.Shared.Controls.AppIcon
            {
                IconName = "check",
                IconSize = 12,
                Foreground = themeBrush("ATextOnPrimary"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        var row = new Grid
        {
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(checkBox, 0);
        row.Children.Add(checkBox);

        var label = new TextBlock
        {
            Text = tag,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = selected ? themeBrush("APrimary") : themeBrush("ATextDim"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Child = row,
        };

        var button = new Button
        {
            Content = border,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(
            button,
            getMsg(
                selected
                    ? "GalleryPage.bulkTagChoiceRemoveAutomationName"
                    : "GalleryPage.bulkTagChoiceAddAutomationName",
                ("tag", tag)));
        button.Click += (_, _) =>
        {
            if (!pendingBulkTags.Add(tag))
                pendingBulkTags.Remove(tag);
            rebuildBulkTagSelectionRows();
            updateBulkTagConfirmLabels(viewModel.selectionState.selectedPhotoPaths.Count);
        };
        return button;
    }

    /// <summary>一括タグ追加確認モーダルの件数表示と実行可否を更新する。</summary>
    private void updateBulkTagConfirmLabels(int selectedPhotoCount)
    {
        var tagCount = pendingBulkTags.Count;
        BulkTagConfirmTagLabel.Text = tagCount == 0
            ? getMsg("GalleryPage.selectTagsPrompt")
            : getMsg(
                "GalleryPage.selectedTags",
                ("tags", string.Join(
                    "、",
                    pendingBulkTags.OrderBy(
                        tag => tag,
                        StringComparer.Create(new System.Globalization.CultureInfo("ja-JP"), false)))));
        BulkTagConfirmSummaryLabel.Text = tagCount == 0
            ? getMsg("GalleryPage.selectedPhotoCount", ("count", selectedPhotoCount))
            : getMsg(
                "GalleryPage.bulkTagSummary",
                ("photoCount", selectedPhotoCount),
                ("tagCount", tagCount));
        BulkTagConfirmButton.IsEnabled = tagCount > 0;
    }

    private async void ConfirmBulkTag_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("GalleryPage.ConfirmBulkTag_Click: enter");
        try
        {
            var tags = pendingBulkTags.ToArray();
            CloseBulkTagConfirmation();
            if (tags.Length > 0)
                await viewModel.bulkAddTags(tags);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GalleryPage.ConfirmBulkTag_Click: threw: {ex}");
        }
        AppLogger.Trace("GalleryPage.ConfirmBulkTag_Click: exit");
    }

    private void CancelBulkTagConfirm_Click(object sender, RoutedEventArgs e)
    {
        try { CloseBulkTagConfirmation(); }
        catch (Exception ex) { AppLogger.Error($"GalleryPage.CancelBulkTagConfirm_Click: threw: {ex}"); }
    }

    private void BulkTagConfirmOverlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            CloseBulkTagConfirmation();
        }
        catch (Exception ex) { AppLogger.Error($"GalleryPage.BulkTagConfirmOverlay_Tapped: threw: {ex}"); }
    }

    private void BulkTagConfirmContent_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>一括タグ確認中の Esc を背面の複数選択解除へ渡さず、確認だけを閉じる。</summary>
    private void Page_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!IsBulkTagConfirmationOpen || e.Key != VirtualKey.Escape)
            return;

        CloseBulkTagConfirmation();
        e.Handled = true;
    }

    /// <summary>一括タグ追加の確認モーダルを閉じ、保留中のタグと対象表示を破棄する。</summary>
    private void CloseBulkTagConfirmation()
    {
        if (!IsBulkTagConfirmationOpen)
            return;

        pendingBulkTags.Clear();
        BulkTagSelectionList.Children.Clear();
        BulkTagConfirmationTargets.Clear();
        BulkTagConfirmOverlay.Visibility = Visibility.Collapsed;
        GridStage.IsEnabled = true;
        BulkOpBar.IsHitTestVisible = true;
        OnBulkTagConfirmationOpenChanged?.Invoke(false);

        var previousFocus = bulkTagPreviousFocus;
        bulkTagPreviousFocus = null;
        previousFocus?.Focus(FocusState.Programmatic);
    }

    private Brush themeBrush(string key)
        => ThemeHelper.Brush(this, key) ?? new SolidColorBrush(Colors.Transparent);

    // 表示中の先頭写真インデックスから、月ナビのアクティブ月を決める。
    private void SyncMonthNavToIndex(int firstVisibleIndex)
    {
        try
        {
            var groups = viewModel.photosState.monthGroups;
            if (!IsMonthNavAvailableForCurrentSort()) return;
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
        SyncMonthNavAvailability();
        GridStage.UpdateLoadingState(viewModel.photosState.IsLoading, viewModel.photosState.TotalCount);
        viewModel.selectionState.syncSelectionWithVisiblePhotos(viewModel.photosState.photos);
    }

    // ページ破棄時に ViewModel と子コントロールへの購読・コールバックを解除する。
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        bulkTagPreviousFocus = null;
        if (IsBulkTagConfirmationOpen)
            CloseBulkTagConfirmation();

        viewModel.selectionState.PropertyChanged -= OnSelectionStateChanged;
        viewModel.selectionState.selectedPhotoPaths.CollectionChanged -= OnSelectedPathsChanged;
        viewModel.displayState.PropertyChanged -= OnDisplayStateChanged;
        viewModel.photosState.PropertyChanged -= OnPhotosStateChanged;
        viewModel.filtersState.PropertyChanged -= OnFiltersStateChanged;

        viewModel.photosState.OnMonthGroupsChanged = null;
        viewModel.photosState.OnPhotosReplaced = null;
        GridStage.OnMasonryRealized = null;
        if (GridStage.MasonryViewControlRef is { } masonry)
        {
            masonry.OnPhotoTapped = null;
            masonry.OnFavoriteClicked = null;
            masonry.OnThumbnailsNeeded = null;
            masonry.OnFirstVisibleIndexChanged = null;
        }
        if (GridStage.MonthNavControlRef is { } monthNav)
        {
            monthNav.OnJumpToMonth = null;
        }
        OnBulkTagConfirmationOpenChanged = null;
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

    private bool IsMonthNavAvailableForCurrentSort()
        => viewModel.filtersState.SortMode == SortMode.dateDesc;

    private void SyncMonthNavAvailability()
        => GridStage.SetMonthNavAvailable(IsMonthNavAvailableForCurrentSort());

    /// <summary>現在のキーボード状態から Shift 範囲選択の有無を取得する。</summary>
    private static bool IsShiftKeyDown()
        => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
}
