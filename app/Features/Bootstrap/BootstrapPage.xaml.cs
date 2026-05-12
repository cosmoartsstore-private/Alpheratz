using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Alpheratz.Features.Bootstrap;

public sealed partial class BootstrapPage : Page
{
    private const double TrackWidth = 420.0;
    private double _current;
    private double _target;
    private readonly DispatcherTimer _anim;

    public BootstrapPage()
    {
        AppLogger.Trace("BootstrapPage.ctor: enter");
        try
        {
            InitializeComponent();
            _anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _anim.Tick += OnAnimTick;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"BootstrapPage.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        AppLogger.Trace("BootstrapPage.ctor: exit");
    }

    /// <summary>
    /// R2-A-14: ページが Unloaded でも DispatcherTimer と Tick ハンドラ参照が生き残り、
    /// ページ本体が GC されずリークしていた。Unloaded で Stop + Tick -= で参照を切る。
    /// </summary>
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_anim is not null)
            {
                _anim.Stop();
                _anim.Tick -= OnAnimTick;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"BootstrapPage.Page_Unloaded: threw: {ex}");
        }
    }

    public void SetPhase(AppLifecyclePhase phase)
    {
        try
        {
            _target = phase switch
            {
                AppLifecyclePhase.booting => 5,
                AppLifecyclePhase.sdkReady => 30,
                AppLifecyclePhase.servicesReady => 60,
                AppLifecyclePhase.dataReady => 100,
                AppLifecyclePhase.uiReady => 100,
                _ => 0,
            };

            if (!_anim.IsEnabled)
                _anim.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"BootstrapPage.SetPhase: threw: {ex}");
        }
    }

    private void OnAnimTick(object? sender, object e)
    {
        var diff = _target - _current;
        if (Math.Abs(diff) < 0.3)
        {
            _current = _target;
            _anim.Stop();
        }
        else
        {
            _current += diff * 0.12;
        }

        var px = TrackWidth * (_current / 100.0);
        ProgressFill.Width = px;
        StarTranslate.X = px;
    }

    public Task FadeInAsync()
    {
        _current = 0;
        ProgressFill.Width = 0;
        StarTranslate.X = 0;

        var tcs = new TaskCompletionSource();
        try
        {
            var sb = new Storyboard();
            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(500)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(fade, SplashContent);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);
            sb.Completed += (_, _) => tcs.TrySetResult();
            sb.Begin();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"BootstrapPage.FadeInAsync: threw: {ex}");
            tcs.TrySetResult();
        }
        return tcs.Task;
    }

    public Task FadeOutAsync()
    {
        _anim.Stop();

        var tcs = new TaskCompletionSource();
        try
        {
            var sb = new Storyboard();
            var fade = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(350)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            Storyboard.SetTarget(fade, SplashContent);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);
            sb.Completed += (_, _) => tcs.TrySetResult();
            sb.Begin();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"BootstrapPage.FadeOutAsync: threw: {ex}");
            tcs.TrySetResult();
        }
        return tcs.Task;
    }
}