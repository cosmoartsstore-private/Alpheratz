using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.PhotoModal;

/// <summary>
/// PhotoModal（写真詳細モーダル）のステート。
/// 選択写真の管理・前後ナビゲーション・メモ保存・補助データの遅延取得を担当する。
/// </summary>
#pragma warning disable CS0162 // Unreachable code (DETACH flags are compile-time constants for debug)
public partial class PhotoModalState : UiThreadSafeObservableObject
{
    // true にするとランタイムデータの取得・保存を全てスキップする（デバッグ用）。
    private const bool DETACH_RUNTIME_DATA = false;

    // true にするとメモ・タグの取得・保存のみスキップする（デバッグ用）。
    // 本番では必ず false にすること。
    private const bool DETACH_AUXILIARY_RUNTIME_DATA = false;

    private readonly PhotoService photoService;
    private readonly ToastService toastService;
    private CancellationTokenSource? selectedPhotoCancellation;

    [ObservableProperty] private PhotoThumbnailItem? selectedPhoto;
    /// <summary>類似ワールド検索時の戻り先スタック。</summary>
    public UiObservableCollection<PhotoThumbnailItem> photoHistory { get; } = [];
    [ObservableProperty] private string localMemo = string.Empty;
    [ObservableProperty] private bool isSavingMemo;

    /// <summary>ギャラリーから渡された写真リスト。前後ナビゲーションに使用する。</summary>
    private List<PhotoThumbnailItem> photoList = [];

    public bool CanGoBack => photoHistory.Count > 0;
    public bool CanGoPrev => selectedPhoto is not null && photoList.Count > 0 && photoList.IndexOf(selectedPhoto) > 0;
    public bool CanGoNext => selectedPhoto is not null && photoList.Count > 0 && photoList.IndexOf(selectedPhoto) < photoList.Count - 1;

    /// <summary>前後ナビゲーション用の写真リストを設定する。</summary>
    public void setPhotoList(IReadOnlyList<PhotoThumbnailItem> list)
    {
        AppLogger.Trace($"PhotoModalState.setPhotoList: enter count={list.Count}");
        photoList = [.. list];
        AppLogger.Trace("PhotoModalState.setPhotoList: exit");
    }

    public PhotoModalState(PhotoService photoService, ToastService toastService)
    {
        AppLogger.Trace("PhotoModalState.ctor: enter");
        this.photoService = photoService;
        this.toastService = toastService;
        AppLogger.Trace("PhotoModalState.ctor: exit");
    }

    /// <summary>現在のローカルメモを DB に保存する。</summary>
    public async Task handleSaveMemo()
    {
        AppLogger.Trace("PhotoModalState.handleSaveMemo: enter");
        if (DETACH_RUNTIME_DATA || DETACH_AUXILIARY_RUNTIME_DATA || SelectedPhoto is null)
        {
            AppLogger.Trace("PhotoModalState.handleSaveMemo: skip (detached or no selection)");
            return;
        }

        IsSavingMemo = true;
        try
        {
            await photoService.SavePhotoMemoAsync(SelectedPhoto.PhotoPath, LocalMemo, SelectedPhoto.SourceSlot).ConfigureAwait(false);
            SelectedPhoto.Memo = LocalMemo;
            toastService.addToast("メモを保存しました。");
        }
        catch (Exception err)
        {
            AppLogger.Error($"PhotoModalState.handleSaveMemo: threw: {err}");
            toastService.addToast($"保存に失敗しました: {err}", ToastType.error);
        }
        finally
        {
            IsSavingMemo = false;
        }
        AppLogger.Trace("PhotoModalState.handleSaveMemo: exit");
    }

    /// <summary>選択写真を直接設定する（補助データの遅延取得も開始する）。</summary>
    public void setSelectedPhoto(PhotoThumbnailItem? photo)
    {
        AppLogger.Trace($"PhotoModalState.setSelectedPhoto: enter photo={photo?.PhotoPath ?? "(null)"}");
        try
        {
            SelectedPhoto = photo;
            _ = loadSelectedPhotoAuxiliaryData(photo);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoModalState.setSelectedPhoto: threw: {ex}");
        }
        AppLogger.Trace("PhotoModalState.setSelectedPhoto: exit");
    }

    /// <summary>
    /// 写真を選択してモーダルに表示する。
    /// isSimilarSearch=true の場合は現在の写真を履歴スタックに積んで戻れるようにする。
    /// </summary>
    public void onSelectPhoto(PhotoThumbnailItem photo, bool isSimilarSearch = false)
    {
        AppLogger.Trace($"PhotoModalState.onSelectPhoto: enter isSimilarSearch={isSimilarSearch}");
        try
        {
            if (SelectedPhoto is not null && isSimilarSearch)
            {
                photoHistory.Add(SelectedPhoto);
            }
            else if (!isSimilarSearch)
            {
                photoHistory.Clear();
            }

            SelectedPhoto = photo;
            LocalMemo = string.Empty;
            _ = loadSelectedPhotoAuxiliaryData(photo);
            notifyNavProps();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoModalState.onSelectPhoto: threw: {ex}");
        }
        AppLogger.Trace("PhotoModalState.onSelectPhoto: exit");
    }

