using System.Collections.Generic;
using Alpheratz.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// グリッド (および GroupDrillDown) の 1 セル分の表示モデル。
/// 写真本体 (<see cref="Photo"/>) に加えて、グルーピング表示用の代表バッジ情報
/// (<see cref="GroupCount"/>, <see cref="GroupKey"/>, <see cref="GroupPhotos"/>) を持つ。
/// 非グループ表示では GroupCount は null で、その派生 (<see cref="GroupCountVisibility"/>) で
/// バッジを非表示にする。
/// </summary>
public partial class PhotoGridItem : UiThreadSafeObservableObject
{
    [ObservableProperty] private PhotoThumbnailItem photo = new();
    [ObservableProperty] private int? groupCount;
    [ObservableProperty] private string? groupKey;
    [ObservableProperty] private IReadOnlyList<PhotoThumbnailItem>? groupPhotos;

    public Visibility GroupCountVisibility =>
        GroupCount is > 1 ? Visibility.Visible : Visibility.Collapsed;

    public string GroupCountLabel =>
        GroupCount is > 1 ? $"{GroupCount}枚" : "";

    partial void OnGroupCountChanged(int? value)
    {
        OnPropertyChanged(nameof(GroupCountVisibility));
        OnPropertyChanged(nameof(GroupCountLabel));
    }
}
