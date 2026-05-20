using System;
using System.Collections.Concurrent;
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
/// キャッシュキーは "&lt;filename&gt;.&lt;pathHash&gt;.thumb.&lt;version&gt;.jpg" 形式で imgCache ディレクトリに置く。
/// pathHash は元写真のフルパスから 8 byte hex を取って衝突回避する
/// (同一 source_slot 下に同名ファイルが別ディレクトリで存在するケースで
/// 旧実装はキャッシュファイルを上書きしていた)。
/// version はアルゴリズム / 仕様変更時にインクリメントすることで、旧版を強制再生成できる。
/// </summary>
public sealed class ThumbnailService
{
    // 同一サムネイルファイルへの並列生成衝突を防ぐ per-path セマフォ。
    // 別パスの生成は並列に走らせたいので、ConcurrentDictionary で path -> Semaphore を引く。
    // OrdinalIgnoreCase: Windows ファイルシステムは大文字小文字を区別しないため。
    // 旧実装はクリティカルセクション終端で _pathLocks.TryRemove を行っていたが、
    // 「Release → 別スレッド GetOrAdd で旧 Semaphore 取得 → TryRemove 実行 →
    //  さらに別スレッド GetOrAdd で新 Semaphore 生成」で 2 スレッドが同時に
    // 同一ファイルを書ける race が成立していたため、cleanup は廃止した。
    // セマフォは写真パス分のメモリ消費だが、ライブラリサイズの上限内で有界。
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _pathLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 元写真フルパスから安定的に短い hex 文字列を生成する。
    /// SHA1 の先頭 8 byte を hex 化することで 16 文字 / 64 bit のキー空間を得る。
    /// 衝突確率は誕生日問題で 10^9 ファイル付近で 1% 程度なので実用上問題なし。
    /// </summary>
    private static string ComputePathHash(string photoPath)
    {
        var normalized = photoPath.Replace('\\', '/').ToLowerInvariant();
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(normalized));
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// グリッド用サムネイル（長辺 512px）を取得する。無ければ生成。
    /// キャッシュキーは "grid.v3"。v2 は 384px だったが、HiDPI 表示で blur に見えるため
    /// 512px に上げて v3 に bump した（旧 v2 ファイルは新規生成で自然に置き換わる）。
    /// </summary>
    public async Task<string> EnsureGridThumbAsync(string photoPath, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"ThumbnailService.EnsureGridThumbAsync: enter path={photoPath} slot={sourceSlot}");
        try
        {
            var path = await EnsureThumbAsync(photoPath, sourceSlot, 512, "grid.v3", ct).ConfigureAwait(false);
            AppLogger.Trace("ThumbnailService.EnsureGridThumbAsync: exit");
            return path;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ThumbnailService.EnsureGridThumbAsync: threw: {ex}");
            throw;
        }
    }

    /// <summary>
    /// PhotoModal 表示用サムネイル（長辺 514px、display.v2）を取得する。無ければ生成。
    /// グリッド用とサイズはほぼ同じだが、キャッシュキーを分けることで将来別仕様にできる。
    /// </summary>
    public async Task<string> EnsureDisplayThumbAsync(string photoPath, long sourceSlot, CancellationToken ct = default)
    {
        AppLogger.Trace($"ThumbnailService.EnsureDisplayThumbAsync: enter path={photoPath} slot={sourceSlot}");
        try
        {
            var path = await EnsureThumbAsync(photoPath, sourceSlot, 514, "display.v2", ct).ConfigureAwait(false);
            AppLogger.Trace("ThumbnailService.EnsureDisplayThumbAsync: exit");
            return path;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ThumbnailService.EnsureDisplayThumbAsync: threw: {ex}");
            throw;
        }
    }

    /// <summary>
    /// キャッシュ存在チェック → 古ければ削除 → 必要なら生成、を per-path セマフォの排他下で行う。
    /// 並列で同一パスのサムネイルを生成しようとすると File.Create が衝突するため、
    /// パス単位の SemaphoreSlim で 1 つに絞る。別パスは並列のまま。
    /// 元画像のタイムスタンプ &gt; キャッシュタイムスタンプなら旧キャッシュを破棄して再生成する
    /// （ユーザがファイルを差し替えたケースで古いサムネが表示され続けるのを防ぐ）。
    /// </summary>
    private static async Task<string> EnsureThumbAsync(string photoPath, long sourceSlot, uint maxSize, string version, CancellationToken ct)
    {
        AppLogger.Trace($"ThumbnailService.EnsureThumbAsync: enter version={version} maxSize={maxSize}");
        try
        {
            var imgCacheDir = AppPaths.GetImgCacheDir(sourceSlot)
                ?? throw new InvalidOperationException("imgCache フォルダを取得できません");
            var filename = Path.GetFileName(photoPath);
            // photoPath を含めたハッシュをキャッシュファイル名に混ぜることで、別ディレクトリの
            // 同名ファイル同士がキャッシュを上書きするのを防ぐ (例: /A/IMG.png と /B/IMG.png)。
            var pathHash = ComputePathHash(photoPath);
            var thumbPath = Path.Combine(imgCacheDir, $"{filename}.{pathHash}.thumb.{version}.jpg");

            var pathLock = _pathLocks.GetOrAdd(thumbPath, _ => new SemaphoreSlim(1, 1));
            await pathLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (File.Exists(thumbPath))
                {
                    // DB に保存されている photo_path は forward-slash 正規化されているが、
                    // .NET の File API は OS ネイティブセパレータを要求する場面があるため、
                    // ここで Path.DirectorySeparatorChar に正規化してから問い合わせる。
                    var sourceModified = File.GetLastWriteTimeUtc(photoPath.Replace('/', Path.DirectorySeparatorChar));
                    var cacheModified = File.GetLastWriteTimeUtc(thumbPath);
                    if (sourceModified <= cacheModified)
                    {
                        AppLogger.Trace("ThumbnailService.EnsureThumbAsync: exit (cache hit)");
                        return thumbPath;
                    }
                    File.Delete(thumbPath);
                }

                await GenerateThumbnailAsync(photoPath, thumbPath, maxSize, ct).ConfigureAwait(false);
            }
            finally
            {
                pathLock.Release();
            }

            AppLogger.Trace("ThumbnailService.EnsureThumbAsync: exit (generated)");
            return thumbPath;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ThumbnailService.EnsureThumbAsync: threw: {ex}");
            throw;
        }
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
            var finalW = (uint)Math.Round(orientedW * scale);
            var finalH = (uint)Math.Round(orientedH * scale);

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
            AppLogger.Error($"ThumbnailService.GenerateThumbnailAsync: threw: {ex}");
            throw;
        }
        AppLogger.Trace("ThumbnailService.GenerateThumbnailAsync: exit");
    }
}