using System.Collections.Generic;
using Alpheratz.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Alpheratz.Features.Gallery;

public partial class PhotoGridItem : UiThreadSafeObservableObject
{
    // TS: DisplayPhotoItem
    [ObservableProperty] private PhotoThumbnailItem photo = new();
    [ObservableProperty] private int? groupCount;
    [ObservableProperty] private string? groupKey;
    [ObservableProperty] private IReadOnlyList<PhotoThumbnailItem>? groupPhotos;

    public Visibility GroupCountVisibility =>
        groupCount is > 1 ? Visibility.Visible : Visibility.Collapsed;

    public string GroupCountLabel =>
        groupCount is > 1 ? $"{groupCount}枚" : "";

    partial void OnGroupCountChanged(int? value)
    {
        OnPropertyChanged(nameof(GroupCountVisibility));
        OnPropertyChanged(nameof(GroupCountLabel));
    }
}
