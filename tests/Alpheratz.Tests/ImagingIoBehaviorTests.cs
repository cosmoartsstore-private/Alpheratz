using Alpheratz.Core;
using Alpheratz.Core.Imaging;
using Alpheratz.Core.Imaging.Pdq;
using System.Security.Cryptography;
using System.Text.Json;
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
public sealed partial class ImagingIoBehaviorTests : IDisposable
{
    private static readonly byte[] CompleteJpegMarkerSequence = [0xFF, 0xD8, 0x00, 0xFF, 0xD9];
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

    /// <summary>最小寸法・長辺上限・縦横比の境界を、確保方法の異なる2入口から実PNGで確認する。</summary>
    [Theory]
    [InlineData(4, 5, 0, 0)]
    [InlineData(5, 4, 0, 0)]
    [InlineData(5, 5, 5, 5)]
    [InlineData(512, 7, 512, 7)]
    [InlineData(7, 512, 7, 512)]
    [InlineData(513, 7, 512, 6)]
    [InlineData(7, 513, 6, 512)]
    [InlineData(320, 640, 256, 512)]
    [InlineData(513, 513, 512, 512)]
    [InlineData(6000, 5, 512, 1)]
    [InlineData(5, 6000, 1, 512)]
    [InlineData(1024, 9, 512, 4)]
    public async Task PdqReaderDimensionBoundariesPreserveImagesAndAgreeAcrossBufferOwners(int width, int height, int expectedWidth, int expectedHeight)
    {
        var path = Path.Combine(tempDir, $"境界 {width}x{height}.png"); await WriteSolidPngAsync(path, (uint)width, (uint)height, 255, 0, 0);
        var bytes = File.ReadAllBytes(path); var image = await PdqImageReader.ReadLumaAsync(path.Replace('\\', '/'));
        using var pooled = await PdqImageReader.ReadPooledLumaAsync(path);
        if (expectedWidth == 0)
        {
            Assert.Null(image); Assert.Null(pooled);
        }
        else
        {
            Assert.NotNull(image); Assert.NotNull(pooled); var decoded = image.Value;
            Assert.Equal(expectedWidth, decoded.width); Assert.Equal(expectedHeight, decoded.height); Assert.Equal(expectedWidth * expectedHeight, decoded.luma.Length);
            Assert.Equal(expectedWidth, pooled.Width); Assert.Equal(expectedHeight, pooled.Height); Assert.True(pooled.Buffer.Length >= decoded.luma.Length);
            Assert.NotSame(decoded.luma, pooled.Buffer); Assert.Equal(decoded.luma, pooled.Buffer.Take(decoded.luma.Length));
            Assert.All(decoded.luma, value => Assert.InRange(value, 76.2449f, 76.2451f));
            // readerは元画像で最小寸法を判定する。縮小後に5px未満となる画像はhasher側で拒否する。
            if (expectedWidth < 5 || expectedHeight < 5) Assert.Equal("", PdqHasher.ComputeHashVariantsHex(decoded.luma, decoded.width, decoded.height));
        }
        Assert.Equal(bytes, File.ReadAllBytes(path));
        WriteImagingEvidence($"boundary-{width}x{height}", new { source = path, width, height, expectedWidth, expectedHeight,
            actual_width = image?.width, actual_height = image?.height, actual_luma_length = image?.luma.Length, pooled_width = pooled?.Width, pooled_height = pooled?.Height,
            source_sha256 = Convert.ToHexString(SHA256.HashData(bytes)), original_bytes_preserved = true, two_real_winrt_reads = true, verification_complete = false });
    }

