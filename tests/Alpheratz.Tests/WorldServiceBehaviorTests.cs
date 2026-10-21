using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging.Pdq;
using Alpheratz.Core;
using Alpheratz.Core.Scanner;
using Alpheratz.Services;

namespace Alpheratz.Tests;

/// <summary>
/// WorldService の類似写真マッチング判定を検証するテスト。
///
/// WorldService は OS 起動やクリップボード操作も持つが、ワールド推定の中核は
/// PDQ ハッシュの距離計算と候補順位付けである。
/// ここでは外部プロセスや UI を呼ばず、内部公開された純粋な判定関数を直接確認する。
/// </summary>
public sealed class WorldServiceBehaviorTests
{
    /// <summary>
    /// 閾値内で最も距離が短い既知ワールドだけが採用されることを確認する。
    ///
    /// ResolveUnknownWorldsFromSimilarPhotosAsync は未知写真ごとに KnownWorldRow を総当たりし、
    /// WorldMatchDistanceThreshold 以下で最短の候補を採用する。
    /// このテストでは完全一致、少し離れた候補、閾値外候補を並べ、
    /// 完全一致が最短として返ることを検証する。
    /// </summary>
    [Fact]
    public void FindBestMatchWithDetails_ReturnsNearestCandidateWithinThreshold()
    {
        var target = Hash(0x00);
        var candidates = new[]
        {
            Row("far", "Far", Hash(0xFF)),
            Row("near", "Near", SingleBitHash()),
            Row("exact", "Exact", Hash(0x00)),
        };

        var result = WorldService.FindBestMatchWithDetails(target, candidates);

        Assert.NotNull(result);
        Assert.Equal("Exact", result.Value.Row.WorldName);
        Assert.Equal(0, result.Value.Distance);
    }

    /// <summary>
    /// 閾値を超える候補しかない場合はマッチなしになることを確認する。
    ///
    /// PDQ は偶然でも半数程度のビットが一致するため、距離が大きい候補を採用すると
    /// 別ワールドの写真に誤った world_name が書き込まれる。
    /// 閾値外候補を null とする現在仕様を、完全反転ハッシュで固定する。
    /// </summary>
    [Fact]
    public void FindBestMatchWithDetails_IgnoresCandidatesOutsideThreshold()
    {
        var target = Hash(0x00);
        var candidates = new[]
        {
            Row("far", "Far", Hash(0xFF)),
        };

        var result = WorldService.FindBestMatchWithDetails(target, candidates);

        Assert.Null(result);
    }

    /// <summary>
    /// 候補順位付けが閾値で除外せず、距離昇順で全候補を返すことを確認する。
    ///
    /// UI の類似ワールド候補表示では、閾値内採用とは別に候補の相対距離を見せる必要がある。
    /// RankCandidatesByDistance は距離が計算できる候補をすべて並べる仕様なので、
    /// 閾値外の遠い候補も末尾に残ることを検証する。
    /// </summary>
    [Fact]
    public void RankCandidatesByDistance_ReturnsAllComparableCandidatesInAscendingDistance()
    {
        var target = Hash(0x00);
        var candidates = new[]
        {
            Row("far", "Far", Hash(0xFF)),
            Row("near", "Near", SingleBitHash()),
            Row("exact", "Exact", Hash(0x00)),
        };

        var ranked = WorldService.RankCandidatesByDistance(target, candidates);

        Assert.Equal(["Exact", "Near", "Far"], ranked.Select(item => item.Row.WorldName).ToArray());
        Assert.Equal([0, 1, 256], ranked.Select(item => item.Distance).ToArray());
    }

