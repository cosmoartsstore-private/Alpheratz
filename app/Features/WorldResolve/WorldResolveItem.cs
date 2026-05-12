using Alpheratz.Core;

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
    public string? MatchPhotoPath { get => matchPhotoPath; set => SetProperty(ref matchPhotoPath, value); }

    private string? matchPhotoFilename;
    public string? MatchPhotoFilename { get => matchPhotoFilename; set => SetProperty(ref matchPhotoFilename, value); }

    private string? matchWorldName;
    public string? MatchWorldName { get => matchWorldName; set => SetProperty(ref matchWorldName, value); }

    private string? matchWorldId;
    public string? MatchWorldId { get => matchWorldId; set => SetProperty(ref matchWorldId, value); }

    private string? matchThumbPath;
    public string? MatchThumbPath { get => matchThumbPath; set => SetProperty(ref matchThumbPath, value); }

    private int? matchDistance;
    public int? MatchDistance { get => matchDistance; set => SetProperty(ref matchDistance, value); }

    // --- State ---
    private bool isApplied;
    public bool IsApplied { get => isApplied; set => SetProperty(ref isApplied, value); }

    public bool HasMatch => MatchPhotoPath is not null;

    public WorldResolveItem(string photoPath, string photoFilename, string phash, long sourceSlot)
    {
        TargetPhotoPath = photoPath;
        TargetPhotoFilename = photoFilename;
        TargetPhash = phash;
        TargetSourceSlot = sourceSlot;
    }
}
