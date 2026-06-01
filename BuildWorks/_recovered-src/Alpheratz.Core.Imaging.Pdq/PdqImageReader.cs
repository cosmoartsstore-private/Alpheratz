using System;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Alpheratz.Core.Imaging.Pdq;

public static class PdqImageReader
{
	public static async Task<(float[] luma, int width, int height)?> ReadLumaAsync(string path)
	{
		_ = 2;
		try
		{
			using IRandomAccessStream stream = await (await StorageFile.GetFileFromPathAsync(path.Replace('/', '\\'))).OpenAsync(FileAccessMode.Read);
			return await ReadLumaCoreAsync(stream).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			AppLogger.Warn("PdqImageReader.ReadLumaAsync failed [" + path + "]: " + ex.Message);
			return null;
		}
	}

	private static async Task<(float[] luma, int width, int height)?> ReadLumaCoreAsync(IRandomAccessStream stream)
	{
		BitmapDecoder bitmapDecoder = await BitmapDecoder.CreateAsync(stream);
		int pixelWidth = (int)bitmapDecoder.PixelWidth;
		int pixelHeight = (int)bitmapDecoder.PixelHeight;
		if (pixelWidth < 5 || pixelHeight < 5)
		{
			return null;
		}
		int targetW;
		int targetH;
		if (pixelWidth > 512 || pixelHeight > 512)
		{
			if (pixelWidth >= pixelHeight)
			{
				targetW = 512;
				targetH = Math.Max(1, (int)((double)pixelHeight / (double)pixelWidth * 512.0));
			}
			else
			{
				targetH = 512;
				targetW = Math.Max(1, (int)((double)pixelWidth / (double)pixelHeight * 512.0));
			}
		}
		else
		{
			targetW = pixelWidth;
			targetH = pixelHeight;
		}
		BitmapTransform transform = new BitmapTransform
		{
			ScaledWidth = (uint)targetW,
			ScaledHeight = (uint)targetH,
			InterpolationMode = BitmapInterpolationMode.Linear
		};
		byte[] array = (await bitmapDecoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
		int num = targetW * targetH;
		float[] array2 = new float[num];
		for (int i = 0; i < num; i++)
		{
			int num2 = i * 4;
			byte b = array[num2];
			byte b2 = array[num2 + 1];
			byte b3 = array[num2 + 2];
			array2[i] = (float)(int)b3 * 0.299f + (float)(int)b2 * 0.587f + (float)(int)b * 0.114f;
		}
		return (array2, targetW, targetH);
	}
}
