using System;

namespace Alpheratz.Core.Imaging.Pdq;

/// <summary>
/// PDQ (Perceptual Hash, Facebook 公開アルゴリズム) のコア計算。
/// 参考: https://github.com/facebook/ThreatExchange/tree/main/pdq
///
/// アルゴリズム概要：
///   1. 入力画像をグレイスケール (luma) に変換し、長辺 512px 以下に縮小（呼び出し側責任）
///   2. Jarosz Box フィルタを XY 2 パスで適用してアンチエイリアシング (ローパス)
///   3. 64x64 に decimate (中心セル サンプリング)
///   4. 64x64 → 16x16 の離散コサイン変換 (DCT) で低周波成分を抽出
///   5. 16x16 (=256 セル) を中央値で二値化 → 256 ビット = 32 バイトハッシュ
///   6. quality は画素分散から算出 (低分散 = 単色寄り = ハッシュ信頼性低)
///
/// 32 バイトハッシュ同士の Hamming 距離が 64 以下なら「ほぼ同じ画像」と判定する慣習。
/// </summary>
public static class PdqHasher
{
    /// <summary>Jarosz/decimate の中間バッファ寸法 (64x64)。</summary>
    public const int BufferWh = PdqDctMatrix.BufferWh;          // 64
    /// <summary>DCT 出力寸法 (16x16)。</summary>
    public const int DctOutputWh = PdqDctMatrix.DctOutputWh;    // 16
    /// <summary>DCT 出力の総セル数 (256)。</summary>
    public const int DctOutputMatrixSize = DctOutputWh * DctOutputWh; // 256
    /// <summary>ハッシュバイト長 (256 ビット = 32 バイト)。</summary>
    public const int HashLength = DctOutputMatrixSize / 8;      // 32 bytes
    /// <summary>幅高がこれ未満の画像はハッシュ不能 (情報量が少なすぎて DCT 係数がノイズになる)。</summary>
    public const int MinHashableDim = 5;
    /// <summary>呼び出し側に推奨する事前縮小上限 (長辺 512px)。これ以上はコストが上がるだけで精度向上は微小。</summary>
    public const int DownsampleDims = 512;
    /// <summary>Jarosz Box フィルタの XY 反復回数 (2 回でガウスフィルタ近似、PDQ 仕様値)。</summary>
    private const int PdqNumJaroszXyPasses = 2;

    // ITU-R BT.601 の RGB→luma 係数。
    public const float LumaFromR = 0.299f;
    public const float LumaFromG = 0.587f;
    public const float LumaFromB = 0.114f;

    /// <summary>
    /// PDQ ハッシュとクオリティを返すパブリックエントリポイント。
    /// luma は呼出側で長辺 DownsampleDims 以下に縮小しグレイスケール化したフロート配列。
    /// 画像が極端に小さい (MinHashableDim 未満) なら null を返す。
    /// </summary>
    public static (byte[] Hash, float Quality)? GeneratePdq(float[] luma, int width, int height)
    {
        if (width < MinHashableDim || height < MinHashableDim) return null;
        return GeneratePdqFullSize(luma, width, height);
    }

    /// <summary>サイズ閾値チェックを省いた本体。Jarosz → Decimate → DCT → 二値化の流れ。</summary>
    public static (byte[] Hash, float Quality) GeneratePdqFullSize(float[] luma, int width, int height)
    {
        var windowAlongRows = ComputeJaroszFilterWindowSize(width, BufferWh);
        var windowAlongCols = ComputeJaroszFilterWindowSize(height, BufferWh);

        var work = (float[])luma.Clone();
        JaroszFilter(work, height, width, windowAlongRows, windowAlongCols, PdqNumJaroszXyPasses);

        var buffer64x64 = Decimate(work, height, width, BufferWh, BufferWh);
        var buffer16x16 = Dct64To16(buffer64x64);

        return (Buffer16x16ToBits(buffer16x16), QualityMetric(buffer64x64));
    }

