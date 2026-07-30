using Alpheratz.Core;
using Alpheratz.Core.Imaging;
using Alpheratz.Services;

namespace Alpheratz.Tests;

/// <summary>サムネイルワーカーの一括停止と再開の契約を検証する。</summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class ThumbnailWorkerBehaviorTests : IDisposable
{
    private readonly string tempDir = Path.Combine(
        Path.GetTempPath(),
        "Alpheratz.ThumbnailWorker.Tests",
        Guid.NewGuid().ToString("N"));

    public ThumbnailWorkerBehaviorTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(tempDir, recursive: true); }
        catch { }
    }

    /// <summary>
    /// 停止開始時点の全処理をキャンセルして終了まで待ち、再開までは新規要求を受け付けないことを確認する。
    /// </summary>
    [Fact]
    public async Task SuspendOperationsAndWaitAsync_CancelsAllOperationsAndBlocksRequestsUntilResume()
    {
        var sourcePaths = Enumerable.Range(1, 3)
            .Select(index => Path.Combine(tempDir, $"source-{index}.png"))
            .ToArray();
        foreach (var sourcePath in sourcePaths)
            await File.WriteAllBytesAsync(sourcePath, [0x00]);

        var allBlockingGenerationsStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCancellation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var generationCount = 0;
        var cancellationCount = 0;
        var readyResults = new List<ThumbnailResult>();
        var service = new ThumbnailService(async (_, destinationPath, _, ct) =>
        {
            var generation = Interlocked.Increment(ref generationCount);
            if (generation <= 2)
            {
                if (generation == 2)
                    allBlockingGenerationsStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                finally
                {
                    if (ct.IsCancellationRequested)
                    {
                        if (Interlocked.Increment(ref cancellationCount) == 2)
                            cancellationObserved.TrySetResult();
                        await finishCancellation.Task;
                    }
                }
                return;
            }

            await File.WriteAllBytesAsync(destinationPath, [0xFF, 0xD8, 0x00, 0xFF, 0xD9], ct);
        });
        var worker = new ThumbnailWorker(service);
        var first = worker.GenerateGridAsync(
            [(AppPaths.NormalizePathForDb(sourcePaths[0]), 1)],
            result => readyResults.Add(result));
        var second = worker.GenerateGridAsync(
            [(AppPaths.NormalizePathForDb(sourcePaths[1]), 1)],
            result => readyResults.Add(result));

        try
        {
            await allBlockingGenerationsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var suspension = worker.SuspendOperationsAndWaitAsync();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(suspension.IsCompleted);
            finishCancellation.TrySetResult();
            await suspension.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, cancellationCount);
            Assert.Empty(readyResults);

            await worker.GenerateGridAsync(
                [(AppPaths.NormalizePathForDb(sourcePaths[2]), 1)],
                result => readyResults.Add(result));
            Assert.Equal(2, generationCount);

            worker.ResumeOperations();
            await worker.GenerateGridAsync(
                [(AppPaths.NormalizePathForDb(sourcePaths[2]), 1)],
                result => readyResults.Add(result));

            var ready = Assert.Single(readyResults);
            Assert.Equal(3, generationCount);
            Assert.True(File.Exists(ready.ThumbPath));
        }
        finally
        {
            finishCancellation.TrySetResult();
            try { await worker.SuspendOperationsAndWaitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { }
            foreach (var ready in readyResults)
            {
                try { File.Delete(ready.ThumbPath); }
                catch { }
            }
        }
    }
}
