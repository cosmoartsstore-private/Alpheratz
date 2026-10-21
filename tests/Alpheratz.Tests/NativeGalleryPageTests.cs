using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.Gallery.Controls;
using Alpheratz.Features.Shell;
using Alpheratz.Features.Shell.Controls;
using Alpheratz.Shared.Controls;
using Alpheratz.Shared.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Alpheratz.Tests;

public sealed partial class NativeStartupTests
{
    [Fact]
    public Task NativeStartup_GalleryActualFiltersAndResetLight() => RunIsolatedAsync("gallery-filters-light", "light", false);

    [Fact]
    public Task NativeStartup_GalleryActualFiltersAndResetDark() => RunIsolatedAsync("gallery-filters-dark", "dark", false);

    private static bool IsGalleryScenario(string kind) => kind is "gallery-filters-light" or "gallery-filters-dark";

    // 実ShellのボタンをUI Automation peerから操作し、filter表示と実GridViewの対象を照合する。
    // ViewModelの操作関数を直接呼ぶ方法や、mockの一覧を画面成功の代用にする方法は使わない。
    private static async Task VerifyActualGalleryFilters(ShellPage shell, ShellViewModel viewModel, StartupObservation observed)
    {
        var page = Assert.Single(Descendants(shell).OfType<GalleryPage>());
        var stage = Assert.IsType<GalleryGridStage>(page.FindName("GridStage"));
        var grid = Assert.IsType<PhotoGrid>(stage.FindName("PhotoGridControl"));
        var itemsView = Assert.IsType<PhotoGridItemsView>(grid.FindName("ItemsViewControl"));
        var items = Assert.IsType<GridView>(itemsView.FindName("PhotoItems"));
        var panel = Assert.IsType<GalleryFilterPanel>(shell.FindName("FilterPanel"));
        var header = Assert.IsType<ShellHeaderBar>(shell.FindName("HeaderBar"));
        var overlay = Assert.IsType<Grid>(shell.FindName("FilterOverlay"));
        Assert.Equal(shell.ActualTheme, panel.ActualTheme);
        Assert.Equal(shell.ActualTheme, page.ActualTheme);
        var dbPath = Path.Combine(AppPaths.InstallLocation!, "Data", "db", "Alpheratz.db");
        var before = DatabaseSnapshot(dbPath);
        var originals = viewModel.galleryViewModel.photosState.photos.ToDictionary(photo => photo.PhotoPath, photo => ReadSharedBytes(photo.PhotoPath));
        observed.SavedPhotoCount = 2;

        string[] DisplayedNames() => items.Items.Cast<PhotoGridItem>().Select(item => item.Photo!.PhotoFilename).ToArray();
        async Task Expect(string message, params string[] expected)
        {
            await WaitPhotoUi(() => !viewModel.galleryViewModel.photosState.IsLoading && DisplayedNames().SequenceEqual(expected), message);
            await WaitPhotoUi(() => Assert.IsType<TextBlock>(panel.FindName("FilteredCountLabel")).Text == $"{expected.Length}枚の写真が該当", "検索条件の該当件数が実一覧と一致しません。");
            if (expected.Length > 0)
            {
                // 一覧の値だけでなく、実体化したカード、decode済み画像、日時、お気に入りglyphも照合する。
                await WaitPhotoUi(() => items.IsLoaded && items.Opacity == 1 && expected.Select((_, index) => items.ContainerFromIndex(index)).All(container => container is GridViewItem { ActualWidth: > 0, ActualHeight: > 0 }), "写真カードが実一覧へ表示されません。");
                for (var index = 0; index < expected.Length; index++)
                {
                    var card = Assert.IsType<GridViewItem>(items.ContainerFromIndex(index));
                    var photo = Assert.IsType<PhotoGridItem>(card.Content).Photo!;
                    Assert.Equal(expected[index], photo.PhotoFilename);
                    await WaitPhotoUi(() => Descendants(card).OfType<Image>().Any(image => image.Name == "ThumbImage" && image.Source is BitmapImage { PixelWidth: > 0, PixelHeight: > 0 }), "実写真カードの画像がdecodeされません。");
                    var expectedTime = expected[index] == "写真①.png" ? "2026-10-09 00:01:00" : "2026-10-09 00:02:00";
                    Assert.Contains(Descendants(card).OfType<TextBlock>(), text => text.Text == expectedTime);
                    var favorite = Assert.Single(Descendants(card).OfType<FavoriteCornerBadge>());
                    Assert.Equal(expected[index] == "写真①.png", favorite.Liked);
                    if (expected[index] == "写真①.png") Assert.Contains(Descendants(card).OfType<TextBlock>(), text => text.Text == "保持ワールド");
                }
            }
            Assert.Equal(expected.Length == 0 ? Visibility.Visible : Visibility.Collapsed, stage.EmptyStateVisibility);
            if (expected.Length == 0)
            {
                var empty = Assert.IsType<EmptyState>(stage.FindName("EmptyStateControl"));
                Assert.Contains(Descendants(empty).OfType<TextBlock>(), text => text.Text == "写真が見つかりません");
                Assert.Contains(Descendants(empty).OfType<TextBlock>(), text => text.Text == "写真フォルダの設定または検索条件を変更してください。");
            }
        }
        async Task Click(string name) => await ClickPhotoButton(Assert.IsType<Button>(panel.FindName(name)));
        async Task Reset()
        {
            var button = Assert.Single(Descendants(panel).OfType<Button>(), item => Equals(item.Content, "リセット"));
            await ClickPhotoButton(button);
            await Expect("条件解除後の一覧が元に戻りません。", "写真②.png", "写真①.png");
            await WaitPhotoUi(() => Assert.IsType<Border>(panel.FindName("BadgeBorder")).Visibility == Visibility.Collapsed, "条件解除後も条件数が残っています。");
        }

        observed.Step = "gallery-filter: opening-actual-panel";
        await Expect("初期の実写真一覧と日付順が一致しません。", "写真②.png", "写真①.png");
        observed.GalleryChecks.Add("実写真カードのgeometry、画像decode、日時とお気に入りglyphが対象写真に一致する");
        Assert.Equal(Visibility.Collapsed, overlay.Visibility);
        await ClickPhotoButton(Assert.IsType<Button>(header.FindName("FilterPillBtn")));
        await WaitPhotoUi(() => overlay.Visibility == Visibility.Visible, "検索条件ボタンが実panelを開きません。");
        Assert.False(Assert.IsType<Button>(header.FindName("SettingsGearBtn")).IsEnabled);
        observed.GalleryChecks.Add("検索条件ボタンから実panelを開き、背面の設定操作を無効にする");

        observed.Step = "gallery-filter: favorite-and-orientation";
        await Click("FavoriteToggleBtn");
        await Expect("お気に入り以外が実一覧へ残っています。", "写真①.png");
        await WaitPhotoUi(() => Assert.IsType<TextBlock>(panel.FindName("BadgeText")).Text == "1", "お気に入りの条件数が一致しません。");
        observed.GalleryChecks.Add("お気に入り条件の対象1枚、該当件数、条件数が一致する");
        await Click("OrientationLandscapeBtn");
        await Expect("お気に入りと横向きの組合せで無該当になりません。");
        await WaitPhotoUi(() => Assert.IsType<TextBlock>(panel.FindName("BadgeText")).Text == "2", "組合せ条件の条件数が一致しません。");
        observed.GalleryChecks.Add("条件の組合せで0枚となり、空表示と解除案内を表示する");
        await Click("OrientationAllBtn");
        await Expect("向き解除後も無該当の旧一覧が残っています。", "写真①.png");
        await Click("FavoriteToggleBtn");
        await Expect("お気に入り解除後の2枚と順序が一致しません。", "写真②.png", "写真①.png");
        observed.GalleryChecks.Add("個別条件の解除後は対象と件数を再表示する");

        await Click("OrientationPortraitBtn");
        await Expect("正方形のfixtureが縦向きに含まれています。");
        await Reset();
        observed.GalleryChecks.Add("縦向きの無該当からリセットすると2枚の日付順と条件数を復元する");

        Assert.Equal(before, DatabaseSnapshot(dbPath));
        foreach (var (path, bytes) in originals) Assert.Equal(bytes, ReadSharedBytes(path));
        observed.GalleryChecks.Add("絞り込み・解除は保存済み全表と写真原本を変更しない");
        observed.Step = "gallery-filter: completed";
    }
}
