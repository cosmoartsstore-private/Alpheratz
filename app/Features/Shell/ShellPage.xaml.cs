using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Features.Settings;
using Alpheratz.Features.TagMaster;
using Alpheratz.Features.Template;
using Alpheratz.Features.WorldResolve;
using Alpheratz.Shared.Animations;
using Alpheratz.Shared.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Shell;

public sealed partial class ShellPage : Page
{
    private readonly ShellViewModel viewModel;
    private GalleryPage? galleryPage;
    private SettingsPage? settingsPage;
    private TagMasterPage? tagMasterPage;
    private TemplatePage? templatePage;
    private PhotoModalPage? cachedModalPage;
    private bool isFilterOpen;
    private bool isModalOpen;

    public ShellPage(ShellViewModel viewModel)
    {
        AppLogger.Trace("ShellPage.ctor: enter");
        try { InitializeComponent(); }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ctor: InitializeComponent failed: {ex}"); throw; }
        AppLogger.Trace("ShellPage.ctor: InitializeComponent done");
        this.viewModel = viewModel;
        DataContext = viewModel;

        try
        {
            HeaderBar.OnToggleFilter = ToggleFilter;
            HeaderBar.OnShowSettings = ShowSettings;
            LeftRail.OnShowGallery = ShowGallery;
            LeftRail.OnShowTagMaster = ShowTagMaster;
            LeftRail.OnShowTemplate = ShowTemplate;
            LeftRail.OnToggleMultiSelect = () =>
            {
                viewModel.galleryViewModel.selectionState.handleToggleMultiSelectMode();
                LeftRail.SetMultiSelectActive(viewModel.galleryViewModel.selectionState.IsMultiSelectMode);
            };
            LeftRail.OnGroupingChange = async mode =>
            {
                if (mode != GroupingMode.none && viewModel.ViewMode == ViewMode.gallery)
                    await viewModel.handleSetViewMode(ViewMode.standard).ConfigureAwait(true);
                viewModel.galleryViewModel.displayState.prepareGroupingModeChange(
                    viewModel.galleryViewModel.filtersState.GroupingMode, mode,
                    m => viewModel.galleryViewModel.filtersState.GroupingMode = m);
                LeftRail.SetGroupingMode(mode);
            };
            LeftRail.OnViewModeChange = async modeStr =>
            {
                var mode = modeStr == "gallery" ? ViewMode.gallery : ViewMode.standard;
                await viewModel.handleSetViewMode(mode).ConfigureAwait(false);
            };

            viewModel.galleryViewModel.selectionState.PropertyChanged += OnSelectionStateChanged;
            viewModel.PropertyChanged += OnShellViewModelChanged;

            // drill-down 中の写真コレクションを GalleryViewModel.updatePhoto に
            // 共有し、PhotoModal 経由のタグ/お気に入り更新が drill-down
            // 一覧にも即時反映されるようにする。
            viewModel.galleryViewModel.drillDownPhotosProvider = () => drillDownPhotos;

            Stage.OnBackToGallery = ShowGallery;
            Stage.ScanningOverlayControlRef.OnCancelScan = viewModel.cancelScan;
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ctor: wiring failed: {ex}"); throw; }

        AppLogger.Trace("ShellPage.ctor: wiring done, calling ShowGallery");
        ShowGallery();
        LeftRail.SetViewMode(viewModel.ViewMode);
        AppLogger.Trace("ShellPage.ctor: exit");
    }

    private void OnSelectionStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        AppLogger.Trace($"ShellPage.OnSelectionStateChanged: enter property={e.PropertyName}");
        try
        {
            if (e.PropertyName == nameof(viewModel.galleryViewModel.selectionState.IsMultiSelectMode))
                LeftRail.SetMultiSelectActive(viewModel.galleryViewModel.selectionState.IsMultiSelectMode);
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.OnSelectionStateChanged: threw: {ex}"); }
        AppLogger.Trace("ShellPage.OnSelectionStateChanged: exit");
    }

    private void OnShellViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        AppLogger.Trace($"ShellPage.OnShellViewModelChanged: enter property={e.PropertyName}");
        try
        {
            if (e.PropertyName == nameof(viewModel.ViewMode))
            {
                LeftRail.SetViewMode(viewModel.ViewMode);
                LeftRail.SetGroupingMode(viewModel.galleryViewModel.filtersState.GroupingMode);
            }
            else if (e.PropertyName == nameof(viewModel.ScanStatus)) UpdateScanningOverlayVisibility();
            else if (e.PropertyName == nameof(viewModel.PendingFolderPath) && viewModel.PendingFolderPath is not null) _ = ShowFolderChangeConfirmAsync();
            else if (e.PropertyName == nameof(viewModel.PendingResetRequest) && viewModel.PendingResetRequest is not null) _ = ShowResetConfirmAsync();
            else if (e.PropertyName == nameof(viewModel.ThemeMode)) ApplyTheme(viewModel.ThemeMode);
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.OnShellViewModelChanged: threw: {ex}"); }
        AppLogger.Trace("ShellPage.OnShellViewModelChanged: exit");
    }

    private async Task<bool?> ShowConfirmDialogAsync(string title, string message, string yesText, string noText, string? cancelText = null)
    {
        try
        {
            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = title, Content = message,
                PrimaryButtonText = yesText, SecondaryButtonText = noText,
                CloseButtonText = cancelText ?? string.Empty, XamlRoot = this.XamlRoot,
                DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            };
            var result = await dialog.ShowAsync();
            return result switch
            {
                Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary => true,
                Microsoft.UI.Xaml.Controls.ContentDialogResult.Secondary => false,
                _ => null,
            };
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowConfirmDialogAsync: threw: {ex}"); return null; }
    }

    private async Task ShowFolderChangeConfirmAsync()
    {
        AppLogger.Trace($"ShellPage.ShowFolderChangeConfirmAsync: enter pending={viewModel.PendingFolderPath}");
        var newPath = viewModel.PendingFolderPath;
        if (string.IsNullOrEmpty(newPath)) return;
        var answer = await ShowConfirmDialogAsync(
            title: "写真フォルダの変更",
            message: $"新しい写真フォルダ:\n{newPath}\n\n既存のキャッシュをリセットして変更しますか？\n（タグは失われる可能性があります）",
            yesText: "変更する",
            noText: "キャンセル").ConfigureAwait(true);
        if (answer != true) { viewModel.PendingFolderPath = null; return; }
        await viewModel.applyFolderChange(newPath).ConfigureAwait(false);
    }

    private async Task ShowResetConfirmAsync()
    {
        AppLogger.Trace("ShellPage.ShowResetConfirmAsync: enter");
        if (viewModel.PendingResetRequest is null) return;
        var answer = await ShowConfirmDialogAsync(
            title: "写真フォルダのリセット",
            message: "現在の写真フォルダ設定とキャッシュをリセットします。",
            yesText: "リセットする",
            noText: "キャンセル").ConfigureAwait(true);
        if (answer != true) { viewModel.PendingResetRequest = null; return; }
        await viewModel.executeResetFolder(viewModel.PendingResetRequest.slot).ConfigureAwait(false);
    }

    public void ShowGallery()
    {
        AppLogger.Trace("ShellPage.ShowGallery: enter");
        try
        {
            if (galleryPage is null)
            {
                galleryPage = new GalleryPage(viewModel.galleryViewModel)
                {
                    OnResetFilters = viewModel.galleryViewModel.resetFilters,
                    OnDatePresetSelect = preset => viewModel.galleryViewModel.filtersState.handleDatePresetSelect(preset),
                    OnSelectPhoto = photo => { if (viewModel.createPhotoModalViewModel(photo) is { } vm) ShowPhotoModal(vm); },
                    OnDrillIntoGroup = item => _ = ShowGroupDrillDown(item),
                    OnChooseFolder = () => viewModel.settingsViewModel.handleChooseFolderPathOnly(),
                    OnDismissFilter = () => { if (isFilterOpen) ToggleFilter(); },
                };
                galleryPage.SetMasterTags(viewModel.tagMasterViewModel.masterTags);
            }
            Stage.MainContent = galleryPage;
            Stage.SetBackButtonVisible(false);
            LeftRail.SetActiveScreen(MainScreen.gallery);
            LeftRail.SetViewMode(viewModel.ViewMode);
            LeftRail.SetGroupingMode(viewModel.galleryViewModel.filtersState.GroupingMode);
            HeaderBar.SetGalleryControlsEnabled(true);
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowGallery: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowGallery: exit");
    }

    private GroupDrillDownPage? drillDownPage;
    private IReadOnlyList<PhotoThumbnailItem>? drillDownPhotos;

    /// <summary>
    /// グループカードをクリックしたときの遷移。
    /// 毎回新しい GroupDrillDownPage と PhotoThumbnailItem リストを確保し、
    /// SQL を発行して取得したデータをメインと同じ PhotoGrid で描画する。
    /// </summary>
    private async Task ShowGroupDrillDown(PhotoGridItem groupItem)
    {
        AppLogger.Trace($"ShellPage.ShowGroupDrillDown: enter groupKey={groupItem.GroupKey}");
        try
        {
            var groupKey = groupItem.GroupKey;
            if (string.IsNullOrEmpty(groupKey)) return;
            var photos = await viewModel.galleryViewModel.getGroupPhotosAsync(groupKey).ConfigureAwait(true);

            // 新規メモリ：毎回 Page を作り直すことで残留状態（スクロール位置、
            // 旧サムネイル購読、旧 GridView Item recycling キャッシュ）を遮断する。
            drillDownPhotos = photos;
            drillDownPage = new GroupDrillDownPage
            {
                OnBack = () => { drillDownPhotos = null; ShowGallery(); },
                OnPhotoActivated = photo =>
                {
                    if (drillDownPhotos is not null
                        && viewModel.createPhotoModalViewModelFromList(photo, drillDownPhotos) is { } vm)
                        ShowPhotoModal(vm);
                },
                OnFavoriteClicked = photo =>
                    _ = viewModel.galleryViewModel.toggleFavorite(photo.PhotoPath, photo.IsFavorite),
                OnThumbnailsNeeded = items =>
                    viewModel.galleryViewModel.photosState.kickThumbnailsForExternal(items),
            };

            var displayName = groupItem.Photo?.WorldName ?? groupKey;
            drillDownPage.SetGroupInfo(displayName, photos);
            Stage.MainContent = drillDownPage;
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowGroupDrillDown: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowGroupDrillDown: exit");
    }

    public void ShowSettings()
    {
        AppLogger.Trace("ShellPage.ShowSettings: enter");
        try
        {
            if (settingsPage is null)
            {
                settingsPage = new SettingsPage(viewModel.settingsViewModel)
                {
                    OnChooseFolder = async slot =>
                    {
                        var path = await viewModel.settingsViewModel.handleChooseFolderPathOnly().ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(path)) return;
                        var currentPath = slot == 1 ? viewModel.PhotoFolderPath : viewModel.SecondaryPhotoFolderPath;
                        if (string.Equals(path, currentPath, StringComparison.Ordinal)) return;
                        if (slot == 1 && !string.IsNullOrEmpty(currentPath))
                            viewModel.promptFolderChange(slot, path);
                        else
                        {
                            viewModel.PendingFolderSlot = slot;
                            await viewModel.applyFolderChange(path).ConfigureAwait(false);
                        }
                    },
                    OnResetFolder = viewModel.handleResetFolder,
                    OnStartupPreferenceChanged = viewModel.handleStartupPreference,
                    OnThemeChanged = isDark => viewModel.handleThemeChange(isDark ? Alpheratz.Shared.Models.ThemeMode.dark : Alpheratz.Shared.Models.ThemeMode.light),
                    OnStartWorldAnalysis = ShowWorldResolveModalAsync,
                };
            }
            Stage.MainContent = settingsPage;
            Stage.SetBackButtonVisible(true);
            LeftRail.SetActiveScreen(MainScreen.settings);
            HeaderBar.SetGalleryControlsEnabled(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowSettings: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowSettings: exit");
    }

    public void ShowTagMaster()
    {
        AppLogger.Trace("ShellPage.ShowTagMaster: enter");
        try
        {
            if (tagMasterPage is null)
            {
                tagMasterPage = new TagMasterPage(viewModel.tagMasterViewModel)
                {
                    OnCreateTag = viewModel.tagMasterViewModel.createTag,
                    OnDeleteTag = viewModel.tagMasterViewModel.deleteTag,
                };
            }
            Stage.MainContent = tagMasterPage;
            Stage.SetBackButtonVisible(true);
            LeftRail.SetActiveScreen(MainScreen.tagMaster);
            HeaderBar.SetGalleryControlsEnabled(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowTagMaster: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowTagMaster: exit");
    }

    public void ShowTemplate()
    {
        AppLogger.Trace("ShellPage.ShowTemplate: enter");
        try
        {
            if (templatePage is null)
            {
                templatePage = new TemplatePage(viewModel.templatePageViewModel)
                {
                    OnCancelEdit = viewModel.templatePageViewModel.cancelEdit,
                    OnStartEdit = viewModel.templatePageViewModel.startEdit,
                    // R2-A-2: deleteTemplate は currentSetting を要求するため、ここで現在の設定を取って渡す。
                    OnDeleteTemplate = template => viewModel.templatePageViewModel.deleteTemplate(template, viewModel.buildSettingPayload()),
                    OnSaveTemplate = () => viewModel.templatePageViewModel.saveTemplate(viewModel.buildSettingPayload()),
                    OnSelectTemplate = async template =>
                    {
                        viewModel.templatePageViewModel.ActiveTweetTemplate = template;
                        await viewModel.templatePageViewModel.saveTemplates(viewModel.buildSettingPayload()).ConfigureAwait(false);
                    },
                };
            }
            Stage.MainContent = templatePage;
            Stage.SetBackButtonVisible(true);
            LeftRail.SetActiveScreen(MainScreen.template);
            HeaderBar.SetGalleryControlsEnabled(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowTemplate: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowTemplate: exit");
    }

    public void ShowPhotoModal(PhotoModalViewModel modalViewModel)
    {
        AppLogger.Trace("ShellPage.ShowPhotoModal: enter");
        try
        {
            if (cachedModalPage is null)
            {
                cachedModalPage = new PhotoModalPage(modalViewModel);
                cachedModalPage.OnAddTag = (photoPath, tag) => viewModel.galleryViewModel.addTag(photoPath, tag);
                cachedModalPage.OnRemoveTag = (photoPath, tag) => viewModel.galleryViewModel.removeTag(photoPath, tag);
            }
            else cachedModalPage.UpdateViewModel(modalViewModel);
            var page = cachedModalPage;
            page.OnClose = () => { modalViewModel.closePhotoModal(); CloseModal(); };
            page.OnOpenWorld = modalViewModel.handleOpenWorld;
            page.OnOpenExplorer = modalViewModel.handleOpenExplorer;
            // PhotoModal からタグマスタ画面への導線。
            page.OnOpenTagMaster = () =>
            {
                modalViewModel.closePhotoModal();
                CloseModal();
                ShowTagMaster();
            };
            page.OnToggleFavorite = async () =>
            {
                var selectedPhoto = modalViewModel.state.SelectedPhoto;
                if (selectedPhoto is not null)
                {
                    var newValue = !selectedPhoto.IsFavorite;
                    await viewModel.galleryViewModel.toggleFavorite(selectedPhoto.PhotoPath, selectedPhoto.IsFavorite).ConfigureAwait(false);
                    DispatcherQueue?.TryEnqueue(() => selectedPhoto.IsFavorite = newValue);
                }
            };
            page.OnTweet = async () =>
            {
                var selectedPhoto = modalViewModel.state.SelectedPhoto;
                if (selectedPhoto is not null)
                    await viewModel.templatePageViewModel.openTweetIntent(selectedPhoto).ConfigureAwait(false);
            };
            page.OnGoBack = () => modalViewModel.goBackPhoto();
            page.OnGoPrev = () => modalViewModel.state.goPrevPhoto();
            page.OnGoNext = () => modalViewModel.state.goNextPhoto();
            page.SetMasterTags(viewModel.tagMasterViewModel.masterTags);
            Stage.ModalContent = page;
            Stage.ModalVisibility = Visibility.Visible;
            isModalOpen = true;
            HeaderBar.Opacity = 0.4;
            LeftRail.Opacity = 0.4;
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowPhotoModal: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowPhotoModal: exit");
    }

    private async Task ShowWorldResolveModalAsync()
    {
        AppLogger.Trace("ShellPage.ShowWorldResolveModalAsync: enter");
        try
        {
            var vm = viewModel.CreateWorldResolveViewModel();
            var page = new WorldResolvePage(vm);
            page.OnClose = CloseModal;
            page.OnApplied = async () =>
            {
                await viewModel.galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);
                viewModel.toastService.addToast("ワールド情報を適用しました");
            };
            Stage.ModalContent = page;
            Stage.ModalVisibility = Visibility.Visible;
            isModalOpen = true;
            HeaderBar.Opacity = 0.4;
            LeftRail.Opacity = 0.4;
            await vm.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowWorldResolveModalAsync: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowWorldResolveModalAsync: exit");
    }

    public void CloseModal()
    {
        AppLogger.Trace("ShellPage.CloseModal: enter");
        try
        {
            isModalOpen = false;
            cachedModalPage?.ReleaseImage();
            Stage.ModalVisibility = Visibility.Collapsed;
            HeaderBar.Opacity = 1.0;
            LeftRail.Opacity = 1.0;
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.CloseModal: threw: {ex}"); }
        AppLogger.Trace("ShellPage.CloseModal: exit");
    }

    private void ToggleFilter()
    {
        AppLogger.Trace($"ShellPage.ToggleFilter: enter isFilterOpen={isFilterOpen}");
        try { isFilterOpen = !isFilterOpen; SetFilterOverlayOpen(isFilterOpen); }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ToggleFilter: threw: {ex}"); }
        AppLogger.Trace($"ShellPage.ToggleFilter: exit isFilterOpen={isFilterOpen}");
    }

    private void SetFilterOverlayOpen(bool isOpen)
    {
        if (isOpen)
        {
            if (FilterPanelHost.Content is null && galleryPage is not null)
                FilterPanelHost.Content = galleryPage.GetFilterPanel();
            FilterOverlay.Visibility = Visibility.Visible;
            AnimationHelper.FadeIn(FilterBackdrop, 250);
            AnimationHelper.SlideIn(FilterPanelContainer, fromX: -16f, durationMs: 180);
        }
        else
        {
            AnimationHelper.FadeOut(FilterBackdrop, 180);
            AnimationHelper.SlideOut(FilterPanelContainer, toX: -16f, durationMs: 180, onCompleted: () =>
            {
                DispatcherQueue?.TryEnqueue(() => FilterOverlay.Visibility = Visibility.Collapsed);
            });
        }
    }

    private void ShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        // ctor で全結線済み。Loaded は冪等な再ハイドレートのみ。
    }

    /// <summary>
    /// アンマウント時に GalleryViewModel 側に残った drillDownPhotosProvider などの
    /// 参照を切る。Page を捨てた後も VM がコールバックを保持していると、
    /// ガベージコレクトされず古い ShellPage インスタンスがリークする。
    /// </summary>
    private void ShellPage_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            viewModel.galleryViewModel.drillDownPhotosProvider = null;
            viewModel.galleryViewModel.selectionState.PropertyChanged -= OnSelectionStateChanged;
            viewModel.PropertyChanged -= OnShellViewModelChanged;
            drillDownPhotos = null;
            if (drillDownPage is not null)
            {
                drillDownPage.OnBack = null;
                drillDownPage.OnPhotoActivated = null;
                drillDownPage.OnFavoriteClicked = null;
                drillDownPage.OnThumbnailsNeeded = null;
                drillDownPage = null;
            }
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShellPage_Unloaded: threw: {ex}"); }
    }

    private void ShellPage_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (isFilterOpen) { ToggleFilter(); e.Handled = true; }
        }
    }

    private void FilterBackdrop_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (isFilterOpen) ToggleFilter();
    }

    private void ModalDismissArea_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (isModalOpen)
        {
            e.Handled = true;
            cachedModalPage?.OnClose?.Invoke();
        }
    }
}