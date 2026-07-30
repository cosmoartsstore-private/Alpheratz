using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Features.PhotoModal;

/// <summary>
/// PhotoModal の ViewModel。state を薄くラップし、
/// ワールド関連の操作（ページ遷移）を追加する。
/// </summary>
#pragma warning disable CS0162 // Unreachable code (DETACH flags are compile-time constants for debug)
public partial class PhotoModalViewModel : UiThreadSafeObservableObject
{
    private const bool DETACH_RUNTIME_DATA = false;
    private const bool DETACH_AUXILIARY_RUNTIME_DATA = false;

    private readonly WorldService worldService;
    private readonly ToastService toastService;

    public PhotoModalState state { get; }

    public PhotoModalViewModel(PhotoModalState state, WorldService worldService, ToastService toastService)
    {
        AppLogger.Trace("PhotoModalViewModel.ctor: enter");
        this.state = state;
        this.worldService = worldService;
        this.toastService = toastService;
        AppLogger.Trace("PhotoModalViewModel.ctor: exit");
    }

    /// <summary>選択写真のワールドページをブラウザで開く。</summary>
    public async Task handleOpenWorld()
    {
        AppLogger.Trace("PhotoModalViewModel.handleOpenWorld: enter");
        if (DETACH_RUNTIME_DATA || DETACH_AUXILIARY_RUNTIME_DATA)
        {
            AppLogger.Trace("PhotoModalViewModel.handleOpenWorld: skip (detached)");
            return;
        }

        var selectedPhoto = state.SelectedPhoto;
        if (selectedPhoto?.WorldId is not { Length: > 0 } worldId)
        {
            AppLogger.Trace("PhotoModalViewModel.handleOpenWorld: skip (no worldId)");
            return;
        }

        try
        {
            await worldService.OpenWorldUrlAsync(worldId).ConfigureAwait(false);
        }
        catch (Exception err)
        {
            AppLogger.Error($"PhotoModalViewModel.handleOpenWorld: threw: {err}");
            toastService.addToast(getMsg("PhotoModalViewModel.worldPageOpenFailed"), ToastType.error);
        }
        AppLogger.Trace("PhotoModalViewModel.handleOpenWorld: exit");
    }

    /// <summary>選択写真の場所をエクスプローラーで開く。</summary>
    public async Task handleOpenExplorer()
    {
        AppLogger.Trace("PhotoModalViewModel.handleOpenExplorer: enter");
        if (DETACH_RUNTIME_DATA || state.SelectedPhoto is null)
        {
            AppLogger.Trace("PhotoModalViewModel.handleOpenExplorer: skip (detached or no selection)");
            return;
        }

        try
        {
            await worldService.ShowInExplorerAsync(state.SelectedPhoto.PhotoPath).ConfigureAwait(false);
        }
        catch (Exception err)
        {
            AppLogger.Error($"PhotoModalViewModel.handleOpenExplorer: threw: {err}");
            toastService.addToast(getMsg("PhotoModalViewModel.explorerOpenFailed"), ToastType.error);
        }
        AppLogger.Trace("PhotoModalViewModel.handleOpenExplorer: exit");
    }

    /// <summary>写真を選択してモーダルに表示する。state に委譲。</summary>
    public void onSelectPhoto(PhotoThumbnailItem photo, bool isSimilarSearch = false)
    {
        AppLogger.Trace($"PhotoModalViewModel.onSelectPhoto: enter isSimilarSearch={isSimilarSearch}");
        state.onSelectPhoto(photo, isSimilarSearch);
        AppLogger.Trace("PhotoModalViewModel.onSelectPhoto: exit");
    }

    /// <summary>履歴スタックから前の写真に戻る。state に委譲。</summary>
    public void goBackPhoto()
    {
        AppLogger.Trace("PhotoModalViewModel.goBackPhoto: enter");
        state.goBackPhoto();
        AppLogger.Trace("PhotoModalViewModel.goBackPhoto: exit");
    }

    /// <summary>モーダルを閉じる。</summary>
    public void closePhotoModal()
    {
        AppLogger.Trace("PhotoModalViewModel.closePhotoModal: enter");
        state.closePhotoModal();
        AppLogger.Trace("PhotoModalViewModel.closePhotoModal: exit");
    }
}
