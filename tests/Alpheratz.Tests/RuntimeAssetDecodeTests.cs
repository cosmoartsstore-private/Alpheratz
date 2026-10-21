using System.Runtime.InteropServices;

namespace Alpheratz.Tests;

public sealed partial class ImagingIoBehaviorTests
{
    /// <summary>runtimeの全bitmap資材を実decoder/OS icon loaderで読み、元fileの保持を確認する。</summary>
    [Fact]
    public async Task RuntimeBitmapAssetsDecodeAndWindowIconLoadsWithoutCreatingAVisibleWindow()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "app", "Alpheratz.Frontend.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var assets = Path.Combine(directory.FullName, "app", "Assets");
        foreach (var name in new[] { "LogoText.png", "avatar.jpg", "icon.png" })
        {
            var path = Path.Combine(assets, name); var before = File.ReadAllBytes(path); Assert.NotEmpty(await Decode(path)); Assert.Equal(before, File.ReadAllBytes(path));
        }
        var iconPath = Path.Combine(assets, "icon.ico"); var iconBytes = File.ReadAllBytes(iconPath);
        var icon = LoadOwnedIconFile(0, iconPath, 1, 0, 0, 0x10);
        Assert.NotEqual(nint.Zero, icon);
        try { Assert.Equal(iconBytes, File.ReadAllBytes(iconPath)); }
        finally { Assert.True(DestroyOwnedIcon(icon)); }
    }

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadOwnedIconFile(nint instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "DestroyIcon")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyOwnedIcon(nint icon);
}
