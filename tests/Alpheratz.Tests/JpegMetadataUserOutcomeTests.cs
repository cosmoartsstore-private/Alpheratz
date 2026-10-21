using System.Buffers.Binary;
using System.Text.Json;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Scanner;
using Microsoft.Data.Sqlite;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Alpheratz.Tests;

public sealed partial class ImagingIoBehaviorTests
{
    /// <summary>実decoderで表示可能なJPEGを解析・保存し、Exif破損によって縦向き検索から消えないことを確認する。</summary>
    [Theory]
    [InlineData(true, 0x7ffffffeu)]
    [InlineData(false, 0x7ffffffeu)]
    [InlineData(true, 0x7fffffffu)]
    [InlineData(false, 0x7fffffffu)]
    public async Task DecodableJpegWithMalformedExifRemainsInPortraitResults(bool littleEndian, uint ifdOffset)
    {
        const uint width = 32, height = 48;
        var path = Path.Combine(tempDir, "VRChat_2026-10-12_00-10-00.000_32x48.jpg");
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4) { pixels[index] = 40; pixels[index + 1] = 80; pixels[index + 2] = 120; pixels[index + 3] = 255; }
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            using var random = stream.AsRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, random);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels); await encoder.FlushAsync();
        }
        var encoded = File.ReadAllBytes(path); Assert.Equal(new byte[] { 0xff, 0xd8 }, encoded.Take(2).ToArray());
        byte[] exif = [0x45, 0x78, 0x69, 0x66, 0, 0, littleEndian ? (byte)'I' : (byte)'M', littleEndian ? (byte)'I' : (byte)'M', 0, 0, 0, 0, 0, 0];
        if (littleEndian) { BinaryPrimitives.WriteUInt16LittleEndian(exif.AsSpan(8), 42); BinaryPrimitives.WriteUInt32LittleEndian(exif.AsSpan(10), ifdOffset); }
        else { BinaryPrimitives.WriteUInt16BigEndian(exif.AsSpan(8), 42); BinaryPrimitives.WriteUInt32BigEndian(exif.AsSpan(10), ifdOffset); }
        // SOIの直後へ破損Exif APP1を挿入する。圧縮画素とSOFは実encoderの出力をそのまま保持する。
        var bytes = new byte[encoded.Length + exif.Length + 4]; encoded.AsSpan(0, 2).CopyTo(bytes);
        bytes[2] = 0xff; bytes[3] = 0xe1; BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)(exif.Length + 2));
        exif.CopyTo(bytes, 6); encoded.AsSpan(2).CopyTo(bytes.AsSpan(6 + exif.Length)); File.WriteAllBytes(path, bytes);

        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var random = stream.AsRandomAccessStream(); var decoder = await BitmapDecoder.CreateAsync(random);
            Assert.Equal(width, decoder.PixelWidth); Assert.Equal(height, decoder.PixelHeight);
            var decoded = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var actualPixels = decoded.DetachPixelData(); Assert.Equal(pixels.Length, actualPixels.Length); Assert.Contains(actualPixels, value => value != 0);
        }
        var photo = PhotoScanner.AnalyzePhoto(path, Path.GetFileName(path), 1, null, ScanRefreshKind.Full);
        var dbPath = Path.Combine(tempDir, "image-outcomes.db"); var db = new AlpheratzDb(dbPath); db.Initialize();
        try
        {
            await db.UpsertPhotoAsync(photo); await db.SetPhotoFavoriteAsync(photo.PhotoPath, true); await db.AddPhotoTagAsync(photo.PhotoPath, "保持するタグ");
            var portrait = await db.GetPhotosPageAsync(new PhotoQueryParams { Orientation = "portrait", FavoritesOnly = true, TagFilters = ["保持するタグ"] });
            var landscape = await db.GetPhotosPageAsync(new PhotoQueryParams { Orientation = "landscape" });
            if (Environment.GetEnvironmentVariable("ALPHERATZ_IMAGE_OUTCOME_EVIDENCE_DIR") is { Length: > 0 } evidence)
            {
                var prefix = $"jpeg-{littleEndian}-{ifdOffset}"; File.Copy(path, Path.Combine(evidence, prefix + ".jpg"));
                File.WriteAllText(Path.Combine(evidence, prefix + ".json"), JsonSerializer.Serialize(new
                {
                    actual_windows_decoder_succeeded = true, decoder_width = width, decoder_height = height,
                    malformed_ifd_offset = ifdOffset, little_endian = littleEndian, metadata_orientation = photo.Orientation,
                    metadata_width = photo.ImageWidth, metadata_height = photo.ImageHeight,
                    portrait_count = portrait.Total, landscape_count = landscape.Total, source_bytes_preserved = bytes.SequenceEqual(File.ReadAllBytes(path)),
                    actual_gallery_controls_verified = false, actual_photo_modal_verified = false, verification_complete = false,
                }, new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
            }
            Assert.Equal("portrait", photo.Orientation); Assert.Equal((long)width, photo.ImageWidth); Assert.Equal((long)height, photo.ImageHeight);
            Assert.Equal(1, portrait.Total); var row = Assert.Single(portrait.Items); Assert.Equal(photo.PhotoPath, row.photo_path);
            Assert.True(row.is_favorite); Assert.Equal(["保持するタグ"], row.tags); Assert.Equal(0, landscape.Total); Assert.Empty(landscape.Items);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()); SqliteConnection.ClearPool(connection);
        }
    }
}
