using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Alpheratz.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using Microsoft.UI.Xaml;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Gallery;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class PhotoGridItem : UiThreadSafeObservableObject
{
	[ObservableProperty]
	private PhotoThumbnailItem photo = new PhotoThumbnailItem();

	[ObservableProperty]
	private int? groupCount;

	[ObservableProperty]
	private string? groupKey;

	[ObservableProperty]
	private IReadOnlyList<PhotoThumbnailItem>? groupPhotos;

	public Visibility GroupCountVisibility
	{
		get
		{
			int? num = GroupCount;
			if (!num.HasValue || num.GetValueOrDefault() <= 1)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	public string GroupCountLabel
	{
		get
		{
			int? num = GroupCount;
			if (!num.HasValue || num.GetValueOrDefault() <= 1)
			{
				return "";
			}
			return $"{GroupCount}枚";
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public PhotoThumbnailItem Photo
	{
		get
		{
			return photo;
		}
		[MemberNotNull("photo")]
		set
		{
			if (!EqualityComparer<PhotoThumbnailItem>.Default.Equals(photo, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Photo);
				photo = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Photo);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int? GroupCount
	{
		get
		{
			return groupCount;
		}
		set
		{
			if (!EqualityComparer<int?>.Default.Equals(groupCount, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GroupCount);
				groupCount = value;
				OnGroupCountChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GroupCount);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? GroupKey
	{
		get
		{
			return groupKey;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(groupKey, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GroupKey);
				groupKey = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GroupKey);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IReadOnlyList<PhotoThumbnailItem>? GroupPhotos
	{
		get
		{
			return groupPhotos;
		}
		set
		{
			if (!EqualityComparer<IReadOnlyList<PhotoThumbnailItem>>.Default.Equals(groupPhotos, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GroupPhotos);
				groupPhotos = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GroupPhotos);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnGroupCountChanged(int? value)
	{
		OnPropertyChanged("GroupCountVisibility");
		OnPropertyChanged("GroupCountLabel");
	}
}
