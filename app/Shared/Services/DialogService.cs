using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Alpheratz.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Alpheratz.Shared.Services;

/// <summary>
/// WinUI のファイル/フォルダ選択ダイアログを呼び出すサービス。
/// MainWindow が未生成のときや OS 側で失敗したときは null を返す。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed class DialogService
{
    /// <summary>フォルダ選択ダイアログを開き、選択されたパスを返す。キャンセル時は null。</summary>
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
