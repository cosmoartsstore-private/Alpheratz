using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alpheratz.Core.Imaging.Pdq;

public static class PdqHasher
{
	public const int BufferWh = 64;

	public const int DctOutputWh = 16;

	public const int DctOutputMatrixSize = 256;

	public const int HashLength = 32;

	public const int MinHashableDim = 5;

	public const int DownsampleDims = 512;

	private const int PdqNumJaroszXyPasses = 2;

	public const float LumaFromR = 0.299f;

	public const float LumaFromG = 0.587f;

	public const float LumaFromB = 0.114f;

	public static (byte[] Hash, float Quality)? GeneratePdq(float[] luma, int width, int height)
	{
		if (width < 5 || height < 5)
		{
			return null;
		}
		return GeneratePdqFullSize(luma, width, height);
	}

	public static (byte[] Hash, float Quality) GeneratePdqFullSize(float[] luma, int width, int height)
	{
		int winRows = ComputeJaroszFilterWindowSize(width, 64);
		int winCols = ComputeJaroszFilterWindowSize(height, 64);
		float[] obj = (float[])luma.Clone();
		JaroszFilter(obj, height, width, winRows, winCols, 2);
		float[][] array = Decimate(obj, height, width, 64, 64);
		return (Hash: Buffer16x16ToBits(Dct64To16(array)), Quality: QualityMetric(array));
	}

	public static int ComputeJaroszFilterWindowSize(int oldDim, int newDim)
	{
		return (oldDim + 2 * newDim - 1) / (2 * newDim);
	}

	public static void JaroszFilter(float[] buffer, int rows, int cols, int winRows, int winCols, int reps)
	{
		float[] array = new float[buffer.Length];
		for (int i = 0; i < reps; i++)
		{
			BoxAlongRows(buffer, array, rows, cols, winRows);
			BoxAlongCols(array, buffer, rows, cols, winCols);
		}
	}

	public static void BoxAlongRows(float[] input, float[] output, int rows, int cols, int win)
	{
		for (int i = 0; i < rows; i++)
		{
			BoxOneD(input, i * cols, output, cols, 1, win);
		}
	}

	public static void BoxAlongCols(float[] input, float[] output, int rows, int cols, int win)
	{
		for (int i = 0; i < cols; i++)
		{
			BoxOneD(input, i, output, rows, cols, win);
		}
	}

	public static void BoxOneD(float[] inv, int inStartOffset, float[] outv, int vectorLen, int stride, int fullWin)
	{
		int num = (fullWin + 2) / 2;
		int num2 = num - 1;
		int num3 = fullWin - num + 1;
		int num4 = num2 * stride;
		int num5 = num3 * stride;
		float num6 = 0f;
		float num7 = 0f;
		int num8 = num4 + inStartOffset;
		for (int i = inStartOffset; i < num8; i += stride)
		{
			num6 += inv[i];
			num7 += 1f;
		}
		int num9 = fullWin * stride + inStartOffset;
		for (int j = num8; j < num9; j += stride)
		{
			int num10 = j - num4;
			num6 += inv[j];
			num7 += 1f;
			outv[num10] = num6 / num7;
		}
		int num11 = vectorLen * stride + inStartOffset;
		for (int k = num9; k < num11; k += stride)
		{
			int num12 = k - num4;
			int num13 = num12 - num5;
			num6 += inv[k];
			num6 -= inv[num13];
			outv[num12] = num6 / num7;
		}
		for (int l = (vectorLen - num + 1) * stride + inStartOffset; l < num11; l += stride)
		{
			int num14 = l - num5;
			num6 -= inv[num14];
			num7 -= 1f;
			outv[l] = num6 / num7;
		}
	}

	public static float[][] Decimate(float[] input, int inRows, int inCols, int outRows, int outCols)
	{
		float[][] array = new float[outRows][];
		for (int i = 0; i < outRows; i++)
		{
			array[i] = new float[outCols];
			int num = (i * 2 + 1) * inRows / (outRows * 2);
			for (int j = 0; j < outCols; j++)
			{
				int num2 = (j * 2 + 1) * inCols / (outCols * 2);
				array[i][j] = input[num * inCols + num2];
			}
		}
		return array;
	}

