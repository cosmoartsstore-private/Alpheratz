using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.PhotoModal;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class PhotoModalViewModel : UiThreadSafeObservableObject
{
	private const bool DETACH_RUNTIME_DATA = false;

	private const bool DETACH_AUXILIARY_RUNTIME_DATA = false;

	private readonly WorldService worldService;

	private readonly ToastService toastService;

	public PhotoModalState state { get; }

	public PhotoModalViewModel(PhotoModalState state, WorldService worldService, ToastService toastService)
	{
		this.state = state;
		this.worldService = worldService;
		this.toastService = toastService;
	}

	public async Task handleOpenWorld()
	{
		string text = state.SelectedPhoto?.WorldId;
		if (text == null || text.Length <= 0)
		{
			return;
		}
		try
		{
			await worldService.OpenWorldUrlAsync(text).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalViewModel.handleOpenWorld: threw: {value}");
			toastService.addToast($"ワールドページを開けませんでした: {value}", ToastType.error);
		}
	}

	public async Task handleOpenExplorer()
	{
		if (state.SelectedPhoto == null)
		{
			return;
		}
		try
		{
			await worldService.ShowInExplorerAsync(state.SelectedPhoto.PhotoPath).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"PhotoModalViewModel.handleOpenExplorer: threw: {value}");
			toastService.addToast($"Explorer で表示できませんでした: {value}", ToastType.error);
		}
	}

	public void onSelectPhoto(PhotoThumbnailItem photo, bool isSimilarSearch = false)
	{
		state.onSelectPhoto(photo, isSimilarSearch);
	}

	public void goBackPhoto()
	{
		state.goBackPhoto();
	}

	public void closePhotoModal()
	{
		state.closePhotoModal();
	}
}
