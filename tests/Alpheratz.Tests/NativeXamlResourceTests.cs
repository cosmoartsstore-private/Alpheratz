using System.Runtime.InteropServices;
using Alpheratz.Core;
using Alpheratz.Shared.Controls;
using Alpheratz.Shared.Converters;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Alpheratz.Tests;

/// <summary>非表示の専用WinUI WindowでCompositionとtheme resourceの実際の解決を確認する。</summary>
[Collection(AppPathsCacheTestCollection.Name)]
public sealed class NativeXamlResourceTests
{
    private const string ChildMarker = "ALPHERATZ_NATIVE_XAML_TEST_CHILD";

    [Fact]
    public async Task NativeXaml_PulseAndToastBrushesFollowActualResources()
    {
        if (Environment.GetEnvironmentVariable(ChildMarker) != "1")
        {
            await NativeDispatcherQueueTests.RunChildProcess(typeof(NativeXamlResourceTests), nameof(NativeXaml_PulseAndToastBrushesFollowActualResources), ChildMarker);
            return;
        }
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiThread = new Thread(() =>
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            try
            {
                Bootstrap.Initialize(0x00010006);
                try { Application.Start(parameters => { SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread())); _ = new ProbeApplication(completed); }); }
                finally { Bootstrap.Shutdown(); }
                stopped.TrySetResult();
            }
            catch (Exception error) { stopped.TrySetException(error); }
        });
        uiThread.SetApartmentState(ApartmentState.STA); uiThread.IsBackground = true; uiThread.Start();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(15)); await completed.Task.WaitAsync(TimeSpan.FromSeconds(1)); Assert.True(uiThread.Join(TimeSpan.FromSeconds(5)), "専用WinUI threadが終了しません。");
        var receipt = Environment.GetEnvironmentVariable(NativeDispatcherQueueTests.ReceiptVariable) ?? throw new InvalidOperationException("専用testの完了記録先がありません。");
        await File.WriteAllTextAsync(receipt, "completed");
    }

    private sealed class ProbeApplication(TaskCompletionSource completed) : Application
    {
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            Window? window = null;
            try
            {
                window = new Window(); var target = new Border(); window.Content = target;
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(window); Assert.NotEqual(IntPtr.Zero, handle); Assert.False(IsWindowVisible(handle));
                VerifyPulse(target); VerifyBrushes(target); VerifyThumbnailSource(); VerifyIconGeometries(); VerifyWrapPanel(); Assert.False(IsWindowVisible(handle)); completed.TrySetResult();
            }
            catch (Exception error) { completed.TrySetException(error); }
            finally { window?.Close(); Exit(); }
        }

        private static void VerifyPulse(Border target)
        {
            var visual = ElementCompositionPreview.GetElementVisual(target); Assert.NotNull(visual.Compositor);
            SkeletonPulseAnimation.Start(target); Assert.Equal(1, target.Opacity);
            SkeletonPulseAnimation.Stop(target); Assert.Equal(0, target.Opacity); Assert.Equal(1f, visual.Opacity);
            SkeletonPulseAnimation.Start(target); Assert.Equal(1, target.Opacity); SkeletonPulseAnimation.Stop(target); Assert.Equal(0, target.Opacity);
        }

        private void VerifyBrushes(Border target)
        {
            var light = new ResourceDictionary(); var dark = new ResourceDictionary();
            foreach (var key in new[] { "APrimary", "ADangerSolid", "ATextDim" }) { light[key] = new SolidColorBrush(Microsoft.UI.Colors.White); dark[key] = new SolidColorBrush(Microsoft.UI.Colors.Black); }
            var themes = new ResourceDictionary(); themes.ThemeDictionaries["Light"] = light; themes.ThemeDictionaries["Default"] = dark; Resources.MergedDictionaries.Add(themes);
            var converter = new ToastAccentBrushConverter();
            foreach (var (theme, expected) in new[] { (ElementTheme.Light, light), (ElementTheme.Dark, dark) })
            {
                ThemeHelper.NotifySelectedThemeChanged(theme);
                foreach (var (type, key) in new[] { (ToastType.success, "APrimary"), (ToastType.error, "ADangerSolid"), (ToastType.info, "ATextDim") }) Assert.Same(expected[key], converter.Convert(type, typeof(Brush), null!, "ja-JP"));
                Assert.Same(expected["ATextDim"], converter.Convert(new object(), typeof(Brush), null!, "ja-JP")); Assert.Null(ThemeHelper.BrushForSelectedTheme("missing"));
            }
            ThemeHelper.NotifySelectedThemeChanged(ElementTheme.Default); var defaultResources = RequestedTheme == ApplicationTheme.Dark ? dark : light;
            Assert.Same(defaultResources["APrimary"], converter.Convert(ToastType.success, typeof(Brush), null!, "ja-JP")); Assert.Throws<NotSupportedException>(() => converter.ConvertBack(new object(), typeof(Brush), null!, "ja-JP"));
            var plain = new SolidColorBrush(Microsoft.UI.Colors.Blue); Resources["probePlain"] = plain; Assert.Same(plain, ThemeHelper.AppResource<Brush>("probePlain")); Assert.Null(ThemeHelper.AppResource<Brush>("missing"));
            var local = new ResourceDictionary(); var localBrush = new SolidColorBrush(Microsoft.UI.Colors.Red); local["probeTheme"] = localBrush; target.Resources.ThemeDictionaries["Light"] = local;
            // WinUIのResourceDictionaryは複数parentへ登録できないため、各themeを別instanceにする。
            var fallback = new ResourceDictionary(); var fallbackBrush = new SolidColorBrush(Microsoft.UI.Colors.Green); fallback["probeTheme"] = fallbackBrush; target.Resources.ThemeDictionaries["Default"] = fallback;
            var isDark = target.ActualTheme == ElementTheme.Dark || (target.ActualTheme == ElementTheme.Default && RequestedTheme == ApplicationTheme.Dark);
            Assert.Same(isDark ? fallbackBrush : localBrush, ThemeHelper.Brush(target, "probeTheme")); Assert.Null(ThemeHelper.Brush(target, "missing"));
        }

        private static void VerifyWrapPanel()
        {
            var panel = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 6 };
            panel.Measure(new Windows.Foundation.Size(100, 1000)); Assert.Equal(new Windows.Foundation.Size(0, 0), panel.DesiredSize);
            var first = new Border { Width = 40, Height = 10 }; var second = new Border { Width = 50, Height = 20 }; var third = new Border { Width = 30, Height = 15 };
            panel.Children.Add(first); panel.Children.Add(second); panel.Children.Add(third);
            panel.Measure(new Windows.Foundation.Size(98, 1000)); Assert.Equal(new Windows.Foundation.Size(98, 41), panel.DesiredSize);
            panel.Arrange(new Windows.Foundation.Rect(0, 0, 98, 41));
            Assert.Equal(new Windows.Foundation.Rect(0, 0, 40, 10), Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(first));
            Assert.Equal(new Windows.Foundation.Rect(48, 0, 50, 20), Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(second));
            Assert.Equal(new Windows.Foundation.Rect(0, 26, 30, 15), Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(third));
            panel.Measure(new Windows.Foundation.Size(97, 1000)); Assert.Equal(new Windows.Foundation.Size(88, 36), panel.DesiredSize);
            panel.Measure(new Windows.Foundation.Size(200, 1000)); Assert.Equal(new Windows.Foundation.Size(136, 20), panel.DesiredSize);
        }

        private static void VerifyIconGeometries()
        {
            string[] names = ["back", "calendar", "calendarRange", "check", "chevronDown", "close", "exclamationCircleFill", "explorer", "gallery", "globe", "grid", "group", "images", "link", "landscape", "menu", "next", "pen", "photo", "portrait", "refresh", "search", "selectMultiple", "settings", "sparkle", "star", "starFill", "tag", "template"];
            foreach (var name in names)
            {
                var geometry = Alpheratz.Shared.Icons.AppIcons.GetGeometry(name); Assert.NotNull(geometry); Assert.True(geometry.Bounds.Width > 0); Assert.True(geometry.Bounds.Height > 0);
                Assert.NotNull(Alpheratz.Shared.Icons.AppIcons.GetGeometry(name.ToUpperInvariant()));
            }
            Assert.Null(Alpheratz.Shared.Icons.AppIcons.GetGeometry("missing-fixture-icon"));
        }

        private static void VerifyThumbnailSource()
        {
            var dataDir = AppPaths.GetDataDir() ?? throw new InvalidOperationException("専用testのData directoryがありません。");
            var converter = new ThumbnailSourceConverter(); var uri = new Uri(Path.Combine(dataDir, "thumbnail.png")).AbsoluteUri;
            var decoded = Assert.IsType<BitmapImage>(converter.Convert(uri, typeof(BitmapImage), null!, "ja-JP"));
            Assert.Equal(new Uri(uri), decoded.UriSource); Assert.Equal(260, decoded.DecodePixelWidth); Assert.Equal(DecodePixelType.Logical, decoded.DecodePixelType); Assert.Equal(BitmapCreateOptions.IgnoreImageCache, decoded.CreateOptions);
            converter.DecodePixelWidth = 0; var original = Assert.IsType<BitmapImage>(converter.Convert(uri, typeof(BitmapImage), null!, "ja-JP")); Assert.Equal(0, original.DecodePixelWidth); Assert.Equal(new Uri(uri), original.UriSource);
            converter.DecodePixelWidth = -1; Assert.Equal(0, Assert.IsType<BitmapImage>(converter.Convert(uri, typeof(BitmapImage), null!, "ja-JP")).DecodePixelWidth);
            Assert.Null(converter.Convert("relative.png", typeof(BitmapImage), null!, "ja-JP")); Assert.Throws<NotSupportedException>(() => converter.ConvertBack(decoded, typeof(string), null!, "ja-JP"));
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
}