    /// <summary>BGRA成分と行順を独立した輝度literalで照合し、production係数を期待値へ流用しない。</summary>
    [Fact]
    public async Task PdqReaderColorChannelsAndPixelOrderMatchIndependentLumaValues()
    {
        var colors = new (byte r, byte g, byte b)[] { (0, 0, 0), (255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255), (10, 20, 30) };
        var expected = new[] { 0f, 76.245f, 149.685f, 29.07f, 255f, 18.15f }; var pixels = new byte[6 * 5 * 4];
        for (var index = 0; index < 30; index++)
        {
            var color = colors[index % colors.Length]; pixels[index * 4] = color.b; pixels[index * 4 + 1] = color.g; pixels[index * 4 + 2] = color.r; pixels[index * 4 + 3] = 255;
        }
        var path = Path.Combine(tempDir, "RGB 行順.png"); await WriteTestPngAsync(path, 6, 5, pixels); var bytes = File.ReadAllBytes(path);
        var image = await PdqImageReader.ReadLumaAsync(path); using var pooled = await PdqImageReader.ReadPooledLumaAsync(path);
        Assert.NotNull(image); Assert.NotNull(pooled); Assert.Equal(6, image.Value.width); Assert.Equal(5, image.Value.height); Assert.Equal(30, image.Value.luma.Length);
        Assert.Equal(6, pooled.Width); Assert.Equal(5, pooled.Height);
        for (var index = 0; index < 30; index++)
        {
            Assert.InRange(image.Value.luma[index], expected[index % expected.Length] - 0.0001f, expected[index % expected.Length] + 0.0001f);
            Assert.Equal(image.Value.luma[index], pooled.Buffer[index]);
        }
        Assert.Equal(bytes, File.ReadAllBytes(path));
        WriteImagingEvidence("color-order", new { source = path, expected, actual_luma = image.Value.luma, pooled_luma = pooled.Buffer.Take(30).ToArray(),
            source_sha256 = Convert.ToHexString(SHA256.HashData(bytes)), independent_luma_literals = true, original_bytes_preserved = true, verification_complete = false });
    }

    /// <summary>生存中の画像間で配列を共有せず、破棄・所有権移譲の拒否後も他画像の輝度を保持する。</summary>
    [Fact]
    public async Task PdqReaderBufferOwnershipKeepsOutstandingImagesIndependentAfterDisposal()
    {
        var redPath = Path.Combine(tempDir, "owner-red.png"); var bluePath = Path.Combine(tempDir, "owner-blue.png"); var greenPath = Path.Combine(tempDir, "owner-green.png");
        await WriteSolidPngAsync(redPath, 5, 5, 255, 0, 0); await WriteSolidPngAsync(bluePath, 5, 5, 0, 0, 255); await WriteSolidPngAsync(greenPath, 5, 5, 0, 255, 0);
        var imagesBefore = new[] { redPath, bluePath, greenPath }.ToDictionary(path => path, File.ReadAllBytes);
        var independent = await PdqImageReader.ReadLumaAsync(redPath); using var red = await PdqImageReader.ReadPooledLumaAsync(redPath); using var blue = await PdqImageReader.ReadPooledLumaAsync(bluePath);
        Assert.NotNull(independent); Assert.NotNull(red); Assert.NotNull(blue); var redBuffer = red.Buffer; var blueBuffer = blue.Buffer;
        Assert.NotSame(independent.Value.luma, redBuffer); Assert.NotSame(independent.Value.luma, blueBuffer); Assert.NotSame(redBuffer, blueBuffer);
        Assert.Throws<InvalidOperationException>(() => red.DetachBuffer()); Assert.Same(redBuffer, red.Buffer);
        blue.Dispose(); blue.Dispose(); Assert.Throws<ObjectDisposedException>(() => blue.Buffer);
        using var green = await PdqImageReader.ReadPooledLumaAsync(greenPath); Assert.NotNull(green); Assert.NotSame(redBuffer, green.Buffer);
        Assert.All(independent.Value.luma, value => Assert.InRange(value, 76.2449f, 76.2451f)); Assert.All(red.Buffer.Take(25), value => Assert.InRange(value, 76.2449f, 76.2451f));
        Assert.All(green.Buffer.Take(25), value => Assert.InRange(value, 149.6849f, 149.6851f));
        red.Dispose(); red.Dispose(); Assert.Throws<ObjectDisposedException>(() => red.Buffer);
        Assert.All(independent.Value.luma, value => Assert.InRange(value, 76.2449f, 76.2451f)); Assert.All(green.Buffer.Take(25), value => Assert.InRange(value, 149.6849f, 149.6851f));
        Assert.All(imagesBefore, item => Assert.Equal(item.Value, File.ReadAllBytes(item.Key)));
        WriteImagingEvidence("ownership", new { source_directory = tempDir, independent_luma = independent.Value.luma, outstanding_green_luma = green.Buffer.Take(25).ToArray(),
            source_sha256 = imagesBefore.ToDictionary(row => row.Key, row => Convert.ToHexString(SHA256.HashData(row.Value))),
            live_buffers_independent = true, pooled_detach_rejected = true, disposed_access_rejected = true, repeated_disposal_succeeds = true,
            original_bytes_preserved = true, shared_pool_exact_reuse_required = false, verification_complete = false });
    }

