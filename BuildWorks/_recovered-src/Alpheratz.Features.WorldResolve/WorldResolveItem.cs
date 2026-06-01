using Alpheratz.Core;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.WorldResolve;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class WorldResolveItem : UiThreadSafeObservableObject
{
	private string? targetThumbPath;

	private string? matchPhotoPath;

	private string? matchPhotoFilename;

	private string? matchWorldName;

	private string? matchWorldId;

	private string? matchThumbPath;

	private int? matchDistance;

	private bool isApplied;

	public string TargetPhotoPath { get; }

	public string TargetPhotoFilename { get; }

	public string TargetPhash { get; }

	public long TargetSourceSlot { get; }

	public string? TargetThumbPath
	{
		get
		{
			return targetThumbPath;
		}
		set
		{
			SetProperty(ref targetThumbPath, value, "TargetThumbPath");
		}
	}

	public string? MatchPhotoPath
	{
		get
		{
			return matchPhotoPath;
		}
		set
		{
			SetProperty(ref matchPhotoPath, value, "MatchPhotoPath");
		}
	}

	public string? MatchPhotoFilename
	{
		get
		{
			return matchPhotoFilename;
		}
		set
		{
			SetProperty(ref matchPhotoFilename, value, "MatchPhotoFilename");
		}
	}

	public string? MatchWorldName
	{
		get
		{
			return matchWorldName;
		}
		set
		{
			SetProperty(ref matchWorldName, value, "MatchWorldName");
		}
	}

	public string? MatchWorldId
	{
		get
		{
			return matchWorldId;
		}
		set
		{
			SetProperty(ref matchWorldId, value, "MatchWorldId");
		}
	}

	public string? MatchThumbPath
	{
		get
		{
			return matchThumbPath;
		}
		set
		{
			SetProperty(ref matchThumbPath, value, "MatchThumbPath");
		}
	}

	public int? MatchDistance
	{
		get
		{
			return matchDistance;
		}
		set
		{
			SetProperty(ref matchDistance, value, "MatchDistance");
		}
	}

	public bool IsApplied
	{
		get
		{
			return isApplied;
		}
		set
		{
			SetProperty(ref isApplied, value, "IsApplied");
		}
	}

	public bool HasMatch => MatchPhotoPath != null;

	public WorldResolveItem(string photoPath, string photoFilename, string phash, long sourceSlot)
	{
		TargetPhotoPath = photoPath;
		TargetPhotoFilename = photoFilename;
		TargetPhash = phash;
		TargetSourceSlot = sourceSlot;
	}
}
