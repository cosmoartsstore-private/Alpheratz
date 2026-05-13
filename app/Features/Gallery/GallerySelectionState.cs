using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.Gallery;

#pragma warning disable CS0162 // Unreachable code (DETACH flags are compile-time constants for debug)
/// <summary>
/// マルチセレクトモードと選択中写真のセットを管理する。
/// 選択が変わるたびに DB から (photo_path, source_slot) ペアを引き直すため、
/// CTS で旧リクエストをキャンセルする仕組みを持つ。
/// </summary>
public partial class GallerySelectionState : UiThreadSafeObservableObject, IDisposable
{
    // 開発時の動作切り分け用デバッグフラグ。本番では常に false。
    // true にすると DB ロード経路を完全に切って、UI 遷移だけをテストできる。
    private const bool DETACH_RUNTIME_DATA = false;

    // 補助ロード (selectedPhotoRefs の DB 引き直し) のみ切る軽量モード。
    // true で bulk-operation ツールバーが「選択枚数 0」のまま動作する。本番では false。
    private const bool DETACH_AUXILIARY_RUNTIME_DATA = false;

    private readonly PhotoService photoService;
    private readonly ToastService toastService;
    private CancellationTokenSource? selectedPhotoRefsCancellation;

    [ObservableProperty] private bool isMultiSelectMode;
    public UiObservableCollection<string> selectedPhotoPaths { get; } = [];
    [ObservableProperty] private string? selectionAnchorPhotoPath;
    public UiObservableCollection<string> bulkTagSelections { get; } = [];
    [ObservableProperty] private bool isBulkTagModalOpen;
    public UiObservableCollection<SelectedPhotoRefDto> selectedPhotoRefs { get; } = [];

    public GallerySelectionState(PhotoService photoService, ToastService toastService)
    {
        AppLogger.Trace("GallerySelectionState.ctor: enter");
        this.photoService = photoService;
        this.toastService = toastService;
        selectedPhotoPaths.CollectionChanged += selectedPhotoPathsChanged;
        AppLogger.Trace("GallerySelectionState.ctor: exit");
    }

