using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Alpheratz.Shared.Services;

public sealed class DialogService
{
    public async Task<string?> openDirectoryAsync()
    {
        AppLogger.Trace("DialogService.openDirectoryAsync: enter");
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            };
            picker.FileTypeFilter.Add("*");

            var window = App.MainWindowInstance;
            if (window is null)
            {
                AppLogger.Error("DialogService.openDirectoryAsync: MainWindow is null");
                return null;
            }
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

            var folder = await picker.PickSingleFolderAsync();
            var path = folder?.Path;
            AppLogger.Trace($"DialogService.openDirectoryAsync: exit path={path ?? "(null)"}");
            return path;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"DialogService.openDirectoryAsync: threw: {ex}");
            return null;
        }
    }
}
