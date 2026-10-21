using Alpheratz.Core.Imaging.Pdq;

namespace Alpheratz.Tests;

/// <summary>
/// PDQハッシュ計算の公開ヘルパを検証するテスト。
///
/// 画像ファイルの読み込みはOSの画像デコーダに依存するため、このテストでは
/// luma配列を直接渡してアルゴリズム部分を確認する。
/// これにより、ハッシュ距離、回転バリアント、縮小処理、最小サイズ判定といった
/// DB保存・類似判定で使う振る舞いを安定して検証できる。
/// </summary>
public sealed class PdqHasherTests
{
    /// <summary>
    /// hex形式とハミング距離の基本仕様を確認する。
    ///
    /// DBにはPDQハッシュをlowercase hexとして保存する。
    /// そのため、byte配列との往復変換、同長hex同士の距離、長さ不一致や不正文字の扱いを
    /// 一度に検証し、類似写真検索で距離計算が壊れないようにする。
    /// </summary>
    [Fact]
    public void HexAndDistanceHelpers_HandleValidAndInvalidInput()
    {
        var hash = Enumerable.Range(0, PdqHasher.HashLength).Select(i => (byte)i).ToArray();
        var hex = PdqHasher.ToHex(hash);

        Assert.Equal(64, hex.Length);
        Assert.Equal(hash, PdqHasher.FromHex(hex));
        Assert.Equal(0, PdqHasher.HammingDistance(hash, hash));
        Assert.Equal(1, PdqHasher.HexHammingDistance("0", "1"));
        Assert.Null(PdqHasher.HexHammingDistance("00", "0"));
        Assert.Null(PdqHasher.HexHammingDistance("gg", "00"));
    }

    /// <summary>
    /// DB保存形式の回転バリアント文字列を安全に解析することを確認する。
    ///
    /// phash列には 0/90/180/270 度の複数ハッシュを "|" 区切りで保存する。
    /// ParseHashVariants は長さ64のhexだけを採用し、不正な要素を捨てる。
    /// ClosestHashDistance は有効な組み合わせの最小距離だけを返す。
    /// </summary>
    [Fact]
    public void VariantHelpers_FilterInvalidHashesAndReturnClosestDistance()
    {
        var zero = new string('0', 64);
        var one = "1" + new string('0', 63);
        var invalid = "not-a-hash";

        var parsed = PdqHasher.ParseHashVariants($"{zero}|{invalid}|{one.ToUpperInvariant()}");

        Assert.Equal([zero, one], parsed);
        Assert.Equal(0, PdqHasher.ClosestHashDistance([zero], [zero, one]));
        Assert.Equal(1, PdqHasher.ClosestHashDistance([zero], [one]));
        Assert.Null(PdqHasher.ClosestHashDistance([], [one]));
    }

    /// <summary>
    /// 回転ヘルパがrow-major配列を期待どおりに並べ替えることを確認する。
    ///
    /// 回転バリアントは、ユーザーが画像を回転保存していても近いPDQ距離を取るための仕組みである。
    /// 小さい2x3配列で90/180/270度の変換結果を固定し、幅と高さの入れ替えも検証する。
    /// </summary>
    [Fact]
    public void RotationHelpers_ReturnExpectedRowMajorBuffers()
    {
        float[] luma = [1, 2, 3, 4, 5, 6];

        var r90 = PdqHasher.Rotate90(luma, 3, 2);
        var r180 = PdqHasher.Rotate180(luma, 3, 2);
        var r270 = PdqHasher.Rotate270(luma, 3, 2);

        Assert.Equal((2, 3), (r90.width, r90.height));
        Assert.Equal([4, 1, 5, 2, 6, 3], r90.luma);
        Assert.Equal((3, 2), (r180.width, r180.height));
        Assert.Equal([6, 5, 4, 3, 2, 1], r180.luma);
        Assert.Equal((2, 3), (r270.width, r270.height));
        Assert.Equal([3, 6, 2, 5, 1, 4], r270.luma);
    }

