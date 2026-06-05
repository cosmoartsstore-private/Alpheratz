using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Scanner;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Features.Settings;
using Alpheratz.Features.TagMaster;
using Alpheratz.Features.Template;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;

namespace Alpheratz.Tests;

/// <summary>
/// UI を起動せずに検証できる ViewModel / State の振る舞いを固定するテスト。
///
/// WinUI の XAML コントロールそのものは Dispatcher や XAML ランタイムに強く依存するため、
/// ここでは画面部品ではなく、その背後でユーザー操作の結果を保持する純粋な状態計算を対象にする。
/// これにより、選択範囲・ナビゲーション・テンプレート展開のようなユーザー可視の仕様を
/// UI 自動化なしで継続的に検証できる。
/// </summary>
public sealed class StateViewModelBehaviorTests : IDisposable
{
    private readonly string tempDir;
    private readonly AlpheratzDb db;
    private readonly PhotoService photoService;
    private readonly ToastService toastService = new();

    /// <summary>
    /// State が非同期補助データを読み込めるように、一時DBと PhotoService を用意する。
    ///
    /// SelectionState と PhotoModalState は setter の内部でタグや選択参照を fire-and-forget で取得する。
    /// DB 未初期化のままテストすると、そのバックグラウンド処理の例外が検証対象外のノイズになるため、
    /// 各テストで独立したDBを初期化してから State を生成する。
    /// </summary>
    public StateViewModelBehaviorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.State.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        db = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
        db.Initialize();
        photoService = new PhotoService(db);
    }

    /// <summary>
    /// 一時DBとテスト用画像ヘッダを削除する。
    /// 削除失敗はテスト対象の仕様ではないため、例外は握りつぶす。
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Cleanup failure should not hide the assertion result.
        }
    }

    /// <summary>
    /// GalleryDisplayState の列幅・行高計算と変更コールバックの現在仕様を確認する。
    ///
    /// ギャラリーはウィンドウ幅に応じて列数とカード幅を再計算する。
    /// この計算がずれると、標準グリッドの仮想化領域や Masonry 切替時の準備表示が崩れるため、
    /// UI サイズ通知を模した直接呼び出しで、最小値・固定列数・準備トークンの挙動を固定する。
    /// </summary>
    [Fact]
    public void GalleryDisplayState_ComputesGridMetricsAndPreparationTokens()
    {
        var state = new GalleryDisplayState();
        var groupingMode = GroupingMode.none;
        var folderMode = DisplayFolderMode.all;

        state.rightPanelRef(1620);
        state.gridWrapperRef(720);
        var token = state.beginViewPreparation("表示を準備中");
        state.finishViewPreparation(token + 1);
        state.prepareGroupingModeChange(groupingMode, GroupingMode.world, value => groupingMode = value);
        state.prepareDisplayFolderModeChange(folderMode, DisplayFolderMode.secondary, value => folderMode = value);
        state.finishViewPreparation(token);

        Assert.Equal(6, state.measuredColumnCount);
        Assert.Equal(6, state.standardColumnCount);
        Assert.Equal(270, state.standardColumnWidth);
        Assert.Equal(180, state.standardRowHeight);
        Assert.Equal(GroupingMode.world, groupingMode);
        Assert.Equal(DisplayFolderMode.secondary, folderMode);
        Assert.Null(state.ViewPreparationLabel);
    }

    /// <summary>
    /// 日付プリセットがローカル日付を基準に想定フォーマットへ展開されることを確認する。
    ///
    /// getDateRangeFromPreset は現在日付に依存するが、全プリセットで yyyy-MM-dd 形式を返すことと、
    /// 「今日」「過去7日」「先月」の包含関係は環境が変わっても成立する。
    /// 検証は当日の固定値ではなく、実行時の DateTime.Today から期待値を構築して行う。
    /// </summary>
    [Fact]
    public void GalleryDisplayState_DatePresetsUseInclusiveLocalDateRanges()
    {
        var today = DateTime.Today;
        var todayText = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var last7From = today.AddDays(-6).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var lastMonthStart = new DateTime(today.Year, today.Month, 1).AddMonths(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var lastMonthEnd = new DateTime(today.Year, today.Month, 1).AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var todayRange = GalleryDisplayState.getDateRangeFromPreset(DatePreset.today);
        var last7Range = GalleryDisplayState.getDateRangeFromPreset(DatePreset.last7days);
        var lastMonthRange = GalleryDisplayState.getDateRangeFromPreset(DatePreset.lastMonth);
        var customRange = GalleryDisplayState.getDateRangeFromPreset(DatePreset.custom);

        Assert.Equal(todayText, todayRange.from);
        Assert.Equal(todayText, todayRange.to);
        Assert.Equal(last7From, last7Range.from);
        Assert.Equal(todayText, last7Range.to);
        Assert.Equal(lastMonthStart, lastMonthRange.from);
        Assert.Equal(lastMonthEnd, lastMonthRange.to);
        Assert.Equal("", customRange.from);
        Assert.Equal("", customRange.to);
    }

    /// <summary>
    /// GalleryScrollState が写真数からスクロール範囲を計算し、ホイール入力を範囲内にクランプすることを確認する。
    ///
    /// 標準グリッドは仮想化された高さを持つため、写真数・列数・行高から論理的な全高を計算する。
    /// ScrollTop が負値や maxScrollTop を超えた値を持つと、表示開始行の計算が壊れるため、
    /// 再計算、スクロール、ホイール、再アタッチ時のクランプをまとめて検証する。
    /// </summary>
    [Fact]
    public void GalleryScrollState_RecalculatesAndClampsScrollPosition()
    {
        var state = new GalleryScrollState();

        state.Recalculate(photosLength: 25, columnCount: 4, gridHeight: 300, ROW_HEIGHT: 100);
        state.handleGridScroll(450);
        state.handleGridWheel(500);
        state.onGridRef(currentScrollTop: 999);
        state.handleGridWheel(-1000);

        Assert.Equal(7, state.totalRows);
        Assert.Equal(700, state.totalHeight);
        Assert.Equal(400, state.maxScrollTop);
        Assert.Equal(0, state.ScrollTop);
    }

    /// <summary>
    /// disableProgrammaticBounds=true のとき、UI 側が持つスクロール値を強制補正しないことを確認する。
    ///
    /// 慣性スクロール中や ScrollViewer 再装着中は、UI コントロール自身が位置を管理している。
    /// このフラグでプログラム側のクランプを止める仕様なので、範囲外の currentScrollTop が
    /// そのまま ScrollTop に反映されることを検証する。
    /// </summary>
    [Fact]
    public void GalleryScrollState_DisableProgrammaticBoundsLeavesUiControlledPosition()
    {
        var state = new GalleryScrollState();

        state.Recalculate(photosLength: 3, columnCount: 3, gridHeight: 500, ROW_HEIGHT: 100, disableProgrammaticBounds: true);
        state.onGridRef(currentScrollTop: 250, disableProgrammaticBounds: true);
        state.handleGridWheel(300, disableProgrammaticBounds: true);

        Assert.Equal(1, state.totalRows);
        Assert.Equal(100, state.totalHeight);
        Assert.Equal(0, state.maxScrollTop);
        Assert.Equal(250, state.ScrollTop);
    }

    /// <summary>
    /// 複数選択で単体選択、範囲選択、解除が選択パス集合へ反映されることを確認する。
    ///
    /// 範囲選択は SelectionAnchorPhotoPath と現在表示中の PhotoGridItem の順序に依存する。
    /// そのため、アンカーを1枚目に作ってから Shift 選択で3枚目まで広げ、
    /// 最後に同じ項目を通常クリックして解除されることを検証する。
    /// </summary>
    [Fact]
    public void GallerySelectionState_TogglesSingleAndRangeSelection()
    {
        using var state = new GallerySelectionState(photoService, toastService);
        var items = new[]
        {
            GridItem("/photo/a.jpg"),
            GridItem("/photo/b.jpg"),
            GridItem("/photo/c.jpg"),
            GridItem("/photo/d.jpg"),
        };

        state.toggleSelectedPhoto(items[0], shiftKey: false, items);
        state.toggleSelectedPhoto(items[2], shiftKey: true, items);
        state.toggleSelectedPhoto(items[1], shiftKey: false, items);

        Assert.Equal("/photo/b.jpg", state.SelectionAnchorPhotoPath);
        Assert.Equal(["/photo/a.jpg", "/photo/c.jpg"], state.selectedPhotoPaths.ToArray());
    }

    /// <summary>
    /// マルチセレクトモードを閉じると選択状態とアンカーが消えることを確認する。
    ///
    /// 一括操作ツールバーは IsMultiSelectMode=false のとき選択が残っていない前提で描画される。
    /// モード終了時に selectedPhotoPaths が残ると、次回の一括操作が過去の選択に対して実行されるため、
    /// トグルによるクリア処理を独立して検証する。
    /// </summary>
    [Fact]
    public void GallerySelectionState_LeavingMultiSelectClearsSelection()
    {
        using var state = new GallerySelectionState(photoService, toastService);
        var item = GridItem("/photo/a.jpg");

        state.handleToggleMultiSelectMode();
        state.toggleSelectedPhoto(item, shiftKey: false, [item]);
        state.handleToggleMultiSelectMode();

        Assert.False(state.IsMultiSelectMode);
        Assert.Empty(state.selectedPhotoPaths);
        Assert.Null(state.SelectionAnchorPhotoPath);
    }

    /// <summary>
    /// 選択パスの変更後に DB から一括操作用の写真参照が読み込まれることを確認する。
    ///
    /// GallerySelectionState は selectedPhotoPaths の CollectionChanged を受けて fire-and-forget で
    /// selectedPhotoRefs を更新する。
    /// 一括お気に入り・一括タグ追加は selectedPhotoRefs を入力にするため、DB に存在する写真だけが
    /// (photo_path, source_slot) として解決されることを検証する。
    /// </summary>
    [Fact]
    public async Task GallerySelectionState_LoadsSelectedPhotoRefsFromDatabase()
    {
        await db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = "/photo/a.jpg",
            PhotoFilename = "a.jpg",
            Timestamp = "2026-06-05 10:00:00",
            SourceSlot = 2,
        });
        using var state = new GallerySelectionState(photoService, toastService);

        state.selectedPhotoPaths.Add("/photo/a.jpg");
        for (var i = 0; i < 20 && state.selectedPhotoRefs.Count == 0; i++)
        {
            await Task.Delay(10);
        }

        var selected = Assert.Single(state.selectedPhotoRefs);
        Assert.Equal("/photo/a.jpg", selected.photo_path);
        Assert.Equal(2, selected.source_slot);
    }

    /// <summary>
    /// PhotoModalState の写真選択、類似検索履歴、前後移動、クローズ処理を確認する。
    ///
    /// モーダルはギャラリーの写真リストを保持し、通常選択では履歴を消し、
    /// 類似検索からの選択では戻るための履歴を積む。
    /// このテストは XAML モーダルを出さずに、ユーザー操作後の選択写真とナビゲーション可否だけを検証する。
    /// </summary>
    [Fact]
    public void PhotoModalState_NavigatesHistoryAndAdjacentPhotos()
    {
        var state = new PhotoModalState(photoService, toastService);
        var first = Thumb("/photo/a.jpg", "a.jpg", "Alpha");
        var second = Thumb("/photo/b.jpg", "b.jpg", "Beta");
        var third = Thumb("/photo/c.jpg", "c.jpg", "Gamma");
        state.setPhotoList([first, second, third]);

        state.onSelectPhoto(second);
        state.goPrevPhoto();
        state.onSelectPhoto(third, isSimilarSearch: true);
        state.goBackPhoto();
        state.goNextPhoto();
        state.closePhotoModal();

        Assert.Null(state.SelectedPhoto);
        Assert.False(state.CanGoBack);
        Assert.Empty(state.photoHistory);
    }

    /// <summary>
    /// PhotoModalViewModel が world_id 未設定時は何もせず、不正 world_id ではエラー toast を出すことを確認する。
    ///
    /// handleOpenWorld は通常ブラウザ起動を伴うが、world_id が無い場合は早期 return する。
    /// 不正な ID は WorldService.OpenWorldUrlAsync が Launcher を呼ぶ前に例外にするため、
    /// 外部プロセスを起動せずにエラー通知分岐を検証できる。
    /// </summary>
    [Fact]
    public async Task PhotoModalViewModel_HandleOpenWorldSkipsMissingIdAndReportsInvalidId()
    {
        var config = new AppConfig(Path.Combine(tempDir, "modal-settings"));
        var scanner = new PhotoScanner(config, db, new LocalEventBus());
        var worldService = new WorldService(db, scanner);
        var state = new PhotoModalState(photoService, toastService);
        var viewModel = new PhotoModalViewModel(state, worldService, toastService);
        var photo = Thumb("/photo/a.jpg", "a.jpg", "Alpha");

        state.setSelectedPhoto(photo);
        await viewModel.handleOpenWorld();
        photo.WorldId = "invalid";
        await viewModel.handleOpenWorld();

        Assert.Contains(toastService.toasts, toast => toast.Msg.Contains("ワールドページを開けませんでした"));
    }

    /// <summary>
    /// TemplatePageViewModel が設定からテンプレート一覧とアクティブテンプレートを正規化することを確認する。
    ///
    /// 設定ファイルには空文字や、一覧に存在しない activeTweetTemplate が入り得る。
    /// ViewModel は空テンプレートを捨て、アクティブ指定が不正な場合は先頭テンプレートへフォールバックするため、
    /// その補正をテストで固定する。
    /// </summary>
    [Fact]
    public void TemplatePageViewModel_ApplySettingsNormalizesTemplates()
    {
        var viewModel = new TemplatePageViewModel(null!, null!, toastService);
        viewModel.startEdit("消えるテンプレート");

        viewModel.applySettings(["  ", "A", "B"], activeTemplate: "missing");

        Assert.Equal(["A", "B"], viewModel.tweetTemplates.ToArray());
        Assert.Equal("A", viewModel.ActiveTweetTemplate);
        Assert.Null(viewModel.EditingTweetTemplate);
        Assert.Equal("", viewModel.TweetTemplateDraft);
    }

    /// <summary>
    /// テンプレート保存の新規追加・重複抑止・編集更新を確認する。
    ///
    /// saveTemplateDraft は永続化を行わず、インメモリのテンプレート一覧だけを更新する。
    /// ここでは空白トリム、重複時の追加抑止、編集中テンプレートを差し替えた場合の
    /// ActiveTweetTemplate 追従をまとめて検証する。
    /// </summary>
    [Fact]
    public void TemplatePageViewModel_SaveTemplateDraftAddsAndUpdatesInMemoryTemplates()
    {
        var viewModel = new TemplatePageViewModel(null!, null!, toastService);

        viewModel.TweetTemplateDraft = "  {world} {tags}  ";
        viewModel.saveTemplateDraft();
        viewModel.TweetTemplateDraft = "{world} {tags}";
        viewModel.saveTemplateDraft();
        viewModel.startEdit("{world} {tags}");
        viewModel.TweetTemplateDraft = "{world-name} {date}";
        viewModel.saveTemplateDraft();

        Assert.Equal(["{world-name} {date}"], viewModel.tweetTemplates.ToArray());
        Assert.Equal("{world-name} {date}", viewModel.ActiveTweetTemplate);
        Assert.Null(viewModel.EditingTweetTemplate);
        Assert.Equal("", viewModel.TweetTemplateDraft);
    }

    /// <summary>
    /// 写真メタデータから投稿テンプレートの各プレースホルダが展開されることを確認する。
    ///
    /// buildTweetText は UI ではなく投稿本文の仕様そのものを作る関数である。
    /// ワールド名、world_id、日付、ファイル名、タグの空白除去が壊れると投稿内容が変わるため、
    /// 代表的なプレースホルダを一つのテンプレートに含めて検証する。
    /// </summary>
    [Fact]
    public void TemplatePageViewModel_BuildTweetTextExpandsPhotoPlaceholders()
    {
        var viewModel = new TemplatePageViewModel(null!, null!, toastService);
        var photo = Thumb("/photo/a.jpg", "a.jpg", "Alpha");
        photo.WorldId = "wrld_123";
        photo.Timestamp = "2026-06-05T12:34:56";
        photo.Tags = ["blue sky", "夜"];

        var text = viewModel.buildTweetText("{world}|{world_id}|{date}|{timestamp}|{file}|{memo}|{tags}", photo);

        Assert.Equal("Alpha|wrld_123|2026-06-05 12:34|2026-06-05T12:34:56|a.jpg||#bluesky #夜", text);
    }

    /// <summary>
    /// TemplatePageViewModel の削除と保存が、テンプレート一覧・アクティブ選択・設定ファイルへ反映されることを確認する。
    ///
    /// deleteTemplate は一覧からの削除だけでなく、削除対象がアクティブまたは編集中だった場合のフォローを行い、
    /// saveTemplates 経由で SettingsService に永続化する。
    /// saveTemplate はドラフト保存と永続化を連続で行うため、設定ファイルを読み直して結果を検証する。
    /// </summary>
    [Fact]
    public async Task TemplatePageViewModel_DeleteAndSaveTemplatePersistSettings()
    {
        var config = new AppConfig(Path.Combine(tempDir, "template-settings"));
        config.SaveSetting(new AlpheratzSetting
        {
            TweetTemplates = ["A", "B"],
            ActiveTweetTemplate = "B",
        });
        var settingsService = new SettingsService(config);
        var viewModel = new TemplatePageViewModel(settingsService, null!, toastService);
        viewModel.applySettings(["A", "B"], "B");
        viewModel.startEdit("B");

        await viewModel.deleteTemplate("B", new AlpheratzSettingDto());
        viewModel.TweetTemplateDraft = "C";
        await viewModel.saveTemplate(new AlpheratzSettingDto());
        var saved = config.LoadSetting();

        Assert.Equal(["A", "C"], viewModel.tweetTemplates.ToArray());
        Assert.Equal("A", viewModel.ActiveTweetTemplate);
        Assert.Null(viewModel.EditingTweetTemplate);
        Assert.Equal(["A", "C"], saved.TweetTemplates);
        Assert.Equal("A", saved.ActiveTweetTemplate);
        Assert.Contains(toastService.toasts, toast => toast.Msg.Contains("テンプレートを保存しました"));
    }

    /// <summary>
    /// TagMasterViewModel が空入力・長すぎる入力を弾き、正常入力はDBへ保存してドラフトをクリアすることを確認する。
    ///
    /// タグマスタはギャラリーやモーダルの候補元になるため、空タグや長すぎるタグを登録しないこと、
    /// 正常登録後に一覧をDBから読み直すことが重要である。
    /// さらに削除成功時にタグ一覧が空へ戻ることも同じテストで確認する。
    /// </summary>
    [Fact]
    public async Task TagMasterViewModel_CreateAndDeleteTagValidateInputAndRefreshList()
    {
        var viewModel = new TagMasterViewModel(db, toastService);

        viewModel.TagDraft = "   ";
        await viewModel.createTag();
        viewModel.TagDraft = new string('x', 41);
        await viewModel.createTag();
        viewModel.TagDraft = "  night  ";
        await viewModel.createTag();
        var deleted = await viewModel.tryDeleteTag("night");

        Assert.True(deleted);
        Assert.Equal("", viewModel.TagDraft);
        Assert.Empty(viewModel.masterTags);
        Assert.Empty(await db.GetAllTagsAsync());
        Assert.Contains(toastService.toasts, toast => toast.Msg.Contains("40文字以内"));
        Assert.Contains(toastService.toasts, toast => toast.Msg.Contains("タグを追加しました"));
        Assert.Contains(toastService.toasts, toast => toast.Msg.Contains("タグを削除しました"));
    }

    /// <summary>
    /// DispatcherService の DispatcherQueue なし経路が、同期実行・例外返却・遅延実行を行うことを確認する。
    ///
    /// ユニットテストや初期化前のサービスでは DispatcherQueue が存在しない。
    /// その場合でも RunOnUiThread は action を同期実行し、例外は faulted Task として呼出元へ返す。
    /// setTimeout も同じ null Dispatcher 経路で action を実行するため、UI ランタイムなしの仕様を固定する。
    /// </summary>
    [Fact]
    public async Task DispatcherService_WithoutDispatcherRunsActionsAndPropagatesFailures()
    {
        var service = new DispatcherService();
        var frameCalled = false;
        var timeoutCalled = false;

        service.requestAnimationFrame(() => frameCalled = true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunOnUiThread(() => throw new InvalidOperationException("boom")));
        await service.setTimeout(() => timeoutCalled = true, milliseconds: 1);

        Assert.True(frameCalled);
        Assert.True(timeoutCalled);
    }

    /// <summary>
    /// ToastService が duration 後に DispatcherQueue なしでも toast を削除することを確認する。
    ///
    /// テスト環境や MainWindow 破棄後は App.MainWindowInstance が null になる。
    /// その場合でも ToastService は UiObservableCollection を直接更新して古い通知を消すため、
    /// 短い duration で追加し、一定時間内にコレクションから取り除かれることを検証する。
    /// </summary>
    [Fact]
    public async Task ToastService_RemovesToastAfterDurationWithoutDispatcher()
    {
        var service = new ToastService();

        service.addToast("short", ToastType.info, duration: 1);
        for (var i = 0; i < 20 && service.toasts.Count > 0; i++)
        {
            await Task.Delay(10);
        }

        Assert.Empty(service.toasts);
    }

    /// <summary>
    /// SettingsViewModel が現在のUI状態と overrides から保存用 DTO を組み立てることを確認する。
    ///
    /// フォルダ変更のように VM 反映前に保存する経路では overrides が優先される。
    /// 一方で、未指定項目は現在の VM 値を使う必要があるため、両者が混ざるケースを検証する。
    /// </summary>
    [Fact]
    public void SettingsViewModel_BuildSettingPayloadMergesOverridesWithCurrentState()
    {
        var viewModel = new SettingsViewModel(null!, null!)
        {
            PhotoFolderPath = "C:/primary",
            SecondaryPhotoFolderPath = "C:/secondary",
            StartupEnabled = true,
            ThemeMode = ThemeMode.dark,
            ViewMode = ViewMode.gallery,
            ActiveTweetTemplate = "A",
        };
        viewModel.tweetTemplates.ReplaceAll(["A", "B"]);

        var payload = viewModel.buildSettingPayload(new AlpheratzSettingDto
        {
            photoFolderPath = "D:/override",
            enableStartup = false,
        });

        Assert.Equal("D:/override", payload.photoFolderPath);
        Assert.Equal("C:/secondary", payload.secondaryPhotoFolderPath);
        Assert.False(payload.enableStartup);
        Assert.Equal(ThemeMode.dark, payload.themeMode);
        Assert.Equal(ViewMode.gallery, payload.viewMode);
        Assert.Equal(["A", "B"], payload.tweetTemplates);
        Assert.Equal("A", payload.activeTweetTemplate);
    }

    /// <summary>
    /// SettingsCompositeViewModel が3つの子 ViewModel をそのまま公開することを確認する。
    ///
    /// SettingsPage の XAML は Settings/TagMaster/Template の階層バインディングに依存する。
    /// 複合VMが参照を差し替えたり新規生成したりしないことを、同じインスタンス参照で検証する。
    /// </summary>
    [Fact]
    public void SettingsCompositeViewModel_ExposesNestedViewModelsByReference()
    {
        var settings = new SettingsViewModel(null!, null!);
        var tagMaster = new TagMasterViewModel(db, toastService);
        var template = new TemplatePageViewModel(null!, null!, toastService);

        var composite = new SettingsCompositeViewModel(settings, tagMaster, template);

        Assert.Same(settings, composite.Settings);
        Assert.Same(tagMaster, composite.TagMaster);
        Assert.Same(template, composite.Template);
    }

    /// <summary>
    /// ThemeHelper の選択テーマ通知と StellaRecord 利用可否確認が例外なく動くことを確認する。
    ///
    /// ThemeHelper.NotifySelectedThemeChanged は Converter 用の静的フォールバック状態を更新するだけなので、
    /// Application を起動せずに検証できる。
    /// StellaRecordRegistration.IsStellaRecordAvailable はレジストリ参照に失敗しても false へ落ちる設計で、
    /// 連携先が無い開発環境でもアプリ起動を妨げない。
    /// </summary>
    [Fact]
    public void SmallStaticHelpers_UpdateThemeFallbackAndCheckOptionalIntegrationAvailability()
    {
        ThemeHelper.NotifySelectedThemeChanged(Microsoft.UI.Xaml.ElementTheme.Dark);

        var available = StellaRecordRegistration.IsStellaRecordAvailable();

        Assert.Equal(Microsoft.UI.Xaml.ElementTheme.Dark, ThemeHelper.SelectedTheme);
        Assert.IsType<bool>(available);
    }

    /// <summary>
    /// PhotoScanner.ProbeImageDimensions がPNG/JPEGヘッダから向きと寸法を読むことを確認する。
    ///
    /// スキャン処理本体はファイル列挙やDB更新を含むが、画像寸法判定は小さなヘッダだけで検証できる。
    /// ここでは有効なPNG、SOF0を持つJPEG、未対応拡張子を使い、
    /// landscape / portrait / unknown の3経路を固定する。
    /// </summary>
    [Fact]
    public void PhotoScanner_ProbeImageDimensionsReadsPngJpegAndUnknownFiles()
    {
        var pngPath = Path.Combine(tempDir, "landscape.png");
        var jpegPath = Path.Combine(tempDir, "portrait.jpg");
        var textPath = Path.Combine(tempDir, "note.txt");
        File.WriteAllBytes(pngPath, PngHeader(width: 320, height: 120));
        File.WriteAllBytes(jpegPath, JpegHeader(width: 80, height: 240));
        File.WriteAllText(textPath, "not an image");

        var png = PhotoScanner.ProbeImageDimensions(pngPath);
        var jpeg = PhotoScanner.ProbeImageDimensions(jpegPath);
        var unknown = PhotoScanner.ProbeImageDimensions(textPath);

        Assert.Equal(("landscape", 320L, 120L), png);
        Assert.Equal(("portrait", 80L, 240L), jpeg);
        Assert.Equal(("unknown", null, null), unknown);
    }

    /// <summary>
    /// PhotoGridItem を短く作るためのテスト専用ファクトリ。
    /// GallerySelectionState は PhotoGridItem.Photo.PhotoPath だけを見るため、他の項目は最小限にする。
    /// </summary>
    private static PhotoGridItem GridItem(string path)
        => new() { Photo = Thumb(path, Path.GetFileName(path), worldName: null) };

    /// <summary>
    /// PhotoThumbnailItem を短く作るためのテスト専用ファクトリ。
    /// テストの読みやすさを保つため、必要なパス・ファイル名・ワールド名だけを引数化する。
    /// </summary>
    private static PhotoThumbnailItem Thumb(string path, string filename, string? worldName)
        => new()
        {
            PhotoPath = path,
            PhotoFilename = filename,
            WorldName = worldName,
            Timestamp = "2026-06-05 12:00:00",
            SourceSlot = 1,
        };

    /// <summary>
    /// PNG のシグネチャと IHDR までを持つ最小ヘッダを作る。
    /// PhotoScanner は幅高さだけを読むため、CRC や IDAT は不要である。
    /// </summary>
    private static byte[] PngHeader(int width, int height)
    {
        var bytes = new byte[24];
        var header = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52 };
        Array.Copy(header, bytes, header.Length);
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    /// <summary>
    /// SOF0 セグメントだけを含む簡易 JPEG ヘッダを作る。
    /// PhotoScanner は SOI から SOF0 までを順に読むため、画像データ本体は不要である。
    /// </summary>
    private static byte[] JpegHeader(int width, int height)
    {
        return
        [
            0xFF, 0xD8,
            0xFF, 0xC0,
            0x00, 0x07,
            0x08,
            (byte)(height >> 8), (byte)height,
            (byte)(width >> 8), (byte)width,
        ];
    }

    /// <summary>
    /// PNG IHDR の幅高さに使う 32bit big-endian 値を書き込む。
    /// テストデータ生成の補助であり、アプリ本体のエンコード処理ではない。
    /// </summary>
    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
