using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Features.Settings;

/// <summary>重複写真1件の表示と削除選択を保持する。</summary>
public partial class DuplicatePhotoItemViewModel : UiThreadSafeObservableObject
{
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool canSelect = true;
    [ObservableProperty] private bool isBusy;

    public string PhotoFilename { get; }
    public string PhotoPath { get; }
    public string PreviewPath { get; }
    public string DirectoryPath { get; }
    public string DetailsText { get; }
    public string SelectionAutomationName { get; }
    public string RetentionLabel { get; } = getMsg("SettingsPage.duplicatePhotosRetainedLabel");
    public long SourceSlot { get; }
    public long FileSize { get; }
    public bool IsRequiredRetention => !IsSelected && !CanSelect;
    public bool IsSelectionEnabled => !IsBusy && CanSelect;

    internal event EventHandler? SelectionChanged;

    public DuplicatePhotoItemViewModel(ExactDuplicatePhoto photo)
    {
        PhotoFilename = string.IsNullOrWhiteSpace(photo.PhotoFilename)
            ? Path.GetFileName(photo.PhotoPath)
            : photo.PhotoFilename;
        PhotoPath = photo.PhotoPath;
        PreviewPath = photo.PhotoPath.Replace('/', Path.DirectorySeparatorChar);
        DirectoryPath = ResolveDirectoryPath(PreviewPath);
        SourceSlot = photo.SourceSlot;
        FileSize = photo.FileSize;
        var sourceLabel = getMsg(photo.SourceSlot == 2
            ? "SettingsPage.secondaryPhotoFolderLabel"
            : "SettingsPage.primaryPhotoFolderLabel");
        var timestamp = string.IsNullOrWhiteSpace(photo.Timestamp)
            ? getMsg("DuplicatePhotosViewModel.unknownDate")
            : photo.Timestamp;
        DetailsText = getMsg(
            "DuplicatePhotosViewModel.photoDetails",
            ("timestamp", timestamp),
            ("size", DuplicatePhotosViewModel.FormatFileSize(photo.FileSize)),
            ("source", sourceLabel));
        SelectionAutomationName = getMsg(
            "DuplicatePhotosViewModel.selectForDeletionAutomationName",
            ("filename", PhotoFilename),
            ("folder", DirectoryPath));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRequiredRetention));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCanSelectChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRequiredRetention));
        OnPropertyChanged(nameof(IsSelectionEnabled));
    }

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(IsSelectionEnabled));

    private static string ResolveDirectoryPath(string photoPath)
    {
        try { return Path.GetDirectoryName(photoPath) ?? photoPath; }
        catch { return photoPath; }
    }
}

/// <summary>内容が完全一致する写真群と、少なくとも1枚を残す選択制約を管理する。</summary>
public sealed class DuplicatePhotoGroupViewModel : UiThreadSafeObservableObject
{
    private int selectedCount;

    public string ContentHash { get; }
    public long FileSize { get; }
    public UiObservableCollection<DuplicatePhotoItemViewModel> Photos { get; } = [];
    public int SelectedCount
    {
        get => selectedCount;
        private set => SetProperty(ref selectedCount, value);
    }
    public string SummaryText => getMsg(
        "DuplicatePhotosViewModel.groupSummary",
        ("count", Photos.Count),
        ("size", DuplicatePhotosViewModel.FormatFileSize(FileSize)));

    internal event EventHandler? SelectionChanged;

    public DuplicatePhotoGroupViewModel(ExactDuplicatePhotoGroup group)
    {
        ContentHash = group.ContentHash;
        FileSize = group.FileSize;
        foreach (var photo in group.Photos)
        {
            var item = new DuplicatePhotoItemViewModel(photo);
            item.SelectionChanged += Photo_SelectionChanged;
            Photos.Add(item);
        }
        UpdateSelectionState();
    }

    internal void RemovePhotos(ISet<string> deletedPaths)
    {
        foreach (var photo in Photos
            .Where(item => deletedPaths.Contains(item.PhotoPath))
            .ToArray())
        {
            photo.SelectionChanged -= Photo_SelectionChanged;
            Photos.Remove(photo);
        }
        UpdateSelectionState();
        OnPropertyChanged(nameof(SummaryText));
    }

    internal void SetBusy(bool value)
    {
        foreach (var photo in Photos)
            photo.IsBusy = value;
    }

