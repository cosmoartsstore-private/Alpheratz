using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging;
using Alpheratz.Core.Scanner;
using Alpheratz.Features.Gallery;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;

namespace Alpheratz.Tests;

/// <summary>
/// GalleryViewModel のユーザー操作境界を検証するテスト。
///
/// GalleryViewModel はフィルタ状態、写真一覧状態、選択状態、表示状態を束ね、
/// お気に入り、タグ、一括操作、写真クリックの分岐をサービスへ橋渡しする。
/// XAML なしで実 DB と実サービスを使い、画面から呼ばれる公開メソッドが
/// State と DB の両方を期待どおり更新することを固定する。
/// </summary>
public sealed class GalleryViewModelBehaviorTests : IDisposable
{
    private readonly string tempDir;
    private readonly AlpheratzDb db;
    private readonly LocalEventBus eventBus = new();
    private readonly ToastService toastService = new();
    private readonly GalleryViewModel viewModel;

    /// <summary>
    /// 各テスト専用の DB と GalleryViewModel 一式を構築する。
    /// ThumbnailWorker は実画像がない場合に個別生成をスキップするため、
    /// ここでは ViewModel の状態更新経路を主に観察する。
    /// </summary>
    public GalleryViewModelBehaviorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.GalleryViewModel.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        db = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
        db.Initialize();

        var config = new AppConfig(Path.Combine(tempDir, "settings"));
        var photoService = new PhotoService(db);
        var scanner = new PhotoScanner(config, db, eventBus);
        var worldService = new WorldService(db, scanner);
        var phashService = new PhashService(db, eventBus);
        var photosState = new GalleryPhotosState(
            photoService,
            eventBus,
            toastService,
            new DispatcherService(),
            new ThumbnailWorker(new ThumbnailService()));
        var filtersState = new GalleryFiltersState();
        var selectionState = new GallerySelectionState(photoService, toastService);
        var displayState = new GalleryDisplayState();
        var scrollState = new GalleryScrollState();

