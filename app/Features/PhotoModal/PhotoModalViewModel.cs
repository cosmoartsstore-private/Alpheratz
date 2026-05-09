using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.PhotoModal;

/// <summary>
/// PhotoModal の ViewModel。state を薄くラップし、
/// ワールド関連の操作（ページ遷移・類似候補検索・候補適用）を追加する。
/// </summary>
#pragma warning disable CS0162 // Unreachable code (DETACH flags are compile-time constants for debug)
public partial class PhotoModalViewModel : UiThreadSafeObservableObject
{
    private const bool DETACH_RUNTIME_DATA = false;
    // true にするとワールド関連操作をスキップする（デバッグ用）。本番では false。
    private const bool DETACH_AUXILIARY_RUNTIME_DATA = false;

    private readonly WorldService worldService;
    private readonly ToastService toastService;

    public PhotoModalState state { get; }
    /// <summary>PDQ ハッシュで検出された類似ワールド候補のリスト。</summary>
    public UiObservableCollection<SimilarWorldCandidateItem> similarWorldCandidates { get; } = [];

    public PhotoModalViewModel(PhotoModalState state, WorldService worldService, ToastService toastService)
    {
        AppLogger.Trace("PhotoModalViewModel.ctor: enter");
        this.state = state;
        this.worldService = worldService;
        this.toastService = toastService;
        AppLogger.Trace("PhotoModalViewModel.ctor: exit");
    }

    /// <summary>state のメモ保存に委譲する。</summary>
    public Task handleSaveMemo()
    {
        AppLogger.Trace("PhotoModalViewModel.handleSaveMemo: enter");
        var task = state.handleSaveMemo();
        AppLogger.Trace("PhotoModalViewModel.handleSaveMemo: exit");
        return task;
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
            toastService.addToast($"ワールドページを開けませんでした: {err}", ToastType.error);
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
            toastService.addToast($"Explorer で表示できませんでした: {err}", ToastType.error);
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

    /// <summary>モーダルを閉じる。類似候補リストもクリアする。</summary>
    public void closePhotoModal()
    {
        AppLogger.Trace("PhotoModalViewModel.closePhotoModal: enter");
        similarWorldCandidates.Clear();
        state.closePhotoModal();
        AppLogger.Trace("PhotoModalViewModel.closePhotoModal: exit");
    }

    /// <summary>選択写真の PDQ ハッシュを元に類似ワールド候補を検索する。</summary>
    public async Task findSimilarWorldCandidates()
    {
        AppLogger.Trace("PhotoModalViewModel.findSimilarWorldCandidates: enter");
        if (DETACH_RUNTIME_DATA || DETACH_AUXILIARY_RUNTIME_DATA)
        {
            AppLogger.Trace("PhotoModalViewModel.findSimilarWorldCandidates: skip (detached)");
            similarWorldCandidates.Clear();
            return;
        }

        var selectedPhoto = state.SelectedPhoto;
        if (selectedPhoto is null)
        {
            AppLogger.Trace("PhotoModalViewModel.findSimilarWorldCandidates: skip (no selection)");
            return;
        }

        try
        {
            var results = await worldService.FindSimilarWorldCandidatesAsync(selectedPhoto.PhotoPath, 24).ConfigureAwait(false);
            similarWorldCandidates.Clear();

            foreach (var item in results.Select(SimilarWorldCandidateItem.FromDto))
            {
                similarWorldCandidates.Add(item);
            }
        }
        catch (Exception err)
        {
            AppLogger.Error($"PhotoModalViewModel.findSimilarWorldCandidates: threw: {err}");
            toastService.addToast($"類似ワールド候補の取得に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("PhotoModalViewModel.findSimilarWorldCandidates: exit");
    }

    /// <summary>選択した類似ワールド候補のワールド情報を現在の写真に適用する。</summary>
    public async Task applySimilarWorldCandidate(object? candidate)
    {
        AppLogger.Trace("PhotoModalViewModel.applySimilarWorldCandidate: enter");
        if (DETACH_RUNTIME_DATA || DETACH_AUXILIARY_RUNTIME_DATA)
        {
            AppLogger.Trace("PhotoModalViewModel.applySimilarWorldCandidate: skip (detached)");
            return;
        }

        if (state.SelectedPhoto is null || candidate is not SimilarWorldCandidateItem item)
        {
            AppLogger.Trace("PhotoModalViewModel.applySimilarWorldCandidate: skip (no selection or wrong type)");
            return;
        }

        try
        {
            await worldService
                .ApplyWorldMatchFromPhotoAsync(state.SelectedPhoto.PhotoPath, item.Photo.PhotoPath)
                .ConfigureAwait(false);

            state.SelectedPhoto.WorldId = item.Photo.WorldId;
            state.SelectedPhoto.WorldName = item.Photo.WorldName;
            state.SelectedPhoto.MatchSource = "phash";

            toastService.addToast("ワールド情報を反映しました。");
        }
        catch (Exception err)
        {
            AppLogger.Error($"PhotoModalViewModel.applySimilarWorldCandidate: threw: {err}");
            toastService.addToast($"ワールド情報の反映に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("PhotoModalViewModel.applySimilarWorldCandidate: exit");
    }
}
