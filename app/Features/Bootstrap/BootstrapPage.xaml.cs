using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Alpheratz.Features.Bootstrap;

/// <summary>
/// 起動時のスプラッシュ画面。ロゴのフェードだけを扱い、短い起動処理へ疑似進捗は表示しない。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class BootstrapPage : Page
{
    private const int FadeInDurationMilliseconds = 500;
    private const int FadeOutDurationMilliseconds = 350;

    // スプラッシュ表示を初期化する。
    public BootstrapPage()
    {
        AppLogger.Trace("BootstrapPage.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"BootstrapPage.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("BootstrapPage.ctor: exit");
    }

    // スプラッシュ本体をフェードインし、完了を Task で返す。
    public Task FadeInAsync()
    {
        var tcs = new TaskCompletionSource();
        try
        {
            var sb = new Storyboard();
            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(FadeInDurationMilliseconds)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            };
            Storyboard.SetTarget(fade, SplashContent);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);
            sb.Completed += (_, _) =>
            {
                SplashContent.Opacity = 1;
                tcs.TrySetResult();
            };
            sb.Begin();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"BootstrapPage.FadeInAsync: threw: {ex}");
            tcs.TrySetResult();
        }
        return tcs.Task;
    }

    // Shell 表示前にスプラッシュ本体をフェードアウトし、完了を Task で返す。
    public Task FadeOutAsync()
    {
        var tcs = new TaskCompletionSource();
        try
        {
            var sb = new Storyboard();
            var fade = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(FadeOutDurationMilliseconds)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.Stop,
            };
            Storyboard.SetTarget(fade, SplashContent);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);
            sb.Completed += (_, _) =>
            {
                SplashContent.Opacity = 0;
                tcs.TrySetResult();
            };
            sb.Begin();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"BootstrapPage.FadeOutAsync: threw: {ex}");
            tcs.TrySetResult();
        }
        return tcs.Task;
    }

    /// <summary>起動処理を継続できない場合に、ロゴ画面上へ説明と終了操作を表示する。</summary>
    public void ShowInitializationError()
    {
        var queue = DispatcherQueue;
        if (queue is not null && !queue.HasThreadAccess)
        {
            if (!queue.TryEnqueue(ShowInitializationError))
                AppLogger.Warn("BootstrapPage.ShowInitializationError: UI 処理をキューへ登録できませんでした");
            return;
        }

        try
        {
            SplashContent.Opacity = 1;
            StartupErrorPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"BootstrapPage.ShowInitializationError: threw: {ex}");
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("BootstrapPage.ExitButton_Click");
        Application.Current.Exit();
    }
}
