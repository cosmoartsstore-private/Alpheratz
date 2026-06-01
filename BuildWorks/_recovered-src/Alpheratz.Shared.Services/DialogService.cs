using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Storage.Pickers;

namespace Alpheratz.Shared.Services;

public sealed class DialogService
{
	public async Task<string?> openDirectoryAsync()
	{
		try
		{
			FolderPicker folderPicker = new FolderPicker
			{
				SuggestedStartLocation = PickerLocationId.PicturesLibrary
			};
			folderPicker.FileTypeFilter.Add("*");
			Window mainWindowInstance = App.MainWindowInstance;
			if ((object)mainWindowInstance == null)
			{
				AppLogger.Error("DialogService.openDirectoryAsync: MainWindow is null");
				return null;
			}
			InitializeWithWindow.Initialize(folderPicker, WindowNative.GetWindowHandle(mainWindowInstance));
			return (await folderPicker.PickSingleFolderAsync())?.Path;
		}
		catch (Exception value)
		{
			AppLogger.Error($"DialogService.openDirectoryAsync: threw: {value}");
			return null;
		}
	}
}
