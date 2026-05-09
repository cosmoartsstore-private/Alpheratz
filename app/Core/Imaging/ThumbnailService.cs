using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Alpheratz.Core.Imaging;

public sealed class ThumbnailService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _pathLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string> EnsureGridThumbAsync(string photoPath, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"ThumbnailService.EnsureGridThumbAsync: enter path={photoPath} slot={sourceSlot}");
        try
        {
            // 512px on the longest edge. Cache key bumped to v3 so previously
            // generated 384px files (v2) get regenerated rather than served
            // as upscaled blurs.
            var path = await EnsureThumbAsync(photoPath, sourceSlot, 512, "grid.v3", ct).ConfigureAwait(false);
            AppLogger.Trace("ThumbnailService.EnsureGridThumbAsync: exit");
            return path;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ThumbnailService.EnsureGridThumbAsync: threw: {ex}");
            throw;
        }
    }

    public async Task<string> EnsureDisplayThumbAsync(string photoPath, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"ThumbnailService.EnsureDisplayThumbAsync: enter path={photoPath} slot={sourceSlot}");
        try
        {
            var path = await EnsureThumbAsync(photoPath, sourceSlot, 514, "display.v2", ct).ConfigureAwait(false);
            AppLogger.Trace("ThumbnailService.EnsureDisplayThumbAsync: exit");
            return path;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ThumbnailService.EnsureDisplayThumbAsync: threw: {ex}");
            throw;
        }
    }

    public async Task<byte[]> ReadThumbBytesAsync(string photoPath, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"ThumbnailService.ReadThumbBytesAsync: enter path={photoPath} slot={sourceSlot}");
        try
        {
            var thumbPath = await EnsureGridThumbAsync(photoPath, sourceSlot, ct).ConfigureAwait(false);
            var bytes = await File.ReadAllBytesAsync(thumbPath, ct).ConfigureAwait(false);
            AppLogger.Trace($"ThumbnailService.ReadThumbBytesAsync: exit bytes={bytes.Length}");
            return bytes;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ThumbnailService.ReadThumbBytesAsync: threw: {ex}");
            throw;
        }
    }

    public async Task<byte[]> ReadDisplayThumbBytesAsync(string photoPath, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"ThumbnailService.ReadDisplayThumbBytesAsync: enter path={photoPath} slot={sourceSlot}");
        try
        {
            var thumbPath = await EnsureDisplayThumbAsync(photoPath, sourceSlot, ct).ConfigureAwait(false);
            var bytes = await File.ReadAllBytesAsync(thumbPath, ct).ConfigureAwait(false);
            AppLogger.Trace($"ThumbnailService.ReadDisplayThumbBytesAsync: exit bytes={bytes.Length}");
            return bytes;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ThumbnailService.ReadDisplayThumbBytesAsync: threw: {ex}");
            throw;
        }
    }

    private static async Task<string> EnsureThumbAsync(string photoPath, long sourceSlot, uint maxSize, string version, CancellationToken ct)
    {
        AppLogger.Trace($"ThumbnailService.EnsureThumbAsync: enter version={version} maxSize={maxSize}");
        try
        {
            var imgCacheDir = AppPaths.GetImgCacheDir(sourceSlot)
                ?? throw new InvalidOperationException("imgCache フォルダを取得できません");
            var filename = Path.GetFileName(photoPath);
            var thumbPath = Path.Combine(imgCacheDir, $"{filename}.thumb.{version}.jpg");

            var pathLock = _pathLocks.GetOrAdd(thumbPath, _ => new SemaphoreSlim(1, 1));
            await pathLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (File.Exists(thumbPath))
                {
                    var sourceModified = File.GetLastWriteTimeUtc(photoPath.Replace('/', Path.DirectorySeparatorChar));
                    var cacheModified = File.GetLastWriteTimeUtc(thumbPath);
                    if (sourceModified <= cacheModified)
                    {
                        AppLogger.Trace("ThumbnailService.EnsureThumbAsync: exit (cache hit)");
                        return thumbPath;
                    }
                    File.Delete(thumbPath);
                }

                await GenerateThumbnailAsync(photoPath, thumbPath, maxSize, ct).ConfigureAwait(false);
            }
            finally
            {
                pathLock.Release();
            }

            AppLogger.Trace("ThumbnailService.EnsureThumbAsync: exit (generated)");
            return thumbPath;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ThumbnailService.EnsureThumbAsync: threw: {ex}");
            throw;
        }
    }

    private static async Task GenerateThumbnailAsync(string sourcePath, string destPath, uint maxSize, CancellationToken ct)
    {
        AppLogger.Trace($"ThumbnailService.GenerateThumbnailAsync: enter src={sourcePath} maxSize={maxSize}");
        try
        {
            // StorageFile.GetFileFromPathAsync rejects forward-slash separators
            // even on Windows (0x800700A1). DB-normalised paths use '/', so
            // flip them back before handing off to WinRT.
            var winPath = sourcePath.Replace('/', Path.DirectorySeparatorChar);
            var sourceFile = await StorageFile.GetFileFromPathAsync(winPath).AsTask(ct).ConfigureAwait(false);
            using var sourceStream = await sourceFile.OpenReadAsync().AsTask(ct).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(sourceStream).AsTask(ct).ConfigureAwait(false);

            var origW = decoder.PixelWidth;
            var origH = decoder.PixelHeight;
            double scale = Math.Min((double)maxSize / origW, (double)maxSize / origH);
            scale = Math.Min(scale, 1.0);
            var newW = (uint)Math.Round(origW * scale);
            var newH = (uint)Math.Round(origH * scale);

            var transform = new BitmapTransform { ScaledWidth = newW, ScaledHeight = newH, InterpolationMode = BitmapInterpolationMode.Fant };
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);
            var pixels = pixelData.DetachPixelData();

            using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, useAsync: true);
            using var outStream = fileStream.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream).AsTask(ct).ConfigureAwait(false);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, newW, newH, 96, 96, pixels);
            await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ThumbnailService.GenerateThumbnailAsync: threw: {ex}");
            throw;
        }
        AppLogger.Trace("ThumbnailService.GenerateThumbnailAsync: exit");
    }
}
