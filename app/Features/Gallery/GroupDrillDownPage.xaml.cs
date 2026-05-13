using System;
using System.Collections.Generic;
using System.Linq;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// グループ（ワールド）ドリルダウン表示。
/// メイン PhotoGrid と同一の共有コントロールを使うことで、
/// カード寸法・サムネイル表示・お気に入り操作・ホバーアニメをメインと揃える。
/// </summary>
public sealed partial class GroupDrillDownPage : UserControl
{
    public Action? OnBack { get; set; }
    public Action<PhotoThumbnailItem>? OnPhotoActivated { get; set; }
    public Action<PhotoThumbnailItem>? OnFavoriteClicked { get; set; }
    public Action<IReadOnlyList<PhotoThumbnailItem>>? OnThumbnailsNeeded { get; set; }

    private readonly UiObservableCollection<PhotoGridItem> displayItems = [];
    private IReadOnlyList<PhotoThumbnailItem> photos = [];

    public IReadOnlyList<PhotoThumbnailItem> CurrentPhotos => photos;

    public GroupDrillDownPage()
    {
        AppLogger.Trace("GroupDrillDownPage.ctor: enter");
        try { InitializeComponent(); }
        catch (Exception ex) { AppLogger.Error($"GroupDrillDownPage.ctor: InitializeComponent failed: {ex}"); throw; }

        try
        {
            PhotoGridControl.SetItemsSource(displayItems);
            PhotoGridControl.OnPhotoActivated = item =>
            {
                if (item?.Photo is { } p) OnPhotoActivated?.Invoke(p);
            };
            PhotoGridControl.OnFavoriteClicked = item =>
            {
                if (item?.Photo is { } p) OnFavoriteClicked?.Invoke(p);
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GroupDrillDownPage.ctor: wiring failed: {ex}");
            throw;
        }
        AppLogger.Trace("GroupDrillDownPage.ctor: exit");
    }

    public void SetGroupInfo(string groupName, IReadOnlyList<PhotoThumbnailItem> items)
    {
        AppLogger.Trace($"GroupDrillDownPage.SetGroupInfo: enter name={groupName} count={items.Count}");
        try
        {
            GroupTitle.Text = $"{groupName}  ({items.Count}枚)";
            photos = items;
            var wrapped = items.Select(p => new PhotoGridItem { Photo = p }).ToArray();
            displayItems.ReplaceAll(wrapped);
            EmptyStateControl.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PhotoGridControl.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            PhotoGridControl.ScrollToTop();

            if (items.Count > 0)
                OnThumbnailsNeeded?.Invoke(items);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GroupDrillDownPage.SetGroupInfo: threw: {ex}");
        }
        AppLogger.Trace("GroupDrillDownPage.SetGroupInfo: exit");
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        OnBack?.Invoke();
    }
}