    /// <summary>
    /// Decimate が入力グリッドの中心寄りサンプルを選ぶことを確認する。
    ///
    /// PDQ本体ではJaroszフィルタ後の画像を64x64へ縮小する。
    /// Decimateは平均ではなく、各出力セルに対応する中心位置をサンプリングするため、
    /// 4x4から2x2への小さな入力で採用されるセルを明確に固定する。
    /// </summary>
    [Fact]
    public void Decimate_SamplesCenterCells()
    {
        var input = Enumerable.Range(0, 16).Select(i => (float)i).ToArray();

        var output = PdqHasher.Decimate(input, inRows: 4, inCols: 4, outRows: 2, outCols: 2);

        Assert.Equal([5f, 7f], output[0]);
        Assert.Equal([13f, 15f], output[1]);
    }

    /// <summary>
    /// 最小サイズ未満の画像はハッシュ不可として扱うことを確認する。
    ///
    /// 極端に小さい画像はDCT係数が意味を持たず、類似判定に使うと誤一致の原因になる。
    /// GeneratePdq は MinHashableDim 未満の幅または高さを検出した場合に null を返す。
    /// </summary>
    [Fact]
    public void GeneratePdq_ReturnsNullForTooSmallImages()
    {
        var luma = Enumerable.Repeat(128f, 4 * 4).ToArray();

        var result = PdqHasher.GeneratePdq(luma, width: 4, height: 4);

        Assert.Null(result);
    }

    /// <summary>
    /// 十分なサイズのluma配列から32バイトのハッシュと0..1の品質値が生成されることを確認する。
    ///
    /// 入力には単調なグラデーションを使う。
    /// 回転ごとの公開entryとバリアント生成の整合を確認する。
    /// 品質は入力の勾配総和16320から得た独立値へ照合し、hashの固定値は別caseで確認する。
    /// </summary>
    [Fact]
    public void GeneratePdq_ProducesHashQualityAndRotationVariants()
    {
        var luma = Enumerable.Range(0, 16 * 16).Select(i => (float)(i % 256)).ToArray();

        var result = PdqHasher.GeneratePdq(luma, width: 16, height: 16);
        var variants = PdqHasher.ComputeHashVariantsHex(luma, w: 16, h: 16).Split('|');
        var rotated90 = PdqHasher.Rotate90(luma, 16, 16);
        var rotated180 = PdqHasher.Rotate180(luma, 16, 16);
        var rotated270 = PdqHasher.Rotate270(luma, 16, 16);
        var expectedVariants = new[]
        {
            PdqHasher.ToHex(result!.Value.Hash),
            PdqHasher.ToHex(PdqHasher.GeneratePdq(rotated90.luma, rotated90.width, rotated90.height)!.Value.Hash),
            PdqHasher.ToHex(PdqHasher.GeneratePdq(rotated180.luma, rotated180.width, rotated180.height)!.Value.Hash),
            PdqHasher.ToHex(PdqHasher.GeneratePdq(rotated270.luma, rotated270.width, rotated270.height)!.Value.Hash),
        };

        Assert.NotNull(result);
        Assert.Equal(32, result.Value.Hash.Length);
        Assert.InRange(result.Value.Quality, 0f, 1f);
        Assert.InRange(MathF.Abs(result.Value.Quality - 0.7111111f), 0, 0.000004f);
        Assert.Equal(64, PdqHasher.ToHex(result.Value.Hash).Length);
        Assert.Equal(4, variants.Length);
        Assert.All(variants, value => Assert.Equal(64, value.Length));
        Assert.Equal(expectedVariants, variants);
    }

