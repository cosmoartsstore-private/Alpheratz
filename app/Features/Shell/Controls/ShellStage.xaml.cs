using System;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Shared.Animations;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Features.Shell.Controls;

/// <summary>
/// メインコンテンツ領域。MainContent (常時 GalleryPage) の上に 2 段スタックの
/// モーダルレイヤーと、ScanningOverlay / ToastHost を重ねる構造。
///   Layer 0: MainContent (GalleryPage 固定)
///   Layer 1: ModalLayerHost (中位: Settings / WorldResolve / GroupDrillDown)
///   Layer 2: TopModalLayerHost (最上位: PhotoModal)
///   Layer 3: ScanningOverlay (スキャン進捗)
///   第4 レイヤー: ToastHost
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class ShellStage : UserControl
{
    public ShellStage()
    {
        AppLogger.Trace("ShellStage.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (System.Exception ex)
        {
            AppLogger.Error($"ShellStage.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        ActualThemeChanged += ShellStage_ActualThemeChanged;
        AppLogger.Trace("ShellStage.ctor: exit");
    }

    public object? MainContent
    {
        get => MainContentHost.Content;
        set => MainContentHost.Content = value;
    }

    /// <summary>
    /// 中位モーダルのコンテンツ (Settings / WorldResolve / GroupDrillDown 等)。
    /// 最上位の PhotoModal は <see cref="TopModalContent"/> を使う。
    /// </summary>
    public object? ModalContent
    {
        get => ModalContentHost.Content;
        set => ModalContentHost.Content = value;
    }

    /// <summary>
    /// 設定画面では背面のギャラリーをぼかし、それ以外の中位モーダルは従来の暗幕を使う。
    /// レイヤーを分けることで、ワールド補完やグループ詳細へぼかしが波及しないようにする。
    /// </summary>
    public bool UseBlurredModalBackdrop
    {
        get => BlurredModalBackdrop.Visibility == Visibility.Visible;
        set
        {
            if (value)
                BlurredModalBackdrop.Background = CreateSettingsBackdropBrush();
            BlurredModalBackdrop.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            StandardModalBackdrop.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void ShellStage_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (UseBlurredModalBackdrop)
            BlurredModalBackdrop.Background = CreateSettingsBackdropBrush();
    }

    // XAML資材側は非対応環境用のsolid fallbackを保持し、対応環境だけ実行時にぼかしを有効化する。
    private Brush CreateSettingsBackdropBrush()
    {
        try
        {
            var isDark = ActualTheme == ElementTheme.Dark;
            return new AcrylicBrush
            {
                TintColor = isDark
                    ? Windows.UI.Color.FromArgb(255, 3, 2, 5)
                    : Windows.UI.Color.FromArgb(255, 244, 240, 250),
                TintOpacity = isDark ? 0.68 : 0.42,
                TintLuminosityOpacity = isDark ? 0.24 : 0.56,
                FallbackColor = isDark
                    ? Windows.UI.Color.FromArgb(232, 3, 4, 6)
                    : Windows.UI.Color.FromArgb(232, 241, 237, 247),
            };
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"ShellStage.CreateSettingsBackdropBrush: fallback: {ex.Message}");
            return ThemeHelper.Brush(this, "ASettingsBackdrop")
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(128, 0, 0, 0));
        }
    }

    // モーダル開閉のバージョンカウンタ。閉じるアニメの onCompleted から見て、
    // 「自分が始めたときの version と現在の version が一致する」ときだけ Content=null
    // などのクリーンアップを実行する。これにより：
    //   Open → Close (アニメ進行中) → Open (新コンテンツ) のシーケンスで、
    //   遅延発火した旧 Close の onCompleted が新コンテンツを誤って消すのを防ぐ。
    private int modalVersion;
    private int topModalVersion;
    private int scanningOverlayVersion;
    private bool isScanningOverlayClosing;

    /// <summary>中位モーダルレイヤーの表示。FadeIn/Out + ScaleIn/Out アニメを伴う。</summary>
    public Visibility ModalVisibility
    {
        get => ModalLayerHost.Visibility;
        set
        {
            var transition = ShellStageLayerLogic.ModalTransition(value, ModalLayerHost.Visibility);
            if (transition == ShellStageLayerTransition.Open)
            {
                // version bump で進行中の close onCompleted を無効化する。
                modalVersion = ShellStageLayerLogic.NextVersion(modalVersion);
                ModalLayerHost.Visibility = Visibility.Visible;
                AnimationHelper.FadeIn(ModalLayerHost, ShellStageLayerLogic.ModalFadeInDurationMilliseconds);
                AnimationHelper.ModalSlideUpIn(ModalContentHost, durationMs: ShellStageLayerLogic.ModalSlideUpDurationMilliseconds);
            }
            else if (transition == ShellStageLayerTransition.Close)
            {
                var ourVersion = ShellStageLayerLogic.NextVersion(modalVersion);
                modalVersion = ourVersion;
                AnimationHelper.FadeOut(ModalLayerHost, ShellStageLayerLogic.ModalFadeOutDurationMilliseconds);
                AnimationHelper.ScaleOut(ModalContentHost, toScale: 0.92f, durationMs: ShellStageLayerLogic.ModalScaleOutDurationMilliseconds, onCompleted: () =>
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        // アニメ中に再オープン (もしくは別の close) が発生していたら version が
                        // 進んでいる。その場合は自分の cleanup は古い遺物なのでスキップ。
                        if (!ShellStageLayerLogic.CanCompleteClose(modalVersion, ourVersion)) return;
                        ModalContentHost.Content = null;
                        ModalLayerHost.Visibility = Visibility.Collapsed;
                        AnimationHelper.ResetVisual(ModalLayerHost);
                        AnimationHelper.ResetVisual(ModalContentHost);
                    });
                });
            }
            else
            {
                ModalLayerHost.Visibility = value;
            }
        }
    }

    /// <summary>
    /// 最上位モーダルのコンテンツ (PhotoModal 専用)。
    /// 中位レイヤー (ModalContent) の上にさらに重ねて表示することで、GroupDrillDown 中の
    /// 写真クリック → PhotoModal を中位 → 最上位の 2 段スタックで描画できる。
    /// </summary>
    public object? TopModalContent
    {
        get => TopModalContentHost.Content;
        set => TopModalContentHost.Content = value;
    }

    /// <summary>
    /// 最上位モーダルレイヤーの表示。閉じるアニメ完了時に PhotoModalPage であれば
    /// ReleaseImage() を呼んで重い BitmapImage 参照を解放する (再表示時は新規取得)。
    /// PhotoModal は最上位レイヤーにあるため、このプロパティで表示と解放をまとめて扱う。
    /// version カウンタで「閉じるアニメ進行中に再オープン」したケースのクリーンアップ
    /// 競合を防ぐ。
    /// </summary>
    public Visibility TopModalVisibility
    {
        get => TopModalLayerHost.Visibility;
        set
        {
            var transition = ShellStageLayerLogic.ModalTransition(value, TopModalLayerHost.Visibility);
            if (transition == ShellStageLayerTransition.Open)
            {
                topModalVersion = ShellStageLayerLogic.NextVersion(topModalVersion);
                TopModalLayerHost.Visibility = Visibility.Visible;
                AnimationHelper.FadeIn(TopModalLayerHost, ShellStageLayerLogic.ModalFadeInDurationMilliseconds);
                AnimationHelper.ModalSlideUpIn(TopModalContentHost, durationMs: ShellStageLayerLogic.ModalSlideUpDurationMilliseconds);
            }
            else if (transition == ShellStageLayerTransition.Close)
            {
                var ourVersion = ShellStageLayerLogic.NextVersion(topModalVersion);
                topModalVersion = ourVersion;
                AnimationHelper.FadeOut(TopModalLayerHost, ShellStageLayerLogic.ModalFadeOutDurationMilliseconds);
                AnimationHelper.ScaleOut(TopModalContentHost, toScale: 0.92f, durationMs: ShellStageLayerLogic.ModalScaleOutDurationMilliseconds, onCompleted: () =>
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        // アニメ中に再オープンされていたら version 不一致 → cleanup スキップ。
                        // ReleaseImage も含めて飛ばす (まだ表示中の image を解放しないように)。
                        if (!ShellStageLayerLogic.CanCompleteClose(topModalVersion, ourVersion)) return;
                        if (TopModalContentHost.Content is PhotoModal.PhotoModalPage modal)
                            modal.ReleaseImage();
                        TopModalContentHost.Content = null;
                        TopModalLayerHost.Visibility = Visibility.Collapsed;
                        AnimationHelper.ResetVisual(TopModalLayerHost);
                        AnimationHelper.ResetVisual(TopModalContentHost);
                    });
                });
            }
            else
            {
                TopModalLayerHost.Visibility = value;
            }
        }
    }

    public Visibility ScanningOverlayVisibility
    {
        get => ScanningOverlayControl.Visibility;
        set
        {
            var transitionVersion = unchecked(++scanningOverlayVersion);
            var transition = ShellStageLayerLogic.OverlayTransition(value, ScanningOverlayControl.Visibility);
            if (transition == ShellStageLayerTransition.Open)
            {
                isScanningOverlayClosing = false;
                ScanningOverlayControl.Visibility = Visibility.Visible;
                AnimationHelper.FadeIn(ScanningOverlayControl, ShellStageLayerLogic.OverlayFadeInDurationMilliseconds);
            }
            else if (transition == ShellStageLayerTransition.Close)
            {
                isScanningOverlayClosing = true;
                AnimationHelper.FadeOut(ScanningOverlayControl, ShellStageLayerLogic.OverlayFadeOutDurationMilliseconds, onCompleted: () =>
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        if (scanningOverlayVersion != transitionVersion)
                            return;
                        isScanningOverlayClosing = false;
                        ScanningOverlayControl.Visibility = Visibility.Collapsed;
                        AnimationHelper.ResetVisual(ScanningOverlayControl);
                    });
                });
            }
            else if (value == Visibility.Visible && isScanningOverlayClosing)
            {
                // FadeOut 中は Visibility がまだ Visible のため通常の遷移判定では Open にならない。
                // 新しい scan 状態を受けた場合は旧 FadeOut を FadeIn で置き換える。
                isScanningOverlayClosing = false;
                AnimationHelper.FadeIn(ScanningOverlayControl, ShellStageLayerLogic.OverlayFadeInDurationMilliseconds);
            }
            else
            {
                isScanningOverlayClosing = false;
                ScanningOverlayControl.Visibility = value;
            }
        }
    }

    public object? ScanningOverlayDataContext
    {
        get => ScanningOverlayControl.DataContext;
        set => ScanningOverlayControl.DataContext = value;
    }

    public object? ToastDataContext
    {
        get => ToastHostControl.DataContext;
        set => ToastHostControl.DataContext = value;
    }

    public Shared.Controls.ScanningOverlay ScanningOverlayControlRef => ScanningOverlayControl;
}
