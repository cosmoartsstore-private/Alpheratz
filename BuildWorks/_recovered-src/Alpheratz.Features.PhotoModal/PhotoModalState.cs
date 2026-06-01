using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.PhotoModal;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class PhotoModalState : UiThreadSafeObservableObject
{
	private const bool DETACH_RUNTIME_DATA = false;

	private const bool DETACH_AUXILIARY_RUNTIME_DATA = false;

	private readonly PhotoService photoService;

	private readonly ToastService toastService;

	private CancellationTokenSource? selectedPhotoCancellation;

	[ObservableProperty]
	private PhotoThumbnailItem? selectedPhoto;

	private List<PhotoThumbnailItem> photoList = new List<PhotoThumbnailItem>();

	public UiObservableCollection<PhotoThumbnailItem> photoHistory { get; } = new UiObservableCollection<PhotoThumbnailItem>();

	public bool CanGoBack => photoHistory.Count > 0;

	public bool CanGoPrev
	{
		get
		{
			if (SelectedPhoto != null && photoList.Count > 0)
			{
				return photoList.IndexOf(SelectedPhoto) > 0;
			}
			return false;
		}
	}

	public bool CanGoNext
	{
		get
		{
			if (SelectedPhoto != null && photoList.Count > 0)
			{
				return photoList.IndexOf(SelectedPhoto) < photoList.Count - 1;
			}
			return false;
		}
	}

	public int CurrentIndex
	{
		get
		{
			if (SelectedPhoto == null)
			{
				return 0;
			}
			return photoList.IndexOf(SelectedPhoto) + 1;
		}
	}

	public int TotalInList => photoList.Count;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public PhotoThumbnailItem? SelectedPhoto
	{
		get
		{
			return selectedPhoto;
		}
		set
		{
			if (!EqualityComparer<PhotoThumbnailItem>.Default.Equals(selectedPhoto, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedPhoto);
				selectedPhoto = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedPhoto);
			}
		}
	}

	public void setPhotoList(IReadOnlyList<PhotoThumbnailItem> list)
	{
		photoList = list.ToList();
	}

	public PhotoModalState(PhotoService photoService, ToastService toastService)
	{
		this.photoService = photoService;
		this.toastService = toastService;
	}

	public void setSelectedPhoto(PhotoThumbnailItem? photo)
	{
		try
		{
			SelectedPhoto = photo;
			loadSelectedPhotoAuxiliaryData(photo);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalState.setSelectedPhoto: threw: {value}");
		}
	}

	public void onSelectPhoto(PhotoThumbnailItem photo, bool isSimilarSearch = false)
	{
		try
		{
			if (SelectedPhoto != null && isSimilarSearch)
			{
				photoHistory.Add(SelectedPhoto);
			}
			else if (!isSimilarSearch)
			{
				photoHistory.Clear();
			}
			SelectedPhoto = photo;
			loadSelectedPhotoAuxiliaryData(photo);
			notifyNavProps();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalState.onSelectPhoto: threw: {value}");
		}
	}

	public void goBackPhoto()
	{
		if (photoHistory.Count <= 0)
		{
			return;
		}
		try
		{
			UiObservableCollection<PhotoThumbnailItem> uiObservableCollection = photoHistory;
			PhotoThumbnailItem photoThumbnailItem = uiObservableCollection[uiObservableCollection.Count - 1];
			photoHistory.RemoveAt(photoHistory.Count - 1);
			SelectedPhoto = photoThumbnailItem;
			notifyNavProps();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalState.goBackPhoto: threw: {value}");
		}
	}

	public void goPrevPhoto()
	{
		if (SelectedPhoto != null)
		{
			int num = photoList.IndexOf(SelectedPhoto);
			if (num > 0)
			{
				onSelectPhoto(photoList[num - 1]);
			}
		}
	}

	public void goNextPhoto()
	{
		if (SelectedPhoto != null)
		{
			int num = photoList.IndexOf(SelectedPhoto);
			if (num >= 0 && num < photoList.Count - 1)
			{
				onSelectPhoto(photoList[num + 1]);
			}
		}
	}

	private void notifyNavProps()
	{
		OnPropertyChanged("CanGoBack");
		OnPropertyChanged("CanGoPrev");
		OnPropertyChanged("CanGoNext");
		OnPropertyChanged("CurrentIndex");
		OnPropertyChanged("TotalInList");
	}

	public void closePhotoModal()
	{
		try
		{
			CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(ref selectedPhotoCancellation, null);
			if (cancellationTokenSource != null)
			{
				try
				{
					cancellationTokenSource.Cancel();
				}
				catch
				{
				}
				cancellationTokenSource.Dispose();
			}
			SelectedPhoto = null;
			photoHistory.Clear();
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalState.closePhotoModal: threw: {value}");
		}
	}

	private async Task loadSelectedPhotoAuxiliaryData(PhotoThumbnailItem? selectedPhotoSnapshot)
	{
		CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		CancellationTokenSource cancellationTokenSource2 = Interlocked.Exchange(ref selectedPhotoCancellation, cancellationTokenSource);
		if (cancellationTokenSource2 != null)
		{
			try
			{
				cancellationTokenSource2.Cancel();
			}
			catch
			{
			}
			cancellationTokenSource2.Dispose();
		}
		CancellationToken token = cancellationTokenSource.Token;
		if (selectedPhotoSnapshot == null)
		{
			return;
		}
		try
		{
			IReadOnlyList<string> tags = await photoService.GetPhotoTagsAsync(selectedPhotoSnapshot.PhotoPath, selectedPhotoSnapshot.SourceSlot, token).ConfigureAwait(continueOnCapturedContext: false);
			if (!token.IsCancellationRequested && SelectedPhoto != null && SelectedPhoto.PhotoPath == selectedPhotoSnapshot.PhotoPath)
			{
				SelectedPhoto.Tags = tags;
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalState.loadSelectedPhotoAuxiliaryData: threw: {value}");
			if (!token.IsCancellationRequested)
			{
				toastService.addToast($"タグの読み込みに失敗しました: {value}", ToastType.error);
			}
		}
	}
}
