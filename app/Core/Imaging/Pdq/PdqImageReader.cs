using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Alpheratz.Core.Imaging.Pdq;

// Loads an image off-disk and returns a luminance buffer suited for PDQ.
// Mirrors the Rust pipeline: thumbnail down to <=512 along the long edge,
// preserve aspect ratio, then convert to single-channel f32.
public static class PdqImageReader
{
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

    private static async Task<(float[] luma, int width, int height)?> ReadLumaCoreAsync(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        // OrientedPixelWidth/Height は EXIF 回転 *後* の寸法 (ユーザ視点)。
        // EXIF が 90°/270° 回転を指示しているケースでは Oriented と Raw で軸が入れ替わる。
        var orientedW = (int)decoder.OrientedPixelWidth;
        var orientedH = (int)decoder.OrientedPixelHeight;
        if (orientedW < PdqHasher.MinHashableDim || orientedH < PdqHasher.MinHashableDim) return null;

        int finalW, finalH;
        if (orientedW > PdqHasher.DownsampleDims || orientedH > PdqHasher.DownsampleDims)
        {
            if (orientedW >= orientedH)
            {
                finalW = PdqHasher.DownsampleDims;
                finalH = Math.Max(1, (int)((double)orientedH / orientedW * PdqHasher.DownsampleDims));
            }
            else
            {
                finalH = PdqHasher.DownsampleDims;
                finalW = Math.Max(1, (int)((double)orientedW / orientedH * PdqHasher.DownsampleDims));
            }
        }
        else
        {
            finalW = orientedW;
            finalH = orientedH;
        }

        // BitmapTransform.ScaledWidth/Height は EXIF 回転 *前* の raw 寸法に適用されるため、
        // EXIF が軸入れ替えしているなら transform 側も入れ替える必要がある
        // (ThumbnailService.GenerateThumbnailAsync と同じロジック)。
        bool axisSwapped = orientedW != (int)decoder.PixelWidth;
        var transformW = axisSwapped ? finalH : finalW;
        var transformH = axisSwapped ? finalW : finalH;

        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)transformW,
            ScaledHeight = (uint)transformH,
            InterpolationMode = BitmapInterpolationMode.Linear,
        };

        // EXIF Orientation を尊重して「ユーザに見える向き」の luma を計算する。
        // ThumbnailService 側も RespectExifOrientation を使っているので両者で一致させる
        // (旧実装 IgnoreExifOrientation だと EXIF 回転のみで同一内容の重複が検出できなかった)。
        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var bgra = pixelData.DetachPixelData();

        var pixelCount = finalW * finalH;
        var luma = new float[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;
            var b = bgra[idx];
            var g = bgra[idx + 1];
            var r = bgra[idx + 2];
            luma[i] = r * PdqHasher.LumaFromR + g * PdqHasher.LumaFromG + b * PdqHasher.LumaFromB;
        }
        return (luma, finalW, finalH);
    }
}