    /// <summary>
    /// 元寸法 oldDim → 目標寸法 newDim にダウンサンプルする際の Box フィルタ窓幅。
    /// 式 (oldDim + 2*newDim - 1) / (2*newDim) は PDQ 仕様（Facebook 公開リファレンス実装）由来。
    /// 直感的には「old/new の倍率の半分 ± 端数調整」で、奇数化することで対称な窓になる。
    /// </summary>
    public static int ComputeJaroszFilterWindowSize(int oldDim, int newDim)
        => (oldDim + 2 * newDim - 1) / (2 * newDim);

    /// <summary>
    /// 2 軸 (rows / cols) で Box フィルタを reps 回適用する。
    /// 行方向 → 一時バッファ → 列方向 → 元バッファ、を 1 ペアとして reps 回繰り返す。
    /// Box ブラーの繰り返しはガウシアンに収束するという数学的性質を使い、安価にガウスフィルタを近似。
    /// </summary>
    public static void JaroszFilter(float[] buffer, int rows, int cols, int winRows, int winCols, int reps)
    {
        var tmp = new float[buffer.Length];
        for (var r = 0; r < reps; r++)
        {
            BoxAlongRows(buffer, tmp, rows, cols, winRows);
            BoxAlongCols(tmp, buffer, rows, cols, winCols);
        }
    }

    /// <summary>各行を 1-D Box フィルタにかける（行内 stride=1）。</summary>
    public static void BoxAlongRows(float[] input, float[] output, int rows, int cols, int win)
    {
        for (var i = 0; i < rows; i++)
        {
            BoxOneD(input, i * cols, output, cols, 1, win);
        }
    }

    /// <summary>各列を 1-D Box フィルタにかける（列をまたぐので stride=cols）。</summary>
    public static void BoxAlongCols(float[] input, float[] output, int rows, int cols, int win)
    {
        for (var j = 0; j < cols; j++)
        {
            BoxOneD(input, j, output, rows, cols, win);
        }
    }

    /// <summary>
    /// 1 次元の移動平均（Box フィルタ）。4 フェーズ実装で端の処理を漏れなく行う：
    ///   PHASE 1: 窓を埋めるまでの累積（出力なし）
    ///   PHASE 2: 窓が成長中の出力（左端の半窓）
    ///   PHASE 3: 完全窓での出力（中央部、最も多い）
    ///   PHASE 4: 窓が縮小中の出力（右端の半窓）
    /// この実装は Facebook PDQ リファレンス実装をそのまま移植したもので、
    /// fullWin が偶数/奇数いずれでも端まで対称になる。
    /// </summary>
    public static void BoxOneD(float[] inv, int inStartOffset, float[] outv, int vectorLen, int stride, int fullWin)
    {
        var halfWin = (fullWin + 2) / 2;
        var phase1Reps = halfWin - 1;
        var phase2Reps = fullWin - halfWin + 1;
        var oiOff = phase1Reps * stride;
        var liOff = phase2Reps * stride;

        var sum = 0f;
        var currentWin = 0f;

        var phase1End = oiOff + inStartOffset;
        // PHASE 1: accumulate first sum, no writes
        for (var ri = inStartOffset; ri < phase1End; ri += stride)
        {
            sum += inv[ri];
            currentWin += 1f;
        }

        var phase2End = fullWin * stride + inStartOffset;
        // PHASE 2: initial writes with growing window
        for (var ri = phase1End; ri < phase2End; ri += stride)
        {
            var oi = ri - oiOff;
            sum += inv[ri];
            currentWin += 1f;
            outv[oi] = sum / currentWin;
        }

        var phase3End = vectorLen * stride + inStartOffset;
        // PHASE 3: writes with full window
        for (var ri = phase2End; ri < phase3End; ri += stride)
        {
            var oi = ri - oiOff;
            var li = oi - liOff;
            sum += inv[ri];
            sum -= inv[li];
            outv[oi] = sum / currentWin;
        }

        var phase4Start = (vectorLen - halfWin + 1) * stride + inStartOffset;
        // PHASE 4: final writes with shrinking window
        for (var oi = phase4Start; oi < phase3End; oi += stride)
        {
            var li = oi - liOff;
            sum -= inv[li];
            currentWin -= 1f;
            outv[oi] = sum / currentWin;
        }
    }

