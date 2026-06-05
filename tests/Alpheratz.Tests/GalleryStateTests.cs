using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Models;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml;

namespace Alpheratz.Tests;

/// <summary>
/// ギャラリーの状態オブジェクトと表示用モデルを検証するテスト。
///
/// ここで扱う型はXAMLコントロールそのものではなく、フィルタ状態、選択状態、
/// サムネイル表示モデルのようなUI非依存のロジックである。
/// 画面を起動せずにテストできるため、ユーザー操作の前提条件を小さく保ちながら
/// ギャラリーの現在仕様を固定できる。
/// </summary>
public sealed class GalleryStateTests
{
    /// <summary>
    /// 検索コマンドをフィルタ状態へ反映したとき、各フィルタ値と件数が更新されることを確認する。
    ///
    /// SearchCommandParser は文字列を構造化するだけであり、実際の画面フィルタ値へ反映する責務は
    /// GalleryFiltersState.applySearchCommands にある。このテストは、日付、向き、お気に入り、
    /// タグ、フォルダ、ソートが一括更新され、BatchCompleted 通知が任意に抑制できる仕様を確認する。
    /// </summary>
    [Fact]
    public void ApplySearchCommands_UpdatesFilterValuesAndActiveCount()
    {
        var state = new GalleryFiltersState();
        var commands = SearchCommandParser.Parse(
            "tag:night since:2026-06-01 until:2026-06-05 orientation:portrait is:fav folder:secondary sort:world");

        state.applySearchCommands(commands, raiseBatchCompleted: false);

        Assert.Equal("2026-06-01", state.DateFrom);
        Assert.Equal("2026-06-05", state.DateTo);
        Assert.Equal(DatePreset.custom, state.DatePreset);
        Assert.Equal("portrait", state.OrientationFilter);
        Assert.True(state.FavoritesOnly);
        Assert.Equal(["night"], state.tagFilters);
        Assert.Equal(DisplayFolderMode.secondary, state.DisplayFolderMode);
        Assert.Equal(SortMode.worldAsc, state.SortMode);
        Assert.Equal(5, state.ActiveFilterCount);
    }

    /// <summary>
    /// resetFilters が全フィルタを初期値へ戻すことを確認する。
    ///
    /// フィルタ状態は複数のコレクションと単体プロパティで構成されている。
    /// 一部だけ初期化漏れがあると、UI上は条件解除に見えてもDBクエリには条件が残る。
    /// このテストは、検索語、ワールド、日付、向き、お気に入り、タグ、グルーピング、
    /// 表示フォルダ、ソートがまとめて初期化されることを固定する。
    /// </summary>
    [Fact]
    public void ResetFilters_ClearsAllFilterState()
    {
        var state = new GalleryFiltersState
        {
            SearchQuery = "query",
            DebouncedQuery = "query",
            DateFrom = "2026-06-01",
            DateTo = "2026-06-05",
            DatePreset = DatePreset.custom,
            OrientationFilter = "landscape",
            FavoritesOnly = true,
            GroupingMode = GroupingMode.world,
            DisplayFolderMode = DisplayFolderMode.primary,
            SortMode = SortMode.worldAsc,
        };
        state.worldFilters.Add("World");
        state.tagFilters.Add("tag");

        state.resetFilters();

        Assert.Equal(string.Empty, state.SearchQuery);
        Assert.Equal(string.Empty, state.DebouncedQuery);
        Assert.Empty(state.worldFilters);
        Assert.Equal(string.Empty, state.DateFrom);
        Assert.Equal(string.Empty, state.DateTo);
        Assert.Equal(DatePreset.none, state.DatePreset);
        Assert.Equal("all", state.OrientationFilter);
        Assert.False(state.FavoritesOnly);
        Assert.Empty(state.tagFilters);
        Assert.Equal(GroupingMode.none, state.GroupingMode);
        Assert.Equal(DisplayFolderMode.all, state.DisplayFolderMode);
        Assert.Equal(SortMode.dateDesc, state.SortMode);
        Assert.Equal(0, state.ActiveFilterCount);
    }

    /// <summary>
    /// 日付プリセット文字列の解析、無効入力フォールバック、タグ件数差し替えを確認する。
    ///
    /// XAML の ComboBox からは enum ではなく文字列としてプリセット値が渡ることがある。
    /// 解析不能な値は none として扱い、タグ件数は null 入力でも空辞書へ戻すことで
    /// バインディング側が null を考慮せずに済む。
    /// </summary>
    [Fact]
    public void DatePresetStringAndTagCounts_HandleValidInvalidAndNullInputs()
    {
        var state = new GalleryFiltersState();

        state.handleDatePresetSelect("today");
        var todayFrom = state.DateFrom;
        state.setTagFilterCounts(new Dictionary<string, long> { ["night"] = 3 });
        state.handleDatePresetSelect("not-a-preset");
        state.setTagFilterCounts(null!);

        Assert.Equal(DatePreset.none, state.DatePreset);
        Assert.Equal("", state.DateFrom);
        Assert.Equal("", state.DateTo);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", todayFrom);
        Assert.Empty(state.TagFilterCounts);
    }