    /// <summary>
    /// 準備済み候補でも通常候補と同じ距離順になり、不正な候補ハッシュが除外されることを確認する。
    ///
    /// WorldResolve UI は大量の未知写真へ同じ既知候補を繰り返し当てるため、
    /// 候補側の PDQ 文字列を先にパースして使い回す。この最適化で順位仕様が変わらないことを固定する。
    /// </summary>
    [Fact]
    public void PreparedCandidates_ReturnSameRankingAndSkipInvalidHashes()
    {
        var target = Hash(0x00);
        var candidates = new[]
        {
            Row("invalid", "Invalid", "not-a-hash"),
            Row("near", "Near", SingleBitHash()),
            Row("exact", "Exact", Hash(0x00)),
        };

        var prepared = WorldService.PrepareKnownWorldRows(candidates);
        var ranked = WorldService.RankCandidatesByDistance(target, prepared);
        var best = WorldService.FindBestMatchWithDetails(target, prepared);

        Assert.Equal(["Exact", "Near"], prepared.Select(item => item.Row.WorldName).Order().ToArray());
        Assert.Equal(["Exact", "Near"], ranked.Select(item => item.Row.WorldName).ToArray());
        Assert.NotNull(best);
        Assert.Equal("Exact", best.Value.Row.WorldName);
    }

    /// <summary>
    /// target 側のハッシュが空または不正な場合、順位付けが空になることを確認する。
    ///
    /// DB には移行途中や破損データとして空の phash が残り得る。
    /// その場合に例外を投げると一括解決処理全体が止まるため、
    /// ParseHashVariants が空を返す入力は候補なしとして処理される。
    /// </summary>
    [Fact]
    public void RankCandidatesByDistance_ReturnsEmptyWhenTargetHashIsInvalid()
    {
        var ranked = WorldService.RankCandidatesByDistance("not-a-hash", [Row("near", "Near", Hash(0x00))]);

        Assert.Empty(ranked);
    }

    /// <summary>直列と並列の切替え前後で、全候補の識別情報と既知のビット距離を保持する。</summary>
    [Theory]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    public void RankCandidatesByDistance_PreservesEveryCandidateAcrossParallelBoundary(int candidateCount)
    {
        var candidates = Enumerable.Range(0, candidateCount)
            .Select(index => Row($"candidate_{index}", $"World {index}", HashWithBitCount(index * 37 % 257)))
            .Prepend(Row("invalid", "Invalid", "not-a-hash"))
            .ToArray();
        var originalHashes = candidates.Select(row => row.Phash).ToArray();
        var expectedByPath = candidates.Skip(1).Select((row, index) => (row.PhotoPath, Distance: index * 37 % 257))
            .ToDictionary(item => item.PhotoPath, item => item.Distance);
        var expectedRowsByPath = candidates.Skip(1).ToDictionary(row => row.PhotoPath, row => row with { Phash = string.Empty });
        var expectedDistances = expectedByPath.Values.Order().ToArray();
        var prepared = WorldService.PrepareKnownWorldRows(candidates);

        Assert.Equal(candidateCount, prepared.Count);
        Assert.Equal(candidates.Skip(1).Select(row => row with { Phash = string.Empty }), prepared.Select(item => item.Row));
        foreach (var ranked in new[]
        {
            WorldService.RankCandidatesByDistance(Hash(0x00), candidates),
            WorldService.RankCandidatesByDistance(Hash(0x00), prepared),
        })
        {
            Assert.Equal(expectedDistances, ranked.Select(item => item.Distance).ToArray());
            Assert.Equal(expectedByPath.Keys.Order(), ranked.Select(item => item.Row.PhotoPath).Order());
            Assert.All(ranked, item =>
            {
                Assert.Equal(expectedByPath[item.Row.PhotoPath], item.Distance);
                Assert.Equal(expectedRowsByPath[item.Row.PhotoPath], item.Row);
            });
        }
        Assert.Equal(originalHashes, candidates.Select(row => row.Phash).ToArray());
    }

