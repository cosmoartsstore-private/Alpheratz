using System.Diagnostics;
using System.Reflection;
using System.Text;
using Alpheratz.Core;
using Alpheratz.Shared.Services;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Alpheratz.Tests;

/// <summary>WinAppSDKの実際のqueueを専用processで起動し、UIへのdispatchと終了後の拒否を検証する。</summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class NativeDispatcherQueueTests
{
    private const string ChildMarker = "ALPHERATZ_NATIVE_DISPATCHER_TEST_CHILD";
    internal const string ReceiptVariable = "ALPHERATZ_NATIVE_DISPATCHER_TEST_RECEIPT";
    private const string ChildClassVariable = "ALPHERATZ_NATIVE_TEST_CLASS";
    private const string ChildFactVariable = "ALPHERATZ_NATIVE_TEST_FACT";

    // 同じFactとcollection fixtureを専用processで実行し、VSTestを再起動せず終了codeへ伝える。
    private static async Task<int> Main()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            var className = Environment.GetEnvironmentVariable(ChildClassVariable);
            var testName = Environment.GetEnvironmentVariable(ChildFactVariable);
            Assert.False(string.IsNullOrEmpty(className), "専用test classの指定がありません。");
            Assert.False(string.IsNullOrEmpty(testName), "専用Factの指定がありません。");
            var testClass = typeof(NativeDispatcherQueueTests).Assembly.GetType(className!, throwOnError: true)!;
            var method = testClass.GetMethod(testName!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.NotNull(method); Assert.NotNull(method.GetCustomAttribute<FactAttribute>()); Assert.Empty(method.GetParameters());
            // 既存のxUnit collectionと同じAppPaths隔離を、XAML／queue／写真stateのchildでも維持する。
            var collection = testClass.CustomAttributes.SingleOrDefault(attribute => attribute.AttributeType == typeof(CollectionAttribute));
            var collectionName = collection?.ConstructorArguments.Single().Value as string;
            using var paths = collectionName == AppPathsCacheTestCollection.Name ? new AppPathsDataDirFixture() : null;
            var instance = Activator.CreateInstance(testClass);
            try { await Assert.IsAssignableFrom<Task>(method.Invoke(instance, null)); }
            finally
            {
                // 子processでもテストクラスを先に破棄し、その後にcollectionの専用領域を解放する。
                if (instance is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
                else if (instance is IDisposable disposable) disposable.Dispose();
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error is TargetInvocationException { InnerException: { } inner } ? inner : error);
            return 1;
        }
    }

    /// <summary>同じtest assemblyのexeを、class・Factと既存のfixture markerを指定して起動する。</summary>
    internal static ProcessStartInfo CreateChildStart(Type testClass, string testName, string childMarker, string markerValue)
    {
        Assert.Same(typeof(NativeDispatcherQueueTests).Assembly, testClass.Assembly);
        var start = new ProcessStartInfo(Path.ChangeExtension(testClass.Assembly.Location, ".exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        start.Environment[ChildClassVariable] = testClass.FullName;
        start.Environment[ChildFactVariable] = testName;
        start.Environment[childMarker] = markerValue;
        start.Environment["ALPHERATZ_VERBOSE_LOGS"] = "0";
        return start;
    }

    [Fact]
    public async Task NativeQueue_SerializesUiActionsPropagatesFailuresAndRejectsAfterShutdown()
    {
        if (Environment.GetEnvironmentVariable(ChildMarker) == "1")
        {
            await VerifyNativeQueue();
            var receipt = Environment.GetEnvironmentVariable(ReceiptVariable) ?? throw new InvalidOperationException("専用testの完了記録先がありません。");
            await File.WriteAllTextAsync(receipt, "completed");
            return;
        }

        await RunChildProcess(typeof(NativeDispatcherQueueTests), nameof(NativeQueue_SerializesUiActionsPropagatesFailuresAndRejectsAfterShutdown), ChildMarker);
    }

    /// <summary>native boundaryのtestを同じassemblyの専用processへ分離し、実行完了を確認する。</summary>
    internal static async Task RunChildProcess(Type testClass, string testName, string childMarker)
    {
        // Bootstrapのprocess全体への影響を他のtestと分離し、再buildせず同じtest assemblyを使う。
        var start = CreateChildStart(testClass, testName, childMarker, "1");
        var dataDir = AppPaths.GetDataDir() ?? throw new InvalidOperationException("専用testのData directoryがありません。");
        var receiptPath = Path.Combine(dataDir, $"native-dispatcher-{Guid.NewGuid():N}.receipt"); start.Environment[ReceiptVariable] = receiptPath;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("専用test processを起動できません。");
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
            catch { if (!child.HasExited) child.Kill(entireProcessTree: true); throw; }
            var output = await stdout; var error = await stderr;
            Assert.True(child.ExitCode == 0, $"専用processの終了code={child.ExitCode}\n{output}\n{error}");
            // test未検出の終了code 0と区別し、CLIの表示言語に依存せず実行完了を確認する。
            Assert.True(File.Exists(receiptPath), $"専用testの完了記録がありません。\n{output}\n{error}");
            Assert.Equal("completed", await File.ReadAllTextAsync(receiptPath));
        }
        finally { File.Delete(receiptPath); }
    }

    private static async Task VerifyNativeQueue()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers(); Bootstrap.Initialize(0x00010006);
        try
        {
            var controller = DispatcherQueueController.CreateOnDedicatedThread(); var queue = controller.DispatcherQueue; var service = new DispatcherService(queue);
            try
            {
                Assert.False(queue.HasThreadAccess);
                var callerThread = Environment.CurrentManagedThreadId; var calls = 0;
                await service.RunOnUiThread(() =>
                {
                    Assert.True(queue.HasThreadAccess); Assert.NotEqual(callerThread, Environment.CurrentManagedThreadId);
                    var inline = service.RunOnUiThread(() => calls++); Assert.True(inline.IsCompletedSuccessfully);
                    var expected = new InvalidOperationException("inlineの検証例外"); var failed = service.RunOnUiThread(() => throw expected);
                    Assert.True(failed.IsFaulted); Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => failed.GetAwaiter().GetResult()));
                });
                Assert.Equal(1, calls);
                var queuedError = new InvalidOperationException("queued actionの検証例外");
                Assert.Same(queuedError, await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunOnUiThread(() => throw queuedError)));

                var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                service.requestAnimationFrame(() => { Assert.True(queue.HasThreadAccess); frame.SetResult(); }); await frame.Task.WaitAsync(TimeSpan.FromSeconds(5));
                service.requestAnimationFrame(() => throw new InvalidOperationException("frameの検証例外")); await service.RunOnUiThread(() => calls++);
                var delayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await service.setTimeout(() => { Assert.True(queue.HasThreadAccess); delayed.SetResult(); }, 1); await delayed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await service.setTimeout(() => throw new InvalidOperationException("delayの検証例外"), 1); await service.RunOnUiThread(() => calls++);
                var invalidDelayRan = false; await service.setTimeout(() => invalidDelayRan = true, -2); Assert.False(invalidDelayRan); Assert.Equal(3, calls);
            }
            finally { await controller.ShutdownQueueAsync(); }

            // 終了済みqueueのTryEnqueue失敗は、待機者へfaulted Taskとして返す。
            var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunOnUiThread(() => { })); Assert.Contains("DispatcherQueue", rejected.Message);
            var ranAfterShutdown = false; service.requestAnimationFrame(() => ranAfterShutdown = true); await service.setTimeout(() => ranAfterShutdown = true, 1); Assert.False(ranAfterShutdown);
        }
        finally { Bootstrap.Shutdown(); }
    }
}
