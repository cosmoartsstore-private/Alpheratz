using Alpheratz.Core;
using System.Diagnostics.CodeAnalysis;
#if !RELEASE_SELF_CONTAINED
using Microsoft.Windows.ApplicationModel.DynamicDependency;
#endif

namespace Alpheratz;

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
partial class Program
{
    // WinUI のプロセス起動点。Bootstrap と Application.Start は OS/WinAppSDK 境界なので直接テストしない。
    [STAThread]
    static void Main(string[] args)
    {
        AppLogger.Trace($"Program.Main: enter args.Length={args.Length}");
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Debug ビルド (RELEASE_SELF_CONTAINED 未定義) ではシステムインストール済みの
        // WindowsAppRuntime を Bootstrap.Initialize 経由で見つける。
        // Release self-contained では WinAppSDK の DLL が AppLocal に同梱されており、
        // WindowsAppSdkUndockedRegFreeWinRTInitialize=true で reg-free WinRT に
        // 自動活性化されるため Bootstrap は呼ばない。両方やるとシステム MSIX と
        // AppLocal DLL が二重ロードされ Microsoft.UI.Xaml.dll で
        // STATUS_FAIL_FAST_EXCEPTION (0xC000027B) を起こす。
#if !RELEASE_SELF_CONTAINED
        try
        {
            Bootstrap.Initialize(0x00010006);
        }
        catch (Exception ex)
        {
            AppLogger.Fatal($"Program.Main: Bootstrap.Initialize failed: {ex}");
            return;
        }
#endif

        try
        {
            AppLogger.Trace("Program.Main: calling Application.Start");
            Microsoft.UI.Xaml.Application.Start(p =>
            {
                AppLogger.Trace("Program.Main.AppStart: enter");
                try
                {
                    var ctx = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                    System.Threading.SynchronizationContext.SetSynchronizationContext(ctx);
                    AppLogger.Trace("Program.Main.AppStart: SyncContext installed");
                    _ = new App();
                    AppLogger.Trace("Program.Main.AppStart: App instantiated");
                }
                catch (Exception ex)
                {
                    // App を構築できない状態では起動を継続できないため、ログ後に停止させる。
                    AppLogger.Fatal($"Program.Main.AppStart: fatal: {ex}");
                    throw;
                }
                AppLogger.Trace("Program.Main.AppStart: exit");
            });
            AppLogger.Trace("Program.Main: Application.Start returned");
        }
        catch (Exception ex)
        {
            AppLogger.Fatal($"Program.Main: Application.Start threw: {ex}");
            throw;
        }

        AppLogger.Trace("Program.Main: exit");
    }
}
