using Alpheratz.Core;
using Alpheratz.Services;

namespace Alpheratz.Features.WorldResolve;

public partial class WorldResolveItem : UiThreadSafeObservableObject
{
    // --- Target (unknown photo) ---
    public string TargetPhotoPath { get; }
    public string TargetPhotoFilename { get; }
    public string TargetPhash { get; }
    public long TargetSourceSlot { get; }

    private string? targetThumbPath;
    public string? TargetThumbPath { get => targetThumbPath; set => SetProperty(ref targetThumbPath, value); }

    // --- Currently selected match candidate ---
    private string? matchPhotoPath;
    public string? MatchPhotoPath
    {
        get => matchPhotoPath;
        set
        {
            if (!SetProperty(ref matchPhotoPath, value)) return;
            OnPropertyChanged(nameof(HasMatchPhoto));
        }
    }

    private string? matchPhotoFilename;
    public string? MatchPhotoFilename { get => matchPhotoFilename; set => SetProperty(ref matchPhotoFilename, value); }

    private string? matchWorldName;
    public string? MatchWorldName
    {
        get => matchWorldName;
        set
        {
            if (!SetProperty(ref matchWorldName, value)) return;
            OnPropertyChanged(nameof(HasMatch));
            OnPropertyChanged(nameof(CanApply));
        }
    }

    private string? matchWorldId;
    public string? MatchWorldId { get => matchWorldId; set => SetProperty(ref matchWorldId, value); }

    private string? matchThumbPath;
    public string? MatchThumbPath { get => matchThumbPath; set => SetProperty(ref matchThumbPath, value); }

    private int? matchDistance;
    public int? MatchDistance
    {
        get => matchDistance;
        set
        {
            if (!SetProperty(ref matchDistance, value)) return;
            OnPropertyChanged(nameof(CanApply));
        }
    }

    private bool isManualMatch;
    /// <summary>利用者が自由入力したワールド名を選択しているか。</summary>
    public bool IsManualMatch
    {
        get => isManualMatch;
        set
        {
            if (!SetProperty(ref isManualMatch, value)) return;
            OnPropertyChanged(nameof(CanApply));
        }
    }

    // --- State ---
    private bool isApplied;
    public bool IsApplied { get => isApplied; set => SetProperty(ref isApplied, value); }

    public bool HasMatch => !string.IsNullOrWhiteSpace(MatchWorldName);
    public bool HasMatchPhoto => !string.IsNullOrWhiteSpace(MatchPhotoPath);
    /// <summary>手動補完、または一致率 71% 以上の自動候補だけを保存対象にできる。</summary>
    public bool CanApply => HasMatch
        && (IsManualMatch || WorldService.CanAutomaticallyComplete(MatchDistance));

    public WorldResolveItem(string photoPath, string photoFilename, string phash, long sourceSlot)
    {
        TargetPhotoPath = photoPath;
        TargetPhotoFilename = photoFilename;
        TargetPhash = phash;
        TargetSourceSlot = sourceSlot;
    }
}
