using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Alpheratz.Core.Imaging;

public sealed class ThumbnailService
{
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> _pathLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

	public async Task<string> EnsureGridThumbAsync(string photoPath, long sourceSlot, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			return await EnsureThumbAsync(photoPath, sourceSlot, 512u, "grid.v3", ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"ThumbnailService.EnsureGridThumbAsync: threw: {value}");
			throw;
		}
	}

	public async Task<string> EnsureDisplayThumbAsync(string photoPath, long sourceSlot, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			return await EnsureThumbAsync(photoPath, sourceSlot, 514u, "display.v2", ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"ThumbnailService.EnsureDisplayThumbAsync: threw: {value}");
			throw;
		}
	}

	private static async Task<string> EnsureThumbAsync(string photoPath, long sourceSlot, uint maxSize, string version, CancellationToken ct)
	{
		_ = 1;
		try
		{
			string path = AppPaths.GetImgCacheDir(sourceSlot) ?? throw new InvalidOperationException("imgCache フォルダを取得できません");
			string fileName = Path.GetFileName(photoPath);
			string thumbPath = Path.Combine(path, fileName + ".thumb." + version + ".jpg");
			SemaphoreSlim pathLock = _pathLocks.GetOrAdd(thumbPath, (string _) => new SemaphoreSlim(1, 1));
			await pathLock.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				if (File.Exists(thumbPath))
				{
					DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(photoPath.Replace('/', Path.DirectorySeparatorChar));
					DateTime lastWriteTimeUtc2 = File.GetLastWriteTimeUtc(thumbPath);
					if (lastWriteTimeUtc <= lastWriteTimeUtc2)
					{
						return thumbPath;
					}
					File.Delete(thumbPath);
				}
				await GenerateThumbnailAsync(photoPath, thumbPath, maxSize, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			finally
			{
				pathLock.Release();
			}
			if (pathLock.CurrentCount == 1)
			{
				_pathLocks.TryRemove(thumbPath, out SemaphoreSlim _);
			}
			return thumbPath;
		}
		catch (Exception value2)
		{
			AppLogger.Error($"ThumbnailService.EnsureThumbAsync: threw: {value2}");
			throw;
		}
	}

	private static async Task GenerateThumbnailAsync(string sourcePath, string destPath, uint maxSize, CancellationToken ct)
	{
		_ = 6;
		try
		{
			using IRandomAccessStreamWithContentType sourceStream = await (await StorageFile.GetFileFromPathAsync(sourcePath.Replace('/', Path.DirectorySeparatorChar)).AsTask(ct).ConfigureAwait(continueOnCapturedContext: false)).OpenReadAsync().AsTask(ct).ConfigureAwait(continueOnCapturedContext: false);
			BitmapDecoder bitmapDecoder = await BitmapDecoder.CreateAsync(sourceStream).AsTask(ct).ConfigureAwait(continueOnCapturedContext: false);
			uint orientedPixelWidth = bitmapDecoder.OrientedPixelWidth;
			uint orientedPixelHeight = bitmapDecoder.OrientedPixelHeight;
			double val = Math.Min((double)maxSize / (double)orientedPixelWidth, (double)maxSize / (double)orientedPixelHeight);
			val = Math.Min(val, 1.0);
			uint finalW = (uint)Math.Round((double)orientedPixelWidth * val);
			uint finalH = (uint)Math.Round((double)orientedPixelHeight * val);
			bool num = orientedPixelWidth != bitmapDecoder.PixelWidth;
			uint scaledWidth = (num ? finalH : finalW);
			uint scaledHeight = (num ? finalW : finalH);
			BitmapTransform transform = new BitmapTransform
			{
				ScaledWidth = scaledWidth,
				ScaledHeight = scaledHeight,
				InterpolationMode = BitmapInterpolationMode.Fant
			};
			byte[] pixels = (await bitmapDecoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(continueOnCapturedContext: false)).DetachPixelData();
			FileStream fileStream = new FileStream(destPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, useAsync: true);
			IRandomAccessStream outStream = null;
			try
			{
				outStream = fileStream.AsRandomAccessStream();
				BitmapEncoder obj = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream).AsTask(ct).ConfigureAwait(continueOnCapturedContext: false);
				obj.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, finalW, finalH, 96.0, 96.0, pixels);
				await obj.FlushAsync().AsTask(ct).ConfigureAwait(continueOnCapturedContext: false);
				await fileStream.FlushAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			finally
			{
				outStream?.Dispose();
				fileStream.Dispose();
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ThumbnailService.GenerateThumbnailAsync: threw: {value}");
			throw;
		}
	}
}
