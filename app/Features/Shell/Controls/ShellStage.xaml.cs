using System;
using Alpheratz.Core;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Shared.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Shell.Controls;

public sealed partial class ShellStage : UserControl
{
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

    public object? ModalContent
    {
        get => ModalContentHost.Content;
        set => ModalContentHost.Content = value;
    }

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
                        if (ModalContentHost.Content is PhotoModal.PhotoModalPage modal)
                            modal.ReleaseImage();
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
