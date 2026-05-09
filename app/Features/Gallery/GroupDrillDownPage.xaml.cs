using System;
using System.Collections.Generic;
using Alpheratz.Core;
using Alpheratz.Shared.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Alpheratz.Features.Gallery;

public sealed partial class GroupDrillDownPage : UserControl
{
    private const double CARD_ASPECT = 0.72;
    private const int CARD_MARGIN_H = 8;
    private const int GRID_PADDING = 12;

    public Action? OnBack { get; set; }
    public Action<PhotoThumbnailItem>? OnPhotoActivated { get; set; }

    private IReadOnlyList<PhotoThumbnailItem> photos = [];

    public GroupDrillDownPage()
    {
        InitializeComponent();
    }

    public void SetGroupInfo(string groupName, IReadOnlyList<PhotoThumbnailItem> items)
    {
        GroupTitle.Text = $"{groupName}  ({items.Count}枚)";
        photos = items;
        PhotoItems.ItemsSource = items;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        OnBack?.Invoke();
    }

    private void PhotoItems_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PhotoThumbnailItem photo)
            OnPhotoActivated?.Invoke(photo);
    }

    private void GridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is GridView gv && gv.ItemsPanelRoot is ItemsWrapGrid wrap)
            RecalcItemSize(wrap, e.NewSize.Width);
    }

    private void PhotoItemsWrapGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ItemsWrapGrid wrap && PhotoItems.ActualWidth > 0)
            RecalcItemSize(wrap, PhotoItems.ActualWidth);
    }

    private void RecalcItemSize(ItemsWrapGrid wrap, double availableWidth)
    {
        var usable = availableWidth - GRID_PADDING * 2;
        var cols = Math.Max(1, (int)Math.Floor(usable / 240.0));
        var cardWidth = Math.Floor(usable / cols) - CARD_MARGIN_H;
        var cardHeight = Math.Floor(cardWidth / CARD_ASPECT);
        wrap.ItemWidth = cardWidth;
        wrap.ItemHeight = cardHeight;
    }

    private void ThumbImage_ImageOpened(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Image img)
            AnimationHelper.FadeIn(img, 200);
    }
}
