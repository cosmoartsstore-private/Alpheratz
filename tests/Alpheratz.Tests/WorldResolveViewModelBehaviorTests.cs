using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging;
using Alpheratz.Core.Imaging.Pdq;
using Alpheratz.Features.WorldResolve;
using Alpheratz.Messages;
using Alpheratz.Services;
using Alpheratz.Shared.Services;
using Microsoft.Data.Sqlite;
using System.Text.Json;

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
            "2/2件",
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
    /// OpenCandidatePickerAsync が採用閾値内の候補を距離順に並べ、SelectCandidate が選択内容を対象 item へ反映して閉じることを確認する。
    ///
    /// 「別ワールドを選ぶ」操作では、一致率 71% 未満の候補を誤選択させない。
    /// CandidateList は採用可能な候補だけを保持し、選択後は ActivePickerItem の
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
        Assert.Equal(["Exact World", "Near World"], viewModel.CandidateList.Select(entry => entry.WorldName).ToArray());
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

    /// <summary>途中の保存失敗で既存の成功分と未保存分を区別し、同じ選択を再適用する。</summary>
    [Fact]
    public async Task ApplyConfirmedAsync_PartialWriteFailurePreservesSelectionAndRetries()
    {
        await InsertKnownAsync("/known/exact.jpg", "exact.jpg", "確定ワールド", "wrld_exact", Hash(0x00));
        await InsertUnknownAsync("/unknown/a.jpg", "a.jpg", Hash(0x00));
        await InsertUnknownAsync("/unknown/b.jpg", "b.jpg", Hash(0x00));
        await viewModel.InitializeAsync(); viewModel.ApplyAll();
        Assert.Equal(["/unknown/a.jpg", "/unknown/b.jpg"], viewModel.Items.Select(item => item.TargetPhotoPath).ToArray());
        var knownBefore = PhotoSnapshot("/known/exact.jpg"); var secondBefore = PhotoSnapshot("/unknown/b.jpg");
        var unrelatedColumns = PhotoSnapshot("/unknown/a.jpg", "photo_path, photo_filename, timestamp, phash, source_slot, is_favorite, orientation, image_width, image_height");
        ExecuteSql("CREATE TRIGGER owned_fail_second_world BEFORE UPDATE OF world_name ON photos WHEN NEW.photo_path = '/unknown/b.jpg' BEGIN SELECT RAISE(ABORT, 'owned second world failure'); END");
        try
        {
            var failure = await Assert.ThrowsAsync<SqliteException>(() => viewModel.ApplyConfirmedAsync());
            Assert.Contains("owned second world failure", failure.Message); Assert.False(viewModel.IsApplying);
            Assert.Equal(2, viewModel.ApplyCount); Assert.All(viewModel.Items, item => Assert.True(item.IsApplied));
            var savedFirst = await db.GetPhotoRecordAsync("/unknown/a.jpg", includePhash: true); Assert.NotNull(savedFirst);
            Assert.Equal("確定ワールド", savedFirst.world_name); Assert.Equal("wrld_exact", savedFirst.world_id); Assert.Equal("phash_confirmed", savedFirst.match_source);
            Assert.Equal(secondBefore, PhotoSnapshot("/unknown/b.jpg")); Assert.Equal(knownBefore, PhotoSnapshot("/known/exact.jpg"));
            Assert.Equal(unrelatedColumns, PhotoSnapshot("/unknown/a.jpg", "photo_path, photo_filename, timestamp, phash, source_slot, is_favorite, orientation, image_width, image_height"));
            Assert.Equal("ワールド名の保存を完了できませんでした。一部が保存されている場合があります。もう一度適用してください。", Assert.Single(toastService.toasts).Msg);
        }
        finally { ExecuteSql("DROP TRIGGER owned_fail_second_world"); }

        Assert.Equal(2, await viewModel.ApplyConfirmedAsync()); Assert.False(viewModel.IsApplying);
        foreach (var path in new[] { "/unknown/a.jpg", "/unknown/b.jpg" })
        {
            var saved = await db.GetPhotoRecordAsync(path, includePhash: true); Assert.NotNull(saved);
            Assert.Equal("確定ワールド", saved.world_name); Assert.Equal("wrld_exact", saved.world_id); Assert.Equal("phash_confirmed", saved.match_source); Assert.Equal(Hash(0x00), saved.phash);
        }
        Assert.Equal(knownBefore, PhotoSnapshot("/known/exact.jpg")); Assert.Single(toastService.toasts);
    }

    /// <summary>開始前の取消しは保存せず、同じ選択の後続保存を妨げない。</summary>
    [Fact]
    public async Task ApplyConfirmedAsync_PreCancelledTokenPreservesDataAndAllowsRetry()
    {
        await InsertKnownAsync("/known/exact.jpg", "exact.jpg", "確定ワールド", "wrld_exact", Hash(0x00));
        await InsertUnknownAsync("/unknown/a.jpg", "a.jpg", Hash(0x00)); await viewModel.InitializeAsync(); viewModel.ApplyAll();
        var before = PhotoSnapshot("/unknown/a.jpg"); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => viewModel.ApplyConfirmedAsync(cancellation.Token));
        Assert.Equal(before, PhotoSnapshot("/unknown/a.jpg")); Assert.False(viewModel.IsApplying); Assert.Equal(1, viewModel.ApplyCount); Assert.True(Assert.Single(viewModel.Items).IsApplied);
        Assert.Equal("ワールド名の保存を完了できませんでした。一部が保存されている場合があります。もう一度適用してください。", Assert.Single(toastService.toasts).Msg);
        Assert.Equal(1, await viewModel.ApplyConfirmedAsync()); var saved = await db.GetPhotoRecordAsync("/unknown/a.jpg", includePhash: true); Assert.NotNull(saved);
        Assert.Equal("確定ワールド", saved.world_name); Assert.Equal("phash_confirmed", saved.match_source); Assert.False(viewModel.IsApplying);
    }

    /// <summary>候補の実DB読込失敗でpickerを閉じ、同じDBの復旧後に候補を再取得する。</summary>
    [Fact]
    public async Task OpenCandidatePickerAsync_DatabaseReadFailureClosesAndRecovers()
    {
        await InsertKnownAsync("/known/exact.jpg", "exact.jpg", "確定ワールド", "wrld_exact", Hash(0x00));
        var before = PhotoSnapshot("/known/exact.jpg"); var item = new WorldResolveItem("/unknown/a.jpg", "a.jpg", Hash(0x00), 1);
        ExecuteSql("ALTER TABLE photos RENAME TO owned_unavailable_photos");
        try
        {
            await viewModel.OpenCandidatePickerAsync(item); Assert.False(viewModel.IsCandidateLoading); Assert.False(viewModel.IsCandidatePickerOpen); Assert.Null(viewModel.ActivePickerItem); Assert.Empty(viewModel.CandidateList);
            Assert.Equal("候補写真を読み込めませんでした。もう一度候補を開いてください。", Assert.Single(toastService.toasts).Msg);
        }
        finally { ExecuteSql("ALTER TABLE owned_unavailable_photos RENAME TO photos"); }

        Assert.Equal(before, PhotoSnapshot("/known/exact.jpg")); await viewModel.OpenCandidatePickerAsync(item);
        Assert.True(viewModel.IsCandidatePickerOpen); Assert.False(viewModel.IsCandidateLoading); Assert.Same(item, viewModel.ActivePickerItem);
        var candidate = Assert.Single(viewModel.CandidateList); Assert.Equal("確定ワールド", candidate.WorldName); Assert.Equal("wrld_exact", candidate.WorldId); Assert.Equal(0, candidate.Distance); Assert.Single(toastService.toasts);
        viewModel.CloseCandidatePicker(); Assert.Null(viewModel.ActivePickerItem);
    }

    /// <summary>実DBの書込待機中の再entryを拒否し、待機解除後に一度だけ保存する。</summary>
    [Fact]
    public async Task ApplyConfirmedAsync_ConcurrentEntryDoesNotDuplicateInFlightSave()
    {
        await InsertKnownAsync("/known/exact.jpg", "exact.jpg", "確定ワールド", "wrld_exact", Hash(0x00));
        await InsertUnknownAsync("/unknown/a.jpg", "a.jpg", Hash(0x00)); await viewModel.InitializeAsync(); viewModel.ApplyAll();
        using var blocker = new SqliteConnection($"Data Source={Path.Combine(tempDir, "Alpheratz.db")};Pooling=False"); blocker.Open(); using var transaction = blocker.BeginTransaction(deferred: false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveApplying(object? sender, System.ComponentModel.PropertyChangedEventArgs args) { if (args.PropertyName == nameof(viewModel.IsApplying) && viewModel.IsApplying) entered.TrySetResult(); }
        viewModel.PropertyChanged += ObserveApplying; var first = Task.Run(() => viewModel.ApplyConfirmedAsync());
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(first.IsCompleted);
            Assert.Equal(0, await viewModel.ApplyConfirmedAsync().WaitAsync(TimeSpan.FromSeconds(5))); Assert.True(viewModel.IsApplying);
            Assert.Equal(1, viewModel.ApplyCount); Assert.True(Assert.Single(viewModel.Items).IsApplied);
        }
        finally { viewModel.PropertyChanged -= ObserveApplying; transaction.Commit(); }
        Assert.Equal(1, await first.WaitAsync(TimeSpan.FromSeconds(5))); Assert.False(viewModel.IsApplying); Assert.Empty(toastService.toasts);
        var saved = await db.GetPhotoRecordAsync("/unknown/a.jpg", includePhash: true); Assert.NotNull(saved); Assert.Equal("確定ワールド", saved.world_name); Assert.Equal("phash_confirmed", saved.match_source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializeAsync_ReadFailureOrCancellationPreservesDataAndAllowsRetry(bool cancel)
    {
        await InsertKnownAsync("/known/exact.jpg", "exact.jpg", "日本語ワールド", "wrld_exact", Hash(0x00));
        await InsertUnknownAsync("/unknown/a.jpg", "a.jpg", Hash(0x00));
        var knownBefore = PhotoSnapshot("/known/exact.jpg"); var unknownBefore = PhotoSnapshot("/unknown/a.jpg");
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.Cancel(); else ExecuteSql("ALTER TABLE photos RENAME TO owned_unavailable_photos;");
        try
        {
            await viewModel.InitializeAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(viewModel.IsLoading); Assert.Empty(viewModel.Items);
            Assert.Equal(0, viewModel.SearchProgressProcessed); Assert.Equal(0, viewModel.SearchProgressTotal);
            Assert.Equal("対象の写真を確認しています…", viewModel.SearchProgressText);
            if (cancel) Assert.Empty(toastService.toasts);
            else Assert.Equal("ワールド名補完を開始できませんでした。画面を閉じてから、もう一度実行してください。", Assert.Single(toastService.toasts).Msg);
        }
        finally { if (!cancel) ExecuteSql("ALTER TABLE owned_unavailable_photos RENAME TO photos;"); }

        Assert.Equal(knownBefore, PhotoSnapshot("/known/exact.jpg")); Assert.Equal(unknownBefore, PhotoSnapshot("/unknown/a.jpg"));
        await viewModel.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var item = Assert.Single(viewModel.Items); Assert.False(viewModel.IsLoading);
        Assert.Equal("/unknown/a.jpg", item.TargetPhotoPath); Assert.Equal("日本語ワールド", item.MatchWorldName);
        Assert.Equal("wrld_exact", item.MatchWorldId); Assert.Equal(0, item.MatchDistance);
        Assert.Equal(1, viewModel.SearchProgressProcessed); Assert.Equal(1, viewModel.SearchProgressTotal);
        Assert.Equal(cancel ? 0 : 1, toastService.toasts.Count);
        Assert.Equal(knownBefore, PhotoSnapshot("/known/exact.jpg")); Assert.Equal(unknownBefore, PhotoSnapshot("/unknown/a.jpg"));
    }

    [Fact]
    public async Task InitializeAsync_EmptyDataLeavesLoadingStateAndCanFindNewPhotosOnRetry()
    {
        await viewModel.InitializeAsync();
        Assert.False(viewModel.IsLoading); Assert.Empty(viewModel.Items);
        Assert.Equal(0, viewModel.SearchProgressProcessed); Assert.Equal(0, viewModel.SearchProgressTotal); Assert.Empty(toastService.toasts);
        await InsertUnknownAsync("/unknown/no-candidate.jpg", "no-candidate.jpg", Hash(0x00));
        var before = PhotoSnapshot("/unknown/no-candidate.jpg"); await viewModel.InitializeAsync();
        var item = Assert.Single(viewModel.Items); Assert.False(viewModel.IsLoading);
        Assert.Equal("/unknown/no-candidate.jpg", item.TargetPhotoPath); Assert.False(item.HasMatch); Assert.False(item.CanApply);
        viewModel.ApplyAll(); viewModel.ToggleApply(item);
        Assert.False(item.IsApplied); Assert.Equal(0, viewModel.ApplyCount); Assert.Equal(0, await viewModel.ApplyConfirmedAsync());
        Assert.False(viewModel.IsApplying); Assert.Equal(before, PhotoSnapshot("/unknown/no-candidate.jpg")); Assert.Empty(toastService.toasts);
    }

    [Fact]
    public async Task OpenCandidatePickerAsync_PreCancelledTokenClosesWithoutNoticeAndAllowsRetry()
    {
        await InsertKnownAsync("/known/exact.jpg", "exact.jpg", "日本語ワールド", "wrld_exact", Hash(0x00));
        await InsertUnknownAsync("/unknown/a.jpg", "a.jpg", Hash(0x00)); var before = PhotoSnapshot("/unknown/a.jpg");
        var item = new WorldResolveItem("/unknown/a.jpg", "a.jpg", Hash(0x00), 1);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await viewModel.OpenCandidatePickerAsync(item, cancellation.Token);
        Assert.False(viewModel.IsCandidateLoading); Assert.False(viewModel.IsCandidatePickerOpen); Assert.Null(viewModel.ActivePickerItem);
        Assert.Empty(viewModel.CandidateList); Assert.Empty(toastService.toasts); Assert.Equal(before, PhotoSnapshot("/unknown/a.jpg"));
        await viewModel.OpenCandidatePickerAsync(item);
        Assert.True(viewModel.IsCandidatePickerOpen); Assert.False(viewModel.IsCandidateLoading); Assert.Same(item, viewModel.ActivePickerItem);
        var candidate = Assert.Single(viewModel.CandidateList); Assert.Equal("日本語ワールド", candidate.WorldName);
        Assert.Equal("wrld_exact", candidate.WorldId); Assert.Equal(0, candidate.Distance);
        Assert.Empty(toastService.toasts); Assert.Equal(before, PhotoSnapshot("/unknown/a.jpg"));
    }

    [Fact]
    public async Task ManualWorldSelectionTrimsInputPreservesOtherDataAndCanReturnToAutomaticCandidate()
    {
        await InsertKnownAsync("/known/exact.jpg", "exact.jpg", "自動候補", "wrld_exact", Hash(0x00));
        await InsertUnknownAsync("/unknown/a.jpg", "a.jpg", Hash(0x00));
        const string unrelatedColumns = "photo_filename, photo_path, timestamp, phash, orientation, image_width, image_height, source_slot, is_favorite, is_missing";
        var knownBefore = PhotoSnapshot("/known/exact.jpg"); var unrelatedBefore = PhotoSnapshot("/unknown/a.jpg", unrelatedColumns);
        Assert.False(viewModel.SelectManualWorldName("対象なし")); await viewModel.InitializeAsync();
        var item = Assert.Single(viewModel.Items); await viewModel.OpenCandidatePickerAsync(item);
        Assert.False(viewModel.SelectManualWorldName(" \t\r\n ")); Assert.True(viewModel.IsCandidatePickerOpen);
        Assert.Same(item, viewModel.ActivePickerItem); Assert.False(item.IsManualMatch); Assert.Equal("自動候補", item.MatchWorldName);
        Assert.True(viewModel.SelectManualWorldName("  手動の日本語ワールド  "));
        Assert.False(viewModel.IsCandidatePickerOpen); Assert.Null(viewModel.ActivePickerItem);
        Assert.True(item.IsManualMatch); Assert.True(item.CanApply); Assert.True(item.HasMatch); Assert.False(item.HasMatchPhoto);
        Assert.Null(item.MatchPhotoPath); Assert.Null(item.MatchPhotoFilename); Assert.Null(item.MatchThumbPath);
        Assert.Null(item.MatchWorldId); Assert.Null(item.MatchDistance); Assert.Equal("手動の日本語ワールド", item.MatchWorldName);
        viewModel.ToggleApply(item); Assert.Equal(1, viewModel.ApplyCount); Assert.Equal(1, await viewModel.ApplyConfirmedAsync());
        var manual = await db.GetPhotoRecordAsync("/unknown/a.jpg", includePhash: true); Assert.NotNull(manual);
        Assert.Equal("手動の日本語ワールド", manual.world_name); Assert.Null(manual.world_id); Assert.Equal("manual", manual.match_source);
        Assert.Equal(unrelatedBefore, PhotoSnapshot("/unknown/a.jpg", unrelatedColumns));
        await viewModel.OpenCandidatePickerAsync(item); viewModel.SelectCandidate(Assert.Single(viewModel.CandidateList));
        Assert.False(item.IsManualMatch); Assert.True(item.HasMatchPhoto); Assert.Equal("自動候補", item.MatchWorldName);
        Assert.Equal("wrld_exact", item.MatchWorldId); Assert.Equal(0, item.MatchDistance); Assert.Equal(1, await viewModel.ApplyConfirmedAsync());
        var automatic = await db.GetPhotoRecordAsync("/unknown/a.jpg", includePhash: true); Assert.NotNull(automatic);
        Assert.Equal("自動候補", automatic.world_name); Assert.Equal("wrld_exact", automatic.world_id); Assert.Equal("phash_confirmed", automatic.match_source);
        Assert.Equal(unrelatedBefore, PhotoSnapshot("/unknown/a.jpg", unrelatedColumns)); Assert.Equal(knownBefore, PhotoSnapshot("/known/exact.jpg"));
        Assert.False(viewModel.IsApplying); Assert.Empty(toastService.toasts);
    }

    /// <summary>実画像の初期候補・候補変更のthumbnailと、確定後のDB再利用まで画面なしで接続する。</summary>
    [Fact]
    public async Task RealImagesDeliverTargetAndChangedCandidateThumbnailsBeforeConfirmedWorldReopens()
    {
        var target = Alpheratz.Core.AppPaths.NormalizePathForDb(Path.Combine(tempDir, "対象.png"));
        var first = Alpheratz.Core.AppPaths.NormalizePathForDb(Path.Combine(tempDir, "候補A.png"));
        var second = Alpheratz.Core.AppPaths.NormalizePathForDb(Path.Combine(tempDir, "候補B.png"));
        foreach (var path in new[] { target, first, second })
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var stream = file.AsRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            var pixels = Enumerable.Range(0, 32 * 48).SelectMany(_ => new byte[] { 30, 80, 160, 255 }).ToArray();
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, 32, 48, 96, 96, pixels); await encoder.FlushAsync();
        }
        var bytes = new[] { target, first, second }.ToDictionary(path => path, File.ReadAllBytes);
        await InsertKnownAsync(first, "候補A.png", "Aのワールド", "wrld_a", Hash(0));
        await InsertKnownAsync(second, "候補B.png", "Bのワールド", "wrld_b", SingleBitHash());
        await InsertUnknownAsync(target, "対象.png", Hash(0));
        await viewModel.InitializeAsync(); var item = Assert.Single(viewModel.Items);
        await WaitForThumbnails(item, () => item.TargetThumbPath is not null && item.MatchThumbPath is not null);
        Assert.Equal(first, item.MatchPhotoPath); var firstThumb = item.MatchThumbPath;
        await AssertDecodedThumbnail(item.TargetThumbPath!); await AssertDecodedThumbnail(firstThumb!);
        await viewModel.OpenCandidatePickerAsync(item); var selected = Assert.Single(viewModel.CandidateList, candidate => candidate.PhotoPath == second);
        viewModel.SelectCandidate(selected);
        await WaitForThumbnails(item, () => item.MatchThumbPath is not null && item.MatchThumbPath != firstThumb);
        Assert.Equal(second, item.MatchPhotoPath); Assert.Equal("Bのワールド", item.MatchWorldName); await AssertDecodedThumbnail(item.MatchThumbPath!);
        Assert.False(viewModel.IsCandidatePickerOpen); viewModel.ToggleApply(item); Assert.Equal(1, await viewModel.ApplyConfirmedAsync());
        var reopened = await new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db")).GetPhotoRecordAsync(target);
        Assert.NotNull(reopened); Assert.Equal("Bのワールド", reopened.world_name); Assert.Equal("wrld_b", reopened.world_id); Assert.Equal("phash_confirmed", reopened.match_source);
        foreach (var (path, original) in bytes) Assert.Equal(original, File.ReadAllBytes(path));
        await viewModel.StopGenerationAsync(); Assert.Empty(toastService.toasts);
    }

    private static async Task WaitForThumbnails(WorldResolveItem item, Func<bool> ready)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) { if (ready()) completed.TrySetResult(); }
        item.PropertyChanged += Changed;
        try { if (ready()) completed.TrySetResult(); await completed.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { item.PropertyChanged -= Changed; }
    }

    private static async Task AssertDecodedThumbnail(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); using var stream = file.AsRandomAccessStream();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream); Assert.True(decoder.PixelWidth > 0); Assert.True(decoder.PixelHeight > 0);
        Assert.NotEmpty((await decoder.GetPixelDataAsync()).DetachPixelData());
    }

    private void ExecuteSql(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(tempDir, "Alpheratz.db")};Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    private string PhotoSnapshot(string path, string columns = "*")
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(tempDir, "Alpheratz.db")};Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = $"SELECT {columns} FROM photos WHERE photo_path = $path"; command.Parameters.AddWithValue("$path", path);
        using var reader = command.ExecuteReader(); Assert.True(reader.Read()); var values = new object[reader.FieldCount]; reader.GetValues(values); Assert.False(reader.Read()); return JsonSerializer.Serialize(values);
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
