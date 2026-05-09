using System;
using System.IO;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml.Media.Imaging;

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
            using var stream = new MemoryStream(bytes);
            var randomAccessStream = stream.AsRandomAccessStream();
            await image.SetSourceAsync(randomAccessStream);
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
