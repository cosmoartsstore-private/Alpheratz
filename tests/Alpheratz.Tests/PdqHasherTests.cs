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
    /// 具体的なハッシュ値はDCT係数に強く依存するため固定しすぎず、
    /// 出力サイズ、hex長、品質範囲、回転バリアント形式を検証する。
    /// </summary>
    [Fact]
    public void GeneratePdq_ProducesHashQualityAndRotationVariants()
    {
        var luma = Enumerable.Range(0, 16 * 16).Select(i => (float)(i % 256)).ToArray();

        var result = PdqHasher.GeneratePdq(luma, width: 16, height: 16);
        var variants = PdqHasher.ComputeHashVariantsHex(luma, w: 16, h: 16).Split('|');

        Assert.NotNull(result);
        Assert.Equal(PdqHasher.HashLength, result.Value.Hash.Length);
        Assert.InRange(result.Value.Quality, 0f, 1f);
        Assert.Equal(64, PdqHasher.ToHex(result.Value.Hash).Length);
        Assert.Equal(4, variants.Length);
        Assert.All(variants, value => Assert.Equal(64, value.Length));
    }
}
