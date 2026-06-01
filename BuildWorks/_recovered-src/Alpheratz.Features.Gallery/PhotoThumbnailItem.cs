using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core;
using Alpheratz.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Gallery;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class PhotoThumbnailItem : UiThreadSafeObservableObject
{
	private string photo_filename = string.Empty;

	private string photo_path = string.Empty;

	private string? resolved_photo_path;

	private string? grid_thumb_path;

	private string? display_thumb_path;

	private string? world_id;

	private string? world_name;

	[ObservableProperty]
	private string timestamp = string.Empty;

	[ObservableProperty]
	private string? phash;

	[ObservableProperty]
	private string? orientation;

	private long? image_width;

	private long? image_height;

	private long source_slot = 1L;

	private bool is_favorite;

	[ObservableProperty]
	private IReadOnlyList<string> tags = Array.Empty<string>();

	private string? match_source;

	private bool is_missing;

	[ObservableProperty]
	private bool isSelected;

	public string PhotoFilename
	{
		get
		{
			return photo_filename;
		}
		set
		{
			SetProperty(ref photo_filename, value, "PhotoFilename");
		}
	}

	public string PhotoPath
	{
		get
		{
			return photo_path;
		}
		set
		{
			if (SetProperty(ref photo_path, value, "PhotoPath"))
			{
				OnPropertyChanged("EffectiveSourcePath");
				OnPropertyChanged("EffectiveDisplayPath");
			}
		}
	}

	public string? ResolvedPhotoPath
	{
		get
		{
			return resolved_photo_path;
		}
		set
		{
			if (SetProperty(ref resolved_photo_path, value, "ResolvedPhotoPath"))
			{
				OnPropertyChanged("EffectiveSourcePath");
				OnPropertyChanged("EffectiveDisplayPath");
			}
		}
	}

	public string? GridThumbPath
	{
		get
		{
			return grid_thumb_path;
		}
		set
		{
			if (SetProperty(ref grid_thumb_path, value, "GridThumbPath"))
			{
				OnPropertyChanged("EffectiveSourcePath");
			}
		}
	}

	public string? DisplayThumbPath
	{
		get
		{
			return display_thumb_path;
		}
		set
		{
			if (SetProperty(ref display_thumb_path, value, "DisplayThumbPath"))
			{
				OnPropertyChanged("EffectiveDisplayPath");
			}
		}
	}

	public string? WorldId
	{
		get
		{
			return world_id;
		}
		set
		{
			SetProperty(ref world_id, value, "WorldId");
		}
	}

	public string? WorldName
	{
		get
		{
			return world_name;
		}
		set
		{
			SetProperty(ref world_name, value, "WorldName");
		}
	}

	public long? ImageWidth
	{
		get
		{
			return image_width;
		}
		set
		{
			SetProperty(ref image_width, value, "ImageWidth");
		}
	}

	public long? ImageHeight
	{
		get
		{
			return image_height;
		}
		set
		{
			SetProperty(ref image_height, value, "ImageHeight");
		}
	}

	public long SourceSlot
	{
		get
		{
			return source_slot;
		}
		set
		{
			SetProperty(ref source_slot, value, "SourceSlot");
		}
	}

	public bool IsFavorite
	{
		get
		{
			return is_favorite;
		}
		set
		{
			SetProperty(ref is_favorite, value, "IsFavorite");
		}
	}

	public string? MatchSource
	{
		get
		{
			return match_source;
		}
		set
		{
			SetProperty(ref match_source, value, "MatchSource");
		}
	}

	public bool IsMissing
	{
		get
		{
			return is_missing;
		}
		set
		{
			SetProperty(ref is_missing, value, "IsMissing");
		}
	}

	public string? EffectiveSourcePath
	{
		get
		{
			string text = ((!string.IsNullOrEmpty(grid_thumb_path)) ? grid_thumb_path : photo_path);
			if (!string.IsNullOrEmpty(text))
			{
				return text.Replace('/', '\\');
			}
			return null;
		}
	}

	public string? EffectiveDisplayPath
	{
		get
		{
			string text = ((!string.IsNullOrEmpty(display_thumb_path)) ? display_thumb_path : ((!string.IsNullOrEmpty(resolved_photo_path)) ? resolved_photo_path : photo_path));
			if (!string.IsNullOrEmpty(text))
			{
				return text.Replace('/', '\\');
			}
			return null;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Timestamp
	{
		get
		{
			return timestamp;
		}
		[MemberNotNull("timestamp")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(timestamp, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Timestamp);
				timestamp = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Timestamp);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? Phash
	{
		get
		{
			return phash;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(phash, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Phash);
				phash = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Phash);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? Orientation
	{
		get
		{
			return orientation;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(orientation, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Orientation);
				orientation = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Orientation);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IReadOnlyList<string> Tags
	{
		get
		{
			return tags;
		}
		[MemberNotNull("tags")]
		set
		{
			if (!EqualityComparer<IReadOnlyList<string>>.Default.Equals(tags, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Tags);
				tags = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Tags);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSelected
	{
		get
		{
			return isSelected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(isSelected, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsSelected);
				isSelected = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsSelected);
			}
		}
	}

	public static PhotoThumbnailItem FromDto(PhotoRecordDto photo)
	{
		return new PhotoThumbnailItem
		{
			photo_filename = photo.photo_filename,
			photo_path = photo.photo_path,
			resolved_photo_path = photo.resolved_photo_path,
			grid_thumb_path = photo.grid_thumb_path,
			display_thumb_path = photo.display_thumb_path,
			world_id = photo.world_id,
			world_name = photo.world_name,
			Timestamp = photo.timestamp,
			Phash = photo.phash,
			Orientation = photo.orientation,
			image_width = photo.image_width,
			image_height = photo.image_height,
			source_slot = photo.source_slot,
			is_favorite = photo.is_favorite,
			Tags = photo.tags,
			match_source = photo.match_source,
			is_missing = photo.is_missing
		};
	}

	public PhotoRecordDto ToDto()
	{
		return new PhotoRecordDto
		{
			photo_filename = photo_filename,
			photo_path = photo_path,
			resolved_photo_path = resolved_photo_path,
			grid_thumb_path = grid_thumb_path,
			display_thumb_path = display_thumb_path,
			world_id = world_id,
			world_name = world_name,
			timestamp = Timestamp,
			phash = Phash,
			orientation = Orientation,
			image_width = image_width,
			image_height = image_height,
			source_slot = source_slot,
			is_favorite = is_favorite,
			tags = Tags,
			match_source = match_source,
			is_missing = is_missing
		};
	}
}
