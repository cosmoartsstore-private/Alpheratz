using System;
using System.IO;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Alpheratz.Shared.Services;

public sealed class ImageLifetimeService
{
    // TS equivalent: useDeferredImageSrc + URL.createObjectURL / URL.revokeObjectURL.
    public async Task<BitmapImage> createBitmapImageAsync(byte[]? bytes)
    {
        AppLogger.Trace($"ImageLifetimeService.createBitmapImageAsync: enter bytes={bytes?.Length ?? 0}");
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            var image = new BitmapImage();
            // R2-A-11: 旧実装は MemoryStream を using で囲んでいたが、SetSourceAsync は
            // 内部でストリームを非同期に読むため、`using` のスコープ終了で Dispose されると
            // デコード途中に基底ストリームが消えて ObjectDisposedException が出る。
            // バイト列を InMemoryRandomAccessStream に書き出してから渡し、メソッドの寿命を超えても
            // BitmapImage が GC されるまで保持されるようにする。
            var ras = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            ras.Seek(0);
            await image.SetSourceAsync(ras);
            AppLogger.Trace("ImageLifetimeService.createBitmapImageAsync: exit");
            return image;
        }
        catch (Exception ex)
        {
            // Rethrow: callers expect a usable BitmapImage and have no
            // sensible fallback when decoding fails.
            AppLogger.Error($"ImageLifetimeService.createBitmapImageAsync: threw: {ex}");
            throw;
        }
    }

    public void revokeObjectUrlEquivalent(BitmapImage? image)
    {
        AppLogger.Trace("ImageLifetimeService.revokeObjectUrlEquivalent: enter");
        // WinUI image source release is performed by dropping references.
        // Caller must clear Image.Source and item-level reference.
        AppLogger.Trace("ImageLifetimeService.revokeObjectUrlEquivalent: exit");
    }
}