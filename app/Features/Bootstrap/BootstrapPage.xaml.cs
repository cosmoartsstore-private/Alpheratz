using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Alpheratz.Features.Bootstrap;

/// <summary>
/// 起動時のスプラッシュ画面。DB 初期化や Window 構築の進捗を 4 段階の論理フェーズで受け取り、
/// ProgressFill のバー幅と sparkle アイコンの位置を補間して滑らかにアニメーション表示する。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class BootstrapPage : Page
{
    /// <summary>現在描画中のパーセント値 (0-100)。フレームごとに _target に近付ける。</summary>
    private double _current;
    /// <summary>目標パーセント値。SetPhase で 5/30/60/100 のいずれかが設定される。</summary>
    private double _target;
    /// <summary>60fps 想定のアニメーション駆動タイマ (16ms 間隔)。</summary>
    private readonly DispatcherTimer _anim;

    // スプラッシュ表示を初期化し、進捗補間用タイマーを用意する。
    public BootstrapPage()
    {
        AppLogger.Trace("BootstrapPage.ctor: enter");
        try
        {
            InitializeComponent();
            _anim = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(BootstrapPageLogic.AnimationFrameIntervalMilliseconds),
            };
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
    /// ページ破棄後に DispatcherTimer と Tick ハンドラが参照を持ち続けないよう解除する。
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

    /// <summary>
    /// ライフサイクルフェーズに応じて目標パーセントを更新する。
    /// 値は手動チューニング：起動の体感速度に合わせて、SDK/サービス/データの各段階で
    /// 進捗バーが極端に止まって見えない刻みにしてある。
    /// uiReady は dataReady と同じ 100% (バー的にはゴール) で、シェル切替アニメーションは
    /// 上位層が制御する。
    /// </summary>
    public void SetPhase(AppLifecyclePhase phase)
    {
        try
        {
            _target = BootstrapPageLogic.TargetPercent(phase);

            if (!_anim.IsEnabled)
                _anim.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"BootstrapPage.SetPhase: threw: {ex}");
        }
    }

    /// <summary>
    /// 毎フレーム呼ばれる補間ロジック。
    /// _current += diff * 0.12 は「残差の 12% を毎フレーム埋める」という指数減衰補間
    /// (ease-out)。0.12 は 60fps で約 0.5 秒で目標の 95% に到達する係数で、
    /// 視覚的にもたつきがちな低 fps でも自然に追従する経験値。
    /// |diff| &lt; 0.3 で打ち切るのは、浮動小数の漸近で永久にタイマーが回り続けるのを防ぐため。
    /// </summary>
    private void OnAnimTick(object? sender, object e)
    {
        var step = BootstrapPageLogic.NextProgress(_current, _target);
        _current = step.CurrentPercent;
        if (step.ShouldStopTimer) _anim.Stop();

        var px = BootstrapPageLogic.ProgressPixels(_current);
        ProgressFill.Width = px;
        StarTranslate.X = px;
    }

    // スプラッシュ本体をフェードインし、完了を Task で返す。
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
                Duration = new Duration(TimeSpan.FromMilliseconds(BootstrapPageLogic.FadeInDurationMilliseconds)),
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

    // Shell 表示前にスプラッシュ本体をフェードアウトし、完了を Task で返す。
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
                Duration = new Duration(TimeSpan.FromMilliseconds(BootstrapPageLogic.FadeOutDurationMilliseconds)),
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
