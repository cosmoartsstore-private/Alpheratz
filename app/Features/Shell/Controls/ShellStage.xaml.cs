using System;
using Alpheratz.Core;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Shared.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Shell.Controls;

/// <summary>
/// メインコンテンツ領域。MainContent を差し替えることでギャラリー・設定・タグマスタ等の
/// ページ遷移を行う。モーダル (PhotoModal/WorldResolve) 表示用の上層スロットも提供する。
/// </summary>
public sealed partial class ShellStage : UserControl
{
    /// <summary>ヘッダの戻るボタンが押されたときに発火 (Settings/TagMaster/Template から Gallery 復帰用)。</summary>
    public Action? OnBackToGallery { get; set; }
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

    /// <summary>中位モーダルレイヤの表示。FadeIn/Out + ScaleIn/Out アニメを伴う。</summary>
    public Visibility ModalVisibility
    {
        get => ModalLayerHost.Visibility;
        set
        {
            if (value == Visibility.Visible)
            {
                ModalLayerHost.Visibility = Visibility.Visible;
                AnimationHelper.FadeIn(ModalLayerHost, 250);
                AnimationHelper.ScaleIn(ModalContentHost, fromScale: 0.88f, durationMs: 350);
            }
            else if (ModalLayerHost.Visibility == Visibility.Visible)
            {
                AnimationHelper.FadeOut(ModalLayerHost, 200);
                AnimationHelper.ScaleOut(ModalContentHost, toScale: 0.92f, durationMs: 200, onCompleted: () =>
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
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
    /// 中位レイヤ (ModalContent) の上にさらに重ねて表示することで、GroupDrillDown 中の
    /// 写真クリック → PhotoModal を中位 → 最上位の 2 段スタックで描画できる。
    /// </summary>
    public object? TopModalContent
    {
        get => TopModalContentHost.Content;
        set => TopModalContentHost.Content = value;
    }

    /// <summary>
    /// 最上位モーダルレイヤの表示。閉じるアニメ完了時に PhotoModalPage であれば
    /// ReleaseImage() を呼んで重い BitmapImage 参照を解放する (再表示時は新規取得)。
    /// 旧実装ではこの責務が ModalVisibility 側にあったが、PhotoModal を最上位レイヤに
    /// 移したのに合わせて移動した。
    /// </summary>
    public Visibility TopModalVisibility
    {
        get => TopModalLayerHost.Visibility;
        set
        {
            if (value == Visibility.Visible)
            {
                TopModalLayerHost.Visibility = Visibility.Visible;
                AnimationHelper.FadeIn(TopModalLayerHost, 250);
                AnimationHelper.ScaleIn(TopModalContentHost, fromScale: 0.88f, durationMs: 350);
            }
            else if (TopModalLayerHost.Visibility == Visibility.Visible)
            {
                AnimationHelper.FadeOut(TopModalLayerHost, 200);
                AnimationHelper.ScaleOut(TopModalContentHost, toScale: 0.92f, durationMs: 200, onCompleted: () =>
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
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
            if (value == Visibility.Visible && ScanningOverlayControl.Visibility != Visibility.Visible)
            {
                ScanningOverlayControl.Visibility = Visibility.Visible;
                AnimationHelper.FadeIn(ScanningOverlayControl, 300);
            }
            else if (value == Visibility.Collapsed && ScanningOverlayControl.Visibility == Visibility.Visible)
            {
                AnimationHelper.FadeOut(ScanningOverlayControl, 250, onCompleted: () =>
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        ScanningOverlayControl.Visibility = Visibility.Collapsed;
                        AnimationHelper.ResetVisual(ScanningOverlayControl);
                    });
                });
            }
            else
            {
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

    public void SetBackButtonVisible(bool visible)
    {
        BackToGalleryBtn.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BackToGalleryBtn_Click(object sender, RoutedEventArgs e)
    {
        try { OnBackToGallery?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"ShellStage.BackToGalleryBtn_Click: threw: {ex}"); }
    }
}
