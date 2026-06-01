using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Gallery;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GallerySelectionStateWinRTTypeDetails))]
public class GallerySelectionState : UiThreadSafeObservableObject, IDisposable
{
	private const bool DETACH_RUNTIME_DATA = false;

	private const bool DETACH_AUXILIARY_RUNTIME_DATA = false;

	private readonly PhotoService photoService;

	private readonly ToastService toastService;

	private CancellationTokenSource? selectedPhotoRefsCancellation;

	[ObservableProperty]
	private bool isMultiSelectMode;

	[ObservableProperty]
	private string? selectionAnchorPhotoPath;

	[ObservableProperty]
	private bool isBulkTagModalOpen;

	public UiObservableCollection<string> selectedPhotoPaths { get; } = new UiObservableCollection<string>();

	public UiObservableCollection<string> bulkTagSelections { get; } = new UiObservableCollection<string>();

	public UiObservableCollection<SelectedPhotoRefDto> selectedPhotoRefs { get; } = new UiObservableCollection<SelectedPhotoRefDto>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsMultiSelectMode
	{
		get
		{
			return isMultiSelectMode;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(isMultiSelectMode, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsMultiSelectMode);
				isMultiSelectMode = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsMultiSelectMode);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? SelectionAnchorPhotoPath
	{
		get
		{
			return selectionAnchorPhotoPath;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(selectionAnchorPhotoPath, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectionAnchorPhotoPath);
				selectionAnchorPhotoPath = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectionAnchorPhotoPath);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsBulkTagModalOpen
	{
		get
		{
			return isBulkTagModalOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(isBulkTagModalOpen, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsBulkTagModalOpen);
				isBulkTagModalOpen = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsBulkTagModalOpen);
			}
		}
	}

	public GallerySelectionState(PhotoService photoService, ToastService toastService)
	{
		this.photoService = photoService;
		this.toastService = toastService;
		selectedPhotoPaths.CollectionChanged += selectedPhotoPathsChanged;
	}

	private void selectedPhotoPathsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		try
		{
			loadSelectedPhotoRefs();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GallerySelectionState.selectedPhotoPathsChanged: threw: {value}");
		}
	}

	public void toggleSelectedPhoto(PhotoGridItem item, bool shiftKey, IReadOnlyList<PhotoGridItem> displayPhotoItems)
	{
		try
		{
			string photoPath = item.Photo.PhotoPath;
			if (shiftKey && SelectionAnchorPhotoPath != null)
			{
				List<PhotoGridItem> list = displayPhotoItems.ToList();
				int num = list.FindIndex((PhotoGridItem entry) => entry.Photo.PhotoPath == SelectionAnchorPhotoPath);
				int num2 = list.FindIndex((PhotoGridItem entry) => entry.Photo.PhotoPath == photoPath);
				if (num >= 0 && num2 >= 0)
				{
					int num3 = Math.Min(num, num2);
					int num4 = Math.Max(num, num2);
					foreach (string item2 in from entry in displayPhotoItems.Skip(num3).Take(num4 - num3 + 1)
						select entry.Photo.PhotoPath)
					{
						if (!selectedPhotoPaths.Contains(item2))
						{
							selectedPhotoPaths.Add(item2);
						}
					}
					SelectionAnchorPhotoPath = photoPath;
					return;
				}
			}
			if (selectedPhotoPaths.Contains(photoPath))
			{
				selectedPhotoPaths.Remove(photoPath);
			}
			else
			{
				selectedPhotoPaths.Add(photoPath);
			}
			SelectionAnchorPhotoPath = photoPath;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GallerySelectionState.toggleSelectedPhoto: threw: {value}");
		}
	}

	public void clearSelectedPhotos()
	{
		try
		{
			selectedPhotoPaths.Clear();
			SelectionAnchorPhotoPath = null;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GallerySelectionState.clearSelectedPhotos: threw: {value}");
		}
	}

	public void handleToggleMultiSelectMode()
	{
		try
		{
			if (IsMultiSelectMode)
			{
				clearSelectedPhotos();
			}
			IsMultiSelectMode = !IsMultiSelectMode;
		}
		catch (Exception value)
		{
			AppLogger.Error($"GallerySelectionState.handleToggleMultiSelectMode: threw: {value}");
		}
	}

	public void setSelectedPhotoRefs(IEnumerable<SelectedPhotoRefDto> refsToSet)
	{
		try
		{
			selectedPhotoRefs.Clear();
			foreach (SelectedPhotoRefDto item in refsToSet)
			{
				selectedPhotoRefs.Add(item);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GallerySelectionState.setSelectedPhotoRefs: threw: {value}");
		}
	}

	public async Task loadSelectedPhotoRefs()
	{
		if (selectedPhotoPaths.Count == 0)
		{
			selectedPhotoRefs.Clear();
			return;
		}
		CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		CancellationTokenSource cancellationTokenSource2 = Interlocked.Exchange(ref selectedPhotoRefsCancellation, cancellationTokenSource);
		if (cancellationTokenSource2 != null)
		{
			try
			{
				cancellationTokenSource2.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
			try
			{
				cancellationTokenSource2.Dispose();
			}
			catch (ObjectDisposedException)
			{
			}
		}
		CancellationToken token = cancellationTokenSource.Token;
		string[] photoPaths = selectedPhotoPaths.ToArray();
		try
		{
			IReadOnlyList<SelectedPhotoRefDto> readOnlyList = await photoService.GetSelectedPhotoRefsAsync(photoPaths, token).ConfigureAwait(continueOnCapturedContext: false);
			if (!token.IsCancellationRequested)
			{
				setSelectedPhotoRefs(readOnlyList);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GallerySelectionState.loadSelectedPhotoRefs: threw: {value}");
			if (!token.IsCancellationRequested)
			{
				toastService.addToast($"選択写真情報の取得に失敗しました: {value}", ToastType.error);
			}
		}
	}

	public void Dispose()
	{
		try
		{
			selectedPhotoPaths.CollectionChanged -= selectedPhotoPathsChanged;
			CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(ref selectedPhotoRefsCancellation, null);
			if (cancellationTokenSource != null)
			{
				try
				{
					cancellationTokenSource.Cancel();
				}
				catch (ObjectDisposedException)
				{
				}
				try
				{
					cancellationTokenSource.Dispose();
					return;
				}
				catch (ObjectDisposedException)
				{
					return;
				}
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GallerySelectionState.Dispose: threw: {value}");
		}
	}
}