    /// <summary>採用距離75と拒否距離76を区別し、同距離では入力順の最初の候補を採用する。</summary>
    [Fact]
    public void FindBestMatchWithDetails_PreservesThresholdAndFirstEqualCandidate()
    {
        var first = Row("first", "First", HashWithBitCount(75));
        var second = Row("second", "Second", HashWithBitCount(75));
        var outside = Row("outside", "Outside", HashWithBitCount(76));
        var target = Hash(0x00);

        var match = WorldService.FindBestMatchWithDetails(target, new[] { outside, first, second });
        var reversed = WorldService.FindBestMatchWithDetails(target, new[] { second, first, outside });

        Assert.NotNull(match);
        Assert.NotNull(reversed);
        Assert.Equal(first with { Phash = string.Empty }, match.Value.Row);
        Assert.Equal(second with { Phash = string.Empty }, reversed.Value.Row);
        Assert.Equal(75, match.Value.Distance);
        Assert.Equal(75, reversed.Value.Distance);
        Assert.Null(WorldService.FindBestMatchWithDetails(target, new[] { outside }));
        Assert.Null(WorldService.FindBestMatchWithDetails("not-a-hash", new[] { first }));
        Assert.Null(WorldService.FindBestMatchWithDetails(target, Array.Empty<AlpheratzDb.KnownWorldRow>()));
        Assert.True(WorldService.CanAutomaticallyComplete(75));
        Assert.False(WorldService.CanAutomaticallyComplete(76));
    }

    /// <summary>候補準備と両overloadは、開始前の取消しtokenを保持して失敗し、同入力を再利用できる。</summary>
    [Fact]
    public void CandidateRanking_PreCanceledTokenPreservedAndSameInputCanRetry()
    {
        var candidates = new[] { Row("exact", "Exact", Hash(0x00)) };
        var prepared = WorldService.PrepareKnownWorldRows(candidates);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Action[] canceledCalls =
        [
            () => WorldService.PrepareKnownWorldRows(candidates, cancellation.Token),
            () => WorldService.FindBestMatchWithDetails(Hash(0x00), candidates, cancellation.Token),
            () => WorldService.FindBestMatchWithDetails(Hash(0x00), prepared, cancellation.Token),
            () => WorldService.RankCandidatesByDistance(Hash(0x00), candidates, cancellation.Token),
            () => WorldService.RankCandidatesByDistance(Hash(0x00), prepared, cancellation.Token),
        ];
        foreach (var call in canceledCalls)
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(call).CancellationToken);