    /// <summary>実共有違反・欠落・壊れたPNGを読込失敗とし、lock解除後に同じfileを読めることを確認する。</summary>
    [Fact]
    public async Task PdqReaderIoFailurePreservesSourceAndTheSamePathCanBeRetriedAfterUnlock()
    {
        var path = Path.Combine(tempDir, "読込 再試行.png"); await WriteSolidPngAsync(path, 5, 5, 255, 0, 0); var bytes = File.ReadAllBytes(path);
        using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => Task.Run(() => File.ReadAllBytes(path)));
            Assert.Null(await PdqImageReader.ReadLumaAsync(path)); Assert.Null(await PdqImageReader.ReadPooledLumaAsync(path));
        }
        Assert.Equal(bytes, File.ReadAllBytes(path)); var retry = await PdqImageReader.ReadLumaAsync(path); using var retryPooled = await PdqImageReader.ReadPooledLumaAsync(path);
        Assert.NotNull(retry); Assert.NotNull(retryPooled); Assert.Equal(5, retry.Value.width); Assert.Equal(5, retry.Value.height);
        Assert.All(retry.Value.luma, value => Assert.InRange(value, 76.2449f, 76.2451f)); Assert.Equal(retry.Value.luma, retryPooled.Buffer.Take(25));
        var missing = Path.Combine(tempDir, "missing.png"); Assert.Null(await PdqImageReader.ReadLumaAsync(missing)); Assert.Null(await PdqImageReader.ReadPooledLumaAsync(missing)); Assert.False(File.Exists(missing));
        var broken = Path.Combine(tempDir, "broken.png"); var brokenBytes = "PNGではない画像"u8.ToArray(); File.WriteAllBytes(broken, brokenBytes);
        Assert.Null(await PdqImageReader.ReadLumaAsync(broken)); Assert.Null(await PdqImageReader.ReadPooledLumaAsync(broken));
        Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(brokenBytes, File.ReadAllBytes(broken));
        WriteImagingEvidence("io-failure-retry", new { source_directory = tempDir, source = path, source_sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            retry_luma = retry.Value.luma, retry_pooled_luma = retryPooled.Buffer.Take(25).ToArray(), exclusive_os_lock_confirmed = true,
            missing_file_not_created = true, malformed_image_preserved = true, original_bytes_preserved = true, verification_complete = false });
    }

    /// <summary>キャンセルを読取失敗の null へ変換せず、呼出側へ伝播することを確認する。</summary>
    [Fact]
    public async Task PdqImageReader_ReadLumaAsync_PropagatesCancellation()
    {
        var imagePath = Path.Combine(tempDir, "cancelled-read.png");
        await WriteGradientPngAsync(imagePath, 96, 96);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        (float[] luma, int width, int height)? returned = null;
        var error = await Record.ExceptionAsync(async () => returned = await PdqImageReader.ReadLumaAsync(imagePath, cancellation.Token));
        var result = returned is null ? "null" : $"image {returned.Value.width}x{returned.Value.height}, luma={returned.Value.luma.Length}";
        Assert.True(error is OperationCanceledException, $"取消し済みの読込でOperationCanceledExceptionが発生しませんでした。exception={error?.GetType().FullName ?? "none"}, result={result}");
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
            Assert.EndsWith(".thumb.grid-512.jpg", gridThumb);
            Assert.EndsWith(".thumb.display-514.jpg", displayThumb);
            Assert.True(new FileInfo(gridThumb).Length > 0);
            Assert.True(new FileInfo(displayThumb).Length > 0);
            Assert.False(ThumbnailService.IsPathLockTracked(gridThumb));
            Assert.False(ThumbnailService.IsPathLockTracked(displayThumb));
            Assert.Empty(Directory.EnumerateFiles(
                Assert.IsType<string>(Path.GetDirectoryName(gridThumb)),
                $"{Path.GetFileName(gridThumb)}.*.tmp"));
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

    /// <summary>初回生成をキャンセルした場合は、途中まで書いた一時ファイルも最終ファイルも残さないことを確認する。</summary>
    [Fact]
    public async Task ThumbnailService_EnsureThumbAsync_DeletesPartialFileWhenInitialGenerationIsCanceled()
    {
        var sourcePath = Path.Combine(tempDir, $"cancel-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(sourcePath, [0x00]);
        var normalizedSource = AppPaths.NormalizePathForDb(sourcePath);
        string? temporaryPath = null;
        using var cancellation = new CancellationTokenSource();
        var service = new ThumbnailService(async (_, destinationPath, _, ct) =>
        {
            temporaryPath = destinationPath;
            await File.WriteAllBytesAsync(destinationPath, [0xFF, 0xD8, 0x00], CancellationToken.None);
            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.EnsureGridThumbAsync(normalizedSource, sourceSlot: 1, cancellation.Token));

        var writtenTemporaryPath = Assert.IsType<string>(temporaryPath);
        var cacheDir = Assert.IsType<string>(Path.GetDirectoryName(writtenTemporaryPath));
        Assert.False(File.Exists(writtenTemporaryPath));
        Assert.Empty(Directory.EnumerateFiles(
            cacheDir,
            $"{Path.GetFileName(sourcePath)}.*.thumb.grid-512.jpg"));
    }

    /// <summary>更新生成が I/O 失敗した場合は一時ファイルを削除し、既存の完全なキャッシュを変更しないことを確認する。</summary>
    [Fact]
    public async Task ThumbnailService_EnsureThumbAsync_PreservesExistingCacheWhenReplacementFails()
    {
        var sourcePath = Path.Combine(tempDir, $"replace-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(sourcePath, [0x00]);
        var normalizedSource = AppPaths.NormalizePathForDb(sourcePath);
        var seedService = new ThumbnailService(
            (_, destinationPath, _, ct) => WriteCompleteTestJpegAsync(destinationPath, ct));
        string? temporaryPath = null;
        string? thumbPath = null;

        try
        {
            thumbPath = await seedService.EnsureGridThumbAsync(normalizedSource, sourceSlot: 1);
            var originalBytes = await File.ReadAllBytesAsync(thumbPath);
            File.SetLastWriteTimeUtc(thumbPath, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow);

            var failingService = new ThumbnailService(async (_, destinationPath, _, _) =>
            {
                temporaryPath = destinationPath;
                await File.WriteAllBytesAsync(destinationPath, [0xFF, 0xD8, 0x00]);
                throw new IOException("テスト用のサムネイル生成失敗");
            });

            await Assert.ThrowsAsync<IOException>(
                () => failingService.EnsureGridThumbAsync(normalizedSource, sourceSlot: 1));

            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(thumbPath));
            Assert.False(File.Exists(Assert.IsType<string>(temporaryPath)));
            Assert.False(ThumbnailService.IsPathLockTracked(thumbPath));
        }
        finally
        {
            if (thumbPath is not null)
                TryDelete(thumbPath);
            if (temporaryPath is not null)
                TryDelete(temporaryPath);
        }
    }

    /// <summary>更新日時が新しくても終端を欠く JPEG はキャッシュヒットにせず、再生成することを確認する。</summary>
    [Fact]
    public async Task ThumbnailService_EnsureThumbAsync_RegeneratesIncompleteFinalFile()
    {
        var sourcePath = Path.Combine(tempDir, $"invalid-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(sourcePath, [0x00]);
        var normalizedSource = AppPaths.NormalizePathForDb(sourcePath);
        var seedService = new ThumbnailService(
            (_, destinationPath, _, ct) => WriteCompleteTestJpegAsync(destinationPath, ct));
        string? thumbPath = null;

        try
        {
            thumbPath = await seedService.EnsureGridThumbAsync(normalizedSource, sourceSlot: 1);
            await File.WriteAllBytesAsync(thumbPath, [0xFF, 0xD8, 0x00]);
            File.SetLastWriteTimeUtc(thumbPath, DateTime.UtcNow.AddMinutes(2));
            var generationCount = 0;
            var repairService = new ThumbnailService((_, destinationPath, _, ct) =>
            {
                Interlocked.Increment(ref generationCount);
                return WriteCompleteTestJpegAsync(destinationPath, ct);
            });

            var repairedPath = await repairService.EnsureGridThumbAsync(normalizedSource, sourceSlot: 1);

            Assert.Equal(thumbPath, repairedPath);
            Assert.Equal(1, generationCount);
            Assert.Equal(CompleteJpegMarkerSequence, await File.ReadAllBytesAsync(repairedPath));
            Assert.False(ThumbnailService.IsPathLockTracked(repairedPath));
        }
        finally
        {
            if (thumbPath is not null)
                TryDelete(thumbPath);
        }
    }

    /// <summary>同一出力先への並列要求を一度の生成へ集約し、完了後にパスロックを除去することを確認する。</summary>
    [Fact]
    public async Task ThumbnailService_EnsureThumbAsync_SerializesSamePathAndReleasesLock()
    {
        var sourcePath = Path.Combine(tempDir, $"parallel-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(sourcePath, [0x00]);
        var normalizedSource = AppPaths.NormalizePathForDb(sourcePath);
        var generationCount = 0;
        var service = new ThumbnailService(async (_, destinationPath, _, ct) =>
        {
            Interlocked.Increment(ref generationCount);
            await Task.Delay(50, ct);
            await WriteCompleteTestJpegAsync(destinationPath, ct);
        });
        string? thumbPath = null;

        try
        {
            var requests = Enumerable.Range(0, 8)
                .Select(_ => service.EnsureGridThumbAsync(normalizedSource, sourceSlot: 1))
                .ToArray();
            var paths = await Task.WhenAll(requests);
            thumbPath = paths[0];

            Assert.All(paths, path => Assert.Equal(thumbPath, path));
            Assert.Equal(1, generationCount);
            Assert.False(ThumbnailService.IsPathLockTracked(thumbPath));
        }
        finally
        {
            if (thumbPath is not null)
                TryDelete(thumbPath);
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

        await WriteTestPngAsync(path, width, height, pixels);
    }

    private static Task WriteSolidPngAsync(string path, uint width, uint height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4) { pixels[index] = b; pixels[index + 1] = g; pixels[index + 2] = r; pixels[index + 3] = 255; }
        return WriteTestPngAsync(path, width, height, pixels);
    }

    private static async Task WriteTestPngAsync(string path, uint width, uint height, byte[] pixels)
    {
        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using IRandomAccessStream randomAccessStream = fileStream.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, randomAccessStream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels);
        await encoder.FlushAsync();
        await fileStream.FlushAsync();
    }

    private static void WriteImagingEvidence(string name, object observation)
    {
        var directory = Environment.GetEnvironmentVariable("ALPHERATZ_STARTUP_TEST_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(directory)) File.WriteAllText(Path.Combine(directory, $"alpheratz-imaging-{name}-evidence.json"), JsonSerializer.Serialize(observation));
    }

    private static Task WriteCompleteTestJpegAsync(string path, CancellationToken ct)
        => File.WriteAllBytesAsync(path, CompleteJpegMarkerSequence, ct);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }
}