    /// <summary>
    /// 入力バッファを outRows x outCols に縮小する。各出力セルは入力グリッドの「中心位置」を
    /// サンプリングする（(2k+1)/(2N) の式）。中心サンプリングにより、端寄りバイアスを避けつつ
    /// Jarosz でローパス済みの値を採取することでエイリアシングを回避する。
    /// </summary>
    public static float[][] Decimate(float[] input, int inRows, int inCols, int outRows, int outCols)
    {
        var output = new float[outRows][];
        for (var outi = 0; outi < outRows; outi++)
        {
            output[outi] = new float[outCols];
            var ini = ((outi * 2 + 1) * inRows) / (outRows * 2);
            for (var outj = 0; outj < outCols; outj++)
            {
                var inj = ((outj * 2 + 1) * inCols) / (outCols * 2);
                output[outi][outj] = input[ini * inCols + inj];
            }
        }
        return output;
    }

    /// <summary>
    /// 64x64 → 16x16 の DCT。2 段階に分けることで計算量を抑える：
    ///   64x64 → 16x64 (行方向 DCT, 中間)
    ///   16x64 → 16x16 (列方向 DCT, 最終)
    /// 直接 64x64 → 16x16 で計算すると 64*64*16*16 演算が必要だが、
    /// 分離可能性を利用すれば 16*64*64 + 16*16*64 演算で済む（約 4 倍速）。
    /// 係数は PdqDctMatrix で事前計算済み（uint32 ビットパターンとして格納し float に復元）。
    /// </summary>
    public static float[] Dct64To16(float[][] input)
    {
        var inRows = input.Length;
        var inCols = input[0].Length;
        var matrix = PdqDctMatrix.Matrix;

        var intermediate = new float[DctOutputWh][];
        for (var i = 0; i < DctOutputWh; i++)
        {
            intermediate[i] = new float[inCols];
            for (var j = 0; j < inCols; j++)
            {
                var sum = 0f;
                for (var k = 0; k < BufferWh; k++)
                {
                    sum += BitConverter.UInt32BitsToSingle(matrix[i][k]) * input[k][j];
                }
                intermediate[i][j] = sum;
            }
        }

        var output = new float[DctOutputMatrixSize];
        for (var i = 0; i < DctOutputWh; i++)
        {
            for (var j = 0; j < DctOutputWh; j++)
            {
                var sum = 0f;
                for (var k = 0; k < BufferWh; k++)
                {
                    sum += intermediate[i][k] * BitConverter.UInt32BitsToSingle(matrix[j][k]);
                }
                output[i * DctOutputWh + j] = sum;
            }
        }
        return output;
    }

    /// <summary>
    /// Torben のアルゴリズムによる O(n) 中央値計算。
    /// 配列を直接ソートせず、min/max 範囲の二分探索で中央値を当てる。
    /// 利点：
    ///   - 入力配列を変更しない (PDQ 計算で 256 要素配列をそのまま二値化に再利用するため必須)
    ///   - 標準のクイックセレクトより安定 (枢軸選択の最悪 O(n²) を避ける)
    /// 参考: https://www.stat.cmu.edu/~ryantibs/median/torben.c
    /// </summary>
    public static float TorbenMedian(float[] m)
    {
        var min = m[0];
        var max = m[0];
        for (var i = 1; i < m.Length; i++)
        {
            if (m[i] < min) min = m[i];
            if (m[i] > max) max = m[i];
        }

        var half = (m.Length + 1) / 2;
        while (true)
        {
            var guess = (min + max) / 2f;
            var less = 0;
            var greater = 0;
            var equal = 0;
            var maxLtGuess = min;
            var minGtGuess = max;
            for (var i = 0; i < m.Length; i++)
            {
                var v = m[i];
                if (v < guess) { less++; if (v > maxLtGuess) maxLtGuess = v; }
                else if (v > guess) { greater++; if (v < minGtGuess) minGtGuess = v; }
                else equal++;
            }
            if (less <= half && greater <= half)
            {
                if (less >= half) return maxLtGuess;
                if (less + equal >= half) return guess;
                return minGtGuess;
            }
            if (less > greater) max = maxLtGuess;
            else min = minGtGuess;
        }
    }