    /// <summary>
    /// 公開C++参照の固定hashで、通常entry・4方向の順序・入力保持・連続要求を照合する。
    /// 参照版: https://github.com/facebook/ThreatExchange/tree/85978d7cabdf631c0e4be9cb2be2816b2b9a6911/pdq
    /// 期待値は同じlumaを物理回転して得た参照出力であり、本体のhash／回転helperから作らない。
    /// </summary>
    [Theory]
    [InlineData(64, 64, "3da424b013df17da1e497071c0442174e6499b5f61e3543b5d3db965a21e6e9f|579353ec90f2e909dd188dcc6d96e439a5746ab39069a00e6d8ef65a752679a8|68f18e1a468abd704b1cdadb95118bdeb31c31f134b6fe91086813cff70bc435|02c6f946c5a743a3e94d276638c3de93f821ca5bc53c8ba538dbdcf13073d302")]
    [InlineData(67, 73, "2abf26ca9c133469a05489446aeeee8b161ac361f5035d353b653a6cc69a7d3f|783756268e7965e8f24c24e6969b512c96570b19c708fdc686d2db48b6f6e42c|7fea8c6049469ec3f50123ee3f3b4421434f69cba056f79f663090c493cfd694|2d62fc8cdb2ccf42a7198e4cc3cefb86c102a1b3925d566cd38771e2e3a34e86")]
    [InlineData(192, 256, "e5dd02a2b17d8ecb36d9c9c933309869bbb18b2f36c707e6f94eda82b430480b|989db592903976f18d79932b9b6da3e99a6900f926cdb6b7c118850d6e66b0b4|399845d58c66b5d97fd9aaeef7f77e8901a499b7e0868d19d8a9964a9046f000|49ea5260aa6ad55554cd7e555ad545958abc42d343d527559b1957a5527d520f")]
    public void GeneratePdq_MatchesIndependentReferenceHashesAndPreservesInput(int width, int height, string expectedVariants)
    {
        var luma = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                luma[y * width + x] = (x * 37 + y * 53 + x * y * 11 + (x ^ y) * 17 + 19) % 256;
        var original = luma.ToArray();
        var result = PdqHasher.GeneratePdq(luma, width, height);

        Assert.NotNull(result);
        Assert.Equal(Convert.FromHexString(expectedVariants[..64]), result.Value.Hash);
        Assert.Equal(1f, result.Value.Quality);
        Assert.Equal(expectedVariants, PdqHasher.ComputeHashVariantsHex(luma, width, height));
        Assert.Equal(expectedVariants, PdqHasher.ComputeHashVariantsHex(luma, width, height));
        Assert.Equal(original, luma);
    }

    /// <summary>64×63本の隣接辺と独立した固定品質値で、連続勾配の現行契約を照合する。</summary>
    [Theory]
    [InlineData(0, 0, 0, 0f)]
    [InlineData(0, 0, 128, 0f)]
    [InlineData(0, 0, 255, 0f)]
    [InlineData(1, 0, 0, 0.1756863f)]
    [InlineData(0, 1, 0, 0.1756863f)]
    [InlineData(1, 1, 0, 0.3513726f)]
    [InlineData(4, 0, 0, 0.7027451f)]
    public void QualityMetric_MatchesIndependentGradientTotals(int xStep, int yStep, int offset, float expected)
    {
        var buffer = Enumerable.Range(0, 64).Select(y => Enumerable.Range(0, 64).Select(x => (float)(x * xStep + y * yStep + offset)).ToArray()).ToArray();
        var quality = PdqHasher.QualityMetric(buffer);
        Assert.InRange(MathF.Abs(quality - expected), 0, 0.00002f);
    }

    /// <summary>全ての隣接辺が0と255を結ぶ格子では、品質を上限1へ固定する。</summary>
    [Fact]
    public void QualityMetric_ClampsCheckerboardAtOne()
    {
        var buffer = Enumerable.Range(0, 64).Select(y => Enumerable.Range(0, 64).Select(x => (float)((x + y) % 2 * 255)).ToArray()).ToArray();
        Assert.Equal(1f, PdqHasher.QualityMetric(buffer));
    }

    /// <summary>幅か高さが最小寸法5未満なら、hashと保存用バリアントを生成しない。</summary>
    [Theory]
    [InlineData(4, 5)]
    [InlineData(5, 4)]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    public void GeneratePdq_RejectsEachDimensionBelowMinimum(int width, int height)
    {
        var luma = new float[width * height];
        Assert.Null(PdqHasher.GeneratePdq(luma, width, height));
        Assert.Equal(string.Empty, PdqHasher.ComputeHashVariantsHex(luma, width, height));
    }

    /// <summary>最小寸法5×5の黒画像は、全係数0による32byteの0と品質0を返す。</summary>
    [Fact]
    public void GeneratePdq_AcceptsMinimumDimensionsAndPreservesZeroInput()
    {
        var luma = new float[25];
        var result = PdqHasher.GeneratePdq(luma, 5, 5);
        Assert.NotNull(result);
        Assert.Equal(new byte[32], result.Value.Hash);
        Assert.Equal(0f, result.Value.Quality);
        Assert.Equal(new float[25], luma);
    }
}