        Assert.Equal(candidates[0].PhotoPath, Assert.Single(WorldService.RankCandidatesByDistance(Hash(0x00), prepared)).Row.PhotoPath);
        Assert.Equal(0, WorldService.FindBestMatchWithDetails(Hash(0x00), candidates)!.Value.Distance);
        Assert.Equal(Hash(0x00), candidates[0].Phash);
    }

    /// <summary>
    /// ResolveUnknownWorldsFromSimilarPhotosAsync が DB 上の未知写真へ最短距離の既知ワールドを反映することを確認する。
    ///
    /// 純粋な距離関数だけでなく、実際のサービスは DB から source_slot ごとに候補を取り、
    /// match_source="phash" として未知写真を更新する。
    /// ここでは完全一致する既知写真を1件置き、未知写真の world_name/world_id が保存されることを検証する。
    /// </summary>
    [Fact]
    public async Task ResolveUnknownWorldsFromSimilarPhotosAsync_UpdatesUnknownPhotosInDatabase()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.WorldService.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var db = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
            db.Initialize();
            var config = new AppConfig(Path.Combine(tempDir, "settings"));
            var bus = new LocalEventBus();
            var scanner = new PhotoScanner(config, db, bus);
            var service = new WorldService(db, scanner);
            await db.UpsertPhotoAsync(new PhotoUpsertData
            {
                PhotoPath = "/known/exact.jpg",
                PhotoFilename = "exact.jpg",
                Timestamp = "2026-06-05 10:00:00",
                WorldName = "Exact World",
                WorldId = "wrld_exact",
                SourceSlot = 1,
            });
            await db.UpdatePhotoPhashAsync("/known/exact.jpg", Hash(0x00));
            await db.UpsertPhotoAsync(new PhotoUpsertData
            {
                PhotoPath = "/unknown/a.jpg",
                PhotoFilename = "a.jpg",
                Timestamp = "2026-06-05 10:05:00",
                SourceSlot = 1,
            });
            await db.UpdatePhotoPhashAsync("/unknown/a.jpg", Hash(0x00));

            var resolved = await service.ResolveUnknownWorldsFromSimilarPhotosAsync("all");
            var saved = await db.GetPhotoRecordAsync("/unknown/a.jpg", includePhash: true);

            Assert.Equal(1, resolved);
            Assert.NotNull(saved);
            Assert.Equal("Exact World", saved.world_name);
            Assert.Equal("wrld_exact", saved.world_id);
            Assert.Equal("phash", saved.match_source);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public async Task ResolveUnknownWorldsFromSimilarPhotosAsync_FiltersByTargetSlot()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.WorldService.Target.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var db = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
            db.Initialize();
            var scanner = new PhotoScanner(new AppConfig(Path.Combine(tempDir, "settings")), db, new LocalEventBus());
            var service = new WorldService(db, scanner);

            await db.UpsertPhotoAsync(new PhotoUpsertData
            {
                PhotoPath = "/slot1/known.jpg",
                PhotoFilename = "known.jpg",
                Timestamp = "2026-06-05 10:00:00",
                WorldName = "Primary World",
                WorldId = "wrld_primary",
                SourceSlot = 1,
            });
            await db.UpsertPhotoAsync(new PhotoUpsertData
            {
                PhotoPath = "/slot2/known.jpg",
                PhotoFilename = "known.jpg",
                Timestamp = "2026-06-05 10:00:00",
                WorldName = "Secondary World",
                WorldId = "wrld_secondary",
                SourceSlot = 2,
            });
            await db.UpsertPhotoAsync(new PhotoUpsertData
            {
                PhotoPath = "/slot1/unknown.jpg",
                PhotoFilename = "unknown.jpg",
                Timestamp = "2026-06-05 10:05:00",
                SourceSlot = 1,
            });
            await db.UpsertPhotoAsync(new PhotoUpsertData
            {
                PhotoPath = "/slot2/unknown.jpg",
                PhotoFilename = "unknown.jpg",
                Timestamp = "2026-06-05 10:05:00",
                SourceSlot = 2,
            });
            foreach (var path in new[] { "/slot1/known.jpg", "/slot2/known.jpg", "/slot1/unknown.jpg", "/slot2/unknown.jpg" })
                await db.UpdatePhotoPhashAsync(path, Hash(0x00));

            var resolved = await service.ResolveUnknownWorldsFromSimilarPhotosAsync("primary");
            var primary = await db.GetPhotoRecordAsync("/slot1/unknown.jpg", includePhash: true);
            var secondary = await db.GetPhotoRecordAsync("/slot2/unknown.jpg", includePhash: true);

            Assert.Equal(1, resolved);
            Assert.Equal("Primary World", primary?.world_name);
            Assert.Equal("wrld_primary", primary?.world_id);
            Assert.Null(secondary?.world_name);
            Assert.Null(secondary?.world_id);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { }
        }
    }

    /// <summary>
    /// VRChat ワールド URL の組み立てが `wrld_` 形式の ID だけを受け付けることを確認する。
    ///
    /// 実際の OpenWorldUrlAsync は OS の Launcher を呼ぶため、URL 生成と入力検証を分離して直接検証する。
    /// 不正 ID を起動前に止めることで、ユーザーのローカル環境に副作用を出さずに失敗を扱える。
    /// </summary>
    [Fact]
    public void BuildWorldUri_AcceptsOnlyVrchatWorldIds()
    {
        var uri = WorldService.BuildWorldUri("wrld_alpha");

        Assert.Equal("https://vrchat.com/home/world/wrld_alpha/info", uri.ToString());
        foreach (var worldId in new[] { "wrld_0", "wrld_a-0_B", "wrld_alpha-beta" })
            Assert.Equal($"https://vrchat.com/home/world/{worldId}/info", WorldService.BuildWorldUri(worldId).AbsoluteUri);
        Assert.Throws<ArgumentException>(() => WorldService.BuildWorldUri("not-a-world-id"));
        Assert.Throws<ArgumentException>(() => WorldService.BuildWorldUri("wrld_"));
        Assert.Throws<ArgumentException>(() => WorldService.BuildWorldUri("wrld_alpha/info"));
        Assert.Throws<ArgumentException>(() => WorldService.BuildWorldUri("wrld_alpha beta"));
        Assert.Throws<ArgumentException>(() => WorldService.BuildWorldUri(""));
        Assert.Throws<ArgumentException>(() => WorldService.BuildWorldUri(null!));
        Assert.Throws<ArgumentException>(() => WorldService.BuildWorldUri("   "));
        foreach (var worldId in new[] { "WRLD_alpha", "wrld_日本語", "wrld_α", "wrld_a?query", "wrld_a#fragment", "wrld_a%2Finfo", "wrld_a\\info", "wrld_a\n" })
            Assert.Throws<ArgumentException>(() => WorldService.BuildWorldUri(worldId));

        var exception = Assert.Throws<ArgumentException>(() => WorldService.BuildWorldUri("wrld_alpha/beta"));
        Assert.DoesNotContain("wrld_alpha", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tweet Intent URL が Twitter/X の投稿 intent だけを通すことを確認する。
    ///
    /// 投稿本文は事前に URL エンコードして渡す設計であるため、ここでは許可済み URL の境界を固定する。
    /// 外部ホストや別用途の URL は Launcher へ渡さず、呼出側のエラー通知へ戻す。
    /// </summary>
    [Fact]
    public void BuildTweetIntentUri_AllowsOnlyTwitterAndXIntentUrls()
    {
        Assert.Equal(
            "https://twitter.com/intent/tweet?text=hello%20world",
            WorldService.BuildTweetIntentUri("https://twitter.com/intent/tweet?text=hello%20world").AbsoluteUri);
        Assert.Equal(
            "https://x.com/intent/tweet?text=hello%20world",
            WorldService.BuildTweetIntentUri("https://x.com/intent/tweet?text=hello%20world").AbsoluteUri);

        // query本文内の符号化文字と追加parameterの区切りを区別する。
        Assert.Equal("https://twitter.com/intent/tweet?text=%E6%97%A5%E6%9C%AC%E8%AA%9E%26%3B",
            WorldService.BuildTweetIntentUri("https://twitter.com/intent/tweet?text=%E6%97%A5%E6%9C%AC%E8%AA%9E%26%3B").AbsoluteUri);
        Assert.Equal("https://x.com/intent/tweet?text=", WorldService.BuildTweetIntentUri("https://X.COM:443/intent/tweet?text=").AbsoluteUri);

        Assert.Throws<ArgumentException>(() => WorldService.BuildTweetIntentUri("https://example.com/intent/tweet?text=hello"));
        Assert.Throws<ArgumentException>(() => WorldService.BuildTweetIntentUri("https://twitter.com/intent/follow?text=hello"));
        Assert.Throws<ArgumentException>(() => WorldService.BuildTweetIntentUri("https://twitter.com/intent/tweet?text=hello&url=https://example.com"));
        Assert.Throws<ArgumentException>(() => WorldService.BuildTweetIntentUri("https://twitter.com/intent/tweet?text=hello;url=https://example.com"));
        Assert.Throws<ArgumentException>(() => WorldService.BuildTweetIntentUri("https://twitter.com/intent/tweet?text=hello#compose"));
        Assert.Throws<ArgumentException>(() => WorldService.BuildTweetIntentUri("https://twitter.com:444/intent/tweet?text=hello"));
        Assert.Throws<ArgumentException>(() => WorldService.BuildTweetIntentUri("https://user@twitter.com/intent/tweet?text=hello"));
        Assert.Throws<ArgumentException>(() => WorldService.BuildTweetIntentUri("http://twitter.com/intent/tweet?text=hello"));
        Assert.Throws<ArgumentException>(() => WorldService.BuildTweetIntentUri(" https://twitter.com/intent/tweet?text=hello "));
        Assert.Throws<ArgumentException>(() => WorldService.BuildTweetIntentUri(""));
        Assert.Throws<ArgumentException>(() => WorldService.BuildTweetIntentUri(null!));
        foreach (var intentUrl in new[] { "not a URI", "https://twitter.com/intent/tweet", "https://twitter.com/intent/tweet?Text=hello", "https://twitter.com/Intent/tweet?text=hello", "https://twitter.com/intent/tweet?text=a&text=b", "https://twitter.com/intent/tweet?text=a;text=b" })
            Assert.Throws<ArgumentException>(() => WorldService.BuildTweetIntentUri(intentUrl));

        var exception = Assert.Throws<ArgumentException>(() =>
            WorldService.BuildTweetIntentUri("https://example.com/intent/tweet?text=invalid-value"));
        Assert.DoesNotContain("example.com", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Explorer 選択表示の起動情報が、選択オプションと対象パスを1つの引数として構築することを確認する。
    ///
    /// Explorer の `/select,<path>` 構文は全体が1引数であるため、文字列連結後の値を ArgumentList へ渡す。
    /// ProcessStartInfo に引用処理を委ね、シェル経由のコマンド文字列は組み立てない。
    /// </summary>
    [Fact]
    public void BuildExplorerSelectionStartInfo_BuildsSingleSelectArgument()
    {
        var input = "C:/photos/a\" & calc.jpg";
        var psi = WorldService.BuildExplorerSelectionStartInfo(input);

        Assert.True(Path.IsPathRooted(psi.FileName));
        Assert.Equal("explorer.exe", Path.GetFileName(psi.FileName));
        Assert.False(psi.UseShellExecute);
        Assert.Equal(["/select," + Path.GetFullPath(input.Replace('/', '\\'))], psi.ArgumentList.ToArray());
        Assert.Throws<ArgumentException>(() => WorldService.BuildExplorerSelectionStartInfo(""));
    }

    /// <summary>
    /// 外部起動系メソッドが不正入力を Launcher 呼び出し前に拒否することを確認する。
    ///
    /// OpenWorldUrlAsync と OpenTweetIntentAsync は正常系では OS の既定ブラウザを起動するため、
    /// ユニットテストでは実行しない。
    /// 不正な world_id / intent URL はメソッド内部の入力チェックで ArgumentException になり、
    /// 外部状態に触れずに検証できる。
    /// </summary>
    [Fact]
    public async Task ExternalLaunchHelpersRejectInvalidInputsBeforeOpeningProcesses()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.WorldService.Invalid.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var db = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
            db.Initialize();
            var scanner = new PhotoScanner(new AppConfig(Path.Combine(tempDir, "settings")), db, new LocalEventBus());
            var service = new WorldService(db, scanner);

            await Assert.ThrowsAsync<ArgumentException>(() => service.OpenWorldUrlAsync("not-a-world-id"));
            await Assert.ThrowsAsync<ArgumentException>(() => service.OpenTweetIntentAsync("https://example.com/not-twitter"));
            await Assert.ThrowsAnyAsync<Exception>(() => service.CopyImageToClipboardAsync(Path.Combine(tempDir, "missing.png")));

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var canceled = await Assert.ThrowsAsync<OperationCanceledException>(() => service.ShowInExplorerAsync(Path.Combine(tempDir, "missing.png"), cancellation.Token));
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { }
        }
    }

    /// <summary>
    /// 32バイトすべてを同じ値で埋めた PDQ 風ハッシュを作る。
    /// ハミング距離を計算しやすい固定データを使い、テスト意図を読み取りやすくする。
    /// </summary>
    private static string Hash(byte value)
        => PdqHasher.ToHex(Enumerable.Repeat(value, 32).ToArray());

    /// <summary>
    /// target から1ビットだけ異なるハッシュを作る。
    /// 完全一致と遠距離候補の間に、距離1の候補を明示的に置くために使う。
    /// </summary>
    private static string SingleBitHash()
    {
        var bytes = new byte[32];
        bytes[0] = 0x01;
        return PdqHasher.ToHex(bytes);
    }

    /// <summary>ゼロhashとの距離を生成時点で確定させ、順位付けの距離算出に依存しない期待値を作る。</summary>
    private static string HashWithBitCount(int bitCount)
    {
        var bytes = new byte[32];
        for (var bit = 0; bit < bitCount; bit++)
            bytes[bit / 8] |= (byte)(1 << (bit % 8));
        return PdqHasher.ToHex(bytes);
    }

    /// <summary>
    /// KnownWorldRow を短く組み立てるテスト専用ファクトリ。
    /// 距離計算では PhotoPath / PhotoFilename / SourceSlot は主目的ではないため、安定した値で固定する。
    /// </summary>
    private static AlpheratzDb.KnownWorldRow Row(string id, string worldName, string phash)
        => new($"/photo/{id}.jpg", $"{id}.jpg", worldName, $"wrld_{id}", phash, 1);
}
