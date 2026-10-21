using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Alpheratz.Tests;

/// <summary>既存の厳密な取消しテストを保持し、実SDKの完了済みWinRT操作との境界を別に確認する。</summary>
public sealed partial class ImagingIoBehaviorTests
{
    [Fact]
    public async Task CompletedWinRtImageOperationsKeepTheirResultsWhenAdaptedWithACancelledToken()
    {
        var path = Path.Combine(tempDir, "completed-cancellation-boundary.png"); await WriteGradientPngAsync(path, 96, 96);
        var bytes = File.ReadAllBytes(path); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();

        var fileOperation = StorageFile.GetFileFromPathAsync(path); var file = await fileOperation.AsTask();
        Assert.Equal(AsyncStatus.Completed, fileOperation.Status); Assert.Equal(path, (await fileOperation.AsTask(cancellation.Token)).Path);
        var streamOperation = file.OpenAsync(FileAccessMode.Read); using var stream = await streamOperation.AsTask();
        Assert.Equal(AsyncStatus.Completed, streamOperation.Status); Assert.Equal(stream.Size, (await streamOperation.AsTask(cancellation.Token)).Size);
        var decoderOperation = BitmapDecoder.CreateAsync(stream); var decoder = await decoderOperation.AsTask();
        Assert.Equal(AsyncStatus.Completed, decoderOperation.Status); var completedDecoder = await decoderOperation.AsTask(cancellation.Token);
        Assert.Equal(96U, completedDecoder.PixelWidth); Assert.Equal(96U, completedDecoder.PixelHeight);
        var pixelOperation = decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        await pixelOperation.AsTask(); Assert.Equal(AsyncStatus.Completed, pixelOperation.Status);
        var pixels = (await pixelOperation.AsTask(cancellation.Token)).DetachPixelData();
        Assert.Equal(96 * 96 * 4, pixels.Length); Assert.Contains(pixels, value => value != 0); Assert.Equal(bytes, File.ReadAllBytes(path));
    }
}
