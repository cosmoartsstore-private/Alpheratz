using System.Collections.Generic;
using Alpheratz.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// グリッドとドリルダウンで使う1セル分の表示モデル。
/// 通常写真に加えて、グループ表示時の件数と代表写真群を持つ。
/// </summary>
public partial class PhotoGridItem : UiThreadSafeObservableObject
{
    [ObservableProperty] private PhotoThumbnailItem photo = new();
    [ObservableProperty] private int? groupCount;
    [ObservableProperty] private string? groupKey;
    [ObservableProperty] private IReadOnlyList<PhotoThumbnailItem>? groupPhotos;

    /// <summary>グループ件数バッジの表示可否。2件以上のグループだけ表示する。</summary>
    public Visibility GroupCountVisibility =>
        GroupCount is > 1 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>グループ件数バッジの表示文字列。</summary>
    public string GroupCountLabel =>
        GroupCount is > 1
            ? getMsg("PhotoGridItem.groupCount", ("count", GroupCount))
            : string.Empty;

    /// <summary>件数変更時に、件数バッジ関連の派生プロパティを更新する。</summary>
    partial void OnGroupCountChanged(int? value)
    {
        OnPropertyChanged(nameof(GroupCountVisibility));
        OnPropertyChanged(nameof(GroupCountLabel));
    }
}
