using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging;
using Alpheratz.Core.Imaging.Pdq;
using Alpheratz.Features.WorldResolve;
using Alpheratz.Messages;
using Alpheratz.Services;
using Alpheratz.Shared.Services;

namespace Alpheratz.Tests;

/// <summary>
/// WorldResolveViewModel のワールド候補生成と確定適用を検証するテスト。
///
/// WorldResolveViewModel は XAML 上ではモーダルとして表示されるが、中核は DB 内の
/// 「ワールド不明 + phash あり」写真と「ワールド既知 + phash あり」写真の距離計算である。
/// ここでは実 SQLite DB と実 WorldService の距離関数を使い、UI を起動せずに
/// 初期解析、候補ピッカー、候補選択、確定反映の動作を固定する。
/// </summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class WorldResolveViewModelBehaviorTests : IDisposable
{
    private readonly string tempDir;
    private readonly AlpheratzDb db;
    private readonly ToastService toastService = new();
    private readonly WorldResolveViewModel viewModel;

    /// <summary>
    /// 一時 DB と WorldResolveViewModel を構築する。
    /// ThumbnailWorker は存在しない画像パスを個別にスキップするため、テストでは DB 上の解析状態だけを観察する。
    /// </summary>
    public WorldResolveViewModelBehaviorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.WorldResolve.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        db = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
        db.Initialize();
        viewModel = new WorldResolveViewModel(
            db,
            new ThumbnailWorker(new ThumbnailService()),
            toastService);
    }

    /// <summary>
    /// 一時 DB とサムネイル生成中に作られ得る補助ファイルを削除する。
    /// 後片付けはテスト対象ではないため、削除失敗は握りつぶす。
    /// </summary>
    public void Dispose()
    {
        try { viewModel.StopGenerationAsync().GetAwaiter().GetResult(); }
        catch { }
        try { Directory.Delete(tempDir, recursive: true); }
        catch { }
    }

    /// <summary>
    /// InitializeAsync が未知ワールド写真を取得し、同じ source_slot の既知写真から最短距離候補を設定することを確認する。
    ///
    /// ワールド解決 UI の初期表示では、ユーザーが何も選ばなくても最有力候補が表示される必要がある。
    /// 完全一致の既知写真と遠距離の既知写真を同じスロットに置き、未知写真の MatchWorldName/Distance が
    /// 完全一致候補へ向くことを検証する。
    /// </summary>
    [Fact]
    public async Task InitializeAsync_BuildsUnknownItemsWithBestMatch()
    {
        await InsertKnownAsync("/known/exact.jpg", "exact.jpg", "Exact World", "wrld_exact", Hash(0x00));
        await InsertKnownAsync("/known/far.jpg", "far.jpg", "Far World", "wrld_far", Hash(0xFF));
        await InsertUnknownAsync("/unknown/a.jpg", "a.jpg", Hash(0x00));

        await viewModel.InitializeAsync();
        var item = Assert.Single(viewModel.Items);

        Assert.False(viewModel.IsLoading);
        Assert.Equal("/unknown/a.jpg", item.TargetPhotoPath);
        Assert.Equal("/known/exact.jpg", item.MatchPhotoPath);
        Assert.Equal("Exact World", item.MatchWorldName);
        Assert.Equal("wrld_exact", item.MatchWorldId);
        Assert.Equal(0, item.MatchDistance);
        Assert.True(item.HasMatch);
    }

    /// <summary>
    /// InitializeAsync が候補探索の対象総数と処理済み件数を更新することを確認する。
    ///
    /// 大量のワールド不明写真を解析する場合、スピナーだけでは進行状況が判断できない。
    /// 解析完了後に処理済み件数が総数へ到達していることを固定し、進捗バーの入力値を保証する。
    /// </summary>
    [Fact]
    public async Task InitializeAsync_ReportsCandidateSearchProgress()
    {
        await InsertKnownAsync("/known/exact.jpg", "exact.jpg", "Exact World", "wrld_exact", Hash(0x00));
        await InsertUnknownAsync("/unknown/a.jpg", "a.jpg", Hash(0x00));
        await InsertUnknownAsync("/unknown/b.jpg", "b.jpg", Hash(0x00));

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsLoading);
        Assert.Equal(2, viewModel.Items.Count);
        Assert.Equal(2, viewModel.SearchProgressProcessed);
        Assert.Equal(2, viewModel.SearchProgressTotal);
        Assert.Equal(
            MessageCatalog.getMsg("WorldResolveViewModel.searchProgress", ("processed", 2), ("total", 2)),
            viewModel.SearchProgressText);
    }

    /// <summary>
    /// ApplyAll / SkipAll / ToggleApply が適用予定件数を更新し、ApplyConfirmedAsync が DB へ確定ワールドを書き込むことを確認する。
    ///
    /// 実際の画面ではユーザーが候補をまとめて選び、確認後に phash_confirmed として保存する。
    /// 適用予定フラグと ApplyCount がずれるとボタン活性や件数表示が誤るため、
    /// 一括選択、全解除、個別選択、確定保存の一連の状態遷移を検証する。
    /// </summary>
    [Fact]
    public async Task ApplySelectionAndConfirmedAsync_UpdateCountAndDatabase()
    {
        await InsertKnownAsync("/known/exact.jpg", "exact.jpg", "Exact World", "wrld_exact", Hash(0x00));
        await InsertUnknownAsync("/unknown/a.jpg", "a.jpg", Hash(0x00));
        await viewModel.InitializeAsync();
        var item = Assert.Single(viewModel.Items);

        viewModel.ApplyAll();
        Assert.Equal(1, viewModel.ApplyCount);
        viewModel.SkipAll();
        Assert.Equal(0, viewModel.ApplyCount);
        viewModel.ToggleApply(item);
        var applied = await viewModel.ApplyConfirmedAsync();
        var saved = await db.GetPhotoRecordAsync("/unknown/a.jpg", includePhash: true);

        Assert.Equal(1, viewModel.ApplyCount);
        Assert.Equal(1, applied);
        Assert.False(viewModel.IsApplying);
        Assert.NotNull(saved);
        Assert.Equal("Exact World", saved.world_name);
        Assert.Equal("wrld_exact", saved.world_id);
        Assert.Equal("phash_confirmed", saved.match_source);
    }

    /// <summary>
    /// OpenCandidatePickerAsync が候補を距離順に並べ、SelectCandidate が選択内容を対象 item へ反映して閉じることを確認する。
    ///
    /// 「別ワールドを選ぶ」操作では、閾値外の候補も距離順の参考情報として表示する。
    /// そのため CandidateList は近い候補から遠い候補まで全件を保持し、選択後は ActivePickerItem の
    /// Match 系プロパティを候補の内容で置き換え、ピッカー状態を閉じる必要がある。
    /// </summary>
    [Fact]
    public async Task CandidatePicker_RanksCandidatesAndSelectsChosenEntry()
    {
        await InsertKnownAsync("/known/exact.jpg", "exact.jpg", "Exact World", "wrld_exact", Hash(0x00));
        await InsertKnownAsync("/known/near.jpg", "near.jpg", "Near World", "wrld_near", SingleBitHash());
        await InsertKnownAsync("/known/far.jpg", "far.jpg", "Far World", "wrld_far", Hash(0xFF));
        await InsertUnknownAsync("/unknown/a.jpg", "a.jpg", Hash(0x00));
        await viewModel.InitializeAsync();
        var item = Assert.Single(viewModel.Items);

        await viewModel.OpenCandidatePickerAsync(item);
        var selected = Assert.Single(viewModel.CandidateList, entry => entry.WorldName == "Near World");
        viewModel.SelectCandidate(selected);

        Assert.False(viewModel.IsCandidateLoading);
        Assert.False(viewModel.IsCandidatePickerOpen);
        Assert.Null(viewModel.ActivePickerItem);
        Assert.Equal(["Exact World", "Near World", "Far World"], viewModel.CandidateList.Select(entry => entry.WorldName).ToArray());
        Assert.Equal("Near World", item.MatchWorldName);
        Assert.Equal("wrld_near", item.MatchWorldId);
        Assert.Equal("/known/near.jpg", item.MatchPhotoPath);
        Assert.Equal(1, item.MatchDistance);
    }

    /// <summary>
    /// 候補ピッカーを閉じる処理が、開閉状態・対象 item・ローディング状態をクリアすることを確認する。
    ///
    /// キャンセルや例外経路でも CloseCandidatePicker が呼ばれるため、
    /// 手動で開いた状態を作ってから閉じ、UI が次回オープン時に古い対象を参照しないことを検証する。
    /// </summary>
    [Fact]
    public void CloseCandidatePicker_ClearsPickerState()
    {
        var item = new WorldResolveItem("/unknown/a.jpg", "a.jpg", Hash(0x00), 1);
        viewModel.ActivePickerItem = item;
        viewModel.IsCandidatePickerOpen = true;
        viewModel.IsCandidateLoading = true;

        viewModel.CloseCandidatePicker();

        Assert.False(viewModel.IsCandidatePickerOpen);
        Assert.False(viewModel.IsCandidateLoading);
        Assert.Null(viewModel.ActivePickerItem);
    }

    /// <summary>
    /// 停止処理が候補サムネイル生成の終了まで待ち、遅延結果と停止後の新規生成を反映しないことを確認する。
    /// </summary>
    [Fact]
    public async Task StopGenerationAsync_IsIdempotentAndPreventsLateThumbnailUpdates()
    {
        var sourcePath = Path.Combine(tempDir, "candidate-source.png").Replace('\\', '/');
        await File.WriteAllBytesAsync(sourcePath.Replace('/', Path.DirectorySeparatorChar), [0x00]);
        var generationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generationCount = 0;
        var thumbnailService = new ThumbnailService(async (_, _, _, ct) =>
        {
            Interlocked.Increment(ref generationCount);
            generationStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                await finishCancellation.Task;
                throw;
            }
        });
        var localViewModel = new WorldResolveViewModel(
            db,
            new ThumbnailWorker(thumbnailService),
            new ToastService());
        var item = new WorldResolveItem("/unknown/a.jpg", "a.jpg", Hash(0x00), 1);
        var entry = new CandidateEntry(sourcePath, "candidate-source.png", "Candidate", "wrld_candidate", 1, 1);
        try
        {
            localViewModel.ActivePickerItem = item;
            localViewModel.IsCandidatePickerOpen = true;
            localViewModel.SelectCandidate(entry);
            await generationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var firstStop = localViewModel.StopGenerationAsync();
            var secondStop = localViewModel.StopGenerationAsync();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Same(firstStop, secondStop);
            Assert.False(firstStop.IsCompleted);
            Assert.Null(item.MatchThumbPath);

            finishCancellation.TrySetResult();
            await Task.WhenAll(firstStop, secondStop).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Null(item.MatchThumbPath);
            localViewModel.ActivePickerItem = item;
            localViewModel.SelectCandidate(entry);
            await localViewModel.InitializeAsync();
            Assert.Equal(1, generationCount);
            Assert.False(localViewModel.IsLoading);
        }
        finally
        {
            finishCancellation.TrySetResult();
            await localViewModel.StopGenerationAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// 既知ワールド写真を DB へ登録し、指定 PDQ ハッシュを設定する。
    /// WorldResolveViewModel は world_name と phash の両方がある写真を既知候補として扱う。
    /// </summary>
    private async Task InsertKnownAsync(string path, string filename, string worldName, string worldId, string phash)
    {
        await db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = path,
            PhotoFilename = filename,
            Timestamp = "2026-06-05 10:00:00",
            WorldName = worldName,
            WorldId = worldId,
            SourceSlot = 1,
        });
        await db.UpdatePhotoPhashAsync(path, phash);
    }

    /// <summary>
    /// ワールド未判定写真を DB へ登録し、指定 PDQ ハッシュを設定する。
    /// world_name を空にしておくことで、GetUnknownWorldPhotosWithPhashAsync の対象にする。
    /// </summary>
    private async Task InsertUnknownAsync(string path, string filename, string phash)
    {
        await db.UpsertPhotoAsync(new PhotoUpsertData
        {
            PhotoPath = path,
            PhotoFilename = filename,
            Timestamp = "2026-06-05 10:00:00",
            SourceSlot = 1,
        });
        await db.UpdatePhotoPhashAsync(path, phash);
    }

    /// <summary>
    /// 32バイトすべてを同じ値で埋めた PDQ 形式の hex 文字列を作る。
    /// 距離計算の期待値を読み取りやすくするため、固定パターンを使う。
    /// </summary>
    private static string Hash(byte value)
        => PdqHasher.ToHex(Enumerable.Repeat(value, 32).ToArray());

    /// <summary>
    /// target から 1 ビットだけ異なる PDQ 形式の hex 文字列を作る。
    /// 候補順位で「完全一致」と「遠距離」の間に距離1の候補を置くために使う。
    /// </summary>
    private static string SingleBitHash()
    {
        var bytes = new byte[32];
        bytes[0] = 0x01;
        return PdqHasher.ToHex(bytes);
    }
}
