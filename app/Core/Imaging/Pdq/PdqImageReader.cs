using System;
using System.Buffers;
using System.IO;
using System.Threading;
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
    public static async Task<(float[] luma, int width, int height)?> ReadLumaAsync(
        string path,
        CancellationToken ct = default)
    {
        var image = await ReadLumaOwnerAsync(path, usePool: false, ct).ConfigureAwait(false);
        if (image is null) return null;
        return (image.DetachBuffer(), image.Width, image.Height);
    }

    /// <summary>
    /// 連続解析用に ArrayPool から借りた輝度配列を返す。
    /// 呼出側はハッシュ計算後に必ず Dispose し、次の画像処理へ作業領域を返却する。
    /// </summary>
    internal static Task<PdqLumaImage?> ReadPooledLumaAsync(
        string path,
        CancellationToken ct = default)
        => ReadLumaOwnerAsync(path, usePool: true, ct);

    private static async Task<PdqLumaImage?> ReadLumaOwnerAsync(
        string path,
        bool usePool,
        CancellationToken ct)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path.Replace('/', '\\')).AsTask(ct).ConfigureAwait(false);
            using var stream = await file.OpenAsync(FileAccessMode.Read).AsTask(ct).ConfigureAwait(false);
            return await ReadLumaCoreAsync(stream, usePool, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"PdqImageReader.ReadLumaAsync failed [{path}]: {ex.Message}");
            return null;
        }
    }

    /// <summary>IRandomAccessStream から画像をデコードし、必要に応じて縮小して luma 配列へ変換する。</summary>
    private static async Task<PdqLumaImage?> ReadLumaCoreAsync(
        IRandomAccessStream stream,
        bool usePool,
        CancellationToken ct)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
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
            ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);
        var bgra = pixelData.DetachPixelData();

        var pixelCount = targetW * targetH;
        var luma = usePool
            ? ArrayPool<float>.Shared.Rent(pixelCount)
            : new float[pixelCount];
        try
        {
            for (var i = 0; i < pixelCount; i++)
            {
                var idx = i * 4;
                var b = bgra[idx];
                var g = bgra[idx + 1];
                var r = bgra[idx + 2];
                luma[i] = r * PdqHasher.LumaFromR + g * PdqHasher.LumaFromG + b * PdqHasher.LumaFromB;
            }
            return new PdqLumaImage(luma, targetW, targetH, usePool);
        }
        catch
        {
            if (usePool)
                ArrayPool<float>.Shared.Return(luma);
            throw;
        }
    }
}

/// <summary>PDQ 連続解析中だけ所有し、破棄時に輝度配列を共有プールへ返す。</summary>
internal sealed class PdqLumaImage : IDisposable
{
    private float[]? buffer;
    private readonly bool isPooled;

    internal PdqLumaImage(float[] buffer, int width, int height, bool isPooled)
    {
        this.buffer = buffer;
        Width = width;
        Height = height;
        this.isPooled = isPooled;
    }

    internal float[] Buffer => buffer ?? throw new ObjectDisposedException(nameof(PdqLumaImage));
    internal int Width { get; }
    internal int Height { get; }

    internal float[] DetachBuffer()
    {
        if (isPooled)
            throw new InvalidOperationException("プール配列は所有権を切り離せません。");
        return Interlocked.Exchange(ref buffer, null)
            ?? throw new ObjectDisposedException(nameof(PdqLumaImage));
    }

    public void Dispose()
    {
        var released = Interlocked.Exchange(ref buffer, null);
        if (released is not null && isPooled)
            ArrayPool<float>.Shared.Return(released);
    }
}