    /// <summary>
    /// 16x16=256 個の DCT 係数を、中央値より大きい/小さいで二値化して 256 ビット = 32 バイトの
    /// ハッシュにする。中央値で割ることで「明るい/暗い」のパターンだけが残り、
    /// 全体の明度オフセットに対して頑健になる。
    /// バイト順は HashLength - i - 1 で反転しているのは PDQ 標準実装の big-endian 出力と
    /// 揃えるため (これにより別実装が出力したハッシュと相互運用できる)。
    /// </summary>
    public static byte[] Buffer16x16ToBits(float[] input)
    {
        var median = TorbenMedian(input);
        var hash = new byte[HashLength];
        for (var i = 0; i < HashLength; i++)
        {
            byte b = 0;
            for (var j = 0; j < 8; j++)
            {
                if (input[i * 8 + j] > median) b |= (byte)(1 << j);
            }
            hash[HashLength - i - 1] = b;
        }
        return hash;
    }

    /// <summary>
    /// ハッシュの信頼度メトリック (0.0 - 1.0)。
    /// 64x64 バッファ内の水平・垂直方向の隣接画素差分の絶対値を合計し、定数で正規化する。
    /// 単色平面のような「ハッシュしても意味の薄い画像」では低い値になり、
    /// PDQ マッチングで低信頼ハッシュを除外するための閾値判定に使う。
    /// </summary>
    public static float QualityMetric(float[][] buffer)
    {
        var rows = buffer.Length;
        var cols = buffer[0].Length;
        var gradient = 0f;

        for (var i = 0; i < rows - 1; i++)
        {
            for (var j = 0; j < cols; j++)
            {
                var d = (buffer[i][j] - buffer[i + 1][j]) / 255f;
                gradient += MathF.Abs(d);
            }
        }
        for (var i = 0; i < rows; i++)
        {
            for (var j = 0; j < cols - 1; j++)
            {
                var d = (buffer[i][j] - buffer[i][j + 1]) / 255f;
                gradient += MathF.Abs(d);
            }
        }

        var quality = gradient / 90f;
        return quality > 1f ? 1f : quality;
    }