    /// <summary>
    /// PhotoThumbnailItem のパス優先順位とDTO変換を確認する。
    ///
    /// ギャラリーの小カードは GridThumbPath を優先し、詳細モーダルは DisplayThumbPath、
    /// ResolvedPhotoPath、PhotoPath の順に表示元を選ぶ。
    /// この優先順位が崩れると、生成済みサムネイルではなく元画像を読みに行ったり、
    /// シンボリックリンク解決後の実パスが使われなくなるため、DTO往復と合わせて固定する。
    /// </summary>
    [Fact]
    public void PhotoThumbnailItem_UsesExpectedEffectivePathsAndRoundTripsDto()
    {
        var item = PhotoThumbnailItem.FromDto(new PhotoRecordDto
        {
            photo_filename = "a.jpg",
            photo_path = "C:/photos/a.jpg",
            resolved_photo_path = "D:/resolved/a.jpg",
            grid_thumb_path = "C:/cache/grid.jpg",
            display_thumb_path = "C:/cache/display.jpg",
            world_id = "wrld",
            world_name = "World",
            timestamp = "2026-06-05 10:00:00",
            phash = "abcd",
            orientation = "landscape",
            image_width = 1920,
            image_height = 1080,
            source_slot = 2,
            is_favorite = true,
            tags = ["tag"],
            match_source = "phash",
        });

        Assert.Equal("C:\\cache\\grid.jpg", item.EffectiveSourcePath);
        Assert.Equal("C:\\cache\\display.jpg", item.EffectiveDisplayPath);

        var dto = item.ToDto();
        Assert.Equal("a.jpg", dto.photo_filename);
        Assert.Equal("wrld", dto.world_id);
        Assert.True(dto.is_favorite);
        Assert.Equal(["tag"], dto.tags);
        Assert.Equal("phash", dto.match_source);
    }

