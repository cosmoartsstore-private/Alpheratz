using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Messages;
using Alpheratz.Models.Events;
using Alpheratz.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Alpheratz.Tests;

/// <summary>
/// バックグラウンド補完サービスの振る舞いを検証するテスト。
///
/// PhashService と OrientationService は UI からはボタンや起動時処理として呼ばれるが、
/// 実体は DB の pending 行を読み、補完結果を書き戻し、LocalEventBus で進捗を通知するワーカーである。
/// ここでは実画像処理の重い成功パスではなく、軽量な画像ヘッダや unreadable 経路を使って
/// サービス境界の進捗・完了・DB更新を固定する。
/// </summary>
public sealed class BackgroundServiceBehaviorTests : IDisposable
{
    private readonly string tempDir;
    private readonly AlpheratzDb db;
    private readonly LocalEventBus eventBus = new();

    /// <summary>
    /// 一時DBと画像ファイル置き場を用意する。
    /// サービスは DB に保存された photo_path を実際に読むため、テストごとに独立したディレクトリを作る。
    /// </summary>
    public BackgroundServiceBehaviorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.BackgroundService.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        db = new AlpheratzDb(Path.Combine(tempDir, "Alpheratz.db"));
        db.Initialize();
    }

    /// <summary>
    /// 一時DBとテスト用ファイルを削除する。
    /// 削除失敗はサービス仕様ではないため、検証結果を隠さないよう握りつぶす。
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Cleanup failure should not hide the assertion result.
        }
    }

    /// <summary>
    /// OrientationService が pending 写真の向きと寸法を更新し、進捗と完了イベントを発行することを確認する。
    ///
    /// 画像本体を完全に用意しなくても、PhotoScanner は PNG ヘッダから幅高さを読める。
    /// ここでは横長PNGを pending として登録し、サービス実行後に DB が landscape/width/height へ更新され、
    /// OrientationProgress と OrientationComplete が発行されることを検証する。
    /// </summary>
    [Fact]
    public async Task OrientationService_UpdatesDimensionsAndPublishesProgress()
    {
        var imagePath = Path.Combine(tempDir, "wide.png");
        File.WriteAllBytes(imagePath, PngHeader(width: 640, height: 320));
        await db.UpsertPhotoAsync(Photo(imagePath, "wide.png", "2026-06-05 10:00:00"));
        var progressEvents = new List<OrientationProgressEvent>();
        var notificationOrder = new List<string>();
        var completeCount = 0;
        var finalProgressStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFinalProgress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var progressSub = eventBus.Subscribe<OrientationProgressEvent>(
            EventNames.OrientationProgress,
            payload =>
            {
                progressEvents.Add(payload);
                notificationOrder.Add("progress");
                if (payload.running)
                    return Task.CompletedTask;

                finalProgressStarted.TrySetResult();
                return releaseFinalProgress.Task;
            });
        await using var completeSub = eventBus.Subscribe(
            EventNames.OrientationComplete,
            () =>
            {
                completeCount++;
                notificationOrder.Add("complete");
                return Task.CompletedTask;
            });
        var service = new OrientationService(db, eventBus);

        var startTask = service.StartOrientationCalculationAsync();
        await finalProgressStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.False(startTask.IsCompleted);
            Assert.Equal(0, completeCount);
        }
        finally
        {
            releaseFinalProgress.TrySetResult();
        }
        await startTask;
        var record = await db.GetPhotoRecordAsync(imagePath.Replace('\\', '/'));
        var currentProgress = await service.GetOrientationProgressAsync();

        Assert.NotNull(record);
        Assert.Equal("landscape", record.orientation);
        Assert.Equal(640, record.image_width);
        Assert.Equal(320, record.image_height);
        Assert.Contains(progressEvents, item => item.running && item.total == 1);
        Assert.Equal(1, currentProgress.processed);
        Assert.Equal(1, currentProgress.total);
        Assert.False(currentProgress.running);
        Assert.Equal(1, completeCount);
        Assert.Equal("complete", notificationOrder[^1]);
        Assert.All(notificationOrder.Take(notificationOrder.Count - 1), item => Assert.Equal("progress", item));
    }

    /// <summary>中断時は完了通知を出さず、停止状態の最終進捗だけを発行することを確認する。</summary>
    [Fact]
    public async Task OrientationService_CancellationDoesNotPublishComplete()
    {
        var progressEvents = new List<OrientationProgressEvent>();
        var completeCount = 0;
        await using var progressSub = eventBus.Subscribe<OrientationProgressEvent>(
            EventNames.OrientationProgress,
            payload =>
            {
                progressEvents.Add(payload);
                return Task.CompletedTask;
            });
        await using var completeSub = eventBus.Subscribe(
            EventNames.OrientationComplete,
            () =>
            {
                completeCount++;
                return Task.CompletedTask;
            });
        var service = new OrientationService(db, eventBus);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await service.StartOrientationCalculationAsync(cancellation.Token);
        var currentProgress = await service.GetOrientationProgressAsync();

        Assert.Equal(0, completeCount);
        Assert.NotEmpty(progressEvents);
        Assert.False(progressEvents[^1].running);
        Assert.False(currentProgress.running);
    }

    /// <summary>
    /// OrientationService が読めない画像を unreadable として終端させることを確認する。
    ///
    /// 画像サイズを取得できない写真を pending のまま残すと、起動のたびに同じ失敗を繰り返す。
    /// 現在仕様では unknown/null の結果を unreadable として保存し、次回以降の補完対象から外すため、
    /// テキストファイルを画像として登録してこの経路を固定する。
    /// </summary>
    [Fact]
    public async Task OrientationService_MarksUnreadableWhenDimensionsCannotBeResolved()
    {
        var imagePath = Path.Combine(tempDir, "broken.jpg");
        File.WriteAllText(imagePath, "not an image");
        await db.UpsertPhotoAsync(Photo(imagePath, "broken.jpg", "2026-06-05 10:00:00"));
        var service = new OrientationService(db, eventBus);

        await service.StartOrientationCalculationAsync();
        var record = await db.GetPhotoRecordAsync(imagePath.Replace('\\', '/'));
        var pendingCount = await db.GetPendingOrientationCountAsync();

        Assert.NotNull(record);
        Assert.Equal("unreadable", record.orientation);
        Assert.Null(record.image_width);
        Assert.Null(record.image_height);
        Assert.Equal(0, pendingCount);
    }

    /// <summary>
    /// PhashService が pending なしの場合に完了イベントを出し、進捗を 0/0 で終えることを確認する。
    ///
    /// 写真がない初回起動や、すでに全写真の phash が埋まっている状態は通常運用で頻出する。
    /// 何も処理しない場合でも UI は完了状態へ戻る必要があるため、
    /// PhashComplete が発行され PhashError が出ないことを検証する。
    /// </summary>
    [Fact]
    public async Task PhashService_PublishesCompleteWhenNoPendingPhotosExist()
    {
        var completeCount = 0;
        var errorMessages = new List<string>();
        var notificationOrder = new List<string>();
        await using var progressSub = eventBus.Subscribe<PhashProgressEvent>(
            EventNames.PhashProgress,
            _ =>
            {
                notificationOrder.Add("progress");
                return Task.CompletedTask;
            });
        await using var completeSub = eventBus.Subscribe(
            EventNames.PhashComplete,
            () =>
            {
                completeCount++;
                notificationOrder.Add("complete");
                return Task.CompletedTask;
            });
        await using var errorSub = eventBus.Subscribe<string>(
            EventNames.PhashError,
            payload =>
            {
                errorMessages.Add(payload);
                return Task.CompletedTask;
            });
        var service = new PhashService(db, eventBus);

        await service.StartPdqAnalysisAsync();
        await Task.Delay(50);
        var progress = await service.GetPhashProgressAsync();

        Assert.Equal(1, completeCount);
        Assert.Empty(errorMessages);
        Assert.Equal(0, progress.done);
        Assert.Equal(0, progress.total);
        Assert.Null(progress.current);
        Assert.Equal("complete", notificationOrder[^1]);
        Assert.All(notificationOrder.Take(notificationOrder.Count - 1), item => Assert.Equal("progress", item));
    }

    /// <summary>
    /// PDQ 解析の中断時はキャンセル文言を発行し、最終進捗の後にエラーとして終えることを確認する。
    /// </summary>
    [Fact]
    public async Task PhashService_CancellationPublishesErrorAfterFinalProgress()
    {
        var notificationOrder = new List<string>();
        var errorMessages = new List<string>();
        var completeCount = 0;
        var finalProgressStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFinalProgress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var progressSub = eventBus.Subscribe<PhashProgressEvent>(
            EventNames.PhashProgress,
            _ =>
            {
                notificationOrder.Add("progress");
                finalProgressStarted.TrySetResult();
                return releaseFinalProgress.Task;
            });
        await using var errorSub = eventBus.Subscribe<string>(
            EventNames.PhashError,
            message =>
            {
                errorMessages.Add(message);
                notificationOrder.Add("error");
                return Task.CompletedTask;
            });
        await using var completeSub = eventBus.Subscribe(
            EventNames.PhashComplete,
            () =>
            {
                completeCount++;
                return Task.CompletedTask;
            });
        var service = new PhashService(db, eventBus);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var startTask = service.StartPdqAnalysisAsync(cancellation.Token);
        await finalProgressStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.False(startTask.IsCompleted);
            Assert.Empty(errorMessages);
            Assert.Equal(0, completeCount);
        }
        finally
        {
            releaseFinalProgress.TrySetResult();
        }
        await startTask;

        Assert.Equal(0, completeCount);
        Assert.Equal([MessageCatalog.getMsg("PhashService.cancelled")], errorMessages);
        Assert.Equal("error", notificationOrder[^1]);
        Assert.All(notificationOrder.Take(notificationOrder.Count - 1), item => Assert.Equal("progress", item));
    }

    /// <summary>
    /// PhashService が読めない画像を unreadable として保存し、pending から外すことを確認する。
    ///
    /// PDQ の実画像成功パスは画像デコード環境に依存するが、読めないファイルの扱いは
    /// DB 更新だけで確実に検証できる。
    /// unreadable に更新されないと同じ壊れたファイルが毎回 pending として戻るため、
    /// 補完ループの終端状態として保存されることを確認する。
    /// </summary>
    [Fact]
    public async Task PhashService_MarksUnreadableFilesAndRemovesThemFromPending()
    {
        var imagePath = Path.Combine(tempDir, "broken.jpg");
        File.WriteAllText(imagePath, "not an image");
        await db.UpsertPhotoAsync(Photo(imagePath, "broken.jpg", "2026-06-05 10:00:00"));
        var service = new PhashService(db, eventBus);

        await service.StartPdqAnalysisAsync();
        var record = await db.GetPhotoRecordAsync(imagePath.Replace('\\', '/'), includePhash: true);
        var pendingCount = await db.GetPendingPhashCountAsync();
        var progress = await service.GetPhashProgressAsync();

        Assert.NotNull(record);
        Assert.Equal("unreadable", record.phash);
        Assert.Equal(0, pendingCount);
        Assert.Equal(1, progress.done);
        Assert.Equal(1, progress.total);
    }

    /// <summary>
    /// PhashService が読める画像の PDQ ハッシュを計算し、DB と進捗へ反映することを確認する。
    ///
    /// 壊れた画像の終端処理だけでなく、正常画像では PdqImageReader と PdqHasher を通って
    /// 4方向分の hex ハッシュが保存される必要がある。
    /// WinRT BitmapEncoder で小さな PNG を生成し、phash が unreadable ではない複数バリアント文字列になることを検証する。
    /// </summary>
    [Fact]
    public async Task PhashService_ComputesHashForReadableImagesAndPublishesProgress()
    {
        var imagePath = Path.Combine(tempDir, "readable.png");
        await WriteGradientPngAsync(imagePath, 96, 96);
        await db.UpsertPhotoAsync(Photo(imagePath, "readable.png", "2026-06-05 10:00:00"));
        var progressEvents = new List<PhashProgressEvent>();
        var completeCount = 0;
        var errorMessages = new List<string>();
        await using var progressSub = eventBus.Subscribe<PhashProgressEvent>(
            EventNames.PhashProgress,
            payload =>
            {
                progressEvents.Add(payload);
                return Task.CompletedTask;
            });
        await using var completeSub = eventBus.Subscribe(
            EventNames.PhashComplete,
            () =>
            {
                completeCount++;
                return Task.CompletedTask;
            });
        await using var errorSub = eventBus.Subscribe<string>(
            EventNames.PhashError,
            payload =>
            {
                errorMessages.Add(payload);
                return Task.CompletedTask;
            });
        var service = new PhashService(db, eventBus);

        await service.StartPdqAnalysisAsync();
        await Task.Delay(50);
        var record = await db.GetPhotoRecordAsync(imagePath.Replace('\\', '/'), includePhash: true);
        var pendingCount = await db.GetPendingPhashCountAsync();
        var progress = await service.GetPhashProgressAsync();

        Assert.NotNull(record);
        Assert.NotNull(record.phash);
        Assert.NotEqual("unreadable", record.phash);
        Assert.Equal(4, record.phash.Split('|').Length);
        Assert.Equal(0, pendingCount);
        Assert.Equal(1, progress.done);
        Assert.Equal(1, progress.total);
        Assert.Contains(progressEvents, item => item.total == 1);
        Assert.Equal(1, completeCount);
        Assert.Empty(errorMessages);
    }

    /// <summary>
    /// PhotoUpsertData を短く作るためのテスト専用ファクトリ。
    /// サービスは DB 上の photo_path を読むため、Windows パスを DB 表記のスラッシュに正規化する。
    /// </summary>
    private static PhotoUpsertData Photo(string path, string filename, string timestamp)
        => new()
        {
            PhotoPath = path.Replace('\\', '/'),
            PhotoFilename = filename,
            Timestamp = timestamp,
            SourceSlot = 1,
        };

    /// <summary>
    /// PNG のシグネチャと IHDR までを持つ最小ヘッダを作る。
    /// OrientationService は PhotoScanner 経由で幅高さだけを読むため、画像データ本体は不要である。
    /// </summary>
    private static byte[] PngHeader(int width, int height)
    {
        var bytes = new byte[24];
        var header = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52 };
        Array.Copy(header, bytes, header.Length);
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    /// <summary>
    /// PNG IHDR の幅高さに使う 32bit big-endian 値を書き込む。
    /// テストデータ生成の補助であり、アプリ本体のエンコード処理ではない。
    /// </summary>
    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    /// <summary>
    /// PDQ 正常系テスト用に、座標で色が変わる PNG を生成する。
    /// 単色画像より DCT 係数が安定して得られるよう、各チャンネルに x/y 由来の勾配を入れる。
    /// </summary>
    private static async Task WriteGradientPngAsync(string path, uint width, uint height)
    {
        var pixels = new byte[(int)(width * height * 4)];
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
