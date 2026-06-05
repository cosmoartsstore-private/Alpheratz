using System;
using System.Diagnostics.CodeAnalysis;
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

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class ShellPage : Page
{
    private readonly ShellViewModel viewModel;
    private GalleryPage? galleryPage;
    private SettingsPage? settingsPage;
    // 現在開いている写真モーダル Page。tunneling PreviewKeyDown (ShellPage_PreviewKeyDown) から
    // OnGoPrev/OnGoNext/OnClose を ShowPhotoModal で結線したのと同じ経路で呼ぶために保持する。
    // CloseModal で必ず null クリアする (モーダルが閉じているのにキーで誤動作しないように)。
    private PhotoModalPage? activePhotoModalPage;
    private bool isModalOpen;
    private long lastModalOpenTick;
    private bool isFilterOpen;

    // Shell 全体の ViewModel と主要コントロールを接続し、初期画面をギャラリーにする。
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
            // ヘッダーバーのコールバック (検索条件 / 設定 / 3 つの切替ボタン)。
            // 「検索条件」ボタンは FilterOverlay を表示/非表示で切り替える。
            // FilterPanel 自体はオーバーレイ内の FilterPanelContainer に固定配置されており
            // 親が変わらないので、テーマ切替が ActualTheme 経由で正しく伝播する。
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
            // 検索ボックスで Enter が押されたときの即時検索。SearchQuery は HeaderBar 側で
            // UpdateSource 済みなので、ここでは applySearchNow() を呼ぶだけ (リアルタイム検索は廃止)。
            HeaderBar.OnSearchSubmit = () => viewModel.galleryViewModel.applySearchNow();

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

    // 複数選択モードの変更をヘッダーのトグル状態へ反映する。
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

    // ShellViewModel の主要な状態変化を画面表示と確認モーダルへ反映する。
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

    private TaskCompletionSource<bool?>? confirmTcs;

    /// <summary>
    /// アプリ独自デザインの確認モーダルを表示し、ユーザの選択を待つ。
    /// WinUI 標準 ContentDialog はデフォルト Fluent のままアプリと馴染まないため、
    /// ASurface カード + AOverlay 背景の自前オーバーレイで統一する。
    /// 戻り値: true=はい / false=いいえ / null=キャンセル（背景タップ・ESC）。
    /// </summary>
    private Task<bool?> ShowConfirmDialogAsync(string title, string message, string yesText, string noText, string? cancelText = null)
    {
        try
        {
            ConfirmTitle.Text = title;
            ConfirmMessage.Text = message;
            ConfirmYesButton.Content = yesText;
            ConfirmNoButton.Content = noText;
            // 前回が残っていればキャンセル扱いで解決してから新規に差し替える。
            confirmTcs?.TrySetResult(null);
            confirmTcs = new TaskCompletionSource<bool?>();
            ConfirmOverlay.Visibility = Visibility.Visible;
            return confirmTcs.Task;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ShellPage.ShowConfirmDialogAsync: threw: {ex}");
            return Task.FromResult<bool?>(null);
        }
    }

    // 確認モーダルを閉じ、待機中の呼び出しへユーザー選択を返す。
    private void CloseConfirmDialog(bool? result)
    {
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        var tcs = confirmTcs;
        confirmTcs = null;
        tcs?.TrySetResult(result);
    }

    // 確認モーダルの肯定ボタンを true として解決する。
    private void ConfirmYes_Click(object sender, RoutedEventArgs e) => CloseConfirmDialog(true);
    // 確認モーダルの否定ボタンを false として解決する。
    private void ConfirmNo_Click(object sender, RoutedEventArgs e) => CloseConfirmDialog(false);
    // 背景タップはキャンセル扱いで確認モーダルを閉じる。
    private void ConfirmBackdrop_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => CloseConfirmDialog(null);
    // 本文タップを背景タップへ伝播させない。
    private void ConfirmContent_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => e.Handled = true;

    // フォルダ変更前にキャッシュリセットの確認を取り、承認時だけ変更を適用する。
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

    // 写真フォルダ設定のリセット前に確認を取り、承認時だけ実行する。
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

    // メイン領域へギャラリー画面を表示し、初回だけ GalleryPage を生成する。
    public void ShowGallery()
    {
        AppLogger.Trace("ShellPage.ShowGallery: enter");
        try
        {
            if (galleryPage is null)
            {
                // FilterPanel は ShellPage の XAML (FilterRailHost 内) で生成済みのインスタンスを
                // 引き渡す。所有を ShellPage 側に置くことで、UserControl の再 parent に伴う
                // ContentControl 例外を回避している。
                galleryPage = new GalleryPage(viewModel.galleryViewModel, FilterPanel)
                {
                    OnResetFilters = viewModel.galleryViewModel.resetFilters,
                    OnDatePresetSelect = preset => viewModel.galleryViewModel.filtersState.handleDatePresetSelect(preset),
                    OnSelectPhoto = photo => { if (viewModel.createPhotoModalViewModel(photo) is { } vm) ShowPhotoModal(vm); },
                    OnDrillIntoGroup = item => _ = ShowGroupDrillDown(item),
                    OnChooseFolder = () => viewModel.settingsViewModel.handleChooseFolderPathOnly(),
                    // 空状態 (ライブラリ未設定) の CTA から設定モーダルを開く。
                    OnOpenSettings = ShowSettings,
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
    /// 各オーバーレイ (最上位モーダル / 中位モーダル / 検索条件パネル) の開閉状態に合わせて、
    /// ヘッダーの操作可否 (SetControlsInteractive) と dim (Opacity) を一元的に同期する。
    /// 何らかのオーバーレイ表示中はヘッダーの操作ボタン類を不活性にし、
    /// 裏で別オーバーレイが開かないようにする (1 の不活性化と 2 のガードで二重の安全策)。
    /// 注意: ヘッダーの操作要素 (ContentRoot) だけを不活性にし、UserControl 自身は hit-test 可能なまま
    /// 残すので、ShellPage.xaml で HeaderBar に付けた Tapped=ModalDismissArea_Tapped は発火し続ける
    /// (= モーダル中もヘッダー余白タップで閉じられる)。
    /// dim の濃さ:
    ///   - モーダル (最上位 / 中位) 表示中: 0.4 (従来どおりはっきり inactive)
    ///   - 検索条件のみ表示中: 0.6 (補助パネルなので軽め)
    ///   - 全クローズ: IsHitTestVisible=true + Opacity=1.0
    /// 既存の HeaderBar.Opacity 直接代入はこのヘルパ経由に集約する。
    /// </summary>
    private void syncHeaderInteractivity()
    {
        try
        {
            var state = ShellPageInteractionLogic.ComputeHeaderInteractivity(isModalOpen, isMiddleModalOpen, isFilterOpen);
            HeaderBar.SetControlsInteractive(state.ControlsInteractive);
            HeaderBar.Opacity = state.Opacity;
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.syncHeaderInteractivity: threw: {ex}"); }
    }

    /// <summary>
    /// グループカードをクリックしたときの遷移。
    /// 毎回新しい GroupDrillDownPage と PhotoThumbnailItem リストを確保し、
    /// グループカードに保持された写真一覧をメインと同じ PhotoGrid で描画する。
    /// GroupPhotos が保持されていないカードだけ DB から取り直す。
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
            var photos = groupItem.GroupPhotos is { Count: > 0 } grouped
                ? grouped
                : await viewModel.galleryViewModel.getGroupPhotosAsync(groupKey).ConfigureAwait(true);

            // 毎回 Page を作り直し、前回表示のスクロール位置、サムネイル購読、
            // GridView のリサイクル状態が次の表示へ残らないようにする。
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

            // groupKey は内部用の合成キーなので、タイトルには代表写真の WorldName を使う。
            // 表示できるワールド名が無い場合は「ワールド不明」を表示する。
            var displayName = string.IsNullOrWhiteSpace(groupItem.Photo?.WorldName)
                ? "ワールド不明"
                : groupItem.Photo!.WorldName!;
            drillDownPage.SetGroupInfo(displayName, photos);
            Stage.ModalContent = drillDownPage;
            Stage.ModalVisibility = Visibility.Visible;
            isMiddleModalOpen = true;
            lastModalOpenTick = Environment.TickCount64;
            syncHeaderInteractivity();
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
            // ヘッダーの dim / 操作可否は残りのオーバーレイ状態に合わせて再同期する
            // (最上位 PhotoModal や検索条件が残っていれば不活性のまま維持)。
            syncHeaderInteractivity();
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
        // モーダル多重化防止: 何らかのオーバーレイ (最上位/中位モーダル/検索条件) が開いている間は
        // ヘッダー起動の設定オープンを no-op にする (1 の不活性化と二重の安全策)。
        if (isModalOpen || isMiddleModalOpen || isFilterOpen) return;
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
                    OnStartupPreferenceChanged = enabled => enabled == viewModel.StartupEnabled
                        ? Task.CompletedTask
                        : viewModel.handleStartupPreference(enabled),
                    OnThemeChanged = isDark => viewModel.handleThemeChange(isDark ? Alpheratz.Shared.Models.ThemeMode.dark : Alpheratz.Shared.Models.ThemeMode.light),
                    // ワールド解析モーダルは Settings モーダルと同じ ModalContent スロットを使う。
                    // CloseModal を経由するとフェードアウト中に新コンテンツを差し込むため、
                    // ShowWorldResolveModalAsync 側で ModalContent を直接差し替える。
                    OnStartWorldAnalysis = ShowWorldResolveModalAsync,

                    // ===== タグマスタ =====
                    OnCreateTag = viewModel.tagMasterViewModel.createTag,
                    OnDeleteTag = viewModel.deleteTagAndRefreshGallery,

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
            lastModalOpenTick = Environment.TickCount64;
            syncHeaderInteractivity();
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShowSettings: threw: {ex}"); }
        AppLogger.Trace("ShellPage.ShowSettings: exit");
    }

    /// <summary>
    /// 写真詳細モーダルを表示する。最上位レイヤ (Stage.TopModalContent) に出すことで、
    /// 中位モーダル (GroupDrillDown / Settings 等) の上にさらに重ねられる構造になる。
    /// 毎回新しく生成する。キャッシュした Page に ViewModel だけ差し替える方式だと、visual tree から
    /// detach されている間に親の RequestedTheme が変わってもページが追従せず、
    /// 再オープン時に古いテーマの {ThemeResource} が残ってしまう問題があったため。
    /// </summary>
    public void ShowPhotoModal(PhotoModalViewModel modalViewModel)
    {
        AppLogger.Trace("ShellPage.ShowPhotoModal: enter");
        try
        {
            var page = new PhotoModalPage(modalViewModel);
            // WinUI 3 では Page が ContentControl にホストされたとき RequestedTheme が
            // 親から自動継承されないことがある。明示的に ShellPage 側のテーマを伝える。
            page.RequestedTheme = RequestedTheme;
            page.OnAddTag = (photoPath, tag) => viewModel.galleryViewModel.addTag(photoPath, tag);
            page.OnRemoveTag = (photoPath, tag) => viewModel.galleryViewModel.removeTag(photoPath, tag);
            page.OnClose = () => { modalViewModel.closePhotoModal(); CloseModal(); };
            page.OnOpenWorld = modalViewModel.handleOpenWorld;
            page.OnOpenExplorer = modalViewModel.handleOpenExplorer;
            // タグマスタは Settings モーダル内のセクションとして扱う。
            // PhotoModal を閉じてから Settings モーダルを開くことで、モーダル階層を 1 つに保つ。
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
                    await viewModel.galleryViewModel.toggleFavorite(selectedPhoto.PhotoPath, selectedPhoto.IsFavorite).ConfigureAwait(false);
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
            // tunneling キー遷移 (ShellPage_PreviewKeyDown) から同じ結線を呼べるよう参照を保持する。
            activePhotoModalPage = page;
            isModalOpen = true;
            lastModalOpenTick = Environment.TickCount64;
            syncHeaderInteractivity();
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
                await Task.WhenAll(
                    viewModel.galleryViewModel.photosState.loadPhotos(),
                    viewModel.galleryViewModel.loadWorldFilterOptions()).ConfigureAwait(false);
                viewModel.toastService.addToast("ワールド情報を適用しました");
            };
            Stage.ModalContent = page;
            Stage.ModalVisibility = Visibility.Visible;
            isMiddleModalOpen = true;
            lastModalOpenTick = Environment.TickCount64;
            syncHeaderInteractivity();
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
            // tunneling キー遷移用の参照を必ず外す (閉じた後にキーで誤動作しないように)。
            activePhotoModalPage = null;
            Stage.TopModalVisibility = Visibility.Collapsed;
            // ヘッダーの dim / 操作可否は残りのオーバーレイ状態に合わせて再同期する
            // (中位モーダルや検索条件が残っていれば不活性のまま維持)。
            syncHeaderInteractivity();
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.CloseModal: threw: {ex}"); }
        AppLogger.Trace("ShellPage.CloseModal: exit");
    }

    /// <summary>
    /// 検索条件オーバーレイの表示/非表示を切り替える。
    /// FilterPanel はオーバーレイ内に固定配置されており、ここでは FilterOverlay.Visibility と
    /// アニメーションだけ操作する。FilterPanel の親は変更しないので ActualTheme は
    /// 切替時も継承され、code-behind の ActualThemeChanged で再着色が走る。
    /// </summary>
    private void ToggleFilter()
    {
        AppLogger.Trace($"ShellPage.ToggleFilter: enter isFilterOpen={isFilterOpen}");
        try
        {
            // 開く側のみガード: モーダル (最上位/中位) 表示中は検索条件パネルを開かない
            // (モーダルの上に検索条件を重ねない)。閉じる側は常に許可する。
            if (!ShellPageInteractionLogic.CanToggleFilter(isFilterOpen, isModalOpen, isMiddleModalOpen)) return;
            isFilterOpen = !isFilterOpen;
            SetFilterOverlayOpen(isFilterOpen);
            // 検索条件の開閉に合わせてヘッダーの不活性化 / dim を同期する。
            syncHeaderInteractivity();
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ToggleFilter: threw: {ex}"); }
        AppLogger.Trace($"ShellPage.ToggleFilter: exit isFilterOpen={isFilterOpen}");
    }

    // 検索条件オーバーレイの表示状態とスライドアニメーションを切り替える。
    private void SetFilterOverlayOpen(bool isOpen)
    {
        if (isOpen)
        {
            // 暗幕を撤去したので背景 (FilterBackdrop) の FadeIn は不要。
            // パネルの SlideIn だけ残す。
            FilterOverlay.Visibility = Visibility.Visible;
            AnimationHelper.SlideIn(FilterPanelContainer, fromX: -16f, durationMs: 180);
        }
        else
        {
            AnimationHelper.SlideOut(FilterPanelContainer, toX: -16f, durationMs: 180, onCompleted: () =>
            {
                DispatcherQueue?.TryEnqueue(() => FilterOverlay.Visibility = Visibility.Collapsed);
            });
        }
    }

    // 検索条件パネル外の背景タップでオーバーレイを閉じる。
    private void FilterBackdrop_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (isFilterOpen) ToggleFilter();
    }

    // パネル本体上のタップは吸収し、背景タップによる閉じる動作 (FilterBackdrop_Tapped) へ
    // 伝播させない。これでパネル内を操作しても overlay が閉じない。
    private void FilterPanelContainer_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => e.Handled = true;

    /// <summary>
    /// 写真モーダルの ←/→/Esc をフォーカス位置に依存せず確実に効かせるための tunneling ハンドラ。
    /// PreviewKeyDown はルート (ShellPage) から子へ向かって先に発火するので、背後のギャラリー
    /// GridView が矢印キーを消費する前にここで捕捉できる。写真モーダルが開いている時だけ
    /// (isModalOpen==true) 次を処理して e.Handled=true にする:
    ///   - Left  → 前の写真へ (ShowPhotoModal で結線したのと同じ OnGoPrev = goPrevPhoto())
    ///   - Right → 次の写真へ (同 OnGoNext = goNextPhoto())
    ///   - Esc   → 写真モーダルを閉じる (同 OnClose = closePhotoModal()+CloseModal())
    /// TextBox にフォーカスがあるときはタグ入力等の誤爆を避けてスキップする。
    /// ここで Handled 済みにするので、PhotoModalPage.Page_PreviewKeyDown・ShellPage_KeyDown
    /// (bubbling)・背後 GridView のいずれにも届かず二重発火しない。Enter (タグ一括追加) や
    /// Backspace (戻る) はここでは扱わず、従来通り PhotoModalPage 側 (モーダル内フォーカス時) が処理する。
    /// </summary>
    private void ShellPage_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        try
        {
            var action = ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(
                isModalOpen,
                activePhotoModalPage is not null,
                Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is TextBox,
                e.Key);
            switch (action)
            {
                case PhotoModalKeyAction.GoPrevious:
                    e.Handled = true;
                    activePhotoModalPage?.OnGoPrev?.Invoke();
                    break;
                case PhotoModalKeyAction.GoNext:
                    e.Handled = true;
                    activePhotoModalPage?.OnGoNext?.Invoke();
                    break;
                case PhotoModalKeyAction.Close:
                    e.Handled = true;
                    activePhotoModalPage?.OnClose?.Invoke();
                    break;
            }
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShellPage_PreviewKeyDown: threw: {ex}"); }
    }

    /// <summary>
    /// シェルレベルのキーボードショートカット。PhotoModal が開いている時の ←/→/Esc は
    /// ShellPage_PreviewKeyDown (tunneling) が先に処理して e.Handled=true にするので、
    /// ここ (bubbling KeyDown) まで来るのはギャラリー / 設定 / タグマスタ画面のいずれか。
    ///   - Esc       → 検索条件 overlay を閉じる / マルチセレクトを解除
    ///   - Ctrl+F    → 検索条件 overlay を開く
    ///   - Ctrl+,    → 設定画面を開く（一般的な「設定」ショートカット）
    /// </summary>
    private void ShellPage_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        try
        {
            var ctrlDown = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

            var action = ShellPageInteractionLogic.ResolveShellKey(
                ConfirmOverlay.Visibility == Visibility.Visible,
                isFilterOpen,
                viewModel.galleryViewModel.selectionState.IsMultiSelectMode,
                isModalOpen,
                isMiddleModalOpen,
                ctrlDown,
                e.Key);

            switch (action)
            {
                case ShellKeyAction.CloseConfirm:
                    CloseConfirmDialog(null);
                    e.Handled = true;
                    return;
                case ShellKeyAction.CloseFilter:
                    ToggleFilter();
                    e.Handled = true;
                    return;
                case ShellKeyAction.ExitMultiSelect:
                    viewModel.galleryViewModel.selectionState.handleToggleMultiSelectMode();
                    e.Handled = true;
                    return;
                case ShellKeyAction.OpenFilter:
                    ToggleFilter();
                    e.Handled = true;
                    return;
                case ShellKeyAction.OpenSettings:
                    ShowSettings();
                    e.Handled = true;
                    return;
            }
        }
        catch (Exception ex) { AppLogger.Error($"ShellPage.ShellPage_KeyDown: threw: {ex}"); }
    }

    /// <summary>
    /// HeaderBar のタップで現在開いているモーダルを閉じる。
    /// 2 段スタックでは「上から順に」閉じるのが直感的なので、最上位 (PhotoModal) が
    /// 開いていれば PhotoModal を先に閉じる。残った中位 (Settings / GroupDrillDown /
    /// WorldResolve) があれば次のタップで中位が閉じる、という挙動。
    /// </summary>
    private void ModalDismissArea_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        switch (ShellPageInteractionLogic.ResolveModalDismiss(Environment.TickCount64, lastModalOpenTick, isModalOpen, isMiddleModalOpen))
        {
            case ModalDismissAction.ClosePhotoModal:
                // 最上位 PhotoModal を閉じる (closePhotoModal + CloseModal を内包)
                e.Handled = true;
                (Stage.TopModalContent as PhotoModalPage)?.OnClose?.Invoke();
                break;
            case ModalDismissAction.CloseMiddleModal:
                // 中位モーダル (Settings / GroupDrillDown / WorldResolve) を閉じる
                e.Handled = true;
                CloseMiddleModal();
                break;
        }
    }
}
