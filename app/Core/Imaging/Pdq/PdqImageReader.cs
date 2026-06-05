using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Alpheratz.Core.Imaging.Pdq;

/// <summary>
/// 画像ファイルを Windows Imaging で読み込み、PDQ 計算用の輝度バッファへ変換する。
/// 長辺 512px 以下に縮小してから単一チャンネル化し、ハッシュ計算の入力サイズを安定させる。
/// </summary>
public static class PdqImageReader
{
    /// <summary>指定パスの画像を読み込み、PDQ 用 luma 配列と縮小後サイズを返す。読めない場合は null。</summary>
    public static async Task<(float[] luma, int width, int height)?> ReadLumaAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path.Replace('/', '\\'));
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            return await ReadLumaCoreAsync(stream).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"PdqImageReader.ReadLumaAsync failed [{path}]: {ex.Message}");
            return null;
        }
    }

    /// <summary>IRandomAccessStream から画像をデコードし、必要に応じて縮小して luma 配列へ変換する。</summary>
    private static async Task<(float[] luma, int width, int height)?> ReadLumaCoreAsync(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var origW = (int)decoder.PixelWidth;
        var origH = (int)decoder.PixelHeight;
        if (origW < PdqHasher.MinHashableDim || origH < PdqHasher.MinHashableDim) return null;

        int targetW, targetH;
        if (origW > PdqHasher.DownsampleDims || origH > PdqHasher.DownsampleDims)
        {
            if (origW >= origH)
            {
                targetW = PdqHasher.DownsampleDims;
                targetH = Math.Max(1, (int)((double)origH / origW * PdqHasher.DownsampleDims));
            }
            else
            {
                targetH = PdqHasher.DownsampleDims;
                targetW = Math.Max(1, (int)((double)origW / origH * PdqHasher.DownsampleDims));
            }
        }
        else
        {
            targetW = origW;
            targetH = origH;
        }

        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)targetW,
            ScaledHeight = (uint)targetH,
            InterpolationMode = BitmapInterpolationMode.Linear,
        };

        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var bgra = pixelData.DetachPixelData();

        var pixelCount = targetW * targetH;
        var luma = new float[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;
            var b = bgra[idx];
            var g = bgra[idx + 1];
            var r = bgra[idx + 2];
            luma[i] = r * PdqHasher.LumaFromR + g * PdqHasher.LumaFromG + b * PdqHasher.LumaFromB;
        }
        return (luma, targetW, targetH);
    }
}
