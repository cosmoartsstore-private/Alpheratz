using System.Reflection;
using Microsoft.Win32;

namespace Alpheratz.Tests
{
    public sealed partial class NativeAppConfigTests
    {
        [Fact]
        public Task LauncherRootsArgumentsAndErrorLogUseOnlyTheIsolatedInstallation() => RunIsolated(fixture =>
        {
            var runtime = Path.Combine(fixture.InstallDirectory, "別runtime folder"); Directory.CreateDirectory(runtime);
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\CosmoArtsStore\Alpheratz", writable: true)!;
            key.SetValue("RuntimeLocation", runtime);
            var resolve = typeof(Launcher.Program).GetMethod("ResolveRuntimeExecutable", BindingFlags.NonPublic | BindingFlags.Static)!;
            var target = (string)resolve.Invoke(null, null)!; Assert.Equal(Path.Combine(runtime, "Alpheratz.Frontend.exe"), target);
            string[] arguments = ["日本語 & space", "", "\"引用\"", "末尾\\", "--flag=✓"];
            var start = Launcher.Program.BuildRuntimeStartInfo(target, arguments);
            Assert.Equal(target, start.FileName); Assert.Equal(runtime, start.WorkingDirectory); Assert.True(start.UseShellExecute); Assert.Equal(arguments, start.ArgumentList);
            Assert.Equal("", start.Arguments); Assert.Equal(arguments, start.ArgumentList);
            var log = typeof(Launcher.Program).GetMethod("TryWriteLaunchError", BindingFlags.NonPublic | BindingFlags.Static)!;
            var error = new InvalidOperationException("所有fixtureの起動失敗");
            Assert.True((bool)log.Invoke(null, [error])!);
            var logPath = Path.Combine(fixture.InstallDirectory, "Data", "logs", "launcher_error.log");
            Assert.Contains("所有fixtureの起動失敗", File.ReadAllText(logPath)); var bytes = File.ReadAllBytes(logPath);
            using (var locked = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.Read)) Assert.False((bool)log.Invoke(null, [new IOException("後の失敗")])!);
            Assert.Equal(bytes, File.ReadAllBytes(logPath)); Assert.True((bool)log.Invoke(null, [new IOException("再試行")])!); Assert.Contains("再試行", File.ReadAllText(logPath));
            var main = typeof(Launcher.Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(2, main.Invoke(null, [Array.Empty<string>()]));
            Assert.Equal("Alpheratz", System.Windows.Forms.MessageBox.LastTitle); Assert.Contains(target, System.Windows.Forms.MessageBox.LastMessage);
            using (var denied = new RunPermissionScope(key, deniedAccess: "0x00000001"))
            {
                Assert.Equal(1, main.Invoke(null, [Array.Empty<string>()]));
                Assert.Equal("Alpheratzを起動できませんでした。エラー情報も保存できませんでした。", System.Windows.Forms.MessageBox.LastMessage);
            }
            Assert.Equal(2, main.Invoke(null, [Array.Empty<string>()])); Assert.Contains(target, System.Windows.Forms.MessageBox.LastMessage);
            key.DeleteValue("RuntimeLocation");
            var fallback = (string)resolve.Invoke(null, null)!;
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CosmoArtsStore", "Alpheratz", "app", "Alpheratz.Frontend.exe"), fallback);
        });
    }
}

// 実MessageBoxは今回の対象外。launcherの構築・log保存・欠落通知の本文までを同じsourceで検証する。
namespace System.Windows.Forms
{
    internal enum MessageBoxButtons { OK }
    internal enum MessageBoxIcon { Error }
    internal static class MessageBox
    {
        internal static string? LastMessage { get; private set; }
        internal static string? LastTitle { get; private set; }
        internal static void Show(string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon) { LastMessage = message; LastTitle = title; }
    }
}