    private void Photo_SelectionChanged(object? sender, EventArgs e)
    {
        UpdateSelectionState();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelectionState()
    {
        SelectedCount = Photos.Count(photo => photo.IsSelected);
        var unselectedCount = Photos.Count - SelectedCount;
        foreach (var photo in Photos)
        {
            // 選択済み項目は解除できるよう有効のままにし、最後の未選択写真だけを固定する。
            photo.CanSelect = photo.IsSelected || unselectedCount > 1;
        }
    }
}

/// <summary>設定画面の重複検出、削除選択、処理結果を管理する。</summary>
public partial class DuplicatePhotosViewModel : UiThreadSafeObservableObject
{
    private readonly PhotoService photoService;
    private readonly DispatcherService dispatcherService;
    private readonly ToastService toastService;
    private long detectionVersion;

    [ObservableProperty] private bool isDetecting;
    [ObservableProperty] private bool isDeleting;
    [ObservableProperty] private bool hasSearched;
    [ObservableProperty] private string progressText = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string detectionSummary = string.Empty;
    [ObservableProperty] private string selectionSummary;
    [ObservableProperty] private int selectedCount;
    [ObservableProperty] private long selectedBytes;

    public UiObservableCollection<DuplicatePhotoGroupViewModel> Groups { get; } = [];
    public bool IsBusy => IsDetecting || IsDeleting;
    public bool CanDetect => !IsBusy;
    public bool CanDeleteSelected => SelectedCount > 0 && !IsBusy;
    public bool HasGroups => Groups.Count > 0;
    public bool HasProgress => IsBusy && !string.IsNullOrWhiteSpace(ProgressText);
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public bool ShowInitialGuidance => !HasSearched && !IsBusy && !HasStatus;
    public string DetectButtonText => getMsg(HasSearched
        ? "DuplicatePhotosViewModel.detectAgainButton"
        : "DuplicatePhotosViewModel.detectButton");

    public DuplicatePhotosViewModel(
        PhotoService photoService,
        DispatcherService dispatcherService,
        ToastService toastService)
    {
        this.photoService = photoService;
        this.dispatcherService = dispatcherService;
        this.toastService = toastService;
        selectionSummary = getMsg("DuplicatePhotosViewModel.selectPhotosGuidance");
    }

