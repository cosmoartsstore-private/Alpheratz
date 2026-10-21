using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Scanner;
using Alpheratz.Features.Gallery;
using Alpheratz.Services;
using Microsoft.Data.Sqlite;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Alpheratz.Tests;

public sealed partial class ImagingIoBehaviorTests
{
    /// <summary>製品が列挙する全画像形式を解析し、検索/詳細の同じ値とコピー先の実decodeまで追跡する。</summary>
    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    public async Task SupportedImageMetadataAndCopiedPixelsRemainUsableAcrossDatabaseReopen(string extension)
    {
        const uint width = 40, height = 60;
        var path = Path.Combine(tempDir, "VRChat_2026-10-20_12-34-56.789_40x60." + extension);
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 30; pixels[i + 1] = 60; pixels[i + 2] = 180; pixels[i + 3] = 255; }
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            using var stream = file.AsRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(extension == "png" ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels); await encoder.FlushAsync();
        }
        var bytes = File.ReadAllBytes(path); var originalPixels = await Decode(path);
        var metadata = PhotoScanner.AnalyzePhoto(path, Path.GetFileName(path), 2, null, ScanRefreshKind.Full);
        Assert.Equal("portrait", metadata.Orientation); Assert.Equal(40L, metadata.ImageWidth); Assert.Equal(60L, metadata.ImageHeight); Assert.Equal("2026-10-20 12:34:56", metadata.Timestamp);
        var dbPath = Path.Combine(tempDir, "delivery.db"); var db = new AlpheratzDb(dbPath); db.Initialize();
        try
        {
            await db.UpsertPhotoAsync(metadata); await db.SetPhotoFavoriteAsync(metadata.PhotoPath, true); await db.AddPhotoTagAsync(metadata.PhotoPath, "選択したタグ");
            var reopened = new AlpheratzDb(dbPath); var service = new PhotoService(reopened);
            var result = await service.GetPhotosAsync(new Alpheratz.Models.PhotoQueryPayload(null, null, null, null, "portrait", true, ["選択したタグ"], null, null, null));
            Assert.Equal(1, result.total); var selected = Assert.Single(result.items); var modal = PhotoThumbnailItem.FromDto(selected);
            Assert.Equal(metadata.PhotoPath, modal.PhotoPath); Assert.Equal(metadata.Timestamp, modal.Timestamp); Assert.Equal(["選択したタグ"], modal.Tags); Assert.True(modal.IsFavorite);
            var groups = await service.GetWorldGroupPhotosAsync(Alpheratz.Models.WorldFilterValues.Unknown, null, null, null, null, null, null); Assert.Contains(groups, photo => photo.photo_path == metadata.PhotoPath);
            var destination = Path.Combine(tempDir, "コピー先"); Directory.CreateDirectory(destination);
            var copy = await service.BulkCopyPhotosAsync([new Alpheratz.Models.SelectedPhotoRefDto { photo_path = selected.photo_path, source_slot = selected.source_slot }], destination);
            Assert.Equal((1, 0), copy); var copied = Path.Combine(destination, selected.photo_filename);
            Assert.Equal(bytes, File.ReadAllBytes(copied)); Assert.Equal(originalPixels, await Decode(copied)); Assert.Equal(bytes, File.ReadAllBytes(path));
            var thumbService = new Alpheratz.Core.Imaging.ThumbnailService(); var thumb = await thumbService.EnsureGridThumbAsync(metadata.PhotoPath, 2);
            Assert.NotEmpty(await Decode(thumb)); Assert.Equal(thumb, await thumbService.EnsureGridThumbAsync(metadata.PhotoPath, 2)); Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()); SqliteConnection.ClearPool(connection); }
    }

    private static async Task<byte[]> Decode(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream); var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var pixels = data.DetachPixelData(); Assert.Equal((int)(decoder.PixelWidth * decoder.PixelHeight * 4), pixels.Length); return pixels;
    }
}