	public static float[] Dct64To16(float[][] input)
	{
		_ = input.Length;
		int num = input[0].Length;
		uint[][] matrix = PdqDctMatrix.Matrix;
		float[][] array = new float[16][];
		for (int i = 0; i < 16; i++)
		{
			array[i] = new float[num];
			for (int j = 0; j < num; j++)
			{
				float num2 = 0f;
				for (int k = 0; k < 64; k++)
				{
					num2 += BitConverter.UInt32BitsToSingle(matrix[i][k]) * input[k][j];
				}
				array[i][j] = num2;
			}
		}
		float[] array2 = new float[256];
		for (int l = 0; l < 16; l++)
		{
			for (int m = 0; m < 16; m++)
			{
				float num3 = 0f;
				for (int n = 0; n < 64; n++)
				{
					num3 += array[l][n] * BitConverter.UInt32BitsToSingle(matrix[m][n]);
				}
				array2[l * 16 + m] = num3;
			}
		}
		return array2;
	}

	public static float TorbenMedian(float[] m)
	{
		float num = m[0];
		float num2 = m[0];
		for (int i = 1; i < m.Length; i++)
		{
			if (m[i] < num)
			{
				num = m[i];
			}
			if (m[i] > num2)
			{
				num2 = m[i];
			}
		}
		int num3 = (m.Length + 1) / 2;
		float num4;
		int num5;
		int num7;
		float num8;
		float num9;
		while (true)
		{
			num4 = (num + num2) / 2f;
			num5 = 0;
			int num6 = 0;
			num7 = 0;
			num8 = num;
			num9 = num2;
			foreach (float num10 in m)
			{
				if (num10 < num4)
				{
					num5++;
					if (num10 > num8)
					{
						num8 = num10;
					}
				}
				else if (num10 > num4)
				{
					num6++;
					if (num10 < num9)
					{
						num9 = num10;
					}
				}
				else
				{
					num7++;
				}
			}
			if (num5 <= num3 && num6 <= num3)
			{
				break;
			}
			if (num5 > num6)
			{
				num2 = num8;
			}
			else
			{
				num = num9;
			}
		}
		if (num5 >= num3)
		{
			return num8;
		}
		if (num5 + num7 >= num3)
		{
			return num4;
		}
		return num9;
	}

	public static byte[] Buffer16x16ToBits(float[] input)
	{
		float num = TorbenMedian(input);
		byte[] array = new byte[32];
		for (int i = 0; i < 32; i++)
		{
			byte b = 0;
			for (int j = 0; j < 8; j++)
			{
				if (input[i * 8 + j] > num)
				{
					b |= (byte)(1 << j);
				}
			}
			array[32 - i - 1] = b;
		}
		return array;
	}

	public static float QualityMetric(float[][] buffer)
	{
		int num = buffer.Length;
		int num2 = buffer[0].Length;
		float num3 = 0f;
		for (int i = 0; i < num - 1; i++)
		{
			for (int j = 0; j < num2; j++)
			{
				float x = (buffer[i][j] - buffer[i + 1][j]) / 255f;
				num3 += MathF.Abs(x);
			}
		}
		for (int k = 0; k < num; k++)
		{
			for (int l = 0; l < num2 - 1; l++)
			{
				float x2 = (buffer[k][l] - buffer[k][l + 1]) / 255f;
				num3 += MathF.Abs(x2);
			}
		}
		float num4 = num3 / 90f;
		if (!(num4 > 1f))
		{
			return num4;
		}
		return 1f;
	}