    /// <summary>
    /// 同サイズの 2 ハッシュ間のハミング距離 (0..256)。
    /// BitOperations.PopCount で 1 バイトずつ XOR 後の立ちビットを数える (HW popcnt 命令を利用)。
    /// </summary>
    public static int HammingDistance(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length) throw new ArgumentException("hash length mismatch");
        var d = 0;
        for (var i = 0; i < a.Length; i++)
        {
            d += System.Numerics.BitOperations.PopCount((uint)(a[i] ^ b[i]));
        }
        return d;
    }

    /// <summary>ハッシュを 64 文字の lowercase hex 文字列にエンコード（DB 保存形式）。</summary>
    public static string ToHex(byte[] hash)
    {
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>hex 文字列をバイト配列に戻す。</summary>
    public static byte[] FromHex(string hex)
    {
        return Convert.FromHexString(hex);
    }

    /// <summary>
    /// hex 文字列同士のハミング距離。バイト配列へ戻さず 4bit ずつ直接 XOR + popcount で計算する
    /// (アロケーションを避けるため)。長さが違うか hex 文字以外を含むと null を返す。
    /// </summary>
    public static int? HexHammingDistance(string left, string right)
    {
        if (left.Length != right.Length) return null;
        var distance = 0;
        for (var i = 0; i < left.Length; i++)
        {
            var l = HexDigit(left[i]);
            var r = HexDigit(right[i]);
            if (l < 0 || r < 0) return null;
            distance += System.Numerics.BitOperations.PopCount((uint)(l ^ r));
        }
        return distance;
    }

    /// <summary>16進文字を 0-15 の値へ変換する。不正文字は -1。</summary>
    private static int HexDigit(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return 10 + (c - 'a');
        if (c >= 'A' && c <= 'F') return 10 + (c - 'A');
        return -1;
    }

    /// <summary>
    /// 2 つのハッシュバリアントリストのクロス積から最小距離を返す。
    /// バリアント = 同一画像を 0°/90°/180°/270° で生成した複数ハッシュを '|' 区切りで持つ仕様。
    /// ユーザがアップした画像の回転状態が不明な場合、複数回転ハッシュを保持して最小距離を取ることで
    /// 回転耐性のあるマッチングを実現する。
    /// </summary>
    public static int? ClosestHashDistance(IReadOnlyList<string> leftVariants, IReadOnlyList<string> rightVariants)
    {
        if (leftVariants.Count == 0 || rightVariants.Count == 0) return null;
        var best = int.MaxValue;
        foreach (var l in leftVariants)
        {
            foreach (var r in rightVariants)
            {
                var d = HexHammingDistance(l, r);
                if (d is null) continue;
                if (d.Value < best) best = d.Value;
                if (best == 0) return 0;
            }
        }
        return best == int.MaxValue ? null : best;
    }

    /// <summary>
    /// DB 格納形式 (パイプ '|' 区切り) のハッシュバリアント文字列をパースしてリスト化する。
    /// 不正な hex (長さ 64 でない / hex 文字以外混入) は捨てる。
    /// </summary>
    public static System.Collections.Generic.List<string> ParseHashVariants(string? value)
    {
        var result = new System.Collections.Generic.List<string>();
        if (string.IsNullOrEmpty(value)) return result;
        foreach (var part in value.Split('|', StringSplitOptions.None))
        {
            var trimmed = part.Trim().ToLowerInvariant();
            if (trimmed.Length != 64) continue;
            var ok = true;
            foreach (var ch in trimmed)
            {
                if (HexDigit(ch) < 0) { ok = false; break; }
            }
            if (ok) result.Add(trimmed);
        }
        return result;
    }

    // -----------------------------------------------------------------
    // Rotation helpers - operate on row-major luma buffers (w * h floats).
    // -----------------------------------------------------------------

    public static (float[] luma, int width, int height) Rotate90(float[] luma, int w, int h)
    {
        var rotated = new float[w * h];
        for (var i = 0; i < h; i++)
        {
            for (var j = 0; j < w; j++)
            {
                // (i, j) -> (j, h - 1 - i) in new w' = h, h' = w
                rotated[j * h + (h - 1 - i)] = luma[i * w + j];
            }
        }
        return (rotated, h, w);
    }

    public static (float[] luma, int width, int height) Rotate180(float[] luma, int w, int h)
    {
        var rotated = new float[w * h];
        for (var i = 0; i < h; i++)
        {
            for (var j = 0; j < w; j++)
            {
                rotated[(h - 1 - i) * w + (w - 1 - j)] = luma[i * w + j];
            }
        }
        return (rotated, w, h);
    }

    public static (float[] luma, int width, int height) Rotate270(float[] luma, int w, int h)
    {
        var rotated = new float[w * h];
        for (var i = 0; i < h; i++)
        {
            for (var j = 0; j < w; j++)
            {
                // (i, j) -> (w - 1 - j, i) in new w' = h, h' = w
                rotated[(w - 1 - j) * h + i] = luma[i * w + j];
            }
        }
        return (rotated, h, w);
    }

    // Compute four hash variants (0/90/180/270) and join with '|' for storage.
    public static string ComputeHashVariantsHex(float[] luma, int w, int h)
    {
        var variants = new string[4];
        var v0 = GeneratePdq(luma, w, h);
        if (v0 is null) return string.Empty;
        variants[0] = ToHex(v0.Value.Hash);

        var (l90, w90, h90) = Rotate90(luma, w, h);
        var v90 = GeneratePdq(l90, w90, h90);
        variants[1] = v90 is null ? string.Empty : ToHex(v90.Value.Hash);

        var (l180, w180, h180) = Rotate180(luma, w, h);
        var v180 = GeneratePdq(l180, w180, h180);
        variants[2] = v180 is null ? string.Empty : ToHex(v180.Value.Hash);

        var (l270, w270, h270) = Rotate270(luma, w, h);
        var v270 = GeneratePdq(l270, w270, h270);
        variants[3] = v270 is null ? string.Empty : ToHex(v270.Value.Hash);

        var nonEmpty = new System.Collections.Generic.List<string>();
        foreach (var s in variants) if (!string.IsNullOrEmpty(s)) nonEmpty.Add(s);
        return string.Join("|", nonEmpty);
    }
}