    private void selectedPhotoPathsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AppLogger.Trace($"GallerySelectionState.selectedPhotoPathsChanged: enter action={e.Action}");
        try
        {
            _ = loadSelectedPhotoRefs();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GallerySelectionState.selectedPhotoPathsChanged: threw: {ex}");
        }
        AppLogger.Trace("GallerySelectionState.selectedPhotoPathsChanged: exit");
    }

    public void toggleSelectedPhoto(PhotoGridItem item, bool shiftKey, IReadOnlyList<PhotoGridItem> displayPhotoItems)
    {
        AppLogger.Trace($"GallerySelectionState.toggleSelectedPhoto: enter shiftKey={shiftKey}");
        try
        {
            var photoPath = item.Photo.PhotoPath;
            if (shiftKey && SelectionAnchorPhotoPath is not null)
            {
                AppLogger.Trace("GallerySelectionState.toggleSelectedPhoto: branch=range select");
                var list = displayPhotoItems.ToList();
                var anchorIndex = list.FindIndex(entry => entry.Photo.PhotoPath == SelectionAnchorPhotoPath);
                var targetIndex = list.FindIndex(entry => entry.Photo.PhotoPath == photoPath);
                if (anchorIndex >= 0 && targetIndex >= 0)
                {
                    var startIndex = Math.Min(anchorIndex, targetIndex);
                    var endIndex = Math.Max(anchorIndex, targetIndex);
                    var rangePhotoPaths = displayPhotoItems
                        .Skip(startIndex)
                        .Take(endIndex - startIndex + 1)
                        .Select(entry => entry.Photo.PhotoPath);

                    foreach (var path in rangePhotoPaths)
                    {
                        if (!selectedPhotoPaths.Contains(path))
                        {
                            selectedPhotoPaths.Add(path);
                        }
                    }

                    SelectionAnchorPhotoPath = photoPath;
                    AppLogger.Trace("GallerySelectionState.toggleSelectedPhoto: exit (range applied)");
                    return;
                }
            }

            if (selectedPhotoPaths.Contains(photoPath))
            {
                AppLogger.Trace("GallerySelectionState.toggleSelectedPhoto: branch=remove");
                selectedPhotoPaths.Remove(photoPath);
            }
            else
            {
                AppLogger.Trace("GallerySelectionState.toggleSelectedPhoto: branch=add");
                selectedPhotoPaths.Add(photoPath);
            }

            SelectionAnchorPhotoPath = photoPath;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GallerySelectionState.toggleSelectedPhoto: threw: {ex}");
        }
        AppLogger.Trace("GallerySelectionState.toggleSelectedPhoto: exit");
    }

    public void clearSelectedPhotos()
    {
        AppLogger.Trace($"GallerySelectionState.clearSelectedPhotos: enter count={selectedPhotoPaths.Count}");
        try
        {
            selectedPhotoPaths.Clear();
            SelectionAnchorPhotoPath = null;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GallerySelectionState.clearSelectedPhotos: threw: {ex}");
        }
        AppLogger.Trace("GallerySelectionState.clearSelectedPhotos: exit");
    }

    public void handleToggleMultiSelectMode()
    {
        AppLogger.Trace($"GallerySelectionState.handleToggleMultiSelectMode: enter IsMultiSelectMode={IsMultiSelectMode}");
        try
        {
            if (IsMultiSelectMode)
            {
                clearSelectedPhotos();
            }

            IsMultiSelectMode = !IsMultiSelectMode;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GallerySelectionState.handleToggleMultiSelectMode: threw: {ex}");
        }
        AppLogger.Trace($"GallerySelectionState.handleToggleMultiSelectMode: exit IsMultiSelectMode={IsMultiSelectMode}");
    }

    public void setSelectedPhotoRefs(IEnumerable<SelectedPhotoRefDto> refsToSet)
    {
        AppLogger.Trace("GallerySelectionState.setSelectedPhotoRefs: enter");
        try
        {
            selectedPhotoRefs.Clear();
            foreach (var item in refsToSet)
            {
                selectedPhotoRefs.Add(item);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GallerySelectionState.setSelectedPhotoRefs: threw: {ex}");
        }
        AppLogger.Trace($"GallerySelectionState.setSelectedPhotoRefs: exit count={selectedPhotoRefs.Count}");
    }

    public async Task loadSelectedPhotoRefs()
    {
        AppLogger.Trace("GallerySelectionState.loadSelectedPhotoRefs: enter");

        if (DETACH_RUNTIME_DATA || DETACH_AUXILIARY_RUNTIME_DATA)
        {
            AppLogger.Trace("GallerySelectionState.loadSelectedPhotoRefs: skip (detached)");
            selectedPhotoRefs.Clear();
            return;
        }

        if (selectedPhotoPaths.Count == 0)
        {
            AppLogger.Trace("GallerySelectionState.loadSelectedPhotoRefs: skip (no selection)");
            selectedPhotoRefs.Clear();
            return;
        }

        // 旧実装: Cancel(); Dispose(); selectedPhotoRefsCancellation = new(...) は
        // 3 ステップが直列でないため、別スレッドの loadSelectedPhotoRefs が同じ CTS を
        // 参照したまま走り抜けて Dispose 済み CTS にアクセスする race があった。
        // 新規 CTS を作ってから Interlocked.Exchange で原子的に差し替え、旧 CTS を
        // ローカル参照で受け取って Cancel/Dispose する。
        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref selectedPhotoRefsCancellation, newCts);
        if (oldCts is not null)
        {
            try { oldCts.Cancel(); } catch (ObjectDisposedException) { }
            try { oldCts.Dispose(); } catch (ObjectDisposedException) { }
        }
        var token = newCts.Token;
        var photoPaths = selectedPhotoPaths.ToArray();

        try
        {
            var refs = await photoService.GetSelectedPhotoRefsAsync(photoPaths, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                AppLogger.Trace("GallerySelectionState.loadSelectedPhotoRefs: cancelled before apply");
                return;
            }

            setSelectedPhotoRefs(refs);
        }
        catch (Exception err)
        {
            // Continue: legacy alpheratz only toasts on non-cancellation
            // failure; cancellations are silent.
            AppLogger.Error($"GallerySelectionState.loadSelectedPhotoRefs: threw: {err}");
            if (!token.IsCancellationRequested)
            {
                toastService.addToast($"選択写真情報の取得に失敗しました: {err}", ToastType.error);
            }
        }
        AppLogger.Trace("GallerySelectionState.loadSelectedPhotoRefs: exit");
    }

    public void Dispose()
    {
        AppLogger.Trace("GallerySelectionState.Dispose: enter");
        try
        {
            selectedPhotoPaths.CollectionChanged -= selectedPhotoPathsChanged;
            var cts = Interlocked.Exchange(ref selectedPhotoRefsCancellation, null);
            if (cts is not null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                try { cts.Dispose(); } catch (ObjectDisposedException) { }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GallerySelectionState.Dispose: threw: {ex}");
        }
        AppLogger.Trace("GallerySelectionState.Dispose: exit");
    }
}