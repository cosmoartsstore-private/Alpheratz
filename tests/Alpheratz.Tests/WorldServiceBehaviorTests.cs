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

    /// <summary>
    /// KnownWorldRow を短く組み立てるテスト専用ファクトリ。
    /// 距離計算では PhotoPath / PhotoFilename / SourceSlot は主目的ではないため、安定した値で固定する。
    /// </summary>
    private static AlpheratzDb.KnownWorldRow Row(string id, string worldName, string phash)
        => new($"/photo/{id}.jpg", $"{id}.jpg", worldName, $"wrld_{id}", phash, 1);
}
