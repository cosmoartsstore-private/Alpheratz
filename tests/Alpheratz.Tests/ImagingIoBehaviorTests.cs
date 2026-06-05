using Alpheratz.Core;
using Alpheratz.Core.Imaging;
using Alpheratz.Core.Imaging.Pdq;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Alpheratz.Tests;

/// <summary>
/// OS 画像デコーダを使う I/O 境界を検証するテスト。
///
/// PdqHasher の純粋なアルゴリズムは別テストで固定している。
/// ここでは実ファイルから Windows Imaging で PNG を読み、PDQ 用 luma 変換や
/// サムネイル生成キャッシュが動くことを確認する。
/// </summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class ImagingIoBehaviorTests : IDisposable
{
    private readonly string tempDir;

    /// <summary>
    /// 実画像ファイルを置く一時ディレクトリを作成する。
    /// サムネイル本体は AppPaths の標準キャッシュに作られるため、各テストで返却パスを個別削除する。
    /// </summary>
    public ImagingIoBehaviorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.ImagingIo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    /// <summary>
    /// 一時画像ディレクトリを削除する。
    /// サムネイルキャッシュはテスト内で返却パスを削除するため、ここでは tempDir のみ対象にする。
    /// </summary>
    public void Dispose()
    {
        try { Directory.Delete(tempDir, recursive: true); }
        catch { }
    }

    /// <summary>
    /// PdqImageReader が実 PNG を luma バッファへ変換し、長辺が 512px を超える画像を縮小することを確認する。
    ///
    /// PDQ 解析では、元画像を長辺 512px 以下に縮小してから単一チャンネルへ変換する。
    /// 640x320 の横長画像を入力し、戻り値が 512x256 になること、luma 配列長が幅高さと一致することを検証する。
    /// 併せて最小サイズ未満の画像は null になる仕様も確認する。
    /// </summary>
    [Fact]
    public async Task PdqImageReader_ReadLumaAsync_DownsamplesLargeImagesAndRejectsTinyImages()
    {
        var largePath = Path.Combine(tempDir, "large.png");
        var tinyPath = Path.Combine(tempDir, "tiny.png");
        await WriteGradientPngAsync(largePath, 640, 320);
        await WriteGradientPngAsync(tinyPath, 4, 4);

        var large = await PdqImageReader.ReadLumaAsync(largePath);
        var tiny = await PdqImageReader.ReadLumaAsync(tinyPath);

        Assert.NotNull(large);
        Assert.Equal(512, large.Value.width);
        Assert.Equal(256, large.Value.height);
        Assert.Equal(512 * 256, large.Value.luma.Length);
        Assert.Contains(large.Value.luma, value => value > 0);
        Assert.Null(tiny);
    }

    /// <summary>
    /// ThumbnailService が実 PNG からグリッド用・表示用サムネイルを生成し、2回目は同じキャッシュを返すことを確認する。
    ///
    /// サムネイル生成は StorageFile/BitmapDecoder/BitmapEncoder をまたぐため、純粋関数では検証できない。
    /// 一時画像を生成して EnsureGridThumbAsync と EnsureDisplayThumbAsync を呼び、返却された JPEG が存在すること、
    /// 同じ画像への再呼び出しでは同じパスが返ることを固定する。
    /// </summary>
    [Fact]
    public async Task ThumbnailService_EnsureThumbAsync_GeneratesAndReusesCacheFiles()
    {
        var sourcePath = Path.Combine(tempDir, "thumb-source.png");
        await WriteGradientPngAsync(sourcePath, 96, 80);
        var normalizedSource = AppPaths.NormalizePathForDb(sourcePath);
        var service = new ThumbnailService();
        var generatedPaths = new List<string>();

        try
        {
            var gridThumb = await service.EnsureGridThumbAsync(normalizedSource, sourceSlot: 1);
            var gridThumbAgain = await service.EnsureGridThumbAsync(normalizedSource, sourceSlot: 1);
            var displayThumb = await service.EnsureDisplayThumbAsync(normalizedSource, sourceSlot: 1);
            generatedPaths.Add(gridThumb);
            generatedPaths.Add(displayThumb);

            Assert.Equal(gridThumb, gridThumbAgain);
            Assert.True(File.Exists(gridThumb));
            Assert.True(File.Exists(displayThumb));
            Assert.EndsWith(".thumb.grid.v3.jpg", gridThumb);
            Assert.EndsWith(".thumb.display.v2.jpg", displayThumb);
            Assert.True(new FileInfo(gridThumb).Length > 0);
            Assert.True(new FileInfo(displayThumb).Length > 0);
        }
        finally
        {
            foreach (var path in generatedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { File.Delete(path); }
                catch { }
            }
        }
    }

    /// <summary>
    /// PNG ファイルを WinRT BitmapEncoder で作成する。
    /// 青成分と赤成分に座標由来の変化を入れ、PDQ luma 変換後も非ゼロ値が得られるようにする。
    /// </summary>
    private static async Task WriteGradientPngAsync(string path, uint width, uint height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var idx = (int)((y * width + x) * 4);
                pixels[idx] = (byte)(x % 256);
                pixels[idx + 1] = (byte)(y % 256);
                pixels[idx + 2] = (byte)((x + y) % 256);
                pixels[idx + 3] = 255;
            }
        }

        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using IRandomAccessStream randomAccessStream = fileStream.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, randomAccessStream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels);
        await encoder.FlushAsync();
        await fileStream.FlushAsync();
    }
}