    /// <summary>履歴スタックから前の写真に戻る。</summary>
    public void goBackPhoto()
    {
        AppLogger.Trace($"PhotoModalState.goBackPhoto: enter historyCount={photoHistory.Count}");
        if (photoHistory.Count <= 0)
        {
            AppLogger.Trace("PhotoModalState.goBackPhoto: skip (empty history)");
            return;
        }

        try
        {
            var lastPhoto = photoHistory[^1];
            photoHistory.RemoveAt(photoHistory.Count - 1);
            SelectedPhoto = lastPhoto;
            LocalMemo = lastPhoto.Memo ?? string.Empty;
            notifyNavProps();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoModalState.goBackPhoto: threw: {ex}");
        }
        AppLogger.Trace("PhotoModalState.goBackPhoto: exit");
    }

    /// <summary>写真リスト上の前の写真に移動する。</summary>
    public void goPrevPhoto()
    {
        AppLogger.Trace("PhotoModalState.goPrevPhoto: enter");
        if (SelectedPhoto is null)
        {
            AppLogger.Trace("PhotoModalState.goPrevPhoto: skip (no selection)");
            return;
        }

        var idx = photoList.IndexOf(SelectedPhoto);
        if (idx <= 0)
        {
            AppLogger.Trace($"PhotoModalState.goPrevPhoto: skip (at start, idx={idx})");
            return;
        }

        onSelectPhoto(photoList[idx - 1]);
        AppLogger.Trace("PhotoModalState.goPrevPhoto: exit");
    }

    /// <summary>写真リスト上の次の写真に移動する。</summary>
    public void goNextPhoto()
    {
        AppLogger.Trace("PhotoModalState.goNextPhoto: enter");
        if (SelectedPhoto is null)
        {
            AppLogger.Trace("PhotoModalState.goNextPhoto: skip (no selection)");
            return;
        }

        var idx = photoList.IndexOf(SelectedPhoto);
        if (idx < 0 || idx >= photoList.Count - 1)
        {
            AppLogger.Trace($"PhotoModalState.goNextPhoto: skip (at end, idx={idx})");
            return;
        }

        onSelectPhoto(photoList[idx + 1]);
        AppLogger.Trace("PhotoModalState.goNextPhoto: exit");
    }

    /// <summary>CanGoBack / CanGoPrev / CanGoNext の変更を通知する。</summary>
    private void notifyNavProps()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
    }

    /// <summary>モーダルを閉じて選択状態・履歴をクリアする。</summary>
    public void closePhotoModal()
    {
        AppLogger.Trace("PhotoModalState.closePhotoModal: enter");
        try
        {
            selectedPhotoCancellation?.Cancel();
            selectedPhotoCancellation?.Dispose();
            selectedPhotoCancellation = null;
            SelectedPhoto = null;
            photoHistory.Clear();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoModalState.closePhotoModal: threw: {ex}");
        }
        AppLogger.Trace("PhotoModalState.closePhotoModal: exit");
    }

    /// <summary>
    /// 選択写真のメモ・タグを DB から非同期取得する。
    /// 写真切替時は前回のリクエストをキャンセルして新しいリクエストを開始する。
    /// </summary>
    private async Task loadSelectedPhotoAuxiliaryData(PhotoThumbnailItem? selectedPhotoSnapshot)
    {
        AppLogger.Trace($"PhotoModalState.loadSelectedPhotoAuxiliaryData: enter snapshot={selectedPhotoSnapshot?.PhotoPath ?? "(null)"}");
        selectedPhotoCancellation?.Cancel();
        selectedPhotoCancellation?.Dispose();
        selectedPhotoCancellation = new CancellationTokenSource();
        var token = selectedPhotoCancellation.Token;

        if (DETACH_RUNTIME_DATA || DETACH_AUXILIARY_RUNTIME_DATA)
        {
            AppLogger.Trace("PhotoModalState.loadSelectedPhotoAuxiliaryData: skip (detached)");
            LocalMemo = string.Empty;
            return;
        }

        if (selectedPhotoSnapshot is null)
        {
            AppLogger.Trace("PhotoModalState.loadSelectedPhotoAuxiliaryData: skip (null snapshot)");
            LocalMemo = string.Empty;
            return;
        }

        LocalMemo = selectedPhotoSnapshot.Memo ?? string.Empty;

        try
        {
            // メモとタグを並列取得する
            var memoTask = photoService.GetPhotoMemoAsync(selectedPhotoSnapshot.PhotoPath, selectedPhotoSnapshot.SourceSlot, token);
            var tagsTask = photoService.GetPhotoTagsAsync(selectedPhotoSnapshot.PhotoPath, selectedPhotoSnapshot.SourceSlot, token);
            await Task.WhenAll(memoTask, tagsTask).ConfigureAwait(false);

            if (token.IsCancellationRequested)
            {
                AppLogger.Trace("PhotoModalState.loadSelectedPhotoAuxiliaryData: cancelled before apply");
                return;
            }

            var memo = await memoTask.ConfigureAwait(false);
            var tags = await tagsTask.ConfigureAwait(false);

            LocalMemo = memo;
            if (SelectedPhoto is not null && SelectedPhoto.PhotoPath == selectedPhotoSnapshot.PhotoPath)
            {
                SelectedPhoto.Memo = memo;
                SelectedPhoto.Tags = tags;
            }
        }
        catch (Exception err)
        {
            AppLogger.Error($"PhotoModalState.loadSelectedPhotoAuxiliaryData: threw: {err}");
            if (!token.IsCancellationRequested)
            {
                toastService.addToast($"メモの読み込みに失敗しました: {err}", ToastType.error);
            }
        }
        AppLogger.Trace("PhotoModalState.loadSelectedPhotoAuxiliaryData: exit");
    }
}
