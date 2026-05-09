using System.Runtime.InteropServices;
using Alpheratz.Core;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Alpheratz;

partial class Program
{
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint hwnd, string text, string caption, uint type);

    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint ShellExecute(nint hwnd, string? op, string file, string? param, string? dir, int show);

    private const string RuntimeDownloadUrl =
        "https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads";

    [STAThread]
    static void Main(string[] args)
    {
        AppLogger.Trace($"Program.Main: enter args.Length={args.Length}");

        try
        {
            AppLogger.Trace("Program.Main: calling Bootstrap.Initialize(0x00010006)");
            Bootstrap.Initialize(0x00010006);
            AppLogger.Trace("Program.Main: Bootstrap.Initialize OK");
        }
        catch (Exception ex)
        {
            // Halt-on-failure mirrors legacy Alpheratz: a missing runtime is
            // unrecoverable, so we surface a localized dialog and exit
            // instead of letting WinUI3 crash silently.
            AppLogger.Error($"Program.Main: bootstrap threw: {ex}");
            ShowRuntimeError(ex.Message);
            AppLogger.Trace("Program.Main: exit (bootstrap exception)");
            return;
        }

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
                    // Rethrow: Application.Start is the entry point and we
                    // cannot continue without a constructed App.
                    AppLogger.Error($"Program.Main.AppStart: fatal: {ex}");
                    throw;
                }
                AppLogger.Trace("Program.Main.AppStart: exit");
            });
            AppLogger.Trace("Program.Main: Application.Start returned");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Program.Main: Application.Start threw: {ex}");
            throw;
        }

        AppLogger.Trace("Program.Main: exit");
    }

    private static void ShowRuntimeError(string? detail)
    {
        AppLogger.Trace($"Program.ShowRuntimeError: enter detail={detail ?? "(null)"}");

        try
        {
            string text =
                "Windows App Runtime 1.6 (x64) の初期化に失敗しました。\r\n" +
                "インストール後、再度起動してください。\r\n\r\n" +
                $"URL:\r\n{RuntimeDownloadUrl}";

            if (!string.IsNullOrEmpty(detail))
                text += $"\r\n\r\n詳細: {detail}";

            text += "\r\n\r\nブラウザで開きますか？";

            const uint MB_YESNO = 0x00000004u;
            const uint MB_ICONEXCLAMATION = 0x00000030u;
            const int IDYES = 6;
            int answer = MessageBox(nint.Zero, text, "Alpheratz - 起動エラー", MB_YESNO | MB_ICONEXCLAMATION);
            AppLogger.Trace($"Program.ShowRuntimeError: messagebox answer={answer}");

            if (answer == IDYES)
            {
                AppLogger.Trace("Program.ShowRuntimeError: opening download URL");
                ShellExecute(nint.Zero, "open", RuntimeDownloadUrl, null, null, 1);
            }
        }
        catch (Exception ex)
        {
            // Continue: caller (Main) returns immediately after this helper,
            // so swallowing keeps the user-facing error path simple.
            AppLogger.Error($"Program.ShowRuntimeError: threw: {ex}");
        }

        AppLogger.Trace("Program.ShowRuntimeError: exit");
    }
}