    /// <summary>
    /// グループ件数表示の閾値を確認する。
    ///
    /// グループカードは件数が2以上のときだけバッジを出す。
    /// 1件だけのカードでバッジを出すと通常カードと見た目の意味がずれるため、
    /// GroupCount の変更時に Visibility と表示ラベルが追従することを確認する。
    /// </summary>
    [Fact]
    public void PhotoGridItem_GroupCountControlsBadgeVisibility()
    {
        var item = new PhotoGridItem();

        item.GroupCount = 1;
        Assert.Equal(Visibility.Collapsed, item.GroupCountVisibility);
        Assert.Equal(string.Empty, item.GroupCountLabel);

        item.GroupCount = 3;
        Assert.Equal(Visibility.Visible, item.GroupCountVisibility);
        Assert.Contains("3", item.GroupCountLabel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Masonry レイアウトが指定列数を幅に応じて制限し、各写真を列へ配置することを確認する。
    ///
    /// Masonry は最も低い列へ次のカードを置く貪欲法を使う。
    /// ここでは、狭いパネルでは列数が最小幅に合わせて制限されること、
    /// 返却される各アイテムの幅が列幅と一致すること、総高さが0より大きくなることを確認する。
    /// </summary>
    [Fact]
    public void GalleryMasonryLayout_BuildsStableColumnLayout()
    {
        var photos = new[]
        {
            Photo("a", width: 1920, height: 1080),
            Photo("b", width: 1080, height: 1920),
            Photo("c", width: 1000, height: 1000),
        };

        var result = GalleryMasonryLayout.Build(photos, panelWidth: 900, requestedColumnCount: 4);

        Assert.Equal(2, result.ColumnCount);
        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(result.ColumnWidth, item.Width));
        Assert.True(result.TotalHeight > 0);
    }

    /// <summary>
    /// GalleryMonthsCalculator が写真のタイムスタンプから月グループを作り、無効な日時を fallback グループに入れることを確認する。
    ///
    /// 月ナビゲーションは photos 配列上の FirstIndex を使ってスクロール先を決める。
    /// 同じ月の連続写真は1グループにまとまり、月が変わった時点のインデックスが保存される必要がある。
    /// また、壊れた timestamp は例外ではなく 0000-01 に寄せる現在仕様なので、未知データでも月ナビが落ちないことを固定する。
    /// </summary>
    [Fact]
    public void GalleryMonthsCalculator_BuildsMonthGroupsAndFallbackGroup()
    {
        var photos = new[]
        {
            PhotoWithTimestamp("a", "2026-06-05 10:00:00"),
            PhotoWithTimestamp("b", "2026-06-01 09:00:00"),
            PhotoWithTimestamp("c", "2026-05-31 23:59:59"),
            PhotoWithTimestamp("d", "not-a-date"),
        };

        var groups = GalleryMonthsCalculator.Build(photos);

        Assert.Equal(3, groups.Count);
        Assert.Equal(new GalleryMonthGroup("2026-06", 2026, 6, "6月", 0, 2), groups[0]);
        Assert.Equal(new GalleryMonthGroup("2026-05", 2026, 5, "5月", 2, 1), groups[1]);
        Assert.Equal(new GalleryMonthGroup("0000-01", 0, 1, "1月", 3, 1), groups[2]);
    }

    /// <summary>
    /// ToastService が複数の Toast に衝突しない ID を割り当てることを確認する。
    ///
    /// ToastMessage は record の値等価なので、ID が衝突すると別通知を誤って削除する可能性がある。
    /// 現在仕様では Interlocked.Increment の単調増加 ID を使うため、同一ミリ秒相当の連続追加でも ID が異なる。
    /// 自動削除は DispatcherQueue の有無により実行経路が変わるため、ここでは UI ランタイムに依存しない
    /// 追加直後の ID 一意性とメッセージ保持だけを検証する。
    /// </summary>
    [Fact]
    public void ToastService_AddToastUsesUniqueIdsForRapidNotifications()
    {
        var service = new ToastService();

        service.addToast("one", ToastType.info, duration: 3000);
        service.addToast("two", ToastType.error, duration: 3000);
        var ids = service.toasts.Select(toast => toast.Id).ToArray();

        Assert.Equal(2, ids.Length);
        Assert.NotEqual(ids[0], ids[1]);
        Assert.Equal(["one", "two"], service.toasts.Select(toast => toast.Msg).ToArray());
    }

    /// <summary>
    /// レイアウトテスト用の写真モデルを最小項目で作成する。
    ///
    /// Masonry の高さ計算では画像サイズとパスだけが必要である。
    /// その他のプロパティは既定値に任せ、テストが配置計算だけに集中できるようにする。
    /// </summary>
    private static PhotoThumbnailItem Photo(string id, long width, long height)
        => new()
        {
            PhotoFilename = $"{id}.jpg",
            PhotoPath = $"/photo/{id}.jpg",
            ImageWidth = width,
            ImageHeight = height,
        };

    /// <summary>
    /// 月グループ計算用の写真モデルを作る。
    /// レイアウト計算とは異なり timestamp だけが重要なので、画像サイズは最小の固定値にする。
    /// </summary>
    private static PhotoThumbnailItem PhotoWithTimestamp(string id, string timestamp)
    {
        var photo = Photo(id, 1, 1);
        photo.Timestamp = timestamp;
        return photo;
    }

}

/// <summary>
/// LocalEventBus の同期的な配信契約を確認するテスト。
///
/// EventBus はバックグラウンド処理とViewModelの連携点であり、
/// publish時に各handlerをawaitして順番に完了させる設計を採っている。
/// ここでは typed payload の受け渡し、購読解除、例外を出す購読者が後続購読者を止めないことを検証する。
/// </summary>
public sealed class LocalEventBusTests
{
    /// <summary>
    /// 型付き購読者がpayloadを受け取り、Dispose後は呼ばれなくなることを確認する。
    ///
    /// Subscribe&lt;T&gt; は内部的にはJsonElement購読へ変換される。
    /// このテストは、JSON変換を挟んでも値が届くことと、返却されたIAsyncDisposableが
    /// 同じhandlerを正しく解除することを固定する。
    /// </summary>
    [Fact]
    public async Task PublishAsync_DeliversTypedPayloadAndHonorsUnsubscribe()
    {
        var bus = new LocalEventBus();
        var received = new List<int>();

        var subscription = bus.Subscribe<int>("number", value =>
        {
            received.Add(value);
            return Task.CompletedTask;
        });

        await bus.PublishAsync("number", 10);
        await subscription.DisposeAsync();
        await bus.PublishAsync("number", 20);

        Assert.Equal([10], received);
    }

    /// <summary>
    /// ある購読者が例外を投げても、後続購読者へ配信が続くことを確認する。
    ///
    /// EventBus は画面更新やスキャン後処理の通知に使われる。
    /// 1つの購読者の失敗で別モジュールの更新まで止めると、状態不整合が広がるため、
    /// PublishAsync は例外をログに留めて次のhandlerを実行する。
    /// </summary>
    [Fact]
    public async Task PublishAsync_ContinuesAfterSubscriberException()
    {
        var bus = new LocalEventBus();
        var delivered = false;

        bus.Subscribe("event", () => throw new InvalidOperationException("subscriber failed"));
        bus.Subscribe("event", () =>
        {
            delivered = true;
            return Task.CompletedTask;
        });

        await bus.PublishAsync("event");

        Assert.True(delivered);
    }
}
