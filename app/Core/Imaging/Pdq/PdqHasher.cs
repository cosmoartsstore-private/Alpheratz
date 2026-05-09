using System;

namespace Alpheratz.Core.Imaging.Pdq;

// PDQ image hash core algorithm. Pure numeric port of legacy alpheratz
// pdq_hash.rs. The image-loading side (PNG/JPG -> grayscale float[]) lives in
// PdqImageReader; this file only handles the math once we have a luma buffer.
public static class PdqHasher
{
    public const int BufferWh = PdqDctMatrix.BufferWh;          // 64
    public const int DctOutputWh = PdqDctMatrix.DctOutputWh;    // 16
    public const int DctOutputMatrixSize = DctOutputWh * DctOutputWh; // 256
    public const int HashLength = DctOutputMatrixSize / 8;      // 32 bytes
    public const int MinHashableDim = 5;
    public const int DownsampleDims = 512;
    private const int PdqNumJaroszXyPasses = 2;

    public const float LumaFromR = 0.299f;
    public const float LumaFromG = 0.587f;
    public const float LumaFromB = 0.114f;

    // Public entry: produces (hash[32], quality). Callers feed an already
    // luma-converted, properly-sized buffer (rows*cols floats).
    // Width/height should already be capped at DownsampleDims (image side max).
    public static (byte[] Hash, float Quality)? GeneratePdq(float[] luma, int width, int height)
    {
        if (width < MinHashableDim || height < MinHashableDim) return null;
        return GeneratePdqFullSize(luma, width, height);
    }

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

    // (old + 2*new - 1) / (2*new)
    public static int ComputeJaroszFilterWindowSize(int oldDim, int newDim)
        => (oldDim + 2 * newDim - 1) / (2 * newDim);

    // Two-pass box blur: row pass into temp, then col pass back into buffer.
    public static void JaroszFilter(float[] buffer, int rows, int cols, int winRows, int winCols, int reps)
    {
        var tmp = new float[buffer.Length];
        for (var r = 0; r < reps; r++)
        {
            BoxAlongRows(buffer, tmp, rows, cols, winRows);
            BoxAlongCols(tmp, buffer, rows, cols, winCols);
        }
    }

    public static void BoxAlongRows(float[] input, float[] output, int rows, int cols, int win)
    {
        for (var i = 0; i < rows; i++)
        {
            BoxOneD(input, i * cols, output, cols, 1, win);
        }
    }

    public static void BoxAlongCols(float[] input, float[] output, int rows, int cols, int win)
    {
        for (var j = 0; j < cols; j++)
        {
            BoxOneD(input, j, output, rows, cols, win);
        }
    }

    // 1-D moving average with three different phases (mirrors Rust).
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

    // Sample buffer at center-of-cell positions to produce outRows x outCols.
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

    // 64x64 -> intermediate 16x64 -> output 16x16, using DCT_MATRIX coefficients.
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

    // Quick-select-style median (Torben's method).
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

    // Convert 256 floats into 256-bit hash (32 bytes), comparing each to the median.
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

    // Hamming distance between two 32-byte hashes (0..256).
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

    // Hex-encode hash (legacy storage format).
    public static string ToHex(byte[] hash)
    {
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static byte[] FromHex(string hex)
    {
        return Convert.FromHexString(hex);
    }

    // Hex-string hamming distance (mirrors legacy alpheratz get_hamming_distance).
    // Both hashes must be lowercase hex of equal length (typically 64 chars).
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

    private static int HexDigit(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return 10 + (c - 'a');
        if (c >= 'A' && c <= 'F') return 10 + (c - 'A');
        return -1;
    }

    // Smallest distance over the cross product of two pipe-separated hash variant lists.
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