	public static int HammingDistance(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
	{
		if (a.Length != b.Length)
		{
			throw new ArgumentException("hash length mismatch");
		}
		int num = 0;
		for (int i = 0; i < a.Length; i++)
		{
			num += BitOperations.PopCount((uint)(a[i] ^ b[i]));
		}
		return num;
	}

	public static string ToHex(byte[] hash)
	{
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	public static byte[] FromHex(string hex)
	{
		return Convert.FromHexString(hex);
	}

	public static int? HexHammingDistance(string left, string right)
	{
		if (left.Length != right.Length)
		{
			return null;
		}
		int num = 0;
		for (int i = 0; i < left.Length; i++)
		{
			int num2 = HexDigit(left[i]);
			int num3 = HexDigit(right[i]);
			if (num2 < 0 || num3 < 0)
			{
				return null;
			}
			num += BitOperations.PopCount((uint)(num2 ^ num3));
		}
		return num;
	}

	private static int HexDigit(char c)
	{
		if (c >= '0' && c <= '9')
		{
			return c - 48;
		}
		if (c >= 'a' && c <= 'f')
		{
			return 10 + (c - 97);
		}
		if (c >= 'A' && c <= 'F')
		{
			return 10 + (c - 65);
		}
		return -1;
	}

	public static int? ClosestHashDistance(IReadOnlyList<string> leftVariants, IReadOnlyList<string> rightVariants)
	{
		if (leftVariants.Count == 0 || rightVariants.Count == 0)
		{
			return null;
		}
		int num = int.MaxValue;
		foreach (string leftVariant in leftVariants)
		{
			foreach (string rightVariant in rightVariants)
			{
				int? num2 = HexHammingDistance(leftVariant, rightVariant);
				if (num2.HasValue)
				{
					if (num2.Value < num)
					{
						num = num2.Value;
					}
					if (num == 0)
					{
						return 0;
					}
				}
			}
		}
		if (num != int.MaxValue)
		{
			return num;
		}
		return null;
	}

	public static List<string> ParseHashVariants(string? value)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrEmpty(value))
		{
			return list;
		}
		string[] array = value.Split('|');
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim().ToLowerInvariant();
			if (text.Length != 64)
			{
				continue;
			}
			bool flag = true;
			string text2 = text;
			for (int j = 0; j < text2.Length; j++)
			{
				if (HexDigit(text2[j]) < 0)
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				list.Add(text);
			}
		}
		return list;
	}

	public static (float[] luma, int width, int height) Rotate90(float[] luma, int w, int h)
	{
		float[] array = new float[w * h];
		for (int i = 0; i < h; i++)
		{
			for (int j = 0; j < w; j++)
			{
				array[j * h + (h - 1 - i)] = luma[i * w + j];
			}
		}
		return (luma: array, width: h, height: w);
	}

	public static (float[] luma, int width, int height) Rotate180(float[] luma, int w, int h)
	{
		float[] array = new float[w * h];
		for (int i = 0; i < h; i++)
		{
			for (int j = 0; j < w; j++)
			{
				array[(h - 1 - i) * w + (w - 1 - j)] = luma[i * w + j];
			}
		}
		return (luma: array, width: w, height: h);
	}

	public static (float[] luma, int width, int height) Rotate270(float[] luma, int w, int h)
	{
		float[] array = new float[w * h];
		for (int i = 0; i < h; i++)
		{
			for (int j = 0; j < w; j++)
			{
				array[(w - 1 - j) * h + i] = luma[i * w + j];
			}
		}
		return (luma: array, width: h, height: w);
	}

	public static string ComputeHashVariantsHex(float[] luma, int w, int h)
	{
		string[] array = new string[4];
		(byte[], float)? tuple = GeneratePdq(luma, w, h);
		if (!tuple.HasValue)
		{
			return string.Empty;
		}
		array[0] = ToHex(tuple.Value.Item1);
		(float[] luma, int width, int height) tuple2 = Rotate90(luma, w, h);
		float[] item = tuple2.luma;
		int item2 = tuple2.width;
		int item3 = tuple2.height;
		(byte[], float)? tuple3 = GeneratePdq(item, item2, item3);
		array[1] = ((!tuple3.HasValue) ? string.Empty : ToHex(tuple3.Value.Item1));
		(float[] luma, int width, int height) tuple4 = Rotate180(luma, w, h);
		float[] item4 = tuple4.luma;
		int item5 = tuple4.width;
		int item6 = tuple4.height;
		(byte[], float)? tuple5 = GeneratePdq(item4, item5, item6);
		array[2] = ((!tuple5.HasValue) ? string.Empty : ToHex(tuple5.Value.Item1));
		(float[] luma, int width, int height) tuple6 = Rotate270(luma, w, h);
		float[] item7 = tuple6.luma;
		int item8 = tuple6.width;
		int item9 = tuple6.height;
		(byte[], float)? tuple7 = GeneratePdq(item7, item8, item9);
		array[3] = ((!tuple7.HasValue) ? string.Empty : ToHex(tuple7.Value.Item1));
		List<string> list = new List<string>();
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (!string.IsNullOrEmpty(text))
			{
				list.Add(text);
			}
		}
		return string.Join("|", list);
	}
}
