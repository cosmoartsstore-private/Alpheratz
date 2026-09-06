using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Alpheratz.Core.Imaging;

/// <summary>
/// グリッド用 / 表示用サムネイルのディスクキャッシュを管理し、無ければ生成する。
/// キャッシュキーは "&lt;filename&gt;.thumb.&lt;variant&gt;.jpg" 形式で imgCache ディレクトリに置く。
/// variant は用途と生成仕様を表し、仕様変更時は別名にして既存キャッシュと混在させない。
/// </summary>
public sealed class ThumbnailService
{
    // 同一サムネイルファイルへの並列生成衝突を防ぐ per-path ロック。
    // 別パスの生成は並列に走らせたいので、ConcurrentDictionary で path -> lock entry を引く。
    // OrdinalIgnoreCase: Windows ファイルシステムは大文字小文字を区別しないため。
    private static readonly ConcurrentDictionary<string, PathLockEntry> _pathLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, string, uint, CancellationToken, Task> _generateThumbnailAsync;

    public ThumbnailService()
        : this(GenerateThumbnailAsync)
    {
    }

    internal ThumbnailService(Func<string, string, uint, CancellationToken, Task> generateThumbnailAsync)
    {
        _generateThumbnailAsync = generateThumbnailAsync
            ?? throw new ArgumentNullException(nameof(generateThumbnailAsync));
    }

    /// <summary>指定出力先のロックエントリが解放後も残留していないかを診断する。</summary>
    internal static bool IsPathLockTracked(string thumbPath) => _pathLocks.ContainsKey(thumbPath);

