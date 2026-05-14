using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Features.Settings;
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
            // ヘッダーバーのコールバック (検索条件 / 設定 / 3 つのトグル)。
            // LeftRail から移植したトグル系も HeaderBar 上で発火するようにする。
            HeaderBar.OnToggleFilter = ToggleFilter;
            HeaderBar.OnShowSettings = ShowSettings;
            HeaderBar.OnToggleMultiSelect = () =>
            {
                viewModel.galleryViewModel.selectionState.handleToggleMultiSelectMode();
                HeaderBar.SetMultiSelectActive(viewModel.galleryViewModel.selectionState.IsMultiSelectMode);
            };
            HeaderBar.OnGroupingChange = async mode =>
            {
                // masonry (gallery) 表示中にグループ化 (world) を要求された場合は、先に
                // 標準グリッドへ戻してからグループ化を適用する。masonry はグループ化非対応。
                if (mode != GroupingMode.none && viewModel.ViewMode == ViewMode.gallery)
                    await viewModel.handleSetViewMode(ViewMode.standard).ConfigureAwait(true);
                viewModel.galleryViewModel.displayState.prepareGroupingModeChange(
                    viewModel.galleryViewModel.filtersState.GroupingMode, mode,
                    m => viewModel.galleryViewModel.filtersState.GroupingMode = m);
                HeaderBar.SetGroupingMode(mode);
            };
            HeaderBar.OnViewModeChange = async modeStr =>
            {
                var mode = modeStr == "gallery" ? ViewMode.gallery : ViewMode.standard;
                await viewModel.handleSetViewMode(mode).ConfigureAwait(false);
            };

            viewModel.galleryViewModel.selectionState.PropertyChanged += OnSelectionStateChanged;
            viewModel.PropertyChanged += OnShellViewModelChanged;

            // drill-down 中の写真コレクションを GalleryViewModel.updatePhoto に
            // Func で参照渡しする。これにより：
            //   - PhotoModal でタグ/お気に入りを変えると、GalleryViewModel.updatePhoto が
            //     photosState.photos と displayItems に加えて drillDownPhotos も走査して
            //     同じ photo_path の項目を見つけて更新する。
            //   - drill-down を抜けたら drillDownPhotos = null になるので参照ループは切れる。
            // delegate 経由にしているのは、ViewModel が UI レイヤ (drillDownPhotos の存在自体) を
            // 知らずに済むようにするため。
            viewModel.galleryViewModel.drillDownPhotosProvider = () => drillDownPhotos;

            Stage.ScanningOverlayControlRef.OnCancelScan = viewModel.cancelScan;
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ctor: wiring failed: {ex}"); throw; }

        AppLogger.Trace("ShellPage.ctor: wiring done, calling ShowGallery");
        ShowGallery();
        HeaderBar.SetViewMode(viewModel.ViewMode);
        HeaderBar.SetGroupingMode(viewModel.galleryViewModel.filtersState.GroupingMode);
        HeaderBar.SetPdqProgress(viewModel.IsPdqRunning, viewModel.PdqProgress?.done ?? 0, viewModel.PdqProgress?.total ?? 0);
        AppLogger.Trace("ShellPage.ctor: exit");
    }

    private void OnSelectionStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        AppLogger.Trace($"ShellPage.OnSelectionStateChanged: enter property={e.PropertyName}");
        try
        {
            if (e.PropertyName == nameof(viewModel.galleryViewModel.selectionState.IsMultiSelectMode))
                HeaderBar.SetMultiSelectActive(viewModel.galleryViewModel.selectionState.IsMultiSelectMode);
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
                HeaderBar.SetViewMode(viewModel.ViewMode);
                HeaderBar.SetGroupingMode(viewModel.galleryViewModel.filtersState.GroupingMode);
            }
            else if (e.PropertyName == nameof(viewModel.ScanStatus)) UpdateScanningOverlayVisibility();
            else if (e.PropertyName == nameof(viewModel.PendingFolderPath) && viewModel.PendingFolderPath is not null) _ = ShowFolderChangeConfirmAsync();
            else if (e.PropertyName == nameof(viewModel.PendingResetRequest) && viewModel.PendingResetRequest is not null) _ = ShowResetConfirmAsync();
            else if (e.PropertyName == nameof(viewModel.ThemeMode)) ApplyTheme(viewModel.ThemeMode);
            else if (e.PropertyName == nameof(viewModel.IsPdqRunning) || e.PropertyName == nameof(viewModel.PdqProgress))
                HeaderBar.SetPdqProgress(viewModel.IsPdqRunning, viewModel.PdqProgress?.done ?? 0, viewModel.PdqProgress?.total ?? 0);
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
            HeaderBar.SetViewMode(viewModel.ViewMode);
            HeaderBar.SetGroupingMode(viewModel.galleryViewModel.filtersState.GroupingMode);
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowGallery: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowGallery: exit");
    }

    private GroupDrillDownPage? drillDownPage;
    private IReadOnlyList<PhotoThumbnailItem>? drillDownPhotos;
    /// <summary>
    /// 中位モーダル (Stage.ModalContent) が現在開いているかのフラグ。
    /// PhotoModal は最上位レイヤを使うため別管理 (isModalOpen)。
    /// </summary>
    private bool isMiddleModalOpen;

    /// <summary>
    /// グループカードをクリックしたときの遷移。
    /// 毎回新しい GroupDrillDownPage と PhotoThumbnailItem リストを確保し、
    /// SQL を発行して取得したデータをメインと同じ PhotoGrid で描画する。
    /// 中位モーダル (Stage.ModalContent) として表示し、内部の写真クリックで
    /// PhotoModal が最上位レイヤに重なる 2 段スタック構造になる。
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
                OnBack = CloseMiddleModal,
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
            Stage.ModalContent = drillDownPage;
            Stage.ModalVisibility = Visibility.Visible;
            isMiddleModalOpen = true;
            HeaderBar.Opacity = 0.4;
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowGroupDrillDown: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowGroupDrillDown: exit");
    }

    /// <summary>
    /// 中位モーダル (Settings / GroupDrillDown / WorldResolve) を閉じる。
    /// 最上位の PhotoModal が開いていれば、PhotoModal の方を先に閉じる。
    /// </summary>
    private void CloseMiddleModal()
    {
        AppLogger.Trace("ShellPage.CloseMiddleModal: enter");
        try
        {
            isMiddleModalOpen = false;
            drillDownPhotos = null;
            Stage.ModalVisibility = Visibility.Collapsed;
            // 最上位 (PhotoModal) も開いていなければ HeaderBar の dim を解除
            if (!isModalOpen)
            {
                HeaderBar.Opacity = 1.0;
            }
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.CloseMiddleModal: threw: {ex}"); }
        AppLogger.Trace("ShellPage.CloseMiddleModal: exit");
    }

    /// <summary>
    /// 設定モーダルを開く。3 セクション (全般 / タグマスタ / テンプレート) を統合した
    /// SettingsPage を Stage.ModalContent 経由で重ねる。Gallery は背景に残ったまま。
    /// インスタンスは初回生成時にキャッシュし、以降の表示は再ハイドレートで使い回す。
    /// </summary>
    public void ShowSettings()
    {
        AppLogger.Trace("ShellPage.ShowSettings: enter");
        try
        {
            if (settingsPage is null)
            {
                var compositeVm = new SettingsCompositeViewModel(
                    viewModel.settingsViewModel,
                    viewModel.tagMasterViewModel,
                    viewModel.templatePageViewModel);

                settingsPage = new SettingsPage(compositeVm)
                {
                    // ===== 共通 =====
                    // Settings は中位モーダルなので CloseMiddleModal を呼ぶ。
                    OnClose = CloseMiddleModal,

                    // ===== 全般 =====
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
                    // ワールド解析モーダルは Settings モーダルと同じ ModalContent スロットを使う。
                    // CloseModal を経由するとフェードアウトアニメ中に新コンテンツを差し込むことになり
                    // race するので、ShowWorldResolveModalAsync 側で ModalContent を直接差し替える。
                    OnStartWorldAnalysis = ShowWorldResolveModalAsync,

                    // ===== タグマスタ =====
                    OnCreateTag = viewModel.tagMasterViewModel.createTag,
                    OnDeleteTag = viewModel.tagMasterViewModel.deleteTag,

                    // ===== 投稿テンプレート =====
                    OnCancelEdit = viewModel.templatePageViewModel.cancelEdit,
                    OnStartEdit = viewModel.templatePageViewModel.startEdit,
                    OnDeleteTemplate = template => viewModel.templatePageViewModel.deleteTemplate(template, viewModel.buildSettingPayload()),
                    OnSaveTemplate = () => viewModel.templatePageViewModel.saveTemplate(viewModel.buildSettingPayload()),
                    OnSelectTemplate = async template =>
                    {
                        viewModel.templatePageViewModel.ActiveTweetTemplate = template;
                        await viewModel.templatePageViewModel.saveTemplates(viewModel.buildSettingPayload()).ConfigureAwait(false);
                    },
                };
            }
            Stage.ModalContent = settingsPage;
            Stage.ModalVisibility = Visibility.Visible;
            isMiddleModalOpen = true;
            HeaderBar.Opacity = 0.4;
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowSettings: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowSettings: exit");
    }

    /// <summary>
    /// 写真詳細モーダルを表示する。最上位レイヤ (Stage.TopModalContent) に出すことで、
    /// 中位モーダル (GroupDrillDown / Settings 等) の上にさらに重ねられる構造になる。
    /// cachedModalPage を再利用するのは、初回生成コスト (XAML パース + Border 階層構築)
    /// が非自明に重く、写真切り替えごとに作り直すと体感の遅れに直結するため。
    /// 既存インスタンスがあれば UpdateViewModel(...) で内部 binding を差し替えるだけにする。
    /// OnAddTag/OnRemoveTag は VM 横断のロジックなので初回に固定で結線し、その他の
    /// コールバックは modalViewModel に依存するので毎回上書きする。
    /// </summary>
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
            // PhotoModal から「タグマスタを編集」する導線は、Step 1 でタグマスタが
            // Settings モーダル内のセクションに統合されたので Settings を開く動作に変更。
            // PhotoModal を閉じてから Settings モーダルを開く。
            page.OnOpenTagMaster = () =>
            {
                modalViewModel.closePhotoModal();
                CloseModal();
                ShowSettings();
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
            Stage.TopModalContent = page;
            Stage.TopModalVisibility = Visibility.Visible;
            isModalOpen = true;
            HeaderBar.Opacity = 0.4;
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowPhotoModal: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowPhotoModal: exit");
    }

    /// <summary>
    /// ワールド解析モーダルを開く。中位モーダル (Stage.ModalContent) に表示する。
    /// 設定モーダルから呼ばれる場合 (OnStartWorldAnalysis) は Settings の中身を
    /// ワールド解析に差し替える形になる。
    /// </summary>
    private async Task ShowWorldResolveModalAsync()
    {
        AppLogger.Trace("ShellPage.ShowWorldResolveModalAsync: enter");
        try
        {
            var vm = viewModel.CreateWorldResolveViewModel();
            var page = new WorldResolvePage(vm);
            page.OnClose = CloseMiddleModal;
            page.OnApplied = async () =>
            {
                await viewModel.galleryViewModel.photosState.loadPhotos().ConfigureAwait(false);
                viewModel.toastService.addToast("ワールド情報を適用しました");
            };
            Stage.ModalContent = page;
            Stage.ModalVisibility = Visibility.Visible;
            isMiddleModalOpen = true;
            HeaderBar.Opacity = 0.4;
            await vm.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowWorldResolveModalAsync: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowWorldResolveModalAsync: exit");
    }

    /// <summary>
    /// 最上位モーダル (PhotoModal) を閉じる。中位モーダル (Settings / GroupDrillDown /
    /// WorldResolve) はそのままで、PhotoModal だけ消えて中位モーダルに戻る。
    /// 中位も無ければ HeaderBar の dim を解除する。
    /// </summary>
    public void CloseModal()
    {
        AppLogger.Trace("ShellPage.CloseModal: enter");
        try
        {
            isModalOpen = false;
            Stage.TopModalVisibility = Visibility.Collapsed;
            // 中位モーダル (Settings / GroupDrillDown / WorldResolve) も開いていなければ
            // HeaderBar の dim を解除する。中位が残っていれば dim 維持。
            if (!isMiddleModalOpen)
            {
                HeaderBar.Opacity = 1.0;
            }
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

    /// <summary>
    /// シェルレベルのキーボードショートカット。PhotoModal が開いている時のキー操作は
    /// PhotoModalPage 側が先に処理して e.Handled=true にするので、ここまで来るのは
    /// ギャラリー / 設定 / タグマスタ画面のいずれか。
    ///   - Esc       → フィルタオーバーレイを閉じる / マルチセレクトを解除
    ///   - Ctrl+F    → フィルタオーバーレイを開く
    ///   - Ctrl+,    → 設定画面を開く（一般的な「設定」ショートカット）
    /// </summary>
    private void ShellPage_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        try
        {
            var ctrlDown = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                if (isFilterOpen) { ToggleFilter(); e.Handled = true; return; }
                if (viewModel.galleryViewModel.selectionState.IsMultiSelectMode)
                {
                    viewModel.galleryViewModel.selectionState.handleToggleMultiSelectMode();
                    e.Handled = true;
                    return;
                }
            }

            // Ctrl 系ショートカットは modal が一切開いていない時のみ発火する
            // (modal の上に modal を重ねるショートカットは UX 上混乱を招くため抑制)。
            if (!isModalOpen && !isMiddleModalOpen)
            {
                if (ctrlDown && e.Key == Windows.System.VirtualKey.F)
                {
                    if (!isFilterOpen) ToggleFilter();
                    e.Handled = true;
                    return;
                }

                if (ctrlDown && e.Key == (Windows.System.VirtualKey)188 /* OEM_COMMA */)
                {
                    ShowSettings();
                    e.Handled = true;
                    return;
                }
            }
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShellPage_KeyDown: threw: {ex}"); }
    }

    private void FilterBackdrop_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (isFilterOpen) ToggleFilter();
    }

    /// <summary>
    /// HeaderBar のタップで現在開いているモーダルを閉じる。
    /// 2 段スタックでは「上から順に」閉じるのが直感的なので、最上位 (PhotoModal) が
    /// 開いていれば PhotoModal を先に閉じる。残った中位 (Settings / GroupDrillDown /
    /// WorldResolve) があれば次のタップで中位が閉じる、という挙動。
    /// </summary>
    private void ModalDismissArea_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (isModalOpen)
        {
            // 最上位 PhotoModal を閉じる (closePhotoModal + CloseModal を内包)
            e.Handled = true;
            cachedModalPage?.OnClose?.Invoke();
            return;
        }
        if (isMiddleModalOpen)
        {
            // 中位モーダル (Settings / GroupDrillDown / WorldResolve) を閉じる
            e.Handled = true;
            CloseMiddleModal();
        }
    }
}