    /// <summary>現在登録されている全写真から完全一致するファイルを検出する。</summary>
    public async Task DetectAsync(CancellationToken ct = default)
    {
        if (IsBusy)
            return;

        var version = Interlocked.Increment(ref detectionVersion);
        await dispatcherService.RunOnUiThread(() =>
        {
            ClearGroups();
            HasSearched = false;
            StatusText = string.Empty;
            DetectionSummary = string.Empty;
            ProgressText = getMsg("DuplicatePhotosViewModel.preparingDetection");
            IsDetecting = true;
            NotifyDisplayStateChanged();
        }).ConfigureAwait(false);

        var progress = new CallbackProgress<DuplicatePhotoScanProgress>(value =>
        {
            _ = dispatcherService.RunOnUiThread(() =>
            {
                if (version == Interlocked.Read(ref detectionVersion) && IsDetecting)
                    ApplyProgress(value);
            });
        });

        try
        {
            var result = await photoService
                .FindExactDuplicatePhotosAsync(progress, ct)
                .ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
            {
                if (version != Interlocked.Read(ref detectionVersion))
                    return;
                ApplyDetectionResult(result);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await dispatcherService.RunOnUiThread(() =>
            {
                if (version == Interlocked.Read(ref detectionVersion))
                    StatusText = getMsg("DuplicatePhotosViewModel.detectionCanceled");
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"DuplicatePhotosViewModel.DetectAsync: threw: {ex}");
            await dispatcherService.RunOnUiThread(() =>
            {
                if (version == Interlocked.Read(ref detectionVersion))
                    StatusText = getMsg("DuplicatePhotosViewModel.detectionFailed");
            }).ConfigureAwait(false);
            toastService.addToast(
                getMsg("DuplicatePhotosViewModel.detectionFailed"),
                ToastType.error);
        }
        finally
        {
            await dispatcherService.RunOnUiThread(() =>
            {
                if (version != Interlocked.Read(ref detectionVersion))
                    return;
                IsDetecting = false;
                ProgressText = string.Empty;
                NotifyDisplayStateChanged();
            }).ConfigureAwait(false);
        }
    }

    /// <summary>現在選択中の写真を再検証付き削除処理へ渡し、成功した項目だけを結果から除く。</summary>
    public async Task DeleteSelectedAsync(
        Func<IReadOnlyList<DuplicatePhotoDeleteTarget>, Task<DuplicatePhotoDeleteResult>> delete,
        CancellationToken ct = default)
    {
        if (IsBusy || !CanDeleteSelected)
            return;

        var targets = BuildDeleteTargets();
        if (targets.Count == 0)
            return;

        await dispatcherService.RunOnUiThread(() =>
        {
            IsDeleting = true;
            StatusText = string.Empty;
            ProgressText = getMsg(
                "DuplicatePhotosViewModel.deletingPhotos",
                ("count", targets.Count));
            NotifyDisplayStateChanged();
        }).ConfigureAwait(false);

        try
        {
            ct.ThrowIfCancellationRequested();
            var result = await delete(targets).ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() => ApplyDeletionResult(result)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await dispatcherService.RunOnUiThread(() =>
                StatusText = getMsg("DuplicatePhotosViewModel.deletionCanceled")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"DuplicatePhotosViewModel.DeleteSelectedAsync: threw: {ex}");
            await dispatcherService.RunOnUiThread(() =>
                StatusText = getMsg("DuplicatePhotosViewModel.deletionFailed")).ConfigureAwait(false);
            toastService.addToast(
                getMsg("DuplicatePhotosViewModel.deletionFailed"),
                ToastType.error);
        }
        finally
        {
            await dispatcherService.RunOnUiThread(() =>
            {
                IsDeleting = false;
                ProgressText = string.Empty;
                NotifyDisplayStateChanged();
            }).ConfigureAwait(false);
        }
    }

    /// <summary>確認ダイアログに表示する選択中写真の合計サイズ。</summary>
    public string SelectedSizeLabel => FormatFileSize(SelectedBytes);

    internal IReadOnlyList<DuplicatePhotoDeleteTarget> BuildDeleteTargets()
    {
        var targets = new List<DuplicatePhotoDeleteTarget>(SelectedCount);
        foreach (var group in Groups)
        {
            var retainedPhotoPaths = group.Photos
                .Where(photo => !photo.IsSelected)
                .Select(photo => photo.PhotoPath)
                .ToArray();
            if (retainedPhotoPaths.Length == 0)
                continue;

            foreach (var photo in group.Photos.Where(item => item.IsSelected))
            {
                targets.Add(new DuplicatePhotoDeleteTarget(
                    new SelectedPhotoRefDto
                    {
                        photo_path = photo.PhotoPath,
                        source_slot = photo.SourceSlot,
                    },
                    group.ContentHash,
                    group.FileSize,
                    retainedPhotoPaths));
            }
        }
        return targets;
    }

    internal static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var displayValue = (double)value;
        var unitIndex = 0;
        while (displayValue >= 1024 && unitIndex < units.Length - 1)
        {
            displayValue /= 1024;
            unitIndex++;
        }

        var format = unitIndex == 0 ? "0" : displayValue >= 100 ? "0" : "0.#";
        return $"{displayValue.ToString(format, CultureInfo.CurrentCulture)} {units[unitIndex]}";
    }

    partial void OnIsDetectingChanged(bool value) => ApplyBusyState();
    partial void OnIsDeletingChanged(bool value) => ApplyBusyState();
    partial void OnHasSearchedChanged(bool value) => NotifyDisplayStateChanged();
    partial void OnProgressTextChanged(string value) => NotifyDisplayStateChanged();
    partial void OnStatusTextChanged(string value) => NotifyDisplayStateChanged();

    private void ApplyProgress(DuplicatePhotoScanProgress progress)
    {
        var key = progress.Stage == DuplicatePhotoScanStage.InspectingFiles
            ? "DuplicatePhotosViewModel.inspectingFiles"
            : "DuplicatePhotosViewModel.comparingContents";
        ProgressText = getMsg(
            key,
            ("processed", progress.Processed),
            ("total", progress.Total));
    }

    private void ApplyDetectionResult(ExactDuplicateDetectionResult result)
    {
        ClearGroups();
        foreach (var detectedGroup in result.Groups)
        {
            var group = new DuplicatePhotoGroupViewModel(detectedGroup);
            group.SetBusy(IsBusy);
            group.SelectionChanged += Group_SelectionChanged;
            Groups.Add(group);
        }

        HasSearched = true;
        UpdateDetectionSummary();
        if (result.TotalPhotoCount == 0)
        {
            StatusText = getMsg("DuplicatePhotosViewModel.noPhotos");
        }
        else if (Groups.Count == 0)
        {
            StatusText = result.UnreadablePhotoCount > 0
                ? getMsg(
                    "DuplicatePhotosViewModel.noDuplicatesWithUnreadable",
                    ("count", result.UnreadablePhotoCount))
                : getMsg("DuplicatePhotosViewModel.noDuplicates");
        }
        else if (result.UnreadablePhotoCount > 0)
        {
            StatusText = getMsg(
                "DuplicatePhotosViewModel.unreadablePhotos",
                ("count", result.UnreadablePhotoCount));
        }
        else
        {
            StatusText = string.Empty;
        }
        UpdateSelectionState();
        NotifyDisplayStateChanged();
    }

    private void ApplyDeletionResult(DuplicatePhotoDeleteResult result)
    {
        var deletedPaths = result.DeletedPhotos
            .Select(photo => photo.photo_path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (deletedPaths.Count > 0)
        {
            foreach (var group in Groups.ToArray())
            {
                group.RemovePhotos(deletedPaths);
                if (group.Photos.Count < 2)
                {
                    group.SelectionChanged -= Group_SelectionChanged;
                    Groups.Remove(group);
                }
            }
        }

        HasSearched = true;
        UpdateDetectionSummary();
        UpdateSelectionState();

        var deletedCount = result.DeletedPhotos.Count;
        var failedCount = result.FailedPhotos.Count;
        ToastType toastType;
        if (!result.DatabaseUpdated)
        {
            StatusText = getMsg(
                "DuplicatePhotosViewModel.deletedDatabaseUpdateFailed",
                ("count", deletedCount));
            toastType = ToastType.error;
        }
        else if (failedCount > 0 && deletedCount > 0)
        {
            StatusText = getMsg(
                "DuplicatePhotosViewModel.partiallyDeleted",
                ("deleted", deletedCount),
                ("failed", failedCount));
            toastType = ToastType.error;
        }
        else if (failedCount > 0)
        {
            StatusText = getMsg(
                "DuplicatePhotosViewModel.noneDeleted",
                ("count", failedCount));
            toastType = ToastType.error;
        }
        else
        {
            StatusText = getMsg(
                "DuplicatePhotosViewModel.deleted",
                ("count", deletedCount));
            toastType = ToastType.info;
        }

        toastService.addToast(StatusText, toastType);
        NotifyDisplayStateChanged();
    }

    private void Group_SelectionChanged(object? sender, EventArgs e)
        => UpdateSelectionState();

    private void UpdateSelectionState()
    {
        SelectedCount = Groups.Sum(group => group.SelectedCount);
        SelectedBytes = Groups.Sum(group => group.Photos
            .Where(photo => photo.IsSelected)
            .Sum(photo => photo.FileSize));
        SelectionSummary = SelectedCount == 0
            ? getMsg("DuplicatePhotosViewModel.selectPhotosGuidance")
            : getMsg(
                "DuplicatePhotosViewModel.selectionSummary",
                ("count", SelectedCount),
                ("size", FormatFileSize(SelectedBytes)));
        OnPropertyChanged(nameof(SelectedSizeLabel));
        NotifyDisplayStateChanged();
    }

    private void UpdateDetectionSummary()
    {
        if (Groups.Count == 0)
        {
            DetectionSummary = string.Empty;
            return;
        }

        var photoCount = Groups.Sum(group => group.Photos.Count);
        var deletableCount = Groups.Sum(group => Math.Max(0, group.Photos.Count - 1));
        var reclaimableBytes = Groups.Sum(group => group.FileSize * Math.Max(0, group.Photos.Count - 1L));
        DetectionSummary = getMsg(
            "DuplicatePhotosViewModel.detectionSummary",
            ("groupCount", Groups.Count),
            ("photoCount", photoCount),
            ("deletableCount", deletableCount),
            ("size", FormatFileSize(reclaimableBytes)));
    }

    private void ClearGroups()
    {
        foreach (var group in Groups)
            group.SelectionChanged -= Group_SelectionChanged;
        Groups.Clear();
        UpdateSelectionState();
    }

    private void ApplyBusyState()
    {
        foreach (var group in Groups)
            group.SetBusy(IsBusy);
        NotifyDisplayStateChanged();
    }

    private void NotifyDisplayStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanDetect));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(ShowInitialGuidance));
        OnPropertyChanged(nameof(DetectButtonText));
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