    /// <summary>
    /// グリッド用サムネイル（長辺 512px）を取得する。無ければ生成。
    /// キャッシュキーは "grid-512"。HiDPI 表示で blur に見えないよう長辺 512px で生成する。
    /// </summary>
    public async Task<string> EnsureGridThumbAsync(string photoPath, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"ThumbnailService.EnsureGridThumbAsync: enter path={photoPath} slot={sourceSlot}");
        try
        {
            var path = await EnsureThumbAsync(photoPath, sourceSlot, 512, "grid-512", ct).ConfigureAwait(false);
            AppLogger.Trace("ThumbnailService.EnsureGridThumbAsync: exit");
            return path;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"ThumbnailService.EnsureGridThumbAsync: threw: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// PhotoModal 表示用サムネイル（長辺 514px、display-514）を取得する。無ければ生成。
    /// グリッド用とサイズはほぼ同じだが、キャッシュキーを分けることで将来別仕様にできる。
    /// </summary>
    public async Task<string> EnsureDisplayThumbAsync(string photoPath, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"ThumbnailService.EnsureDisplayThumbAsync: enter path={photoPath} slot={sourceSlot}");
        try
        {
            var path = await EnsureThumbAsync(photoPath, sourceSlot, 514, "display-514", ct).ConfigureAwait(false);
            AppLogger.Trace("ThumbnailService.EnsureDisplayThumbAsync: exit");
            return path;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"ThumbnailService.EnsureDisplayThumbAsync: threw: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 指定写真から生成されたサムネイルをすべて削除する。
    /// 呼び出し側は先に ThumbnailWorker の処理を停止し、生成中ファイルとの競合を避ける。
    /// キャッシュ削除の失敗は元写真の削除結果へ影響させず、警告だけを記録する。
    /// </summary>
    public int DeleteCachedThumbnails(string photoPath, long sourceSlot)
    {
        try
        {
            var imgCacheDir = AppPaths.GetImgCacheDir(sourceSlot);
            if (imgCacheDir is null || !Directory.Exists(imgCacheDir))
                return 0;

            var nativePhotoPath = photoPath.Replace('/', Path.DirectorySeparatorChar);
            var filename = Path.GetFileName(nativePhotoPath);
            if (string.IsNullOrWhiteSpace(filename))
                return 0;

            var cachePrefix = $"{filename}.{BuildCacheKey(photoPath)}.thumb.";
            var deleted = 0;
            foreach (var cachePath in Directory.EnumerateFiles(imgCacheDir, $"{cachePrefix}*", SearchOption.TopDirectoryOnly))
            {
                if (!Path.GetFileName(cachePath).StartsWith(cachePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    File.Delete(cachePath);
                    deleted++;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"ThumbnailService.DeleteCachedThumbnails: failed path={cachePath}: {ex.Message}");
                }
            }
            return deleted;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"ThumbnailService.DeleteCachedThumbnails: threw path={photoPath}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// キャッシュ存在チェック → 必要なら一時ファイルへ生成 → 最終ファイルを置換、を per-path セマフォの排他下で行う。
    /// 並列で同一パスのサムネイルを生成しようとすると File.Create が衝突するため、
    /// パス単位の SemaphoreSlim で 1 つに絞る。別パスは並列のまま。
    /// 元画像のタイムスタンプ &gt; キャッシュタイムスタンプなら完成後に旧キャッシュを置き換える
    /// （ユーザがファイルを差し替えたケースで古いサムネが表示され続けるのを防ぐ）。
    /// </summary>
    private async Task<string> EnsureThumbAsync(string photoPath, long sourceSlot, uint maxSize, string variant, CancellationToken ct)
    {
        AppLogger.Trace($"ThumbnailService.EnsureThumbAsync: enter variant={variant} maxSize={maxSize}");
        try
        {
            var imgCacheDir = AppPaths.GetImgCacheDir(sourceSlot)
                ?? throw new InvalidOperationException("imgCache フォルダを取得できません");
            var nativePhotoPath = photoPath.Replace('/', Path.DirectorySeparatorChar);
            var filename = Path.GetFileName(nativePhotoPath);
            var cacheKey = BuildCacheKey(photoPath);
            var thumbPath = Path.Combine(imgCacheDir, $"{filename}.{cacheKey}.thumb.{variant}.jpg");

            var pathLock = RentPathLock(thumbPath);
            var lockTaken = false;
            try
            {
                await pathLock.Semaphore.WaitAsync(ct).ConfigureAwait(false);
                lockTaken = true;

                if (File.Exists(thumbPath) && IsUsableThumbnailFile(thumbPath))
                {
                    // DB に保存されている photo_path は forward-slash 正規化されているが、
                    // .NET の File API は OS ネイティブセパレータを要求する場面があるため、
                    // ここで Path.DirectorySeparatorChar に正規化してから問い合わせる。
                    var sourceModified = File.GetLastWriteTimeUtc(nativePhotoPath);
                    var cacheModified = File.GetLastWriteTimeUtc(thumbPath);
                    if (sourceModified <= cacheModified)
                    {
                        AppLogger.Trace("ThumbnailService.EnsureThumbAsync: exit (cache hit)");
                        return thumbPath;
                    }
                }

                var temporaryPath = BuildTemporaryPath(thumbPath);
                try
                {
                    await _generateThumbnailAsync(photoPath, temporaryPath, maxSize, ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (!IsUsableThumbnailFile(temporaryPath))
                        throw new InvalidDataException("生成したサムネイルが完全な JPEG ではありません");

                    // 同一ディレクトリ内の rename によって、完成済みファイルだけを最終名へ公開する。
                    // 既存キャッシュがある場合も、生成が完了するまでは旧ファイルを維持する。
                    File.Move(temporaryPath, thumbPath, overwrite: true);
                }
                finally
                {
                    DeleteTemporaryFile(temporaryPath);
                }

                AppLogger.Trace("ThumbnailService.EnsureThumbAsync: exit (generated)");
                return thumbPath;
            }
            finally
            {
                if (lockTaken)
                    pathLock.Semaphore.Release();
                ReturnPathLock(thumbPath, pathLock);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"ThumbnailService.EnsureThumbAsync: threw: {ex.Message}");
            throw;
        }
    }

    /// <summary>同じ出力先を使う呼び出しが、辞書から除去されるまで同一ロックを参照するよう貸し出す。</summary>
    private static PathLockEntry RentPathLock(string thumbPath)
    {
        while (true)
        {
            if (_pathLocks.TryGetValue(thumbPath, out var existing))
            {
                if (existing.TryAddReference())
                    return existing;

                RemoveMatchingPathLock(thumbPath, existing);
                continue;
            }

            var created = new PathLockEntry();
            if (_pathLocks.TryAdd(thumbPath, created))
            {
                // 登録直後に別呼び出しが先に利用・返却すると、ここへ戻る前に廃止され得る。
                // 参照追加に失敗した場合は、廃止済みエントリを返さず新しい登録を取り直す。
                if (created.TryAddReference())
                    return created;
                continue;
            }

            created.Dispose();
        }
    }

    /// <summary>最後の利用者だけがロックを廃止し、同じインスタンスが登録中の場合に限って辞書から除去する。</summary>
    private static void ReturnPathLock(string thumbPath, PathLockEntry pathLock)
    {
        if (pathLock.ReleaseReference() != 0 || !pathLock.TryRetire())
            return;

        RemoveMatchingPathLock(thumbPath, pathLock);
        pathLock.Dispose();
    }

    private static void RemoveMatchingPathLock(string thumbPath, PathLockEntry pathLock)
    {
        ((ICollection<KeyValuePair<string, PathLockEntry>>)_pathLocks)
            .Remove(new KeyValuePair<string, PathLockEntry>(thumbPath, pathLock));
    }

    /// <summary>同じディレクトリ内で生成し、最終ファイル名と衝突しない一時パスを返す。</summary>
    private static string BuildTemporaryPath(string thumbPath)
        => $"{thumbPath}.{Guid.NewGuid():N}.tmp";

    /// <summary>生成途中の切断ファイルをキャッシュヒットとして扱わないよう JPEG の開始・終了マーカーを確認する。</summary>
    private static bool IsUsableThumbnailFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 4)
                return false;

            if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
                return false;

            stream.Seek(-2, SeekOrigin.End);
            return stream.ReadByte() == 0xFF && stream.ReadByte() == 0xD9;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>生成失敗時の一時ファイルを消し、元の例外を清掃失敗で置き換えない。</summary>
    private static void DeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"ThumbnailService.DeleteTemporaryFile: failed path={temporaryPath}: {ex.Message}");
        }
    }

    /// <summary>写真パスからキャッシュファイル名用の短い安定ハッシュを作る。</summary>
    private static string BuildCacheKey(string photoPath)
    {
        var normalized = photoPath.Replace('\\', '/').ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>
    /// 元画像をデコードし、長辺 maxSize 以下に縮小して JPEG で書き出す。
    /// EXIF 回転の扱いがこの関数の中核：
    ///   - BitmapDecoder.OrientedPixelWidth/Height は EXIF 回転 *後* の寸法（ユーザ視点の寸法）。
    ///   - BitmapTransform.ScaledWidth/Height は EXIF 回転 *前* の raw 寸法に適用される。
    ///   - そのため、EXIF Orientation が縦横入替（90°/270°）の場合は Transform の幅高を入替える必要がある。
    ///   - axisSwapped = (orientedW != decoder.PixelWidth) で「軸が入れ替わったか」を検出する。
    ///   - ExifOrientationMode.RespectExifOrientation を渡せばデコード時に自動で適用される。
    /// </summary>
    private static async Task GenerateThumbnailAsync(string sourcePath, string destPath, uint maxSize, CancellationToken ct)
    {
        AppLogger.Trace($"ThumbnailService.GenerateThumbnailAsync: enter src={sourcePath} maxSize={maxSize}");
        try
        {
            // StorageFile.GetFileFromPathAsync は Windows 上でも forward-slash 区切りを拒絶し
            // E_INVALIDARG (0x800700A1) を返す。DB 正規化パスは '/' なので '\' に直す。
            var winPath = sourcePath.Replace('/', Path.DirectorySeparatorChar);
            var sourceFile = await StorageFile.GetFileFromPathAsync(winPath).AsTask(ct).ConfigureAwait(false);
            using var sourceStream = await sourceFile.OpenReadAsync().AsTask(ct).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(sourceStream).AsTask(ct).ConfigureAwait(false);

            // EXIF 回転後の「見た目寸法」でスケール係数を出す。
            var orientedW = decoder.OrientedPixelWidth;
            var orientedH = decoder.OrientedPixelHeight;
            double scale = Math.Min((double)maxSize / orientedW, (double)maxSize / orientedH);
            scale = Math.Min(scale, 1.0); // 元画像より拡大はしない（無駄な処理 + blur）
            // 有効な極端な縦長・横長画像では、短辺を丸めた結果が 0 になり得る。
            // BitmapTransform / BitmapEncoder は 0px を受け付けないため、両辺を最低 1px に保つ。
            var finalW = Math.Max(1u, (uint)Math.Round(orientedW * scale));
            var finalH = Math.Max(1u, (uint)Math.Round(orientedH * scale));

            // raw 寸法とオリエンテッド寸法が違う = EXIF 回転で縦横が入れ替わっている、ということ。
            // この場合 Transform の入力サイズも入れ替える（詳細はメソッド doc コメント参照）。
            bool axisSwapped = orientedW != decoder.PixelWidth;
            var transformW = axisSwapped ? finalH : finalW;
            var transformH = axisSwapped ? finalW : finalH;

            var transform = new BitmapTransform { ScaledWidth = transformW, ScaledHeight = transformH, InterpolationMode = BitmapInterpolationMode.Fant };
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);
            var pixels = pixelData.DetachPixelData();

            var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, useAsync: true);
            IRandomAccessStream? outStream = null;
            try
            {
                outStream = fileStream.AsRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream).AsTask(ct).ConfigureAwait(false);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, finalW, finalH, 96, 96, pixels);
                await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
                await fileStream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                outStream?.Dispose();
                fileStream.Dispose();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"ThumbnailService.GenerateThumbnailAsync: threw: {ex.Message}");
            throw;
        }
        AppLogger.Trace("ThumbnailService.GenerateThumbnailAsync: exit");
    }

    /// <summary>
    /// 待機中を含む利用者数と廃止状態を単一の整数で管理する。
    /// 参照数 0 から廃止済みへの CAS と参照追加を競合させることで、古いロックを除去した直後に
    /// 同じインスタンスを取得する呼び出しが生じず、同一出力先に複数の SemaphoreSlim が共存しない。
    /// </summary>
    private sealed class PathLockEntry : IDisposable
    {
        private const int RetiredState = -1;
        private int referenceState;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAddReference()
        {
            while (true)
            {
                var state = Volatile.Read(ref referenceState);
                if (state < 0)
                    return false;
                if (state == int.MaxValue)
                    throw new InvalidOperationException("サムネイル生成ロックの参照数が上限に達しました");

                if (Interlocked.CompareExchange(ref referenceState, state + 1, state) == state)
                    return true;
            }
        }

        public int ReleaseReference() => Interlocked.Decrement(ref referenceState);

        public bool TryRetire()
            => Interlocked.CompareExchange(ref referenceState, RetiredState, 0) == 0;

        public void Dispose() => Semaphore.Dispose();
    }
}