        viewModel = new GalleryViewModel(
            photoService,
            worldService,
            phashService,
            toastService,
            new DispatcherService(),
            photosState,
            filtersState,
            selectionState,
            displayState,
            scrollState);
    }

    /// <summary>
    /// 一時 DB と設定ディレクトリを削除し、ViewModel の購読を解除する。
    /// GalleryViewModel は filtersState の PropertyChanged を購読するため、明示的に Cleanup して
    /// テスト終了後の fire-and-forget ロードが次テストへ影響しないようにする。
    /// </summary>
    public void Dispose()
    {
        viewModel.Cleanup();
        viewModel.selectionState.Dispose();
        try { Directory.Delete(tempDir, recursive: true); }
        catch { }
    }

    /// <summary>
    /// applySearchNow が検索コマンドをフィルタ状態へ即時反映することを確認する。
    ///
    /// 検索ボックスの Enter は debounce を待たずにこのメソッドを呼ぶ。
    /// tag/is/folder/sort のようなコマンドが即座に filtersState へ移らないと、
    /// UI の検索条件表示と実際の DB クエリ条件が食い違うため、代表的なコマンドをまとめて検証する。
    /// </summary>
    [Fact]
    public void ApplySearchNow_AppliesCommandFiltersImmediately()
    {
        viewModel.filtersState.SearchQuery = "tag:night is:fav folder:primary sort:world";

        viewModel.applySearchNow();

        Assert.Equal("", viewModel.filtersState.DebouncedQuery);
        Assert.Equal(["night"], viewModel.filtersState.tagFilters);
        Assert.True(viewModel.filtersState.FavoritesOnly);
        Assert.Equal(DisplayFolderMode.primary, viewModel.filtersState.DisplayFolderMode);
        Assert.Equal(SortMode.worldAsc, viewModel.filtersState.SortMode);
    }

    /// <summary>
    /// SearchQuery の変更が 400ms デバウンス後に DebouncedQuery へ反映されることを確認する。
    ///
    /// 通常入力ではキー入力ごとに DB を叩かないよう、GalleryViewModel は SearchQuery 変更を遅延して
    /// DebouncedQuery に移す。
    /// 連続入力の最後の値だけが残ることを、短時間に2回代入してから待機して検証する。
    /// </summary>
    [Fact]
    public async Task SearchQueryChange_DebouncesPlainTextBeforeReload()
    {
        viewModel.filtersState.SearchQuery = "first";
        viewModel.filtersState.SearchQuery = "second";

        for (var i = 0; i < 20 && viewModel.filtersState.DebouncedQuery != "second"; i++)
        {
            await Task.Delay(50);
        }

        Assert.Equal("second", viewModel.filtersState.DebouncedQuery);
    }

    /// <summary>
    /// 単体のお気に入り・タグ追加・タグ削除が DB と表示中 PhotoThumbnailItem の両方へ反映されることを確認する。
    ///
    /// PhotoModal やカード上の星ボタンは GalleryViewModel 経由で PhotoService を呼び、
    /// 成功後に photosState.photos/displayItems 内の同一 photo_path を更新する。
    /// この同期が漏れると DB は更新済みでも画面だけ古いままになるため、同じ写真インスタンスの状態変化を検証する。
    /// </summary>
    [Fact]
    public async Task SinglePhotoMutations_UpdateDatabaseAndVisiblePhotoItems()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00"));
        var item = Thumb("/photo/a.jpg", "a.jpg");
        viewModel.photosState.setPhotos([item], autoGenerateThumbnails: false);
        viewModel.photosState.rebuildDisplayItems(GroupingMode.none);

        await viewModel.toggleFavorite("/photo/a.jpg", currentIsFavorite: false);
        await viewModel.addTag("/photo/a.jpg", " night ");
        await viewModel.removeTag("/photo/a.jpg", "night");
        var record = await db.GetPhotoRecordAsync("/photo/a.jpg");
        var tags = await db.GetPhotoTagsAsync("/photo/a.jpg");

        Assert.NotNull(record);
        Assert.True(record.is_favorite);
        Assert.True(item.IsFavorite);
        Assert.Empty(tags);
        Assert.Empty(item.Tags);
    }

    /// <summary>
    /// タグ追加が空文字、長すぎるタグ、重複タグを保存前に弾くことを確認する。
    ///
    /// addTag は DB へ渡す前に入力を正規化し、空・最大長超過・既に付与済みのタグを no-op にする。
    /// これらの分岐が壊れると、不要な DB 書き込みや UI 上の重複表示が起きるため、代表入力を固定する。
    /// </summary>
    [Fact]
    public async Task AddTag_RejectsEmptyTooLongAndDuplicateTagsBeforeDatabaseWrite()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00"));
        var item = Thumb("/photo/a.jpg", "a.jpg");
        item.Tags = ["exists"];
        viewModel.photosState.setPhotos([item], autoGenerateThumbnails: false);

        await viewModel.addTag("/photo/a.jpg", "   ");
        await viewModel.addTag("/photo/a.jpg", new string('x', 41));
        await viewModel.addTag("/photo/a.jpg", "exists");
        var tags = await db.GetPhotoTagsAsync("/photo/a.jpg");

        Assert.Empty(tags);
        Assert.Equal(["exists"], item.Tags);
        Assert.Contains(toastService.toasts, toast => toast.Msg.Contains("40文字以内"));
    }

    /// <summary>
    /// 一括お気に入りと一括タグ追加が選択中写真参照に対して実行され、
    /// 表示中写真の状態にも反映されることを確認する。
    ///
    /// 一括操作バーは GallerySelectionState.selectedPhotoRefs を入力として使う。
    /// DB 更新だけではなく、表示中の各 PhotoThumbnailItem にも IsFavorite と Tags が反映される必要があるため、
    /// 2枚を選択参照として登録して一括操作後のインメモリ状態を確認する。
    /// </summary>
    [Fact]
    public async Task BulkMutations_UpdateSelectedPhotosAndVisibleItems()
    {
        await db.UpsertPhotoAsync(Photo("/photo/a.jpg", "a.jpg", "2026-06-05 10:00:00"));
        await db.UpsertPhotoAsync(Photo("/photo/b.jpg", "b.jpg", "2026-06-05 11:00:00"));
        var first = Thumb("/photo/a.jpg", "a.jpg");
        var second = Thumb("/photo/b.jpg", "b.jpg");
        viewModel.photosState.setPhotos([first, second], autoGenerateThumbnails: false);
        viewModel.photosState.rebuildDisplayItems(GroupingMode.none);
        viewModel.selectionState.setSelectedPhotoRefs(
        [
            new SelectedPhotoRefDto { photo_path = "/photo/a.jpg", source_slot = 1 },
            new SelectedPhotoRefDto { photo_path = "/photo/b.jpg", source_slot = 1 },
        ]);

        await viewModel.bulkSetFavorite(true);
        await viewModel.bulkAddTag("bulk");

        Assert.True(first.IsFavorite);
        Assert.True(second.IsFavorite);
        Assert.Equal(["bulk"], first.Tags);
        Assert.Equal(["bulk"], second.Tags);
    }

    /// <summary>
    /// 一括タグ追加が空文字・長すぎるタグ・選択なしを no-op として扱うことを確認する。
    ///
    /// 一括操作バーでは、ユーザー入力が未確定のまま実行されることがある。
    /// 空文字や最大長超過を DB に渡さず、選択写真が無い場合にも通知だけで戻る現在仕様を固定する。
    /// </summary>
    [Fact]
    public async Task BulkAddTag_RejectsInvalidInputAndNoSelection()
    {
        await viewModel.bulkAddTag("   ");
        await viewModel.bulkAddTag(new string('x', 41));
        await viewModel.bulkAddTag("valid-but-no-selection");

        Assert.Empty(viewModel.selectionState.selectedPhotoRefs);
        Assert.Contains(toastService.toasts, toast => toast.Msg.Contains("40文字以内"));
    }

    /// <summary>
    /// 写真カードの起動が、複数選択モードでは選択切替、通常モードでは詳細表示コールバックへ分岐することを確認する。
    ///
    /// 同じカードクリックでも、IsMultiSelectMode によって意味が変わる。
    /// 複数選択中に PhotoModal が開くと一括操作が中断されるため、選択切替だけが起きること、
    /// 通常時には onSelectPhoto が呼ばれることを1つのテストで固定する。
    /// </summary>
    [Fact]
    public void HandlePhotoActivate_ChoosesSelectionOrModalCallbackByMode()
    {
        var photo = Thumb("/photo/a.jpg", "a.jpg");
        var item = new PhotoGridItem { Photo = photo };
        PhotoThumbnailItem? activated = null;

        viewModel.selectionState.handleToggleMultiSelectMode();
        viewModel.handlePhotoActivate(item, shiftKey: false, selected => activated = selected);
        Assert.Equal(["/photo/a.jpg"], viewModel.selectionState.selectedPhotoPaths.ToArray());
        Assert.Null(activated);

        viewModel.selectionState.handleToggleMultiSelectMode();
        viewModel.handlePhotoActivate(item, shiftKey: false, selected => activated = selected);

        Assert.Empty(viewModel.selectionState.selectedPhotoPaths);
        Assert.Same(photo, activated);
    }

    /// <summary>
    /// 類似写真からワールド情報を反映する操作が、DB・表示中写真・選択中写真を同じ値へ更新することを確認する。
    ///
    /// WorldResolve では候補写真のワールドを、対象写真へ手動適用する。
    /// GalleryViewModel.applySimilarWorldMatch は DB 更新後に photos/displayItems とモーダル側の選択写真へも
    /// 同じ world_name/world_id/match_source を反映するため、画面を再ロードしなくても表示が更新される。
    /// </summary>
    [Fact]
    public async Task ApplySimilarWorldMatch_CopiesWorldInfoToTargetPhotoAndSelection()
    {
        await db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = "/photo/source.jpg",
            PhotoFilename = "source.jpg",
            Timestamp = "2026-06-05 10:00:00",
            SourceSlot = 1,
            WorldName = "Source World",
            WorldId = "wrld_source",
            MatchSource = "metadata",
        });
        await db.UpsertPhotoAsync(Photo("/photo/target.jpg", "target.jpg", "2026-06-05 11:00:00"));
        var source = Thumb("/photo/source.jpg", "source.jpg");
        source.WorldName = "Source World";
        source.WorldId = "wrld_source";
        var target = Thumb("/photo/target.jpg", "target.jpg");
        var selectedSnapshots = new List<PhotoThumbnailItem>();
        viewModel.photosState.setPhotos([source, target], autoGenerateThumbnails: false);
        viewModel.photosState.rebuildDisplayItems(GroupingMode.none);
        viewModel.filtersState.GroupingMode = GroupingMode.world;

        await viewModel.applySimilarWorldMatch(source, target, updated => selectedSnapshots.Add(updated));
        var saved = await db.GetPhotoRecordAsync("/photo/target.jpg");

        Assert.NotNull(saved);
        Assert.Equal("Source World", saved.world_name);
        Assert.Equal("wrld_source", saved.world_id);
        Assert.Equal("phash", saved.match_source);
        Assert.Equal("Source World", target.WorldName);
        Assert.Equal("wrld_source", target.WorldId);
        Assert.Equal("phash", target.MatchSource);
        Assert.Single(selectedSnapshots);
        Assert.Contains(viewModel.photosState.displayItems, item => item.GroupKey == "Source World");
    }

    /// <summary>
    /// ワールドグループのドリルダウン取得が、現在のフィルタを使って対象ワールドの写真だけを返すことを確認する。
    ///
    /// グループカードを開いたときは、メインの photosState.photos を流用せず DB から取り直す。
    /// これによりメインビューの並べ替えや参照差し替えと独立したリストになるため、
    /// 2つのワールドを登録して片方の groupKey だけが返ることを検証する。
    /// </summary>
    [Fact]
    public async Task GetGroupPhotosAsync_ReturnsPhotosForRequestedWorldGroup()
    {
        await db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = "/photo/alpha-a.jpg",
            PhotoFilename = "alpha-a.jpg",
            Timestamp = "2026-06-05 10:00:00",
            WorldName = "Alpha",
            SourceSlot = 1,
        });
        await db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = "/photo/alpha-b.jpg",
            PhotoFilename = "alpha-b.jpg",
            Timestamp = "2026-06-05 11:00:00",
            WorldName = "Alpha",
            SourceSlot = 1,
        });
        await db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = "/photo/beta.jpg",
            PhotoFilename = "beta.jpg",
            Timestamp = "2026-06-05 12:00:00",
            WorldName = "Beta",
            SourceSlot = 1,
        });

        var photos = await viewModel.getGroupPhotosAsync("Alpha");

        Assert.Equal(2, photos.Count);
        Assert.All(photos, photo => Assert.Equal("Alpha", photo.WorldName));
        Assert.DoesNotContain(photos, photo => photo.PhotoFilename == "beta.jpg");
    }

    /// <summary>
    /// 一括コピーが選択中写真をコピーし、既存ファイルはスキップ件数として通知することを確認する。
    ///
    /// PhotoService.BulkCopyPhotosAsync はファイルI/Oを伴うため、ViewModel 側では戻り値の copied/skipped を
    /// toast へ変換することが主責務になる。
    /// 同じコピーを2回実行し、1回目はコピー成功、2回目は既存ファイルによるスキップになることを検証する。
    /// </summary>
    [Fact]
    public async Task BulkCopyPhotos_CopiesFilesAndReportsSkippedExistingFiles()
    {
        var sourceDir = Path.Combine(tempDir, "copy-source");
        var destinationDir = Path.Combine(tempDir, "copy-destination");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destinationDir);
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        File.WriteAllText(sourcePath, "photo");
        viewModel.selectionState.setSelectedPhotoRefs(
        [
            new SelectedPhotoRefDto { photo_path = AppPaths.NormalizePathForDb(sourcePath), source_slot = 1 },
        ]);

        await viewModel.bulkCopyPhotos(destinationDir);
        await viewModel.bulkCopyPhotos(destinationDir);

        Assert.True(File.Exists(Path.Combine(destinationDir, "a.jpg")));
        Assert.Contains(toastService.toasts, toast => toast.Msg.Contains("1 枚のファイルをコピーしました"));
        Assert.Contains(toastService.toasts, toast => toast.Msg.Contains("1 枚はスキップ"));
    }

    /// <summary>
    /// ワールド不明写真の解析開始操作が PhashService を呼び、開始通知を出すことを確認する。
    ///
    /// pending が無い場合でも StartPdqAnalysisAsync は正常完了し、ViewModel は「開始した」通知を出す。
    /// これはユーザー操作に対する即時フィードバックなので、DB に対象写真がない軽量ケースで固定する。
    /// </summary>
    [Fact]
    public async Task HandleStartUnknownWorldAnalysis_RunsPhashServiceAndAddsToast()
    {
        await viewModel.handleStartUnknownWorldAnalysis();

        Assert.Contains(toastService.toasts, toast => toast.Msg.Contains("一括分析を開始しました"));
    }

    /// <summary>
    /// DB 登録用の最小写真データを作る。
    /// GalleryViewModel のサービス呼び出し検証では、パス・ファイル名・撮影日時だけで十分である。
    /// </summary>
    private static PhotoUpsertData Photo(string path, string filename, string timestamp)
        => new()
        {
            PhotoPath = path,
            PhotoFilename = filename,
            Timestamp = timestamp,
            SourceSlot = 1,
        };

    /// <summary>
    /// 表示中写真として使う PhotoThumbnailItem を作る。
    /// ViewModel は PhotoPath と SourceSlot をキーに DB 更新とインメモリ更新を対応付ける。
    /// </summary>
    private static PhotoThumbnailItem Thumb(string path, string filename)
        => new()
        {
            PhotoPath = path,
            PhotoFilename = filename,
            Timestamp = "2026-06-05 10:00:00",
            SourceSlot = 1,
            Tags = [],
        };
}
